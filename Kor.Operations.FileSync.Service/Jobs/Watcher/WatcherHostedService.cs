#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Kor.Operations.FileSync.Service.ControlPlane;
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
//   3. Drains events through a Channel into a single worker loop so the FSW
//      thread never blocks on async work. The worker:
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
//      see the service stayed up vs. silently died.
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
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<WatcherHostedService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly Channel<WatcherEvent> _channel = Channel.CreateUnbounded<WatcherEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly InFlightDebouncer _debouncer = new();
    private readonly SyncBucketRouter _router = new();

    private FileSystemWatcher? _watcher;
    private LockRegistry? _lockRegistry;
    private WatcherOptions _options = WatcherOptions.FromKnobs(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
    private JobConfig? _config;
    private DateTimeOffset _configFetchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRealEventAt = DateTimeOffset.Now;
    private int _watcherGen;

    public WatcherHostedService(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<WatcherHostedService> logger,
        ILoggerFactory loggerFactory)
    {
        _store = store;
        _dispatcher = dispatcher;
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
            (bucket, root, file, ct) => DispatchSyncAsync(bucket, root, "PostRun", "LockPoller", ct));
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

            DisposeWatcher();
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

    // ---- Worker loop: serialize event processing so the FSW thread stays unblocked ----

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
        if (string.Equals(fileName, _options.ControlFileName, StringComparison.OrdinalIgnoreCase))
        {
            // Removal (or Renamed-from) -> initial sync of the project.
            if (evt.Change == WatcherChange.Deleted ||
                (evt.Change == WatcherChange.Renamed && IsControlFileRemoval(evt)))
            {
                var projectDir = evt.Change == WatcherChange.Renamed && evt.OldFullPath is not null
                    ? Path.GetDirectoryName(evt.OldFullPath)
                    : dir;
                if (IsUnderWatchPath(projectDir))
                {
                    _logger.LogInformation("Control-file REMOVED at '{Project}' -> firing init.", projectDir);
                    await DispatchInitAsync(projectDir!, ct).ConfigureAwait(false);
                }

                return;
            }

            // Add/Change/Renamed-to -> CLEAN.
            var cleanDir = evt.Change == WatcherChange.Renamed
                ? Path.GetDirectoryName(evt.FullPath)
                : dir;
            if (IsUnderWatchPath(cleanDir))
            {
                _logger.LogInformation("Control-file APPEARED at '{Project}' -> firing clean.", cleanDir);
                await DispatchCleanAsync(cleanDir!, ct).ConfigureAwait(false);
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
            await DispatchSyncAsync(resolved.Bucket, resolved.Root, "Watcher", "FSW.Deleted", ct).ConfigureAwait(false);
            return;
        }

        // Created / Changed / Renamed-to: only act if the file is unlocked + stable now.
        // Otherwise register with the lock registry and let the poller re-evaluate.
        if (!File.Exists(path)) return;

        if (ControlFileGuard.IsUnlockedAndStable(path, _options.FileStabilitySleepMs, ct))
        {
            _lockRegistry?.Drop(path);
            _logger.LogInformation("Unlocked+stable -> sync '{Root}' (file: '{Path}').", resolved.Root, path);
            await DispatchSyncAsync(resolved.Bucket, resolved.Root, "Watcher", $"FSW.{evt.Change}", ct).ConfigureAwait(false);
            // Post-run window catches a late save/close on the same file.
            _lockRegistry?.RegisterPostRun(path, resolved.Bucket, resolved.Root);
        }
        else
        {
            _lockRegistry?.RegisterLocked(path, resolved.Bucket, resolved.Root);
        }
    }

    private bool IsUnderWatchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.StartsWith(_options.WatchPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlFileRemoval(WatcherEvent evt)
    {
        if (evt.OldFullPath is null) return false;
        // PS1 treats a rename-AWAY-from the control file name as a removal.
        return string.Equals(Path.GetFileName(evt.OldFullPath), evt.OldFileName, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(Path.GetFileName(evt.FullPath), Path.GetFileName(evt.OldFullPath), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Dispatch helpers (route every Watcher invocation through JobDispatcher) ----

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

        return RunDispatchAsync(key, async innerCt =>
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
    }

    private Task DispatchInitAsync(string projectDir, CancellationToken ct)
    {
        var key = $"init|{projectDir.ToLowerInvariant()}";
        var debounce = TimeSpan.FromSeconds(_options.DebounceSeconds);
        if (!_debouncer.TryStart(key, debounce, force: true, out var skipReason))
        {
            _logger.LogDebug("Skip init '{Key}': {Reason}", key, skipReason);
            return Task.CompletedTask;
        }

        return RunDispatchAsync(key, async innerCt =>
        {
            if (_config is null) return;
            await _dispatcher.DispatchAsync(
                _config,
                triggerSource: "Watcher",
                triggeredBy: "FSW.ControlRemoved",
                WatcherArgs.EncodeInit(projectDir),
                triggerId: null,
                innerCt).ConfigureAwait(false);
        }, ct);
    }

    private Task DispatchCleanAsync(string projectDir, CancellationToken ct)
    {
        var key = $"clean|{projectDir.ToLowerInvariant()}";
        var debounce = TimeSpan.FromSeconds(_options.DebounceSeconds);
        if (!_debouncer.TryStart(key, debounce, force: true, out var skipReason))
        {
            _logger.LogDebug("Skip clean '{Key}': {Reason}", key, skipReason);
            return Task.CompletedTask;
        }

        return RunDispatchAsync(key, async innerCt =>
        {
            if (_config is null) return;
            await _dispatcher.DispatchAsync(
                _config,
                triggerSource: "Watcher",
                triggeredBy: "FSW.ControlAdded",
                WatcherArgs.EncodeClean(projectDir),
                triggerId: null,
                innerCt).ConfigureAwait(false);
        }, ct);
    }

    private async Task RunDispatchAsync(string key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        var maxSync = TimeSpan.FromMinutes(Math.Max(1, _options.MaxSyncMinutes));
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        localCts.CancelAfter(maxSync);
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
                jobsRegistered: 0,
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
