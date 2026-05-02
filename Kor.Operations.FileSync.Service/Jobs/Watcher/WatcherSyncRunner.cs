#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Options;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// IJobRunner facade for Watcher work. Every Watcher dispatch (file event,
// post-run verification, control-file flip, manual fire from Command
// Center) lands here via JobDispatcher, which means every sync attempt
// shows up in FileSync.JobRuns with proper Mode/TriggerSource and gets
// alert-on-failure for free.
//
// Args are decoded by WatcherArgs.Parse:
//   op=sync   bucket=<X> root=<UNC>     -> BucketSyncOp
//   op=clean  projectDir=<UNC>          -> ProjectCleanOp
//   op=init   projectDir=<UNC>          -> all 4 BucketSyncOp passes (control-file removal)
internal sealed class WatcherSyncRunner : IJobRunner
{
    public const string Name = "Watcher";

    private readonly IControlPlaneStore _store;
    private readonly IGraphFacade _facade;
    private readonly GraphServiceClient _graph;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<WatcherSyncRunner> _logger;

    public WatcherSyncRunner(
        IControlPlaneStore store,
        IGraphFacade facade,
        GraphServiceClient graph,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<WatcherSyncRunner> logger)
    {
        _store = store;
        _facade = facade;
        _graph = graph;
        _fsOpts = fsOpts.Value;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var driveId = _fsOpts.DriveId;
        if (string.IsNullOrWhiteSpace(driveId))
            return new JobRunResult(false, "FileSyncOptions.DriveId is empty.");

        WatcherArgs parsed;
        try { parsed = WatcherArgs.Parse(args); }
        catch (Exception ex) { return new JobRunResult(false, $"Bad args: {ex.Message}"); }

        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = WatcherOptions.FromKnobs(knobs);
        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);

        switch (parsed.Op)
        {
            case WatcherOp.Sync:
            {
                if (parsed.Bucket is null || string.IsNullOrWhiteSpace(parsed.Root))
                    return new JobRunResult(false, "Sync requires bucket and root.");
                if (!TryValidateSyncRoot(parsed.Root!, parsed.Bucket, opts, out var canonRoot, out var reason))
                    return new JobRunResult(false, $"Refused sync: {reason}");

                var op = new BucketSyncOp(_facade, opts, driveId, _logger);
                var r = await op.RunAsync(parsed.Bucket, canonRoot, isShadow, ct).ConfigureAwait(false);
                var verb = isShadow ? "Would sync" : "Synced";
                return new JobRunResult(
                    Success: r.Failed == 0,
                    Summary: $"{verb} bucket={parsed.Bucket.Name} root='{canonRoot}' sp='{r.SharePointFolder}' " +
                             $"local={r.LocalCount} remote={r.RemoteCount} uploaded={r.Uploaded} skipped={r.SkippedSame} deleted={r.Deleted} failed={r.Failed}");
            }

            case WatcherOp.Init:
            {
                if (string.IsNullOrWhiteSpace(parsed.ProjectDir))
                    return new JobRunResult(false, "Init requires projectDir.");
                if (!TryValidateProjectDir(parsed.ProjectDir!, opts, out var canonProjectDir, out var reason))
                    return new JobRunResult(false, $"Refused init: {reason}");
                var router = new SyncBucketRouter();
                var op = new BucketSyncOp(_facade, opts, driveId, _logger);
                int totalUp = 0, totalSkip = 0, totalDel = 0, totalFail = 0;
                foreach (var resolved in router.ResolveAllForProject(canonProjectDir))
                {
                    if (!Directory.Exists(resolved.Root))
                    {
                        if (!isShadow)
                        {
                            try { Directory.CreateDirectory(resolved.Root); }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Init: could not create '{Root}'; skipping bucket {Bucket}.", resolved.Root, resolved.Bucket.Name);
                                continue;
                            }
                        }
                        else { continue; }
                    }

                    try
                    {
                        var r = await op.RunAsync(resolved.Bucket, resolved.Root, isShadow, ct).ConfigureAwait(false);
                        totalUp += r.Uploaded; totalSkip += r.SkippedSame; totalDel += r.Deleted; totalFail += r.Failed;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Init bucket {Bucket} failed for '{Root}'.", resolved.Bucket.Name, resolved.Root);
                        totalFail++;
                    }
                }

                var verb2 = isShadow ? "Would init" : "Init";
                return new JobRunResult(
                    Success: totalFail == 0,
                    Summary: $"{verb2} project='{canonProjectDir}' uploaded={totalUp} skipped={totalSkip} deleted={totalDel} failed={totalFail}");
            }

            case WatcherOp.Clean:
            {
                if (string.IsNullOrWhiteSpace(parsed.ProjectDir))
                    return new JobRunResult(false, "Clean requires projectDir.");
                if (!TryValidateProjectDir(parsed.ProjectDir!, opts, out var canonProjectDir, out var reason))
                    return new JobRunResult(false, $"Refused clean: {reason}");
                var clean = new ProjectCleanOp(_graph, _facade, opts, driveId, _logger);
                var r = await clean.RunAsync(canonProjectDir, isShadow, ct).ConfigureAwait(false);
                var verb3 = isShadow ? "Would move" : (r.Moved ? "Moved" : "Did not move");
                return new JobRunResult(
                    Success: true,
                    Summary: $"{verb3} '{r.TargetName}' -> '{opts.HoldingFolderName}'" +
                             (r.RenamedTo is null ? string.Empty : $" as '{r.RenamedTo}'") +
                             (r.Note is null ? string.Empty : $" ({r.Note})"));
            }

            default:
                return new JobRunResult(false, $"Unknown op: {parsed.Op}");
        }
    }

    // --- Trust boundary on JobTriggers.Args -------------------------------
    // Watcher-driven dispatches always supply paths the FileSystemWatcher
    // observed under WatchPath. Manual fires from Command Center / direct
    // INSERT into FileSync.JobTriggers can supply anything, including paths
    // outside the watch tree. Without these checks, op=sync against an
    // empty arbitrary local folder would derive a SharePoint project name
    // and DELETE every remote file not present locally. Refuse loudly.

    private static bool TryValidateSyncRoot(string root, SyncBucket bucket, WatcherOptions opts, out string canonical, out string reason)
    {
        canonical = string.Empty;
        if (!TryCanonicalizeUnderWatchPath(root, opts.WatchPath, out canonical, out reason))
            return false;

        // Sync is per-bucket: the root must end with the bucket's LocalSubpath
        // (case-insensitive), so we never accidentally treat a random folder
        // as a bucket root and prune SharePoint against it.
        var subpath = bucket.LocalSubpath.TrimEnd('\\', '/').ToLowerInvariant();
        var trimmed = canonical.TrimEnd('\\', '/').ToLowerInvariant();
        if (!trimmed.EndsWith("\\" + subpath, StringComparison.Ordinal) && trimmed != subpath)
        {
            reason = $"root '{root}' does not end with bucket subpath '{bucket.LocalSubpath}'";
            return false;
        }

        return true;
    }

    private static bool TryValidateProjectDir(string projectDir, WatcherOptions opts, out string canonical, out string reason)
        => TryCanonicalizeUnderWatchPath(projectDir, opts.WatchPath, out canonical, out reason);

    private static bool TryCanonicalizeUnderWatchPath(string path, string watchPath, out string canonical, out string reason)
    {
        canonical = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "path is empty";
            return false;
        }

        string fullPath, fullWatch;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
            fullWatch = Path.GetFullPath(watchPath).TrimEnd('\\', '/');
        }
        catch (Exception ex)
        {
            reason = $"path canonicalization threw ({ex.GetType().Name})";
            return false;
        }

        // Allow exact-match (the watch root itself) OR strict containment.
        var isUnder = string.Equals(fullPath, fullWatch, StringComparison.OrdinalIgnoreCase)
                      || fullPath.StartsWith(fullWatch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isUnder)
        {
            reason = $"'{fullPath}' is not under WatchPath '{fullWatch}'";
            return false;
        }

        canonical = fullPath;
        return true;
    }
}
