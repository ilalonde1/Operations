#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Options;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;

namespace Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;

// Port of _Scripts Rebuild/FileSync/Production/RenameReportsUploads.ps1.
//
// Pure renames -- no moves, no deletes, no email. Walks every project
// folder under the drive root that matches ^\d{5}-\d{2} (and isn't
// _Archived), and for each file in <project>/Reports normalizes the
// name to "<project> CRM YYYY-MM-DD Report NN.<ext>" using each file's
// createdDateTime as the date.
//
// Two cases (verbatim from PS1 §125-150 and §152-171):
//   A) Already in the canonical shape but with a date that doesn't match
//      createdDateTime -> rename to fix the date, keep the counter.
//   B) Anything else -> assign the next free counter (max already-used
//      counter + 1, then incremented per renamed file).
//
// Shadow logs "WOULD rename" lines and never PATCHes.
internal sealed class RenameReportsUploadsRunner : IJobRunner
{
    public const string Name = "RenameReportsUploads";

    private readonly IControlPlaneStore _store;
    private readonly IGraphFacade _facade;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<RenameReportsUploadsRunner> _logger;

    public RenameReportsUploadsRunner(
        IControlPlaneStore store,
        IGraphFacade facade,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<RenameReportsUploadsRunner> logger)
    {
        _store = store;
        _facade = facade;
        _fsOpts = fsOpts.Value;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = RenameReportsUploadsOptions.FromKnobs(knobs);
        var driveId = _fsOpts.DriveId;
        if (string.IsNullOrWhiteSpace(driveId))
            return new JobRunResult(false, "FileSyncOptions.DriveId is empty.");

        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
        var projectRegex = new Regex(opts.ProjectFolderRegex, RegexOptions.Compiled);

        _logger.LogInformation("Starting RenameReportsUploads (mode={Mode}, source={Source}).", config.Mode, triggerSource);

        // Top-level project folders.
        var projects = new List<DriveItem>();
        await foreach (var child in _facade.ListChildrenByPathIfExistsAsync(driveId, string.Empty, ct).ConfigureAwait(false))
        {
            if (child.Folder is null || string.IsNullOrWhiteSpace(child.Name))
                continue;
            if (string.Equals(child.Name, opts.ArchivedFolderName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!projectRegex.IsMatch(child.Name))
                continue;
            projects.Add(child);
        }

        _logger.LogInformation("Scanning {Count} project folder(s).", projects.Count);

        int totalRenamed = 0, totalAlreadyOk = 0, totalDateFixed = 0, totalAssigned = 0, totalFailed = 0, totalProjectsHit = 0;

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var projectName = project.Name!;
            var projectNumber = projectName.Split(' ', 2)[0];
            var reportsPath = $"{projectName}/{opts.ReportsSubfolderName}";

            List<DriveItem> files;
            try
            {
                files = new List<DriveItem>();
                await foreach (var c in _facade.ListChildrenByPathIfExistsAsync(driveId, reportsPath, ct).ConfigureAwait(false))
                {
                    if (c.Folder is null && !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                        files.Add(c);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Skipping '{Path}' ({Reason})", reportsPath, ex.Message);
                continue;
            }

            if (files.Count == 0) continue;
            totalProjectsHit++;

            // Pass 1: max counter already used (Case A's regex captures the counter).
            var canonRegex = new Regex(
                $@"^{Regex.Escape(projectNumber)} CRM \d{{4}}-\d{{2}}-\d{{2}} Report (\d{{2}})",
                RegexOptions.Compiled);
            int maxCounter = 0;
            foreach (var f in files)
            {
                var m = canonRegex.Match(f.Name!);
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > maxCounter)
                    maxCounter = n;
            }

            int counter = maxCounter + 1;

            // Pass 2: rename. Sort by name to mirror PS1's `Sort-Object name`.
            foreach (var file in files.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var name = file.Name!;
                var ext = Path.GetExtension(name);
                var createdLocal = (file.CreatedDateTime ?? DateTimeOffset.UtcNow).ToLocalTime();
                var stamp = createdLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                // Case A: already in canonical shape; possibly wrong date.
                var caseAPattern = $@"^({Regex.Escape(projectNumber)} CRM )\d{{4}}-\d{{2}}-\d{{2}}( Report \d{{2}}{Regex.Escape(ext)})$";
                var caseA = Regex.Match(name, caseAPattern);
                if (caseA.Success)
                {
                    var corrected = $"{caseA.Groups[1].Value}{stamp}{caseA.Groups[2].Value}";
                    if (string.Equals(corrected, name, StringComparison.Ordinal))
                    {
                        totalAlreadyOk++;
                        continue;
                    }

                    if (await TryRenameAsync(driveId, file, corrected, isShadow, ct).ConfigureAwait(false))
                    {
                        totalDateFixed++;
                        totalRenamed++;
                    }
                    else
                    {
                        totalFailed++;
                    }

                    continue;
                }

                // Case B: anything else -> assign next free counter.
                var newName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} CRM {1} Report {2:D2}{3}",
                    projectNumber, stamp, counter, ext);

                // Advance the counter on every attempt regardless of mode or
                // outcome. Two reasons:
                //   1. Shadow predicts Live: if we leave counter pinned in
                //      Shadow, every "WOULD rename" line in a project shows
                //      the SAME target name -- useless for parity comparison.
                //   2. One 409 on a transient Graph hiccup must not cascade
                //      through every following file in the project. Bumping
                //      the counter on failure means File N+1 tries the next
                //      slot instead of re-colliding on the same one.
                // PS1 doesn't advance on failure; this is a deliberate
                // improvement and the canon-regex maxCounter pass on the next
                // run still re-anchors against actual SharePoint state.
                var renamed = await TryRenameAsync(driveId, file, newName, isShadow, ct).ConfigureAwait(false);
                if (renamed)
                {
                    totalAssigned++;
                    totalRenamed++;
                }
                else
                {
                    totalFailed++;
                }
                counter++;
            }
        }

        var verb = isShadow ? "Would rename" : "Renamed";
        return new JobRunResult(
            Success: totalFailed == 0,
            Summary: $"{verb} {totalRenamed} file(s) across {totalProjectsHit}/{projects.Count} project(s); " +
                     $"date-fixed={totalDateFixed}, newly-assigned={totalAssigned}, already-ok={totalAlreadyOk}, failed={totalFailed}.");
    }

    private async Task<bool> TryRenameAsync(string driveId, DriveItem file, string newName, bool isShadow, CancellationToken ct)
    {
        if (isShadow)
        {
            _logger.LogInformation("WOULD rename '{Old}' -> '{New}'", file.Name, newName);
            return true;
        }

        try
        {
            await _facade.RenameItemAsync(driveId, file.Id!, newName, ct).ConfigureAwait(false);
            _logger.LogInformation("Renamed '{Old}' -> '{New}'", file.Name, newName);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rename failed for '{Old}' -> '{New}'", file.Name, newName);
            return false;
        }
    }
}
