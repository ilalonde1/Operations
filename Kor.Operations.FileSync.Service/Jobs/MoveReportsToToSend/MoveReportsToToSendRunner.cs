#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.FileSync.Service.ControlPlane;
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
// Behavior parity vs PS1:
//   - Lists every EOR folder under _FIELD REVIEWS TO INITIAL.
//   - If the EOR's monthly control file STILL exists, that EOR has not
//     acknowledged this month -> skip the folder entirely.
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
        var monthName = now.ToString("MMMM", CultureInfo.InvariantCulture);
        var controlFileName = $"Acknowledge and Move To Server {monthName}.txt";
        var nameRegex = new Regex(opts.ProjectFilenameRegex, RegexOptions.Compiled);

        _logger.LogInformation(
            "Starting MoveReportsToToSend (mode={Mode}, source={Source}). Looking for ack'd EORs (control file '{Ctrl}' missing).",
            config.Mode, triggerSource, controlFileName);

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
        catch (Exception ex)
        {
            return new JobRunResult(false, $"Failed to list EOR folders under '{opts.EorRootRelativePath}': {ex.Message}");
        }

        _logger.LogInformation("Found {Count} EOR folder(s).", eorFolders.Count);

        var results = new List<ToSendMoveResult>();
        int eorsScanned = 0, eorsSkippedAck = 0;

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list '{Path}'; skipping.", eorPath);
                continue;
            }

            var controlPresent = children.Any(c =>
                c.Folder is null && string.Equals(c.Name, controlFileName, StringComparison.OrdinalIgnoreCase));
            if (controlPresent)
            {
                _logger.LogInformation("Control file present for '{Eor}' -- not yet acknowledged. Skipping.", eorName);
                eorsSkippedAck++;
                continue;
            }

            eorsScanned++;
            var dataFiles = children
                .Where(c => c.Folder is null && !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                .ToList();

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

        // Summary email (Live + at least one moved).
        var movedRows = results.Where(r => r.Status == ToSendMoveStatus.Moved).ToList();
        bool emailed = false;
        if (!isShadow && movedRows.Count > 0)
        {
            try
            {
                await SendSummaryAsync(opts, movedRows, ct).ConfigureAwait(false);
                emailed = true;
                _logger.LogInformation("Summary emailed To={To}, Cc={Cc} ({Count} file(s)).", opts.SummaryTo, opts.GlobalCc, movedRows.Count);
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
            Summary: $"{verb} {(isShadow ? wouldMove : moved)} file(s) across {eorsScanned} acknowledged EOR(s); " +
                     $"skipped {eorsSkippedAck} EOR(s) (control file present), " +
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

    private async Task SendSummaryAsync(MoveReportsToToSendOptions opts, IReadOnlyList<ToSendMoveResult> moved, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hello,");
        sb.AppendLine();
        sb.AppendLine("The following files have been moved to the project server:");
        foreach (var r in moved)
            sb.AppendLine($"- {r.FileName} => {r.DestinationPath}");
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
