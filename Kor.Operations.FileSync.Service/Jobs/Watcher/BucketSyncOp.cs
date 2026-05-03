#nullable enable
using System.Globalization;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// One-pass folder sync: enumerate the local bucket folder, compare with
// the SharePoint subfolder, upload missing/changed files, and prune SP
// items that no longer exist locally.
//
// Behavior parity vs PS1 SINGLE_SYNC_<bucket>.ps1:
//   * Stickfile uploads to the SP project ROOT (no subfolder), and also
//     ensures <project>/Reports exists -- matches PS1 §296-298 of
//     SINGLE_SYNC_Stickfile.ps1. Stickfile also uses a stricter compare:
//     upload only when local is meaningfully NEWER than SP (15s skew),
//     not just size-mismatched (Should-Upload §158-176).
//   * SSI/RFI/Photos compare on size only (PS1 size-mismatch rule).
//   * Upload path: simple PUT for files <= SimpleVsChunkedThresholdBytes,
//     chunked upload session otherwise.
//   * Delete pass filters SP listing to the bucket's extension set so
//     metadata files / unexpected types in the SP folder are left alone.
//
// Returns counts so the WatcherSyncRunner can roll them into the JobRuns
// summary.
internal sealed class BucketSyncOp
{
    public sealed record Result(
        int Uploaded, int SkippedSame, int Deleted, int Failed, int Deferred,
        int LocalCount, int RemoteCount, string SharePointFolder);

    private readonly IGraphFacade _facade;
    private readonly ILogger _logger;
    private readonly WatcherOptions _options;
    private readonly string _driveId;

    public BucketSyncOp(IGraphFacade facade, WatcherOptions options, string driveId, ILogger logger)
    {
        _facade = facade;
        _options = options;
        _driveId = driveId;
        _logger = logger;
    }

    public async Task<Result> RunAsync(SyncBucket bucket, string root, bool isShadow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException($"Bucket root not reachable: '{root}'");

        var (projectName, spProjectFolder, spTargetFolder) = ResolveSharePointPaths(bucket, root);

        _logger.LogInformation(
            "BucketSyncOp begin: bucket={Bucket} project='{Project}' root='{Root}' sp='{Sp}' mode={Mode}",
            bucket.Name, projectName, root, spTargetFolder, isShadow ? "Shadow" : "Live");

        // Local files matching the bucket's extension set.
        var local = new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => bucket.Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Remote: ensure folder, then list. Stickfile also ensures Reports/.
        string targetFolderId;
        if (isShadow)
        {
            // Shadow: probe existence only (TryGet); never auto-create the path.
            targetFolderId = await TryResolveExistingFolderIdAsync(spTargetFolder, ct).ConfigureAwait(false) ?? string.Empty;
        }
        else
        {
            targetFolderId = await _facade.EnsureFolderAsync(spTargetFolder, ct).ConfigureAwait(false);
            if (string.Equals(bucket.Name, "Stickfile", StringComparison.OrdinalIgnoreCase))
            {
                // PS1 ensures <project>/Reports/ exists alongside the project root.
                _ = await _facade.EnsureFolderAsync($"{spProjectFolder}/Reports", ct).ConfigureAwait(false);
            }
        }

        var remote = new List<DriveItem>();
        if (!string.IsNullOrEmpty(targetFolderId))
        {
            await foreach (var child in _facade.ListChildrenAsync(_driveId, targetFolderId, ct).ConfigureAwait(false))
            {
                if (child.File is null || string.IsNullOrWhiteSpace(child.Name)) continue;
                var ext = Path.GetExtension(child.Name);
                if (!bucket.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                remote.Add(child);
            }
        }

        var byName = remote.ToDictionary(r => r.Name!, r => r, StringComparer.OrdinalIgnoreCase);
        int uploaded = 0, skipped = 0, deleted = 0, failed = 0, deferred = 0;

        // ---- Upload phase ----
        var skewTolerance = TimeSpan.FromSeconds(15);
        foreach (var f in local)
        {
            ct.ThrowIfCancellationRequested();
            byName.TryGetValue(f.Name, out var sp);

            var needsUpload = ShouldUpload(bucket, f, sp, skewTolerance);
            if (!needsUpload)
            {
                skipped++;
                continue;
            }

            if (isShadow)
            {
                _logger.LogInformation("WOULD UPLOAD '{Name}' -> '{Sp}'", f.Name, spTargetFolder);
                uploaded++;
                continue;
            }

            try
            {
                if (f.Length <= _options.SimpleVsChunkedThresholdBytes)
                    await _facade.UploadSimpleAsync(_driveId, targetFolderId, f.Name, f.FullName, ct).ConfigureAwait(false);
                else
                    await _facade.UploadToFolderAsync(targetFolderId, f.Name, f.FullName, progress: null, chunkSizeBytes: _options.ImageUploadChunkBytes, ct).ConfigureAwait(false);
                _logger.LogInformation("Uploaded '{Name}' -> '{Sp}' ({Bytes:n0} bytes)", f.Name, spTargetFolder, f.Length);
                uploaded++;
            }
            catch (IOException io) when (IsFileLocked(io))
            {
                // File held open by an editor / another process. Don't fail the run --
                // a future watcher event (or the next sweep) will retry naturally.
                _logger.LogWarning("Deferred '{Name}' -> '{Sp}' (file locked: {Msg})", f.Name, spTargetFolder, io.Message);
                deferred++;
            }
            catch (UnauthorizedAccessException ua)
            {
                _logger.LogWarning("Deferred '{Name}' -> '{Sp}' (access denied: {Msg})", f.Name, spTargetFolder, ua.Message);
                deferred++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed '{Name}' -> '{Sp}'", f.Name, spTargetFolder);
                failed++;
            }
        }

        // ---- Delete phase: remove SP files not present locally ----
        var localNames = new HashSet<string>(local.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in remote)
        {
            ct.ThrowIfCancellationRequested();
            if (localNames.Contains(item.Name!)) continue;

            if (isShadow)
            {
                _logger.LogInformation("WOULD DELETE '{Name}' from '{Sp}'", item.Name, spTargetFolder);
                deleted++;
                continue;
            }

            try
            {
                await _facade.DeleteItemAsync(_driveId, item.Id!, ct).ConfigureAwait(false);
                _logger.LogInformation("Deleted '{Name}' from '{Sp}'", item.Name, spTargetFolder);
                deleted++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odata) when (odata.ResponseStatusCode == 404)
            {
                // Item was already gone -- likely moved by ProjectCleanOp or a parallel sync.
                // Idempotent: count as a successful delete instead of a failure.
                _logger.LogInformation("Delete '{Name}' on '{Sp}' was a no-op (already gone).", item.Name, spTargetFolder);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete failed '{Name}' on '{Sp}'", item.Name, spTargetFolder);
                failed++;
            }
        }

        return new Result(uploaded, skipped, deleted, failed, deferred, local.Count, remote.Count, spTargetFolder);
    }

    // Win32 sharing-violation HRESULTs that mean "another process has it open".
    // 32 = ERROR_SHARING_VIOLATION, 33 = ERROR_LOCK_VIOLATION.
    private static bool IsFileLocked(IOException io)
    {
        var hr = io.HResult & 0xFFFF;
        return hr == 32 || hr == 33;
    }

    private async Task<string?> TryResolveExistingFolderIdAsync(string relativePath, CancellationToken ct)
    {
        // CRITICAL: must NOT call EnsureFolderAsync here. EnsureFolderPathAsync
        // creates missing path segments, which would make Shadow mode silently
        // perform Graph writes -- defeating the entire point of Shadow.
        var item = await _facade.TryGetItemByPathAsync(_driveId, relativePath, ct).ConfigureAwait(false);
        if (item is null)
        {
            _logger.LogInformation("Shadow mode: SP folder '{Path}' not found; skipping remote diff.", relativePath);
            return null;
        }

        return item.Id;
    }

    private static bool ShouldUpload(SyncBucket bucket, FileInfo local, DriveItem? sp, TimeSpan skewTolerance)
    {
        if (sp is null) return true;
        var spSize = sp.Size ?? 0;
        if (local.Length != spSize) return true;

        // For non-Stickfile buckets, size match is sufficient (matches PS1).
        if (!string.Equals(bucket.Name, "Stickfile", StringComparison.OrdinalIgnoreCase))
            return false;

        // Stickfile: also require local to be meaningfully newer than SP.
        var spUtc = sp.FileSystemInfo?.LastModifiedDateTime ?? sp.LastModifiedDateTime;
        if (spUtc is null) return false; // can't compare; assume SP is fine

        return local.LastWriteTimeUtc > spUtc.Value.UtcDateTime.Add(skewTolerance);
    }

    // GivenPath shape (PS1 SINGLE_SYNC scripts):
    //   Stickfile :  <project>\05 Stickfile                                              -> 1 level up = project
    //   SSI       :  <project>\04 Construction Admin\02 SSI...                           -> 2 levels up = project
    //   RFI       :  <project>\04 Construction Admin\03 RFI...\Sent to Inspectors        -> 3 levels up = project
    //   Photos    :  <project>\04 Construction Admin\07 Photos                           -> 2 levels up = project
    //
    // We derive levels-up from the bucket's LocalSubpath (each backslash = one extra level).
    // Stickfile lands files at the SP project ROOT; the rest land in <project>/<subfolder>.
    private static (string projectName, string spProjectFolder, string spTargetFolder)
        ResolveSharePointPaths(SyncBucket bucket, string root)
    {
        var levelsUp = bucket.LocalSubpath.Count(c => c == '\\') + 1;
        var current = root;
        for (var i = 0; i < levelsUp; i++)
            current = Path.GetDirectoryName(current) ?? current;

        var projectName = Path.GetFileName(current);
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException($"Could not derive project name from root '{root}' for bucket '{bucket.Name}'.");

        var spProjectFolder = projectName;
        var spTargetFolder = string.IsNullOrWhiteSpace(bucket.SharePointSubfolder)
            ? spProjectFolder
            : $"{spProjectFolder}/{bucket.SharePointSubfolder}";
        return (projectName, spProjectFolder, spTargetFolder);
    }
}
