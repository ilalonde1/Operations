#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Options;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// The real-time engine. Mirrors watcher.ps1 end-to-end:
//
//   1. Reads the 'Watcher' job row from FileSync.Jobs and honors Mode/Enabled
//      flips on a ConfigPollSeconds cadence (default 60s). Disabled = no
//      FileSystemWatcher attached, no events processed -- the loop just polls.
//   2. Owns one FileSystemWatcher rooted at WatcherOptions.WatchPath with
//      IncludeSubdirectories=true. Subscribes to Created/Changed/Deleted/
//      Renamed and an Error handler that restarts the watcher with backoff.
//   3. Drains events through a Channel into a worker that fans dispatches
//      out via Task.Run. The InFlightDebouncer prevents redundant work, so
//      parallel dispatches across DIFFERENT (op,bucket,root) keys are safe
//      and one slow Photos sync doesn't block other buckets.
//        - Filters ignored extensions / name prefixes / Newforma\email subtree.
//        - Detects control-file events (CLEAN on add, INIT on remove).
//        - Routes other events through SyncBucketRouter and only fires when
//          the event sits in the exact bucket root.
//        - For non-Deleted events, checks Test-FileUnlockedStable; if the
//          file is still being written, registers with LockRegistry and
//          lets the lock poller fire the sync once the writer releases.
//        - For Deleted, fires immediately. For successful syncs, registers
//          a PostRun window so a late-arriving save/close fires one more pass.
//   4. Self-heal: writes ServiceHeartbeat every HeartbeatMinutes, recycles
//      the FileSystemWatcher if it sees no real event for LivenessThresholdHours,
//      and increments the gen counter every cycle so the Command Center can
//      see the service stayed up vs. silently died. Shared IWatcherState
//      lets the Worker's faster heartbeat reuse the same gen value rather
//      than nulling it out four ticks out of five.
internal sealed class WatcherHostedService : BackgroundService
{
    private static readonly Regex IgnoredDirRegex = new(
        @"\\Newforma\\email($|\\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] IgnoredExtensions =
        { ".tmp", ".bak", ".log", ".rws", ".dat", ".dwgtmp" };

    private static readonly string[] IgnoredNameStarts =
        { "~$", "pulse-", "n4newforma-" };

    private static readonly string ServiceVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly JobRunnerRegistry _runners;
    private readonly IWatcherState _state;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<WatcherHostedService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly Channel<WatcherEvent> _channel = Channel.CreateUnbounded<WatcherEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly InFlightDebouncer _debouncer = new();
    private readonly SyncBucketRouter _router = new();

    // Tracks Task.Run dispatches so shutdown can wait for them to drain.
    private readonly object _dispatchLock = new();
    private readonly HashSet<Task> _activeDispatches = new();

    private FileSystemWatcher? _watcher;
    private LockRegistry? _lockRegistry;
    private WatcherOptions _options = WatcherOptions.FromKnobs(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
    private JobConfig? _config;
    private DateTimeOffset _configFetchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRealEventAt = DateTimeOffset.Now;
    private DateTimeOffset _lastDebouncerPruneAt = DateTimeOffset.Now;
    private int _watcherGen;

    public WatcherHostedService(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        JobRunnerRegistry runners,
        IWatcherState state,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<WatcherHostedService> logger,
        ILoggerFactory loggerFactory)
    {
        _store = store;
        _dispatcher = dispatcher;
        _runners = runners;
        _state = state;
        _fsOpts = fsOpts.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WatcherHostedService starting on {Host}.", Environment.MachineName);

        // Initial config + knobs.
        await RefreshConfigAsync(stoppingToken).ConfigureAwait(false);

        // Start the lock registry; its callback feeds back into our dispatch path.
        _lockRegistry = new LockRegistry(_options, _loggerFactory.CreateLogger<LockRegistry>(),
            (bucket, root, _, ct) => DispatchSyncAsync(bucket, root, "PostRun", "LockPoller", ct));
        await _lockRegistry.StartAsync(stoppingToken).ConfigureAwait(false);

        // Spin up the worker loop that drains the channel.
        var workerTask = Task.Run(() => RunWorkerAsync(stoppingToken), stoppingToken);

        try
        {
            await RunSupervisorAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        finally
        {
            _channel.Writer.TryComplete();
            try { await workerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { _logger.LogWarning(ex, "Watcher worker terminated unexpectedly."); }

            await DrainActiveDispatchesAsync().ConfigureAwait(false);

            DisposeWatcher();
            _state.SetDetached();
            if (_lockRegistry is not null)
                await _lockRegistry.DisposeAsync().ConfigureAwait(false);

            _logger.LogInformation("WatcherHostedService stopped (gen={Gen}).", _watcherGen);
        }
    }

    // Top-level supervisor: handles config polling, heartbeat, liveness check.
    private async Task RunSupervisorAsync(CancellationToken stoppingToken)
    {
        var heartbeatPeriod = TimeSpan.FromMinutes(Math.Max(1, _options.HeartbeatMinutes));
        var lastHeartbeat = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshConfigAsync(stoppingToken).ConfigureAwait(false);

                var enabled = _config?.Enabled == true;
                if (enabled && _watcher is null)
                {
                    StartWatcher();
                }
                else if (!enabled && _watcher is not null)
                {
                    _logger.LogInformation("Watcher Enabled=false; tearing down FileSystemWatcher.");
                    DisposeWatcher();
                    _state.SetDetached();
                }

                // Heartbeat
                var now = DateTimeOffset.Now;
                if (now - lastHeartbeat >= heartbeatPeriod)
                {
                    await WriteHeartbeatAsync(stoppingToken).ConfigureAwait(false);
                    lastHeartbeat = now;
                }

                // Liveness: cycle the watcher if no real events for a long time.
                if (_watcher is not null
                    && (now - _lastRealEventAt).TotalHours >= _options.LivenessThresholdHours
                    && Directory.Exists(_options.WatchPath))
                {
                    _logger.LogWarning(
                        "Liveness: no events for {Hours:0.0}h; recycling watcher.",
                        (now - _lastRealEventAt).TotalHours);
                    await RestartWatcherWithBackoffAsync(stoppingToken).ConfigureAwait(false);
                    _lastRealEventAt = DateTimeOffset.Now;
                }

                // Periodic prune of the debouncer's "recent" map. Cutoff is
                // 10x DebounceSeconds: long enough that no live debounce
                // window can be affected, short enough that long-tail keys
                // don't accumulate forever.
                if ((now - _lastDebouncerPruneAt).TotalMinutes >= 5)
                {
                    var cutoff = TimeSpan.FromSeconds(Math.Max(60, _options.DebounceSeconds * 10));
                    var dropped = _debouncer.PruneRecentOlderThan(cutoff);
                    if (dropped > 0)
                        _logger.LogDebug("Debouncer prune: removed {Count} stale recent entries (cutoff {Cutoff}s, remaining={Remaining}).",
                            dropped, cutoff.TotalSeconds, _debouncer.RecentCount);
                    _lastDebouncerPruneAt = now;
                }

                heartbeatPeriod = TimeSpan.FromMinutes(Math.Max(1, _options.HeartbeatMinutes));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watcher supervisor tick threw.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.ConfigPollSeconds), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
        }
    }

    private async Task RefreshConfigAsync(CancellationToken ct)
    {
        // Refresh at most once per ConfigPollSeconds even if called more often.
        var now = DateTimeOffset.Now;
        if ((now - _configFetchedAt).TotalSeconds < _options.ConfigPollSeconds && _config is not null) return;

        try
        {
            _config = await _store.GetJobAsync(WatcherSyncRunner.Name, ct).ConfigureAwait(false);
            var knobs = await _store.GetKnobsAsync(WatcherSyncRunner.Name, ct).ConfigureAwait(false);
            var newOptions = WatcherOptions.FromKnobs(knobs);
            if (!ReferenceEquals(_options, newOptions))
            {
                if (_options.WatchPath != newOptions.WatchPath && _watcher is not null)
                {
                    _logger.LogInformation("WatchPath changed '{Old}' -> '{New}'; recycling watcher.", _options.WatchPath, newOptions.WatchPath);
                    DisposeWatcher();
                }

                _options = newOptions;
            }

            _configFetchedAt = now;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watcher config refresh failed; using last-known values.");
        }
    }

    private void StartWatcher()
    {
        if (!Directory.Exists(_options.WatchPath))
        {
            _logger.LogError("Watcher cannot start: WatchPath '{Path}' not reachable.", _options.WatchPath);
            return;
        }

        DisposeWatcher();
        _watcherGen++;

        var w = new FileSystemWatcher(_options.WatchPath)
        {
            Filter = "*.*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            InternalBufferSize = 65536,
            EnableRaisingEvents = false,
        };
        w.Created += OnCreated;
        w.Changed += OnChanged;
        w.Deleted += OnDeleted;
        w.Renamed += OnRenamed;
        w.Error += OnError;
        w.EnableRaisingEvents = true;
        _watcher = w;
        _state.SetAttached(_watcherGen);

        _logger.LogInformation("Watcher (gen {Gen}) started for '{Path}'.", _watcherGen, _options.WatchPath);
    }

    private void DisposeWatcher()
    {
        var w = Interlocked.Exchange(ref _watcher, null);
        if (w is null) return;
        try { w.EnableRaisingEvents = false; } catch { /* shutting down */ }
        w.Created -= OnCreated;
        w.Changed -= OnChanged;
        w.Deleted -= OnDeleted;
        w.Renamed -= OnRenamed;
        w.Error -= OnError;
        try { w.Dispose(); } catch { /* shutting down */ }
    }

    private async Task RestartWatcherWithBackoffAsync(CancellationToken ct)
    {
        _logger.LogInformation("Restarting watcher; verifying path availability: {Path}", _options.WatchPath);
        var elapsed = 0;
        while (!Directory.Exists(_options.WatchPath))
        {
            if (elapsed >= _options.RestartBackoffMaxSeconds)
            {
                _logger.LogError("Watcher restart aborted: '{Path}' still unavailable after {Max}s.", _options.WatchPath, _options.RestartBackoffMaxSeconds);
                return;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            elapsed += 5;
        }

        StartWatcher();
    }

    // ---- Event handlers (run on FSW threadpool callbacks) ----

    private void OnCreated(object sender, FileSystemEventArgs e) => Enqueue(WatcherEvent.From(e, WatcherChange.Created));

    private void OnChanged(object sender, FileSystemEventArgs e) => Enqueue(WatcherEvent.From(e, WatcherChange.Changed));

    private void OnDeleted(object sender, FileSystemEventArgs e) => Enqueue(WatcherEvent.From(e, WatcherChange.Deleted));

    private void OnRenamed(object sender, RenamedEventArgs e) => Enqueue(WatcherEvent.From(e));

    private void OnError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _logger.LogError(ex, "FileSystemWatcher Error event; will recycle.");
        Enqueue(WatcherEvent.MakeRecycle());
    }

    private void Enqueue(WatcherEvent evt)
    {
        if (!_channel.Writer.TryWrite(evt))
            _logger.LogWarning("Watcher channel write rejected for '{Path}'.", evt.FullPath);
    }

    // ---- Worker loop: classify events serially, fan dispatches out to Task.Run ----

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                if (evt.Recycle)
                {
                    await RestartWatcherWithBackoffAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                _lastRealEventAt = DateTimeOffset.Now;
                _state.NoteEvent();
                await ProcessEventAsync(evt, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker loop threw on event '{Path}' ({Change}).", evt.FullPath, evt.Change);
            }
        }
    }

    private async Task ProcessEventAsync(WatcherEvent evt, CancellationToken ct)
    {
        var path = evt.FullPath;
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir)) return;
        if (IgnoredDirRegex.IsMatch(dir)) return;

        var fileName = Path.GetFileName(path);

        // ---- Control-file events ----
        // Match PS1 watcher.ps1 §326-353 exactly. PS1 routes by NEW-name only:
        //   Deleted-and-name==control               -> INIT (project dir = parent)
        //   Renamed-and-NEW-name==control           -> INIT (project dir = OLD parent)
        //   Created/Changed-and-name==control       -> CLEAN (project dir = parent)
        // The Renamed branch is a PS1 quirk -- a "rename TO control file" is
        // semantically an ADD, but PS1 treats it as a removal at the OLD
        // path's parent. We mirror that for parity rather than silently
        // diverging. Renamed-AWAY-from-control isn't observable from new-name
        // alone, so neither implementation handles it.
        if (string.Equals(fileName, _options.ControlFileName, StringComparison.OrdinalIgnoreCase))
        {
            if (evt.Change == WatcherChange.Deleted || evt.Change == WatcherChange.Renamed)
            {
                var projectDir = (evt.Change == WatcherChange.Renamed && evt.OldFullPath is not null)
                    ? Path.GetDirectoryName(evt.OldFullPath)
                    : dir;
                if (IsUnderWatchPath(projectDir))
                {
                    _logger.LogInformation("Control-file Deleted/Renamed-to at '{Project}' -> firing init.", projectDir);
                    DispatchInit(projectDir!, ct);
                }

                return;
            }

            // Created / Changed -> CLEAN this project on SP.
            if (IsUnderWatchPath(dir))
            {
                _logger.LogInformation("Control-file APPEARED at '{Project}' -> firing clean.", dir);
                DispatchClean(dir!, ct);
            }

            return;
        }

        // ---- Standard file events ----
        var resolved = _router.Resolve(dir);
        if (resolved is null) return;

        // Only act when the event is in the exact bucket root, not a subfolder.
        if (!SyncBucketRouter.IsExactRoot(dir, resolved.Root)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (IgnoredExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return;
        foreach (var pfx in IgnoredNameStarts)
        {
            if (fileName.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) return;
        }

        if (!resolved.Bucket.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return;

        // Walk-up control-file guard.
        if (ControlFileGuard.ShouldIgnore(dir, _options.WatchPath, _options.ControlFileName))
        {
            _logger.LogDebug("Control-file ancestor present; skipping event for '{Path}'.", path);
            return;
        }

        if (evt.Change == WatcherChange.Deleted)
        {
            _logger.LogInformation("Delete event -> immediate sync for '{Root}' (file: '{Path}').", resolved.Root, path);
            DispatchSync(resolved.Bucket, resolved.Root, "Watcher", "FSW.Deleted", ct);
            return;
        }

        // Created / Changed / Renamed-to: only act if the file is unlocked + stable now.
        // Otherwise register with the lock registry and let the poller re-evaluate.
        if (!File.Exists(path)) return;

        if (ControlFileGuard.IsUnlockedAndStable(path, _options.FileStabilitySleepMs, ct))
        {
            _lockRegistry?.Drop(path);
            _logger.LogInformation("Unlocked+stable -> sync '{Root}' (file: '{Path}').", resolved.Root, path);
            // Register the post-run window only AFTER the dispatch finishes.
            // If we registered eagerly, the lock poller could fire its retry
            // while the original sync is still in-flight; the InFlightDebouncer
            // would reject the retry as in-flight AND the registry entry would
            // already be removed -- losing the post-run safety pass entirely.
            DispatchSyncThenPostRun(resolved.Bucket, resolved.Root, $"FSW.{evt.Change}", path, ct);
        }
        else
        {
            _lockRegistry?.RegisterLocked(path, resolved.Bucket, resolved.Root);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void DispatchSyncThenPostRun(SyncBucket bucket, string root, string triggeredBy, string filePath, CancellationToken ct)
    {
        var dispatch = DispatchSyncAsync(bucket, root, "Watcher", triggeredBy, ct);
        _ = dispatch.ContinueWith(t =>
        {
            // Always register the post-run window -- even on failure -- so a late
            // save/close on this exact file gets one more chance through the poller.
            _lockRegistry?.RegisterPostRun(filePath, bucket, root);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private bool IsUnderWatchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.StartsWith(_options.WatchPath, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Dispatch helpers ---------------------------------------------------
    // The synchronous Dispatch* facade gates on the InFlightDebouncer and
    // (when admitted) launches a Task.Run that owns the dispatch lifetime.
    // The supervisor awaits these tasks on shutdown via DrainActiveDispatchesAsync.
    // The async DispatchSyncAsync overload is used by the LockRegistry callback,
    // which is itself running on a poller thread we don't want to block.

    private void DispatchSync(SyncBucket bucket, string root, string triggerSource, string triggeredBy, CancellationToken ct)
        => _ = DispatchSyncAsync(bucket, root, triggerSource, triggeredBy, ct);

    private void DispatchInit(string projectDir, CancellationToken ct)
    {
        var key = $"init|{projectDir.ToLowerInvariant()}";
        var debounce = TimeSpan.FromSeconds(_options.DebounceSeconds);
        if (!_debouncer.TryStart(key, debounce, force: true, out var skipReason))
        {
            _logger.LogDebug("Skip init '{Key}': {Reason}", key, skipReason);
            return;
        }

        TrackDispatch(RunDispatchAsync(key, async innerCt =>
        {
            if (_config is null) return;
            await _dispatcher.DispatchAsync(
                _config,
                triggerSource: "Watcher",
                triggeredBy: "FSW.ControlRemoved",
                WatcherArgs.EncodeInit(projectDir),
                triggerId: null,
                innerCt).ConfigureAwait(false);
        }, ct));
    }

    private void DispatchClean(string projectDir, CancellationToken ct)
    {
        var key = $"clean|{projectDir.ToLowerInvariant()}";
        var debounce = TimeSpan.FromSeconds(_options.DebounceSeconds);
        if (!_debouncer.TryStart(key, debounce, force: true, out var skipReason))
        {
            _logger.LogDebug("Skip clean '{Key}': {Reason}", key, skipReason);
            return;
        }

        TrackDispatch(RunDispatchAsync(key, async innerCt =>
        {
            if (_config is null) return;
            await _dispatcher.DispatchAsync(
                _config,
                triggerSource: "Watcher",
                triggeredBy: "FSW.ControlAdded",
                WatcherArgs.EncodeClean(projectDir),
                triggerId: null,
                innerCt).ConfigureAwait(false);
        }, ct));
    }

    private Task DispatchSyncAsync(SyncBucket bucket, string root, string triggerSource, string triggeredBy, CancellationToken ct)
    {
        var key = $"sync|{bucket.Name}|{root.ToLowerInvariant()}";
        var debounce = TimeSpan.FromSeconds(_options.DebounceSeconds);
        var force = string.Equals(triggerSource, "PostRun", StringComparison.OrdinalIgnoreCase);
        if (!_debouncer.TryStart(key, debounce, force, out var skipReason))
        {
            _logger.LogDebug("Skip sync '{Key}': {Reason}", key, skipReason);
            return Task.CompletedTask;
        }

        var t = RunDispatchAsync(key, async innerCt =>
        {
            if (_config is null)
            {
                _logger.LogWarning("Watcher config not loaded; skipping sync dispatch for '{Root}'.", root);
                return;
            }

            await _dispatcher.DispatchAsync(
                _config,
                triggerSource,
                triggeredBy,
                WatcherArgs.EncodeSync(bucket, root),
                triggerId: null,
                innerCt).ConfigureAwait(false);
        }, ct);

        TrackDispatch(t);
        return t;
    }

    private void TrackDispatch(Task t)
    {
        lock (_dispatchLock) _activeDispatches.Add(t);
        t.ContinueWith(done =>
        {
            lock (_dispatchLock) _activeDispatches.Remove(done);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task DrainActiveDispatchesAsync()
    {
        Task[] snapshot;
        lock (_dispatchLock) snapshot = _activeDispatches.ToArray();
        if (snapshot.Length == 0) return;
        _logger.LogInformation("Draining {Count} in-flight watcher dispatch(es).", snapshot.Length);
        try { await Task.WhenAll(snapshot).ConfigureAwait(false); }
        catch { /* individual failures already logged inside RunDispatchAsync */ }
    }

    private async Task RunDispatchAsync(string key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        var maxSync = TimeSpan.FromMinutes(Math.Max(1, _options.MaxSyncMinutes));
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        localCts.CancelAfter(maxSync);
        // Hand off to the threadpool so the worker loop can advance to the next
        // event without waiting for Graph round-trips on this dispatch.
        await Task.Run(async () =>
        {
            try
            {
                await action(localCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (localCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogError("Dispatch '{Key}' exceeded {Max}; cancelled.", key, maxSync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch '{Key}' threw.", key);
            }
            finally
            {
                _debouncer.Release(key);
            }
        }, localCts.Token).ConfigureAwait(false);
    }

    private async Task WriteHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            await _store.WriteHeartbeatAsync(
                hostName: Environment.MachineName,
                startedAt: DateTimeOffset.Now,
                mode: _config?.Mode ?? _fsOpts.Mode.ToString(),
                version: ServiceVersion,
                jobsRegistered: _runners.RegisteredCount,
                watcherGen: _watcherGen,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watcher heartbeat write failed.");
        }
    }

    private enum WatcherChange { Created, Changed, Deleted, Renamed }

    private sealed record WatcherEvent(
        WatcherChange Change,
        string FullPath,
        string? OldFullPath,
        string? OldFileName,
        bool Recycle)
    {
        public static WatcherEvent From(FileSystemEventArgs e, WatcherChange change)
            => new(change, e.FullPath, null, null, false);

        public static WatcherEvent From(RenamedEventArgs e)
            => new(WatcherChange.Renamed, e.FullPath, e.OldFullPath, Path.GetFileName(e.OldFullPath), false);

        public static WatcherEvent MakeRecycle()
            => new(WatcherChange.Changed, string.Empty, null, null, true);
    }
}
