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

namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;

// Port of _Scripts Rebuild/FileSync/Production/Move_Reports_To_EOR.ps1.
//
// Behavior parity vs PS1:
//   - Loads _FIELD REVIEWS TO INITIAL/EOR.csv to map ProjectNumber -> EOR last name.
//   - Lists existing EOR folders and builds a word -> folder lookup so any
//     token in the folder name can resolve the EOR (matches PS1's split/lower).
//   - Iterates every top-level project folder matching ^\d{5}-\d{2}, skipping
//     _Archived. For each, enumerates <project>/Reports and routes every file
//     to the resolved EOR folder (or CatchAll).
//   - Move = download stream -> local temp -> upload-replace -> delete original
//     (same 3-step PS1 sequence, identical visible end state).
//   - On first successful upload to a non-CatchAll EOR, drops a control file
//     "Acknowledge and Move To Server <Month>.txt" once per EOR (de-duped here;
//     PS1 was per-project and idempotent via PUT-replace).
//   - Writes audit CSV Move_Reports_Audit_<MM-yyyy>.csv to AuditLogDir on the
//     file server (Live) or ShadowOutputDir on the local box (Shadow).
//   - Emails each notified EOR a plain-text body with file count and the
//     control file name (cc'd to GlobalCc). Skips EORs missing from the map.
//
// Shadow mode never moves, never deletes, never emails, and never writes to
// the file-server audit dir; everything lands locally under ShadowOutputDir.
internal sealed class MoveReportsToEorRunner : IJobRunner
{
    public const string Name = "MoveReportsToEor";

    private readonly IControlPlaneStore _store;
    private readonly IGraphFacade _facade;
    private readonly GraphServiceClient _graph;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<MoveReportsToEorRunner> _logger;

    public MoveReportsToEorRunner(
        IControlPlaneStore store,
        IGraphFacade facade,
        GraphServiceClient graph,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<MoveReportsToEorRunner> logger)
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
        var opts = MoveReportsToEorOptions.FromKnobs(knobs);
        var driveId = _fsOpts.DriveId;
        if (string.IsNullOrWhiteSpace(driveId))
            return new JobRunResult(false, "FileSyncOptions.DriveId is empty.");

        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
        var monthTag = now.ToString("MM-yyyy", CultureInfo.InvariantCulture);
        var monthName = now.ToString("MMMM", CultureInfo.InvariantCulture);
        var controlFileName = $"Acknowledge and Move To Server {monthName}.txt";
        var projectRegex = new Regex(opts.ProjectFolderRegex, RegexOptions.Compiled);

        _logger.LogInformation(
            "Starting MoveReportsToEor for {Month} (mode={Mode}, source={Source}).",
            monthTag, config.Mode, triggerSource);

        // 1) Load EOR.csv (read-only; safe in both Shadow and Live).
        var csvPath = $"{opts.EorRootRelativePath.TrimEnd('/')}/{opts.EorCsvFileName}";
        Dictionary<string, string> eorMap;
        try
        {
            using var csvStream = await _facade.DownloadByPathAsync(driveId, csvPath, ct).ConfigureAwait(false);
            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            var csvText = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            eorMap = ParseEorCsv(csvText);
            _logger.LogInformation("Loaded {Count} EOR mapping(s) from {Path}.", eorMap.Count, csvPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read EOR.csv at '{Path}'.", csvPath);
            return new JobRunResult(false, $"Failed to read {csvPath}: {ex.Message}");
        }

        // 2) Enumerate EOR root folders -> word -> folder lookup (PS1 §155-163).
        var eorFolders = new List<string>();
        await foreach (var child in _facade.ListChildrenByPathIfExistsAsync(driveId, opts.EorRootRelativePath, ct).ConfigureAwait(false))
        {
            if (child.Folder is not null && !string.IsNullOrWhiteSpace(child.Name))
                eorFolders.Add(child.Name);
        }

        var eorFolderMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in eorFolders)
        {
            foreach (var word in folder.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!eorFolderMap.TryGetValue(word, out var list))
                    eorFolderMap[word] = list = new List<string>();
                list.Add(folder);
            }
        }

        _logger.LogInformation("Discovered {Count} EOR folder(s) under '{Root}'.", eorFolders.Count, opts.EorRootRelativePath);

        // Cache EOR-folder ID lookups so each EOR is resolved at most once per run.
        var eorFolderIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        async Task<string?> ResolveEorFolderIdAsync(string eorName)
        {
            if (eorFolderIdCache.TryGetValue(eorName, out var cached)) return cached;
            try
            {
                var id = await _facade.EnsureFolderAsync($"{opts.EorRootRelativePath}/{eorName}", ct).ConfigureAwait(false);
                eorFolderIdCache[eorName] = id;
                return id;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resolve EOR folder '{Eor}'.", eorName);
                return null;
            }
        }

        // 3) Top-level project folders.
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

        _logger.LogInformation("Found {Count} project folder(s) to scan.", projects.Count);

        var results = new List<ReportMoveResult>();
        var notifiedEors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var controlFileCreatedFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 4) Walk each project's Reports/ folder.
        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var projectName = project.Name!;
            var projectNumber = projectName.Split(' ', 2)[0];
            var eorName = ResolveEorName(projectNumber, eorMap, eorFolderMap, opts.CatchAllFolderName);

            var reportsPath = $"{projectName}/{opts.ReportsSubfolderName}";
            List<DriveItem> files;
            try
            {
                files = new List<DriveItem>();
                await foreach (var child in _facade.ListChildrenByPathIfExistsAsync(driveId, reportsPath, ct).ConfigureAwait(false))
                {
                    if (child.Folder is null && !string.IsNullOrWhiteSpace(child.Name) && !string.IsNullOrWhiteSpace(child.Id))
                        files.Add(child);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("No Reports folder for '{Project}': {Reason}", projectName, ex.Message);
                continue;
            }

            if (files.Count == 0)
            {
                _logger.LogDebug("No files in Reports folder for '{Project}'.", projectName);
                continue;
            }

            string? eorFolderId = null;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var destPath = $"{opts.EorRootRelativePath}/{eorName}/{file.Name}";

                if (isShadow)
                {
                    results.Add(new ReportMoveResult(ReportMoveStatus.WouldMove, projectName, file.Name!, eorName, destPath, null));
                    _logger.LogInformation("WOULD MOVE: {Project}/{File} -> {Dst}", projectName, file.Name, destPath);
                }
                else
                {
                    eorFolderId ??= await ResolveEorFolderIdAsync(eorName).ConfigureAwait(false);
                    if (eorFolderId is null)
                    {
                        results.Add(new ReportMoveResult(ReportMoveStatus.Failed, projectName, file.Name!, eorName, destPath, "EOR folder unresolved"));
                        continue;
                    }

                    try
                    {
                        await MoveOneAsync(driveId, file, eorFolderId, ct).ConfigureAwait(false);
                        results.Add(new ReportMoveResult(ReportMoveStatus.Moved, projectName, file.Name!, eorName, destPath, null));
                        _logger.LogInformation("MOVED: {Project}/{File} -> {Dst}", projectName, file.Name, destPath);

                        // First successful upload into a real EOR folder -> drop the control file once.
                        if (!string.Equals(eorName, opts.CatchAllFolderName, StringComparison.OrdinalIgnoreCase)
                            && controlFileCreatedFor.Add(eorName))
                        {
                            await TryCreateControlFileAsync(eorFolderId, controlFileName, ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new ReportMoveResult(ReportMoveStatus.Failed, projectName, file.Name!, eorName, destPath, ex.Message));
                        _logger.LogError(ex, "Move failed: {Project}/{File} -> {Dst}", projectName, file.Name, destPath);
                    }
                }

                if (!string.Equals(eorName, opts.CatchAllFolderName, StringComparison.OrdinalIgnoreCase))
                    notifiedEors[eorName] = notifiedEors.TryGetValue(eorName, out var c) ? c + 1 : 1;
            }
        }

        // 5) Audit CSV.
        var auditDir = isShadow
            ? Path.Combine(opts.ShadowOutputDir, monthTag + "_" + now.ToString("HHmmss", CultureInfo.InvariantCulture))
            : opts.AuditLogDir;
        var auditPath = Path.Combine(auditDir, $"Move_Reports_Audit_{monthTag}.csv");
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

        // 6) Notifications (Live only).
        int emailedCount = 0, skippedNoEmail = 0;
        if (!isShadow)
        {
            foreach (var (eorName, count) in notifiedEors)
            {
                var to = EorDirectory.TryGetEmail(eorName);
                if (to is null)
                {
                    _logger.LogWarning("No email mapping for EOR '{Eor}'; skipping notification.", eorName);
                    skippedNoEmail++;
                    continue;
                }

                try
                {
                    var subject = "Reports copied to your SharePoint folder";
                    var body =
                        $"Hello {eorName},\r\n\r\n" +
                        $"{count} reports have been automatically copied to your SharePoint folder under {opts.EorRootRelativePath}\\{eorName}.\r\n" +
                        $"Please acknowledge them by deleting the file: {controlFileName}.\r\n\r\n" +
                        "Thank you.";
                    await SendMailAsync(opts.SenderAddress, to, opts.GlobalCc, subject, body, ct).ConfigureAwait(false);
                    _logger.LogInformation("Emailed EOR={Eor} To={To} Count={Count}", eorName, to, count);
                    emailedCount++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Email failed for EOR={Eor} To={To}", eorName, to);
                }
            }
        }

        int moved = results.Count(r => r.Status == ReportMoveStatus.Moved);
        int wouldMove = results.Count(r => r.Status == ReportMoveStatus.WouldMove);
        int failed = results.Count(r => r.Status == ReportMoveStatus.Failed);

        var verb = isShadow ? "Would move" : "Moved";
        var emailVerb = isShadow ? "Would email" : "Emailed";
        var emailCount = isShadow ? notifiedEors.Count : emailedCount;
        return new JobRunResult(
            Success: failed == 0,
            Summary: $"{verb} {(isShadow ? wouldMove : moved)} file(s) across {projects.Count} project(s) for {monthTag}; " +
                     $"failed={failed}. {emailVerb} {emailCount} EOR(s); skipped {skippedNoEmail} (no email mapping). " +
                     $"Audit: {auditPath}");
    }

    private static string ResolveEorName(
        string projectNumber,
        Dictionary<string, string> eorMap,
        Dictionary<string, List<string>> eorFolderMap,
        string catchAllName)
    {
        if (!eorMap.TryGetValue(projectNumber, out var lastName) || string.IsNullOrWhiteSpace(lastName))
            return catchAllName;
        if (eorFolderMap.TryGetValue(lastName, out var folders) && folders.Count > 0)
            return folders[0];
        return catchAllName;
    }

    private async Task MoveOneAsync(string driveId, DriveItem file, string destFolderId, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"eor_{Guid.NewGuid():N}_{file.Name}");
        try
        {
            using (var src = await _facade.DownloadAsync(driveId, file.Id!, ct).ConfigureAwait(false))
            using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            await _facade.UploadToFolderAsync(destFolderId, file.Name!, temp, progress: null, ct).ConfigureAwait(false);
            await _facade.DeleteItemAsync(driveId, file.Id!, ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best-effort */ }
        }
    }

    private async Task TryCreateControlFileAsync(string eorFolderId, string controlFileName, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"ctrl_{Guid.NewGuid():N}_{controlFileName}");
        try
        {
            await File.WriteAllTextAsync(
                temp,
                "These reports have been automatically copied. Please acknowledge and move them to the server.",
                Encoding.UTF8,
                ct).ConfigureAwait(false);
            await _facade.UploadToFolderAsync(eorFolderId, controlFileName, temp, progress: null, ct).ConfigureAwait(false);
            _logger.LogInformation("Dropped control file '{Name}' into EOR folder.", controlFileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to drop control file '{Name}'.", controlFileName);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best-effort */ }
        }
    }

    private static Dictionary<string, string> ParseEorCsv(string csv)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header is null) return map;

        var cols = SplitCsvLine(header);
        int projIdx = -1, eorIdx = -1;
        for (int i = 0; i < cols.Length; i++)
        {
            var h = cols[i].Trim().Trim('"');
            if (string.Equals(h, "ProjectNumber", StringComparison.OrdinalIgnoreCase)) projIdx = i;
            else if (string.Equals(h, "EOR", StringComparison.OrdinalIgnoreCase)) eorIdx = i;
        }

        if (projIdx < 0 || eorIdx < 0) return map;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = SplitCsvLine(line);
            if (parts.Length <= Math.Max(projIdx, eorIdx)) continue;
            var p = parts[projIdx].Trim().Trim('"');
            var e = parts[eorIdx].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(p) && !map.ContainsKey(p))
                map[p] = e;
        }

        return map;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
            }
            else if (c == ',' && !inQuote)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static void WriteAuditCsv(string path, IReadOnlyList<ReportMoveResult> rows, bool isShadow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Project,FileName,Destination,Time,Status,Note,Simulated");
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var r in rows)
        {
            sb.Append(CsvEsc(r.ProjectFolderName)).Append(',')
              .Append(CsvEsc(r.FileName)).Append(',')
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

    private async Task SendMailAsync(string from, string to, string? cc, string subject, string body, CancellationToken ct)
    {
        var ccList = new List<Recipient>();
        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in cc.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ccList.Add(new Recipient { EmailAddress = new EmailAddress { Address = addr } });
        }

        var requestBody = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal),
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } },
                },
                CcRecipients = ccList,
            },
            SaveToSentItems = false,
        };

        await _graph.Users[from].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
    }
}
