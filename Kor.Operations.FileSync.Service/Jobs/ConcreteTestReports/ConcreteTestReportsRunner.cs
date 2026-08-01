#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Kor.Operations.FileSync.Service.Alerting;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;

// Port of _Scripts Rebuild/Concrete Test Reports/File-ConcreteTestReports.ps1,
// hardened 2026-07-06 after the 430-file silent-backlog incident:
//
//   - Matching is AgencyNo-containment ONLY. The PS1's agency-name token
//     fallback was removed: across 2026-04..06 it produced 178 false
//     "Ambiguous" results (generic tokens like "and"/"west"/"hastings")
//     and not a single good move. A file without a recognizable agency
//     number is now honestly Unmatched.
//   - Nested-key preference: when both "MB53192" and "MB53192-1" rows
//     match, the longer (more specific) key wins instead of Ambiguous.
//   - Default window (no args) is ALL files older than the current month,
//     not just the previous calendar month. Agencies routinely deliver
//     late with prior-month write times preserved; the old one-shot
//     window stranded those files forever.
//   - Live runs email AlertRecipient a list of every file that could NOT
//     be filed (unmatched/ambiguous/noTarget/failed). Leftovers used to
//     sit invisibly while the run reported Success.
//
// Otherwise behavior-parity with the PS1: per-PM rollup CSVs + Unassigned
// catch-all, HTML-table PM email with CSV attachment, RenameOnConflict
// default = overwrite.
//
// Shadow mode: NEVER touches the file server (no moves, no email sends);
// summaries land under ShadowOutputDir/<runStamp>/ on the local box.
// Live mode: writes to OutputRoot/<MonthTag>/ on the file server, performs
// the actual moves, and sends Graph mail.
//
// Args: optional yyyy-MM string. Empty/null => everything before the
// current month; explicit month => that month only (targeted re-runs).
internal sealed class ConcreteTestReportsRunner : IJobRunner
{
    public const string Name = "ConcreteTestReports";

    private readonly IControlPlaneStore _store;
    private readonly GraphServiceClient _graph;
    private readonly IAlertNotifier _alerter;
    private readonly ILogger<ConcreteTestReportsRunner> _logger;

    public ConcreteTestReportsRunner(
        IControlPlaneStore store,
        GraphServiceClient graph,
        IAlertNotifier alerter,
        ILogger<ConcreteTestReportsRunner> logger)
    {
        _store = store;
        _graph = graph;
        _alerter = alerter;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = ConcreteTestReportsOptions.FromKnobs(knobs);

        // Explicit yyyy-MM args => that month only (targeted re-run).
        // Default => sweep EVERYTHING older than the current month, so
        // late-delivered files with prior-month write times are picked up
        // by the next scheduled run instead of stranded forever.
        var explicitMonth = TryParseMonth(args);
        var today = DateTime.Today;
        var currentMonthStart = new DateTime(today.Year, today.Month, 1);
        var windowStart = explicitMonth ?? DateTime.MinValue;
        var windowEnd = explicitMonth?.AddMonths(1) ?? currentMonthStart;
        var monthTag = (explicitMonth ?? currentMonthStart.AddMonths(-1)).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var windowLabel = explicitMonth is null ? $"all months through {monthTag}" : monthTag;
        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Starting CTR run for {Window} (mode={Mode}, source={Source}).", windowLabel, config.Mode, triggerSource);

        if (!File.Exists(opts.MappingXlsx))
            return new JobRunResult(false, $"Mapping workbook not found: {opts.MappingXlsx}");

        if (!Directory.Exists(opts.SourceRoot))
            return new JobRunResult(false, $"SourceRoot not reachable: {opts.SourceRoot}");

        var localXlsx = CopyToTemp(opts.MappingXlsx);
        if (localXlsx is null)
            return new JobRunResult(false, $"Failed to stage mapping workbook to temp from '{opts.MappingXlsx}'.");

        try
        {
            var mapping = ReadMapping(localXlsx, opts.MappingSheetName);
            mapping = mapping.Where(m => !string.IsNullOrWhiteSpace(m.TargetRoot)).ToList();
            _logger.LogInformation("Loaded {Count} mapping rows with target folders.", mapping.Count);
            if (mapping.Count == 0)
                return new JobRunResult(false, $"Mapping sheet '{opts.MappingSheetName}' produced 0 rows with target folders.");

            // Lookup indexes used during matching
            var byAgencyNo = new Dictionary<string, List<MappingRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mapping)
            {
                if (string.IsNullOrWhiteSpace(m.AgencyNo)) continue;
                var key = m.AgencyNo.ToLowerInvariant();
                if (!byAgencyNo.TryGetValue(key, out var list))
                    byAgencyNo[key] = list = new List<MappingRow>();
                list.Add(m);
            }

            var sourceFiles = new DirectoryInfo(opts.SourceRoot)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => opts.Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .Where(f => f.LastWriteTime >= windowStart && f.LastWriteTime < windowEnd)
                .ToList();
            _logger.LogInformation("Found {Count} candidate file(s) for {Window} (root only).", sourceFiles.Count, windowLabel);

            var results = new List<FileMoveResult>(sourceFiles.Count);
            foreach (var f in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();
                var match = MatchFile(f.Name, byAgencyNo);
                if (match.Status != FileMatchStatus.Single)
                {
                    var status = match.Status switch
                    {
                        FileMatchStatus.None => FileMoveStatus.Unmatched,
                        FileMatchStatus.Multiple => FileMoveStatus.Ambiguous,
                        _ => FileMoveStatus.Unmatched,
                    };
                    var note = match.Status == FileMatchStatus.Multiple
                        ? $"Candidates: {string.Join(", ", match.Candidates.Select(c => c.ProjectNo))}"
                        : null;
                    results.Add(new FileMoveResult(status, f.FullName, null, null, null, null, null, note));
                    _logger.LogWarning("{Status}: {File} {Note}", status, f.FullName, note);
                    continue;
                }

                var m = match.Candidates[0];
                if (string.IsNullOrWhiteSpace(m.TargetRoot))
                {
                    results.Add(new FileMoveResult(FileMoveStatus.NoTarget, f.FullName, m.ProjectNo, m.ProjectName, m.ProjectMgr, null, null, "Mapping fix needed"));
                    _logger.LogWarning("NoTarget: {File} (project {Proj})", f.FullName, m.ProjectNo);
                    continue;
                }

                var dest = Path.Combine(m.TargetRoot, f.Name);
                if (isShadow)
                {
                    results.Add(new FileMoveResult(FileMoveStatus.WouldMove, f.FullName, m.ProjectNo, m.ProjectName, m.ProjectMgr, m.TargetRoot, dest, null));
                    _logger.LogInformation("WOULD MOVE: {Src} -> {Dst}", f.FullName, dest);
                }
                else
                {
                    try
                    {
                        Directory.CreateDirectory(m.TargetRoot);
                        var actual = MoveWithPolicy(f.FullName, dest, opts.RenameOnConflict);
                        results.Add(new FileMoveResult(FileMoveStatus.Moved, f.FullName, m.ProjectNo, m.ProjectName, m.ProjectMgr, m.TargetRoot, actual, null));
                        _logger.LogInformation("MOVED: {Src} -> {Dst}", f.FullName, actual);
                    }
                    catch (Exception ex)
                    {
                        results.Add(new FileMoveResult(FileMoveStatus.Failed, f.FullName, m.ProjectNo, m.ProjectName, m.ProjectMgr, m.TargetRoot, dest, ex.Message));
                        _logger.LogError(ex, "Move failed: {Src} -> {Dst}", f.FullName, dest);
                    }
                }
            }

            // Pick the run output root: ShadowOutputDir on local box for Shadow,
            // OutputRoot on the file server for Live. Identical CSV layout either way.
            var runRoot = isShadow
                ? Path.Combine(opts.ShadowOutputDir, monthTag + "_" + DateTimeOffset.Now.ToString("HHmmss", CultureInfo.InvariantCulture))
                : Path.Combine(opts.OutputRoot, monthTag);
            var byPmDir = Path.Combine(runRoot, "ByPM");
            Directory.CreateDirectory(runRoot);
            Directory.CreateDirectory(byPmDir);

            var masterCsvPath = Path.Combine(runRoot, $"Summary_Master_{monthTag}.csv");
            WriteMasterCsv(masterCsvPath, monthTag, results, opts.RenameOnConflict);
            _logger.LogInformation("Wrote master summary: {Path}", masterCsvPath);

            // PM rollups: only Moved / WouldMove rows; one row per project.
            var byProject = results
                .Where(r => r.Status is FileMoveStatus.Moved or FileMoveStatus.WouldMove)
                .GroupBy(r => r.ProjectNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (Row: g.First(), Count: g.Count()), StringComparer.OrdinalIgnoreCase);
            var pmProjects = results
                .Where(r => r.Status is FileMoveStatus.Moved or FileMoveStatus.WouldMove && !string.IsNullOrWhiteSpace(r.ProjectMgr))
                .GroupBy(r => r.ProjectMgr!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ProjectNo!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            int pmCsvCount = 0, pmEmailedCount = 0, pmSkippedNoEmail = 0;
            foreach (var (pm, projects) in pmProjects)
            {
                var pmCsv = Path.Combine(byPmDir, $"{SafeFileName(pm)}_{monthTag}.csv");
                WritePmCsv(pmCsv, monthTag, projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => byProject[p]));
                pmCsvCount++;

                var to = PmDirectory.TryGetEmail(pm);
                if (to is null)
                {
                    pmSkippedNoEmail++;
                    _logger.LogWarning("No email mapping for PM '{Pm}'; skipping email.", pm);
                    continue;
                }

                if (isShadow)
                {
                    _logger.LogInformation("WOULD EMAIL PM={Pm} To={To} Cc={Cc} Csv='{Csv}'", pm, to, opts.GlobalCc, pmCsv);
                }
                else
                {
                    try
                    {
                        var html = BuildPmHtml(pm, monthTag, projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => byProject[p]));
                        var subject = $"Concrete Test Reports - {monthTag} - {pm}";
                        await SendMailAsync(opts.SenderAddress, to, opts.GlobalCc, subject, html, pmCsv, ct).ConfigureAwait(false);
                        _logger.LogInformation("Emailed PM={Pm} To={To}", pm, to);
                        pmEmailedCount++;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Email failed for PM={Pm} To={To}", pm, to);
                    }
                }
            }

            // Catch-all for projects whose PM is blank or not in the map.
            var unassigned = byProject
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value.Row.ProjectMgr) || PmDirectory.TryGetEmail(kv.Value.Row.ProjectMgr) is null)
                .Select(kv => kv.Value)
                .ToList();
            if (unassigned.Count > 0)
            {
                var unCsv = Path.Combine(byPmDir, $"Unassigned_{monthTag}.csv");
                WritePmCsv(unCsv, monthTag, unassigned.OrderBy(x => x.Row.ProjectNo, StringComparer.OrdinalIgnoreCase));
                _logger.LogInformation("Unassigned summary -> {Path} ({Count} project(s))", unCsv, unassigned.Count);
            }

            int moved = results.Count(r => r.Status == FileMoveStatus.Moved);
            int wouldMove = results.Count(r => r.Status == FileMoveStatus.WouldMove);
            int unmatched = results.Count(r => r.Status == FileMoveStatus.Unmatched);
            int ambiguous = results.Count(r => r.Status == FileMoveStatus.Ambiguous);
            int noTarget = results.Count(r => r.Status == FileMoveStatus.NoTarget);
            int failed = results.Count(r => r.Status == FileMoveStatus.Failed);

            // A Success run that quietly leaves files behind is how a
            // 430-file backlog built up unnoticed (2026-02..07). In Live,
            // email the operator the exact leftover list; best-effort so a
            // mail hiccup never poisons the run result.
            var leftovers = results
                .Where(r => r.Status is FileMoveStatus.Unmatched or FileMoveStatus.Ambiguous or FileMoveStatus.NoTarget or FileMoveStatus.Failed)
                .ToList();
            if (!isShadow && leftovers.Count > 0)
                await TrySendLeftoverAlertAsync(windowLabel, leftovers, masterCsvPath, opts, ct).ConfigureAwait(false);

            var verb = isShadow ? "Would move" : "Moved";
            var emailVerb = isShadow ? "Would email" : "Emailed";
            return new JobRunResult(
                Success: failed == 0,
                Summary: $"{verb} {(isShadow ? wouldMove : moved)}/{sourceFiles.Count} files for {windowLabel}; " +
                    $"unmatched={unmatched}, ambiguous={ambiguous}, noTarget={noTarget}, failed={failed}" +
                    $"{(!isShadow && leftovers.Count > 0 ? $" (leftover alert emailed for {leftovers.Count})" : string.Empty)}. " +
                    $"{emailVerb} {(isShadow ? pmCsvCount - pmSkippedNoEmail : pmEmailedCount)} PM(s); skipped {pmSkippedNoEmail} (no email mapping). " +
                    $"Output: {runRoot}");
        }
        finally
        {
            TryDelete(localXlsx);
        }
    }

    private static DateTime? TryParseMonth(string? args)
    {
        if (!string.IsNullOrWhiteSpace(args)
            && DateTime.TryParseExact(args.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    // Live-only: tell the operator exactly which reports the run could not
    // file and why, instead of letting them silently accumulate in the
    // source folder. Failure to send is logged, never thrown.
    private async Task TrySendLeftoverAlertAsync(
        string windowLabel,
        IReadOnlyList<FileMoveResult> leftovers,
        string masterCsvPath,
        ConcreteTestReportsOptions opts,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ConcreteTestReports run for {windowLabel} could not file {leftovers.Count} report(s).");
            sb.AppendLine($"They are still sitting in: {opts.SourceRoot}");
            sb.AppendLine();
            foreach (var r in leftovers.OrderBy(r => r.Status).ThenBy(r => r.SourceFile, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(r.Status).Append("  ").Append(Path.GetFileName(r.SourceFile));
                if (!string.IsNullOrWhiteSpace(r.Note)) sb.Append("  [").Append(r.Note).Append(']');
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Unmatched  = no row in the mapping sheet has this file's agency project number (or the filename has no number).");
            sb.AppendLine("Ambiguous  = more than one mapping row matched; candidates listed in brackets.");
            sb.AppendLine($"Mapping sheet: {opts.MappingXlsx}");
            sb.AppendLine($"Run detail:    {masterCsvPath}");
            sb.AppendLine();
            sb.AppendLine("Fix the sheet (or the filenames), then the next monthly run sweeps them up automatically.");

            using var alertCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            alertCts.CancelAfter(TimeSpan.FromSeconds(30));
            await _alerter.SendAlertAsync(
                $"[FileSync] ConcreteTestReports: {leftovers.Count} report(s) NOT filed ({windowLabel})",
                sb.ToString(),
                alertCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Leftover alert sent ({Count} file(s)).", leftovers.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send leftover alert ({Count} file(s)).", leftovers.Count);
        }
    }

    private static string? CopyToTemp(string source)
    {
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), "CTRMapping_" + Path.GetFileName(source));
            File.Copy(source, dest, overwrite: true);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); } catch { /* best-effort */ }
    }

    private List<MappingRow> ReadMapping(string xlsxPath, string sheetName)
    {
        var rows = new List<MappingRow>();
        using var wb = new XLWorkbook(xlsxPath);
        var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (ws is null)
        {
            _logger.LogError("Mapping sheet '{Sheet}' not found in {Path}.", sheetName, xlsxPath);
            return rows;
        }

        var headerRow = ws.FirstRowUsed();
        if (headerRow is null) return rows;

        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= lastCol; c++)
        {
            var name = headerRow.Cell(c).GetString().Trim();
            if (!string.IsNullOrEmpty(name) && !headers.ContainsKey(name))
                headers[name] = c;
        }

        // Required headers, verbatim from File-ConcreteTestReports.ps1 §209-217.
        string[] required = {
            "Project No.", "Project Manager", "Project Name",
            "Concrete Testing Engineer", "Concrete Testing Project Number",
            "Concrete Testing Project Name", "CTR Folder on Projects Drive",
        };
        foreach (var k in required)
        {
            if (!headers.ContainsKey(k))
            {
                _logger.LogError("Missing expected mapping column: '{Header}'.", k);
                return rows;
            }
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var projNo = row.Cell(headers["Project No."]).GetString().Trim();
            var pmRaw = row.Cell(headers["Project Manager"]).GetString();
            var projName = row.Cell(headers["Project Name"]).GetString().Trim();
            var agency = row.Cell(headers["Concrete Testing Engineer"]).GetString().Trim();
            var agencyNo = row.Cell(headers["Concrete Testing Project Number"]).GetString().Trim();
            var agencyName = row.Cell(headers["Concrete Testing Project Name"]).GetString().Trim();
            var ctrPath = row.Cell(headers["CTR Folder on Projects Drive"]).GetString().Trim();

            if (string.IsNullOrWhiteSpace(projNo) && string.IsNullOrWhiteSpace(ctrPath)) continue;

            rows.Add(new MappingRow(
                ProjectNo: projNo,
                ProjectMgr: PmDirectory.NormalizePmName(pmRaw),
                ProjectName: projName,
                Agency: agency,
                AgencyNo: agencyNo,
                AgencyName: agencyName,
                TargetRoot: ctrPath));
        }

        return rows;
    }

    private enum FileMatchStatus { None, Single, Multiple }

    private sealed record FileMatch(FileMatchStatus Status, IReadOnlyList<MappingRow> Candidates);

    private static FileMatch MatchFile(
        string fileName,
        Dictionary<string, List<MappingRow>> byAgencyNo)
    {
        var lower = fileName.ToLowerInvariant();

        // AgencyNo containment only -- see class comment for why the
        // agency-name token fallback was removed.
        var hitKeys = new List<string>();
        foreach (var key in byAgencyNo.Keys)
        {
            if (lower.Contains(key, StringComparison.Ordinal))
                hitKeys.Add(key);
        }

        if (hitKeys.Count == 0)
            return new FileMatch(FileMatchStatus.None, Array.Empty<MappingRow>());

        // Nested-key preference: when one matched key is a substring of a
        // longer matched key ("MB53192" vs "MB53192-1"), the longer key is
        // the more specific row -- drop the shorter one instead of calling
        // the file Ambiguous.
        if (hitKeys.Count > 1)
        {
            hitKeys = hitKeys
                .Where(k => !hitKeys.Any(other => other.Length > k.Length && other.Contains(k, StringComparison.Ordinal)))
                .ToList();
        }

        var hits = new HashSet<MappingRow>(hitKeys.SelectMany(k => byAgencyNo[k]));

        // Disambiguation: if any candidate's Agency string is also in the
        // filename, prefer those.
        var preferred = hits
            .Where(c => !string.IsNullOrWhiteSpace(c.Agency) && lower.Contains(c.Agency.ToLowerInvariant(), StringComparison.Ordinal))
            .ToList();
        var final = preferred.Count > 0 ? preferred : hits.ToList();

        // Multiple sheet rows that agree on the destination folder are one
        // match, not an ambiguity (the sheet legitimately carries more than
        // one row per project, e.g. per testing scope).
        if (final.Count > 1
            && final.Select(c => c.TargetRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            final = new List<MappingRow> { final[0] };
        }

        return final.Count == 1
            ? new FileMatch(FileMatchStatus.Single, final)
            : new FileMatch(FileMatchStatus.Multiple, final);
    }

    private static string MoveWithPolicy(string src, string dst, bool renameOnConflict)
    {
        if (!File.Exists(dst))
        {
            File.Move(src, dst);
            return dst;
        }

        if (!renameOnConflict)
        {
            // Overwrite-on-conflict (PS1 default). File.Move on .NET 8 supports overwrite.
            File.Move(src, dst, overwrite: true);
            return dst;
        }

        var dir = Path.GetDirectoryName(dst) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(dst);
        var ext = Path.GetExtension(dst);
        int n = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            n++;
        }
        while (File.Exists(candidate));
        File.Move(src, candidate);
        return candidate;
    }

    private static void WriteMasterCsv(string path, string monthTag, IEnumerable<FileMoveResult> rows, bool renameOnConflict)
    {
        var actionLabel = renameOnConflict ? "Move (rename if needed)" : "Move/Overwrite";
        var sb = new StringBuilder();
        sb.AppendLine("Month,Status,SourceFile,ProjectNo,ProjectName,ProjectMgr,TargetFolder,Action");
        foreach (var r in rows)
        {
            sb.Append(CsvEsc(monthTag)).Append(',')
              .Append(CsvEsc(r.Status.ToString())).Append(',')
              .Append(CsvEsc(r.SourceFile)).Append(',')
              .Append(CsvEsc(r.ProjectNo)).Append(',')
              .Append(CsvEsc(r.ProjectName)).Append(',')
              .Append(CsvEsc(r.ProjectMgr)).Append(',')
              .Append(CsvEsc(r.TargetFolder)).Append(',')
              .Append(CsvEsc(r.Status is FileMoveStatus.Moved or FileMoveStatus.WouldMove ? actionLabel : (r.Note ?? string.Empty)))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WritePmCsv(string path, string monthTag, IEnumerable<(FileMoveResult Row, int Count)> projects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Month,ProjectNo,ProjectName,ReportsMoved,TargetFolder");
        foreach (var (r, count) in projects)
        {
            sb.Append(CsvEsc(monthTag)).Append(',')
              .Append(CsvEsc(r.ProjectNo)).Append(',')
              .Append(CsvEsc(r.ProjectName)).Append(',')
              .Append(count.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(CsvEsc(r.TargetFolder))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string BuildPmHtml(string pmName, string monthTag, IEnumerable<(FileMoveResult Row, int Count)> projects)
    {
        var sb = new StringBuilder();
        sb.Append("<table border='1' cellspacing='0' cellpadding='4' style='border-collapse:collapse;font-family:Segoe UI,Arial;font-size:12px;'>");
        sb.Append("<tr><th>ProjectNo</th><th>ProjectName</th><th>ReportsMoved</th><th>Folder</th></tr>");
        foreach (var (r, count) in projects)
        {
            var folder = HtmlEnc(r.TargetFolder);
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{HtmlEnc(r.ProjectNo)}</td><td>{HtmlEnc(r.ProjectName)}</td><td style='text-align:center;'>{count}</td><td><a href=\"{folder}\">{folder}</a></td></tr>");
        }

        sb.Append("</table>");
        var tbl = sb.ToString();
        return $"""
<p>Hello {HtmlEnc(pmName)},</p>
<p>Here are the projects that received concrete test reports in <b>{monthTag}</b>. Your CSV is attached; a quick view is below.</p>
{tbl}
<p>- Auto-filed via KOR Operations FileSync</p>
""";
    }

    private static string HtmlEnc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return WebUtility.HtmlEncode(s);
    }

    private static string CsvEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var needsQuote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var escaped = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }

    private static string SafeFileName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_' ? ch : '_');
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "_unknown" : result;
    }

    private async Task SendMailAsync(string from, string to, string? cc, string subject, string html, string attachmentPath, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(attachmentPath, ct).ConfigureAwait(false);
        var attachments = new List<Attachment>
        {
            new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = Path.GetFileName(attachmentPath),
                ContentType = "text/csv",
                ContentBytes = bytes,
            },
        };

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
                Body = new ItemBody { ContentType = BodyType.Html, Content = html },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } },
                },
                CcRecipients = ccList,
                Attachments = attachments,
            },
            SaveToSentItems = true,
        };

        await _graph.Users[from].SendMail.PostAsync(requestBody, cancellationToken: ct).ConfigureAwait(false);
    }
}
