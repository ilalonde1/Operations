#nullable enable
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Move-to-holding-folder for a SharePoint project, fired when the watcher
// sees the control file 'NOT SYNCED TO ACTIVE PROJECTS.txt' appear.
//
// Verbatim from SINGLE_CLEAN_Projects.ps1:
//   * Resolve the SP project folder by exact name first, then by project-
//     number prefix (^\d{5}-\d{2}\s*\() if the user only handed us a PN.
//   * Get-or-create the holding folder at the SP root (rename-on-conflict).
//   * PATCH parentReference to move the project. On a name collision in
//     the holding folder, retry the PATCH with a timestamped name --
//     "<original> (archived YYYYMMDD_HHMMSS)" -- to mirror PS1 §144-163.
//   * Per-project Mutex prevents concurrent CLEANs against the same
//     target on the same host (PS1 used Global\CLEAN_<safeName>).
internal sealed class ProjectCleanOp
{
    public sealed record Result(string TargetName, string? RenamedTo, bool Moved, string? Note);

    private static readonly Regex ProjectNumberPrefix = new(@"^\d{5}-\d{2}\s*\(", RegexOptions.Compiled);

    private readonly GraphServiceClient _graph;
    private readonly IGraphFacade _facade;
    private readonly WatcherOptions _options;
    private readonly ILogger _logger;
    private readonly string _driveId;

    public ProjectCleanOp(GraphServiceClient graph, IGraphFacade facade, WatcherOptions options, string driveId, ILogger logger)
    {
        _graph = graph;
        _facade = facade;
        _options = options;
        _driveId = driveId;
        _logger = logger;
    }

    public async Task<Result> RunAsync(string projectDir, bool isShadow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentException("projectDir required.", nameof(projectDir));

        var targetName = ResolveTargetName(projectDir);
        var mutexName = "Global\\KorFileSyncClean_" + Regex.Replace(targetName, @"[^\w\-]+", "_");

        using var mtx = new Mutex(initiallyOwned: false, name: mutexName);
        bool acquired = false;
        try { acquired = mtx.WaitOne(TimeSpan.FromSeconds(1)); }
        catch (AbandonedMutexException) { acquired = true; } // previous owner died; we own it now

        if (!acquired)
        {
            _logger.LogInformation("ProjectCleanOp: mutex held for '{Target}' on this host; skipping duplicate invocation.", targetName);
            return new Result(targetName, null, Moved: false, Note: "duplicate invocation");
        }

        try
        {
            var item = await FindProjectFolderAsync(targetName, ct).ConfigureAwait(false);
            if (item is null)
            {
                _logger.LogInformation("ProjectCleanOp: SP folder '{Target}' not found; nothing to move.", targetName);
                return new Result(targetName, null, Moved: false, Note: "not found on SharePoint");
            }

            if (isShadow)
            {
                _logger.LogInformation("WOULD MOVE SP folder '{Target}' -> '{Holding}'", item.Name, _options.HoldingFolderName);
                return new Result(item.Name ?? targetName, null, Moved: false, Note: "shadow");
            }

            var holdingId = await GetOrCreateHoldingFolderIdAsync(ct).ConfigureAwait(false);

            try
            {
                await _facade.MoveItemAsync(_driveId, item.Id!, holdingId, newName: null, ct).ConfigureAwait(false);
                _logger.LogInformation("Moved '{Name}' -> '{Holding}'", item.Name, _options.HoldingFolderName);
                return new Result(item.Name ?? targetName, null, Moved: true, Note: null);
            }
            catch (ODataError ex) when (GraphFacade.IsNameConflict(ex))
            {
                var stamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var renamed = $"{item.Name} (archived {stamp})";
                _logger.LogWarning(
                    "Conflict moving '{Name}' to holding; retrying with renamed name '{Renamed}'.",
                    item.Name, renamed);
                await _facade.MoveItemAsync(_driveId, item.Id!, holdingId, newName: renamed, ct).ConfigureAwait(false);
                return new Result(item.Name ?? targetName, renamed, Moved: true, Note: "renamed on conflict");
            }
        }
        finally
        {
            try { mtx.ReleaseMutex(); } catch { /* shutting down or already released */ }
        }
    }

    // The control file event hands us the local project DIR; the SP folder
    // name follows the same "<NNNNN-NN> (Project Name ...)" convention.
    private static string ResolveTargetName(string projectDir)
    {
        var leaf = Path.GetFileName(projectDir.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf))
            throw new InvalidOperationException($"Cannot derive project leaf from '{projectDir}'.");

        // PS1 also tolerates being handed a child folder (e.g. ..\05 Stickfile);
        // walk up if the leaf looks like a known bucket folder.
        var bucketPrefixes = new[] { "05 Stickfile", "04 Construction Admin", "Newforma", "Photos", "SSI", "RFI" };
        foreach (var pfx in bucketPrefixes)
        {
            if (leaf.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(projectDir.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent))
                    return Path.GetFileName(parent.TrimEnd('\\', '/'));
            }
        }

        return leaf;
    }

    private async Task<DriveItem?> FindProjectFolderAsync(string targetName, CancellationToken ct)
    {
        var hits = new List<DriveItem>();
        await foreach (var child in _facade.ListChildrenByPathIfExistsAsync(_driveId, string.Empty, ct).ConfigureAwait(false))
        {
            if (child.Folder is null || string.IsNullOrWhiteSpace(child.Name)) continue;
            hits.Add(child);
        }

        // Exact match preferred when the caller passed a full "NNNNN-NN (Name ...)" leaf.
        if (ProjectNumberPrefix.IsMatch(targetName))
        {
            var exact = hits.FirstOrDefault(h => string.Equals(h.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var pn = targetName.Split(' ', 2)[0];
            return hits.FirstOrDefault(h => h.Name!.StartsWith(pn, StringComparison.OrdinalIgnoreCase));
        }

        return hits.FirstOrDefault(h => h.Name!.StartsWith(targetName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GetOrCreateHoldingFolderIdAsync(CancellationToken ct)
    {
        // The holding folder lives at the SP root next to project folders.
        // EnsureFolderAsync handles the get-or-create and returns the ID.
        return await _facade.EnsureFolderAsync(_options.HoldingFolderName, ct).ConfigureAwait(false);
    }
}
