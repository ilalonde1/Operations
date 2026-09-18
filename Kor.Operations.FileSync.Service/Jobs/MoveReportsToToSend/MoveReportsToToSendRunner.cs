#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs.Shared;
using Kor.Operations.FileSync.Service.Options;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;

// Port of _Scripts Rebuild/FileSync/Production/Move_Reports_To_ToSend.ps1.
//
// Direction is reversed from MoveReportsToEor: SharePoint -> file server.
// Fires monthly on the 5th @ 08:00 PT (Quartz cron in QuartzInstaller),
// mirroring the "Move Reports From EOR To Server" Scheduled Task on
// KOR-APP01. EORs are expected to delete the "Acknowledge..." control file
// from their SharePoint folder during the first few days of the month.
//
// Behaviour (changed 2026-09-17; the PS1 and the first port swept any folder
// with no control file, which emptied CatchAll -- where nobody is ever asked
// to initial anything -- onto the server every month; see EorRouting):
//   - Lists every EOR folder under _FIELD REVIEWS TO INITIAL.
//   - CatchAll is never swept. It is reported.
//   - A folder is swept only if MoveReportsToEor recorded a control file for
//     it THIS period (FileSync.EorControlFiles) and that file is now gone.
//     Still there -> not acknowledged -> skip. No record -> nothing was asked
//     of that engineer -> skip, and say so.
//   - For every file whose name matches ^\d{5}-\d{2}, derives the 8-char
//     project number and finds the matching project folder under
//     \\KOR-FS01\Projects\Projects\<category>\<project>* (categories whose
//     names start with "00" are excluded, same as PS1).
//   - Move = download to TempDir -> copy to <project>\04 Construction Admin\
//     01 Inspection Reports\To Send -> delete original SharePoint item.
//   - Audit CSV Move_ToSend_Audit_<MM-yyyy>.csv goes to AuditLogDir on the
//     file server (Live) or ShadowOutputDir (Shadow). Live also fires a
//     plain-text summary email to admin@ cc'd to ilalonde@.
//
// Shadow never deletes the SharePoint original, never copies to the file
// server, never emails. Audit CSV lands locally with WouldMove rows.
internal sealed class MoveReportsToToSendRunner : IJobRunner
{
    public const string Name = "MoveReportsToToSend";

    private readonly IControlPlaneStore _store;
    private readonly IGraphFacade _facade;
    private readonly GraphServiceClient _graph;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<MoveReportsToToSendRunner> _logger;

    public MoveReportsToToSendRunner(
        IControlPlaneStore store,
        IGraphFacade facade,
        GraphServiceClient graph,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<MoveReportsToToSendRunner> logger)
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
        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = MoveReportsToToSendOptions.FromKnobs(knobs);
        var driveId = _fsOpts.DriveId;
        if (string.IsNullOrWhiteSpace(driveId))
            return new JobRunResult(false, "FileSyncOptions.DriveId is empty.");

        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
        var monthTag = now.ToString("MM-yyyy", CultureInfo.InvariantCulture);
        var periodKey = EorRouting.PeriodKey(now);
        var nameRegex = new Regex(opts.ProjectFilenameRegex, RegexOptions.Compiled);

        // Which folders were actually given a note this period. Read before
        // touching SharePoint: with no records there is nothing to sweep, and
        // a store failure must not degrade into "sweep everything".
        IReadOnlyDictionary<string, string> pending;
        try
        {
            pending = await _store.GetEorControlFilesAsync(periodKey, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobRunResult(false, $"Could not read FileSync.EorControlFiles for {periodKey}: {ex.Message}. Refusing to sweep.");
        }

        _logger.LogInformation(
            "Starting MoveReportsToToSend (mode={Mode}, source={Source}, period={Period}). {Count} EOR folder(s) were given a note this period: {Eors}.",
            config.Mode, triggerSource, periodKey, pending.Count, string.Join(", ", pending.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));

        // Cache project-number -> ToSend folder path so we don't rescan categories per file.
        var projectFolderCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Categories under \\KOR-FS01\Projects\Projects, excluding "00*".
        IReadOnlyList<DirectoryInfo> categories;
        try
        {
            var root = new DirectoryInfo(opts.ProjectsRootPath);
            if (!root.Exists)
                return new JobRunResult(false, $"ProjectsRootPath not reachable: {opts.ProjectsRootPath}");
            categories = root.EnumerateDirectories()
                .Where(d => !d.Name.StartsWith(opts.CategoryFolderExcludePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _logger.LogInformation("Found {Count} project category folder(s).", categories.Count);
        }
        catch (Exception ex)
        {
            return new JobRunResult(false, $"Failed to enumerate '{opts.ProjectsRootPath}': {ex.Message}");
        }

        // The EOR root is REQUIRED. PS1 returns "ERROR retrieving EOR folders"
        // if it's missing; we must not silently treat a misconfigured path as
        // "0 EORs to process / success." Probe explicitly first, then use the
        // safe-list helper for the children.
        DriveItem? eorRoot;
        try
        {
            eorRoot = await _facade.TryGetItemByPathAsync(driveId, opts.EorRootRelativePath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobRunResult(false, $"Failed probing EOR root '{opts.EorRootRelativePath}': {ex.Message}");
        }

        if (eorRoot is null)
        {
            return new JobRunResult(false, $"EOR root not found at '{opts.EorRootRelativePath}'. Refusing to run.");
        }

        // List EOR folders.
        var eorFolders = new List<DriveItem>();
        try
        {
            await foreach (var child in _facade.ListChildrenByPathIfExistsAsync(driveId, opts.EorRootRelativePath, ct).ConfigureAwait(false))
            {
                if (child.Folder is not null && !string.IsNullOrWhiteSpace(child.Name))
                    eorFolders.Add(child);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobRunResult(false, $"Failed to list EOR folders under '{opts.EorRootRelativePath}': {ex.Message}");
        }

        _logger.LogInformation("Found {Count} EOR folder(s).", eorFolders.Count);

        var results = new List<ToSendMoveResult>();
        int eorsScanned = 0, eorsSkippedAck = 0;
        // folder -> project-file count left behind, by reason, for the summary
        var leftNotAcked = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var leftNoBatch = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var catchAllLeft = new List<string>();

        foreach (var eor in eorFolders)
        {
            ct.ThrowIfCancellationRequested();
            var eorName = eor.Name!;
            var eorPath = $"{opts.EorRootRelativePath}/{eorName}";

            // Single listing: pull all children, look for the control file
            // and the data files in one pass.
            var children = new List<DriveItem>();
            try
            {
                await foreach (var c in _facade.ListChildrenByPathIfExistsAsync(driveId, eorPath, ct).ConfigureAwait(false))
                    children.Add(c);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list '{Path}'; skipping.", eorPath);
                continue;
            }

            var dataFiles = children
                .Where(c => c.Folder is null && !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                .ToList();
            var projectFileCount = dataFiles.Count(f => f.Name!.Length >= opts.ProjectNumberLength && nameRegex.IsMatch(f.Name!));

            var decision = EorRouting.DecideSweep(eorName, opts.CatchAllFolderName, pending, dataFiles.Select(f => f.Name!).ToList());
            switch (decision.Action)
            {
                case EorRouting.SweepAction.SkipCatchAll:
                    catchAllLeft.AddRange(dataFiles.Select(f => f.Name!));
                    _logger.LogInformation("'{Eor}' is the catch-all: {Count} file(s) left in place, never swept.", eorName, dataFiles.Count);
                    continue;
                case EorRouting.SweepAction.SkipNoBatch:
                    if (projectFileCount > 0) leftNoBatch[eorName] = projectFileCount;
                    _logger.LogInformation("No note was dropped in '{Eor}' this period ({Period}); {Count} project file(s) left in place.", eorName, periodKey, projectFileCount);
                    continue;
                case EorRouting.SweepAction.SkipNotAcked:
                    eorsSkippedAck++;
                    if (projectFileCount > 0) leftNotAcked[eorName] = projectFileCount;
                    _logger.LogInformation("Control file '{Ctrl}' still present for '{Eor}' -- not yet acknowledged. Skipping.", decision.ControlFileName, eorName);
                    continue;
                case EorRouting.SweepAction.Sweep:
                    _logger.LogInformation("'{Eor}' acknowledged ('{Ctrl}' is gone); sweeping {Count} file(s).", eorName, decision.ControlFileName, dataFiles.Count);
                    break;
            }

            eorsScanned++;

            foreach (var file in dataFiles)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = file.Name!;

                if (fileName.Length < opts.ProjectNumberLength || !nameRegex.IsMatch(fileName))
                {
                    results.Add(new ToSendMoveResult(ToSendMoveStatus.SkippedName, eorName, fileName, null, null, "Name doesn't match project regex"));
                    _logger.LogInformation("Skipping invalid project file name: {File}", fileName);
                    continue;
                }

                var projectNumber = fileName.Substring(0, opts.ProjectNumberLength);

                if (!projectFolderCache.TryGetValue(projectNumber, out var toSendPath))
                {
                    toSendPath = ResolveToSendPath(categories, projectNumber, opts.ToSendRelativePath);
                    projectFolderCache[projectNumber] = toSendPath;
                }

                if (toSendPath is null)
                {
                    results.Add(new ToSendMoveResult(ToSendMoveStatus.SkippedNoProj, eorName, fileName, projectNumber, null, "No project folder match"));
                    _logger.LogWarning("No matching project folder for {Project} -- skipping {File}", projectNumber, fileName);
                    continue;
                }

                var dest = Path.Combine(toSendPath, fileName);
                if (isShadow)
                {
                    results.Add(new ToSendMoveResult(ToSendMoveStatus.WouldMove, eorName, fileName, projectNumber, dest, null));
                    _logger.LogInformation("WOULD MOVE: SP/{Eor}/{File} -> {Dst}", eorName, fileName, dest);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(toSendPath);
                    await CopyToFileServerAsync(driveId, file, dest, ct).ConfigureAwait(false);
                    await _facade.DeleteItemAsync(driveId, file.Id!, ct).ConfigureAwait(false);
                    results.Add(new ToSendMoveResult(ToSendMoveStatus.Moved, eorName, fileName, projectNumber, dest, null));
                    _logger.LogInformation("MOVED: SP/{Eor}/{File} -> {Dst}", eorName, fileName, dest);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    results.Add(new ToSendMoveResult(ToSendMoveStatus.Failed, eorName, fileName, projectNumber, dest, ex.Message));
                    _logger.LogError(ex, "Move failed: SP/{Eor}/{File} -> {Dst}", eorName, fileName, dest);
                }
            }
        }

        // Audit CSV.
        var auditDir = isShadow
            ? Path.Combine(opts.ShadowOutputDir, monthTag + "_" + now.ToString("HHmmss", CultureInfo.InvariantCulture))
            : opts.AuditLogDir;
        var auditPath = Path.Combine(auditDir, $"Move_ToSend_Audit_{monthTag}.csv");
        try
        {
            Directory.CreateDirectory(auditDir);
            WriteAuditCsv(auditPath, results, isShadow);
            _logger.LogInformation("Audit CSV: {Path}", auditPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit CSV at '{Path}'.", auditPath);
        }

        // Summary email (Live; whenever something moved OR something was left
        // behind that a person should know about).
        var movedRows = results.Where(r => r.Status == ToSendMoveStatus.Moved).ToList();
        bool emailed = false;
        bool anythingToSay = movedRows.Count > 0 || leftNotAcked.Count > 0 || leftNoBatch.Count > 0 || catchAllLeft.Count > 0;
        if (!isShadow && anythingToSay)
        {
            try
            {
                await SendSummaryAsync(opts, movedRows, leftNotAcked, leftNoBatch, catchAllLeft, periodKey, ct).ConfigureAwait(false);
                emailed = true;
                _logger.LogInformation("Summary emailed To={To}, Cc={Cc} ({Count} file(s)).", opts.SummaryTo, opts.GlobalCc, movedRows.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Summary email failed.");
            }
        }

        int moved = movedRows.Count;
        int wouldMove = results.Count(r => r.Status == ToSendMoveStatus.WouldMove);
        int skippedName = results.Count(r => r.Status == ToSendMoveStatus.SkippedName);
        int skippedNoProj = results.Count(r => r.Status == ToSendMoveStatus.SkippedNoProj);
        int failed = results.Count(r => r.Status == ToSendMoveStatus.Failed);
        var verb = isShadow ? "Would move" : "Moved";

        return new JobRunResult(
            Success: failed == 0,
            Summary: $"{verb} {(isShadow ? wouldMove : moved)} file(s) across {eorsScanned} acknowledged EOR(s) of {pending.Count} given a note for {periodKey}; " +
                     $"not acknowledged: {eorsSkippedAck} ({leftNotAcked.Values.Sum()} file(s) waiting); " +
                     $"no note this period but holding files: {leftNoBatch.Count} ({leftNoBatch.Values.Sum()} file(s)); " +
                     $"{opts.CatchAllFolderName}: {catchAllLeft.Count} file(s) left in place; " +
                     $"{skippedName} bad name(s), {skippedNoProj} no-project, {failed} failed. " +
                     $"Audit: {auditPath}. {(isShadow ? "Email skipped (Shadow)." : emailed ? "Summary email sent." : "No summary email.")}");
    }

    private static string? ResolveToSendPath(IReadOnlyList<DirectoryInfo> categories, string projectNumber, string toSendRelative)
    {
        foreach (var cat in categories)
        {
            DirectoryInfo? match;
            try
            {
                match = cat.EnumerateDirectories(projectNumber + "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            catch
            {
                continue;
            }

            if (match is not null)
                return Path.Combine(match.FullName, toSendRelative);
        }

        return null;
    }

    private async Task CopyToFileServerAsync(string driveId, DriveItem file, string destinationFullPath, CancellationToken ct)
    {
        // PS1 path: SharePoint stream -> local Temp -> file server. We bypass
        // the local hop and stream straight to the file-server destination,
        // which preserves the same end state and skips a redundant copy.
        using var src = await _facade.DownloadAsync(driveId, file.Id!, ct).ConfigureAwait(false);
        using var dst = new FileStream(destinationFullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
    }

    private static void WriteAuditCsv(string path, IReadOnlyList<ToSendMoveResult> rows, bool isShadow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EorFolder,FileName,ProjectNumber,Destination,Time,Status,Note,Simulated");
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var r in rows)
        {
            sb.Append(CsvEsc(r.EorFolderName)).Append(',')
              .Append(CsvEsc(r.FileName)).Append(',')
              .Append(CsvEsc(r.ProjectNumber)).Append(',')
              .Append(CsvEsc(r.DestinationPath)).Append(',')
              .Append(CsvEsc(stamp)).Append(',')
              .Append(CsvEsc(r.Status.ToString())).Append(',')
              .Append(CsvEsc(r.Note)).Append(',')
              .Append(isShadow ? "True" : "False")
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var needsQuote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var escaped = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }

    private async Task SendSummaryAsync(
        MoveReportsToToSendOptions opts,
        IReadOnlyList<ToSendMoveResult> moved,
        IReadOnlyDictionary<string, int> leftNotAcked,
        IReadOnlyDictionary<string, int> leftNoBatch,
        IReadOnlyList<string> catchAllLeft,
        string periodKey,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hello,");
        sb.AppendLine();
        if (moved.Count > 0)
        {
            sb.AppendLine("The following files have been moved to the project server (their engineer acknowledged them):");
            foreach (var r in moved)
                sb.AppendLine($"- {r.FileName} => {r.DestinationPath}");
        }
        else
        {
            sb.AppendLine("No files were moved to the project server this run.");
        }

        if (leftNotAcked.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Still waiting for the engineer to initial and delete the note (nothing moved):");
            foreach (var (eor, n) in leftNotAcked)
                sb.AppendLine($"- {eor}: {n} report(s)");
        }

        if (leftNoBatch.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Holding reports but were not given a note on the 1st ({periodKey}) -- these will not move until that engineer gets a batch, or they are initialled and moved by hand:");
            foreach (var (eor, n) in leftNoBatch)
                sb.AppendLine($"- {eor}: {n} report(s)");
        }

        if (catchAllLeft.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{opts.CatchAllFolderName} holds {catchAllLeft.Count} file(s) with no engineer. They are never moved automatically; fix EOR.csv and re-file them:");
            foreach (var name in catchAllLeft.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- {name}");
        }

        sb.AppendLine();
        sb.AppendLine("Thank you.");

        var ccList = new List<Recipient>();
        if (!string.IsNullOrWhiteSpace(opts.GlobalCc))
        {
            foreach (var addr in opts.GlobalCc.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ccList.Add(new Recipient { EmailAddress = new EmailAddress { Address = addr } });
        }

        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = "Project Reports Moved to Server",
                Body = new ItemBody { ContentType = BodyType.Text, Content = sb.ToString() },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = opts.SummaryTo } },
                },
                CcRecipients = ccList,
            },
            SaveToSentItems = false,
        };

        await _graph.Users[opts.SenderAddress].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
    }
}
