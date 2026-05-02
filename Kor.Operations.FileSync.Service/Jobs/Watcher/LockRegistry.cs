#nullable enable
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Tracks files that were locked when first seen, retests them periodically,
// and fires the appropriate bucket sync once the file becomes unlocked +
// stable (or disappears). Verbatim from watcher.ps1 §222-308:
//
//   * "Locked" entry: file was open by another process at event time.
//     Polled every LockPollSeconds. When openable + size-stable -> fire.
//     Stale entries (FirstSeen > MaxLockTrackHours ago) get dropped.
//   * "Post-run" entry: registered AFTER a successful sync. Catches the
//     save/close race where the editor finishes writing right after we
//     scanned. Expires after PostRunRetrySeconds.
//
// `Trigger` is invoked in the poller's thread; the hosted service supplies
// it as a callback so the registry stays single-purpose.
internal sealed class LockRegistry : IAsyncDisposable
{
    public sealed record Entry(
        string Path,
        SyncBucket Bucket,
        string Root,
        DateTimeOffset FirstSeen,
        bool IsPostRun,
        DateTimeOffset? ExpireAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;
    private readonly Func<SyncBucket, string, string, CancellationToken, Task> _trigger;
    // Read through a callback so knob changes (LockPollSeconds,
    // PostRunRetrySeconds, MaxLockTrackHours, FileStabilitySleepMs) take
    // effect on the next tick without restarting the registry.
    private readonly Func<WatcherOptions> _getOptions;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LockRegistry(
        Func<WatcherOptions> optionsAccessor,
        ILogger logger,
        Func<SyncBucket, string, string, CancellationToken, Task> trigger)
    {
        _getOptions = optionsAccessor;
        _logger = logger;
        _trigger = trigger;
    }

    public int Count => _entries.Count;

    public void RegisterLocked(string path, SyncBucket bucket, string root)
    {
        var key = path.ToLowerInvariant();
        _entries[key] = new Entry(path, bucket, root, DateTimeOffset.Now, IsPostRun: false, ExpireAt: null);
        _logger.LogDebug("LockRegistry: registered locked file '{Path}' (root='{Root}', bucket='{Bucket}').", path, root, bucket.Name);
    }

    public void RegisterPostRun(string path, SyncBucket bucket, string root)
    {
        var key = path.ToLowerInvariant();
        var expire = DateTimeOffset.Now.AddSeconds(_getOptions().PostRunRetrySeconds);
        _entries[key] = new Entry(path, bucket, root, DateTimeOffset.Now, IsPostRun: true, ExpireAt: expire);
        _logger.LogDebug("LockRegistry: post-run watch on '{Path}' (expires {Expire:o}).", path, expire);
    }

    public void Drop(string path)
    {
        _entries.TryRemove(path.ToLowerInvariant(), out _);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_loop is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        var o = _getOptions();
        _logger.LogInformation(
            "LockRegistry started (poll={Poll}s, post-run={PostRun}s, max-track={MaxHours}h).",
            o.LockPollSeconds, o.PostRunRetrySeconds, o.MaxLockTrackHours);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); }
        catch { /* shutting down */ }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { _logger.LogWarning(ex, "LockRegistry loop terminated unexpectedly."); }
        }

        _cts?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // Re-read LockPollSeconds each iteration so knob changes apply on
        // the next tick. PeriodicTimer can't be reconfigured after Create,
        // so we use a Task.Delay loop instead.
        while (!ct.IsCancellationRequested)
        {
            var period = TimeSpan.FromSeconds(Math.Max(1, _getOptions().LockPollSeconds));
            try
            {
                await Task.Delay(period, ct).ConfigureAwait(false);
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "LockRegistry tick threw."); }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (_entries.IsEmpty) return;
        var now = DateTimeOffset.Now;
        // Snapshot once per tick so all paths see consistent values; the
        // accessor itself is cheap but avoids a re-read mid-loop if the
        // host swaps options between iterations.
        var o = _getOptions();

        foreach (var key in _entries.Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(key, out var entry)) continue;

            if (entry.IsPostRun)
            {
                if (entry.ExpireAt is { } exp && now > exp)
                {
                    _entries.TryRemove(key, out _);
                    _logger.LogDebug("LockRegistry: post-run window expired for '{Path}'.", entry.Path);
                    continue;
                }

                if (!File.Exists(entry.Path))
                {
                    _logger.LogInformation("LockRegistry: post-run file gone -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                    _entries.TryRemove(key, out _);
                    await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
                    continue;
                }

                if (ControlFileGuard.IsUnlockedAndStable(entry.Path, o.FileStabilitySleepMs, ct))
                {
                    _logger.LogInformation("LockRegistry: post-run verified -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                    _entries.TryRemove(key, out _);
                    await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
                }

                continue;
            }

            // Standard locked entry.
            if ((now - entry.FirstSeen).TotalHours >= o.MaxLockTrackHours)
            {
                _entries.TryRemove(key, out _);
                _logger.LogInformation("LockRegistry: dropped stale locked entry '{Path}' (> {Hours}h).", entry.Path, o.MaxLockTrackHours);
                continue;
            }

            if (!File.Exists(entry.Path))
            {
                _logger.LogInformation("LockRegistry: locked file deleted -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                _entries.TryRemove(key, out _);
                await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
                continue;
            }

            if (ControlFileGuard.IsUnlockedAndStable(entry.Path, o.FileStabilitySleepMs, ct))
            {
                _logger.LogInformation("LockRegistry: lock cleared -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                _entries.TryRemove(key, out _);
                await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
            }
        }
    }
}
