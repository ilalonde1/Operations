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
    private readonly WatcherOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LockRegistry(
        WatcherOptions options,
        ILogger logger,
        Func<SyncBucket, string, string, CancellationToken, Task> trigger)
    {
        _options = options;
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
        var expire = DateTimeOffset.Now.AddSeconds(_options.PostRunRetrySeconds);
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
        _logger.LogInformation(
            "LockRegistry started (poll={Poll}s, post-run={PostRun}s, max-track={MaxHours}h).",
            _options.LockPollSeconds, _options.PostRunRetrySeconds, _options.MaxLockTrackHours);
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
        var period = TimeSpan.FromSeconds(_options.LockPollSeconds);
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try { await TickAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "LockRegistry tick threw."); }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (_entries.IsEmpty) return;
        var now = DateTimeOffset.Now;

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

                if (ControlFileGuard.IsUnlockedAndStable(entry.Path, _options.FileStabilitySleepMs, ct))
                {
                    _logger.LogInformation("LockRegistry: post-run verified -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                    _entries.TryRemove(key, out _);
                    await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
                }

                continue;
            }

            // Standard locked entry.
            if ((now - entry.FirstSeen).TotalHours >= _options.MaxLockTrackHours)
            {
                _entries.TryRemove(key, out _);
                _logger.LogInformation("LockRegistry: dropped stale locked entry '{Path}' (> {Hours}h).", entry.Path, _options.MaxLockTrackHours);
                continue;
            }

            if (!File.Exists(entry.Path))
            {
                _logger.LogInformation("LockRegistry: locked file deleted -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                _entries.TryRemove(key, out _);
                await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
                continue;
            }

            if (ControlFileGuard.IsUnlockedAndStable(entry.Path, _options.FileStabilitySleepMs, ct))
            {
                _logger.LogInformation("LockRegistry: lock cleared -> firing sync for '{Root}' (file: '{Path}').", entry.Root, entry.Path);
                _entries.TryRemove(key, out _);
                await _trigger(entry.Bucket, entry.Root, entry.Path, ct).ConfigureAwait(false);
            }
        }
    }
}
