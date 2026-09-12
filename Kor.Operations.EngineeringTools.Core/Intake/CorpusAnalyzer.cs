#nullable enable
using System.Globalization;
using System.Text;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// The whole drawing corpus through the one ingestion point, into a ledger: every job's current
/// structural stick file mirrored once, built by <see cref="PdfOnlyBuild"/> exactly as the verbs
/// build one, and recorded set by set and sheet by sheet — what was read, what was placed, why not
/// — in <c>analysis.IntakeSet</c> / <c>analysis.IntakeSheet</c> (migration 083) and as CSV beside
/// the work. The rules are then chosen by how many sets share a failure and measured on every set
/// before they are kept (completion plan WP1, 2026-09-11).
/// </summary>
/// <remarks>
/// INCREMENTAL BY WHAT CHANGED: a set is rebuilt when its PDF's size or date, or the tool's build,
/// differs from the manifest in its work folder; otherwise the last outcome is re-read from the
/// work folder's CSV and reported again under the new run. The tool's build stamp is part of the
/// key on purpose — a rule can touch any set, so a rebuilt tool rebuilds every set; what the
/// manifest saves is the rerun after a crash, a network drop, or a run stopped at set 140.
/// THE YARDSTICK: where the job has the engineer's own model - the KOR-210 export under the yardstick
/// folder, else the newest .e2k in its model folder - the built model is measured against it
/// (<see cref="ModelYardstick"/>) and the figures go in the set's row: storeys shared, how the frames
/// met (grids or column registration, with its support), and both residual shares.
/// WHAT THIS DOES NOT DO: read the architects' sets (they need sorting into sets and sheets first);
/// run on the share (the mirror is read once, the loop reads the mirror).
/// </remarks>
public static class CorpusAnalyzer
{
    public sealed record SetRow(
        Guid RunId, DateTime RunAtUtc, DateTime ToolBuiltAtUtc, string Job, string Category, string SetKind, string Pdf,
        string? IssueDate, bool IssueDateFromName, long Bytes, int Pages, int PlanSheets, int SheetsWritten, int SheetsFailed, int SheetsNotPlan,
        int AssemblyCards, int StoreysRead, bool HasModel, string? ModelError, int? SheetsPlaced, int? StoreysBuilt, int? Walls, int? Columns,
        int? Floors, int? StoreysWithPlate, double Seconds, string? Error,
        // the yardstick: the engineer's own model of the job, where one exists (null columns where none, or no model of ours)
        string? Yardstick = null, int? YardstickStoreys = null, int? SharedStoreys = null, bool? FrameFromGrids = null, int? FrameSupport = null,
        int? OursCompared = null, double? OursMedianMm = null, int? OursWithin100 = null, int? TheirsCompared = null, int? TheirsWithin100 = null, string? YardstickNote = null);

    public sealed record SheetRow(
        Guid RunId, string Job, int Page, string? SheetNumber, string SheetType, string? Title, string? Level, string? ScaleNote, int? ScaleDenominator,
        int Slabs, int Columns, int Walls, int Lines, string? DxfFiles, string? SelfCheck, bool? Placed, string? Storeys, string? Flags, string? Failure);

    public sealed record RunResult(Guid RunId, IReadOnlyList<SetRow> Sets, IReadOnlyList<SheetRow> Sheets, int Rebuilt, int Reused, TimeSpan Elapsed);

    /// <summary>When the code that produced a row was built — the Core assembly's file time, the honest stamp a build carries.</summary>
    public static DateTime ToolBuiltAtUtc => File.GetLastWriteTimeUtc(typeof(CorpusAnalyzer).Assembly.Location);

    /// <summary>The fallback scale for a sheet that states none (every sheet is read at the scale it states, step 31); KOR's plans are 1/8" = 1'-0".</summary>
    // the scale a set is read at when a sheet states none is a row now (dxf.pdf.fallback-scale, WP5); every sheet that states one is read at that (step 31)

    /// <summary>Where the engineers' models exported on KOR-210 land: &lt;job&gt;.e2k, one per job (EtabsExportE2k).</summary>
    public static string DefaultYardstickFolder => Path.Combine(DrawingMirror.Root, "yardsticks");

    /// <summary>
    /// The engineer's model to measure a job against: the export under the yardstick folder, else the
    /// newest .e2k in the job's own model folder (mirrored). Null when the job has neither.
    /// </summary>
    public static string? YardstickFor(StickFileCorpus.JobCensus job, string yardstickFolder)
    {
        string exported = Path.Combine(yardstickFolder, job.Job + ".e2k");
        if (File.Exists(exported)) return exported;
        if (job.ModelFolder is null || job.E2kModels == 0) return null;
        try
        {
            // the newest .e2k the ENGINEER wrote: a model folder also holds what this tool published there
            // (31168's "31168-FROM-DRAWINGS.e2k", the Revit route's output), and the first pass measured
            // the PDF route against it - our own output as the yardstick. Ours name their members KW/KC/KF.
            var e2ks = new DirectoryInfo(job.ModelFolder).EnumerateFiles("*.e2k", SearchOption.TopDirectoryOnly)
                .Concat(new DirectoryInfo(job.ModelFolder).EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.e2k", SearchOption.TopDirectoryOnly)))
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in e2ks)
            {
                string local = DrawingMirror.SingleFile(f.FullName);
                if (!IsKorGenerated(local) && HasColumns(local)) return local;   // a shell (grids and storeys, no members) measures nothing
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>An .e2k this tool wrote: its members are named KW (walls), KC (columns), KF (floors), KP (points).</summary>
    public static bool IsKorGenerated(string e2k)
    {
        foreach (var line in File.ReadLines(e2k))
        {
            string t = line.TrimStart();
            if (t.StartsWith("LINE " + Quote + "KC", StringComparison.Ordinal) || t.StartsWith("AREA " + Quote + "KW", StringComparison.Ordinal)
                || t.StartsWith("AREA " + Quote + "KF", StringComparison.Ordinal) || t.StartsWith("POINT " + Quote + "KP", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>A model with at least one column in it: a shell of storeys and grids is not a yardstick.</summary>
    public static bool HasColumns(string e2k)
    {
        foreach (var line in File.ReadLines(e2k))
        {
            string t = line.TrimStart();
            if (t.StartsWith("LINE ", StringComparison.Ordinal) && t.Contains(" COLUMN ", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private const string Quote = "\"";

    public static RunResult Run(
        IReadOnlyList<StickFileCorpus.JobCensus> census, string workRoot, PdfIntakeOptions options, string? rulesConnection,
        int parallel, bool force, Action<string> log, string? onlyJobs = null, string? yardstickFolder = null, bool reuseBuilds = false, bool recompose = false)
    {
        yardstickFolder ??= DefaultYardstickFolder;
        ArgumentNullException.ThrowIfNull(census);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        Directory.CreateDirectory(workRoot);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        var runAt = DateTime.UtcNow;
        var built = ToolBuiltAtUtc;
        var only = onlyJobs is null ? null : new HashSet<string>(onlyJobs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        var jobs = census.Where(j => j.HasStickFile && (only is null || only.Contains(j.Job))).ToList();
        log($"{jobs.Count} set(s) to analyze; tool built {built:yyyy-MM-dd HH:mm} UTC; run {runId}");

        var sets = new SetRow?[jobs.Count];
        var sheets = new List<SheetRow>[jobs.Count];
        int rebuilt = 0, reused = 0, done = 0;
        var gate = new object();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            var job = jobs[i];
            var issue = job.Newest!;
            string work = Path.Combine(workRoot, job.Job);
            var rows = new List<SheetRow>();
            SetRow row;
            try
            {
                // the mirror is the local copy the tests use; DrawingMirror copies only when size or date differ
                string pdf = DrawingMirror.SingleFile(issue.Path);
                string? yardstick = YardstickFor(job, yardstickFolder);
                string stamp = $"{issue.Bytes}|{File.GetLastWriteTimeUtc(issue.Path):O}|{built:O}|{(yardstick is null ? "-" : Path.GetFileName(yardstick) + ":" + new FileInfo(yardstick).Length)}";
                string manifest = Path.Combine(work, "manifest.txt");
                // --reuse: the builds stand whatever the tool's stamp says - for a change proven outside the
                // build path (the six-set bank byte-identical), so the yardstick pass over 292 built models
                // costs minutes, not the hours of rebuilding them. The build part of the stamp still has to
                // match: a changed PDF is rebuilt regardless.
                string buildStamp = $"{issue.Bytes}|{File.GetLastWriteTimeUtc(issue.Path):O}";
                bool manifestStands = File.Exists(manifest) && File.Exists(Path.Combine(work, "set.csv"))
                    && (File.ReadAllText(manifest).Trim() == stamp || ((reuseBuilds || recompose) && File.ReadAllText(manifest).Trim().StartsWith(buildStamp + "|", StringComparison.Ordinal)));
                if (!force && manifestStands && recompose && File.ReadAllText(manifest).Trim().StartsWith(buildStamp + "|", StringComparison.Ordinal))
                {
                    // --recompose: the views a previous build wrote stand; the ladder and the composer run again
                    // (a change to how storeys are found, step 45), and the set's and sheets' rows are remade from
                    // the new model - the sheets' reader-side columns kept, their composer-side ones re-joined
                    var kept = ReadSetRow(Path.Combine(work, "set.csv"), runId, runAt);
                    var keptSheets = ReadSheetRows(Path.Combine(work, "sheets.csv"), runId).ToList();
                    var sheetsResult = new PdfOnlyBuild.SheetsResult(
                        keptSheets.Select(r => new PdfOnlyBuild.SheetOutcome(r.Page, r.SheetNumber, r.SheetType, r.Title, r.Level, r.ScaleNote, r.ScaleDenominator, 0, 0, r.Slabs, r.Columns, r.Walls, 0, r.Lines,
                            r.DxfFiles is null ? [] : r.DxfFiles.Split(" | "), r.SelfCheck ?? "", r.Failure)).ToList(),
                        [], kept.SheetsWritten, 0, kept.SheetsNotPlan, kept.SheetsFailed, 0, 0, 0, 0, 0, 0, 0);
                    var outcome = PdfOnlyBuild.Recompose(pdf, work, kept.Pages, sheetsResult, options, rulesConnection);
                    (row, rows) = Rows(outcome, job, issue, runId, runAt, built);
                    row = row with { AssemblyCards = kept.AssemblyCards, Seconds = kept.Seconds + outcome.Elapsed.TotalSeconds };
                    if (yardstick is not null && outcome.Model is not null) row = Measured(row, outcome.OutputE2k, yardstick, work);
                    else if (yardstick is not null) row = row with { Yardstick = yardstick };
                    WriteSetCsv(Path.Combine(work, "set.csv"), [row]);
                    WriteSheetCsv(Path.Combine(work, "sheets.csv"), rows);
                    File.WriteAllText(manifest, stamp);
                    lock (gate) rebuilt++;
                }
                else if (!force && manifestStands)
                {
                    row = ReadSetRow(Path.Combine(work, "set.csv"), runId, runAt) with { RunId = runId, RunAtUtc = runAt };
                    rows.AddRange(ReadSheetRows(Path.Combine(work, "sheets.csv"), runId));
                    string outE2k = Path.Combine(work, "out.e2k");
                    if (yardstick is not null && row.HasModel && File.Exists(outE2k) && (row.OursCompared is null || reuseBuilds))
                    {
                        row = Measured(row, outE2k, yardstick, work);
                        WriteSetCsv(Path.Combine(work, "set.csv"), [row]);
                        File.WriteAllText(manifest, stamp);
                    }
                    lock (gate) reused++;
                }
                else
                {
                    var outcome = PdfOnlyBuild.Build(pdf, work, options.FallbackScale, options, rulesConnection);
                    (row, rows) = Rows(outcome, job, issue, runId, runAt, built);
                    if (yardstick is not null && outcome.Model is not null) row = Measured(row, outcome.OutputE2k, yardstick, work);
                    else if (yardstick is not null) row = row with { Yardstick = yardstick };
                    Directory.CreateDirectory(work);
                    WriteSetCsv(Path.Combine(work, "set.csv"), [row]);
                    WriteSheetCsv(Path.Combine(work, "sheets.csv"), rows);
                    File.WriteAllText(manifest, stamp);
                    lock (gate) rebuilt++;
                }
            }
            catch (Exception ex)
            {
                row = new SetRow(runId, runAt, built, job.Job, job.Category, "structural", issue.Path, issue.Date, issue.DateFromName, issue.Bytes,
                    0, 0, 0, 0, 0, 0, 0, false, null, null, null, null, null, null, null, 0, $"{ex.GetType().Name}: {ex.Message}");
            }
            sets[i] = row;
            sheets[i] = rows;
            int n;
            lock (gate) n = ++done;
            string yard = row.OursCompared is int oc && oc > 0 ? $"; yardstick: {row.SharedStoreys} shared storeys, {row.OursWithin100}/{oc} of ours within 100 mm{(row.FrameFromGrids == true ? "" : " (frame from columns)")}" : row.Yardstick is not null ? "; yardstick: no shared storey with columns" : "";
            log($"  [{n}/{jobs.Count}] {job.Job} {(row.Error is not null ? "ERROR " + row.Error : row.HasModel ? $"model: {row.StoreysBuilt} storeys, {row.Walls} walls, {row.Columns} columns, {row.SheetsPlaced}/{row.SheetsWritten} placed" : "no model: " + row.ModelError)}{yard}  {row.Seconds:F0} s");
        });

        var allSets = sets.Where(s => s is not null).Select(s => s!).OrderBy(s => s.Job, StringComparer.Ordinal).ToList();
        var allSheets = sheets.Where(s => s is not null).SelectMany(s => s).OrderBy(s => s.Job, StringComparer.Ordinal).ThenBy(s => s.Page).ToList();
        WriteSetCsv(Path.Combine(workRoot, "ledger-sets.csv"), allSets);
        WriteSheetCsv(Path.Combine(workRoot, "ledger-sheets.csv"), allSheets);
        return new RunResult(runId, allSets, allSheets, rebuilt, reused, watch.Elapsed);
    }

    /// <summary>The set's row with its yardstick figures, and the comparison's own summary beside the model.</summary>
    private static SetRow Measured(SetRow row, string outE2k, string yardstick, string work)
    {
        try
        {
            var c = ModelYardstick.Compare(outE2k, yardstick);
            File.WriteAllText(Path.Combine(work, "yardstick.txt"), ModelYardstick.Summary(c));
            return row with
            {
                Yardstick = yardstick, YardstickStoreys = c.YardstickStoreys, SharedStoreys = c.Storeys.Count, FrameFromGrids = c.ShiftFromGrids, FrameSupport = c.FrameSupport,
                OursCompared = c.OursCompared, OursMedianMm = c.OursCompared == 0 ? null : c.OursMedianMm, OursWithin100 = c.OursWithin100,
                TheirsCompared = c.TheirsCompared, TheirsWithin100 = c.TheirsWithin100, YardstickNote = c.Notes.Count == 0 ? null : string.Join("; ", c.Notes),
            };
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
        {
            return row with { Yardstick = yardstick, YardstickNote = $"yardstick unreadable: {ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static (SetRow, List<SheetRow>) Rows(PdfOnlyBuild.BuildOutcome o, StickFileCorpus.JobCensus job, StickFileCorpus.Issue issue, Guid runId, DateTime runAt, DateTime built)
    {
        var model = o.Model;
        // the composer's view of each written view, by file name: which storeys it went to, and its flags
        var byFile = new Dictionary<string, Dxf.SheetOutcome>(StringComparer.OrdinalIgnoreCase);
        if (model is not null) foreach (var s in model.Sheets) byFile[Path.GetFileName(s.File)] = s;
        var placedFiles = new HashSet<string>(model?.SheetsSetOnGridByName ?? [], StringComparer.OrdinalIgnoreCase);
        int? placed = model is null ? null : placedFiles.Count;

        var rows = new List<SheetRow>();
        foreach (var s in o.Sheets.Sheets)
        {
            bool? wasPlaced = null;
            var storeys = new List<string>();
            var flags = new List<string>();
            if (model is not null && s.DxfFiles.Count > 0)
            {
                wasPlaced = s.DxfFiles.Any(f => placedFiles.Contains(f));
                foreach (var f in s.DxfFiles)
                    if (byFile.TryGetValue(f, out var c)) { storeys.AddRange(c.Stories); flags.AddRange(c.Flags); }
            }
            rows.Add(new SheetRow(runId, job.Job, s.Page, s.SheetNumber, s.SheetType, s.Title, s.Level, s.ScaleNote, s.ScaleDenominator,
                s.Slabs, s.Columns, s.Walls, s.Lines, s.DxfFiles.Count == 0 ? null : string.Join(" | ", s.DxfFiles), s.SelfCheck.Trim().Length == 0 ? null : s.SelfCheck.Trim(),
                wasPlaced, storeys.Count == 0 ? null : string.Join(",", storeys.Distinct()), flags.Count == 0 ? null : string.Join("; ", flags.Distinct()), s.Failure));
        }

        var saved = model?.SavedModel;
        var row = new SetRow(runId, runAt, built, job.Job, job.Category, "structural", issue.Path, issue.Date, issue.DateFromName, issue.Bytes,
            o.Pages, o.Sheets.Sheets.Count(s => s.IsPlan), o.Sheets.Written, o.Sheets.Failed, o.Sheets.NotPlan, o.Sheets.Assemblies.Count,
            o.Levels?.Levels.Count ?? 0, model is not null, o.ModelError, placed,
            saved?.Storeys.Count, saved?.Walls, saved?.Columns, saved?.Floors, saved?.PlatesByStorey.Count(p => p.Value > 0),
            o.Elapsed.TotalSeconds, null);
        return (row, rows);
    }

    /// <summary>The corpus in one paragraph: X of Y, never a sample.</summary>
    public static string Summary(RunResult r)
    {
        var sb = new StringBuilder();
        var sets = r.Sets;
        int n = sets.Count;
        sb.AppendLine(CultureInfo.InvariantCulture, $"{n} sets: {r.Rebuilt} built, {r.Reused} reused from their last run; {r.Elapsed.TotalMinutes:F0} min; run {r.RunId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {sets.Count(s => s.HasModel)} of {n} build a model; {sets.Count(s => s.Error is not null)} the analyzer failed on");
        foreach (var g in sets.Where(s => !s.HasModel && s.Error is null).GroupBy(s => Reason(s.ModelError)).OrderByDescending(g => g.Count()))
            sb.AppendLine(CultureInfo.InvariantCulture, $"    no model, {g.Count()} set(s): {g.Key}");
        var with = sets.Where(s => s.HasModel).ToList();
        if (with.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  of the {with.Count} with a model: {with.Sum(s => s.SheetsWritten)} plan views, {with.Sum(s => s.SheetsPlaced ?? 0)} set on the grid by axis name; " +
                $"{with.Sum(s => s.StoreysBuilt ?? 0)} storeys, {with.Sum(s => s.StoreysWithPlate ?? 0)} with a plate; {with.Sum(s => s.Walls ?? 0):N0} walls, {with.Sum(s => s.Columns ?? 0):N0} columns");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  sets with every view placed: {with.Count(s => s.SheetsPlaced == s.SheetsWritten)} of {with.Count}; with a plate on every storey: {with.Count(s => s.StoreysWithPlate == s.StoreysBuilt)} of {with.Count}");
        }
        var yard = sets.Where(s => s.OursCompared is > 0).ToList();
        if (yard.Count > 0 || sets.Any(s => s.Yardstick is not null))
        {
            int oursAll = yard.Sum(s => s.OursCompared ?? 0), within = yard.Sum(s => s.OursWithin100 ?? 0);
            int theirsAll = yard.Sum(s => s.TheirsCompared ?? 0), theirsWithin = yard.Sum(s => s.TheirsWithin100 ?? 0);
            sb.AppendLine(CultureInfo.InvariantCulture, $"  yardsticks: {sets.Count(s => s.Yardstick is not null)} sets have the engineer's model; {yard.Count} share a storey with columns; frames from grids {yard.Count(s => s.FrameFromGrids == true)}, from columns {yard.Count(s => s.FrameFromGrids == false)}");
            if (oursAll > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"    ours -> theirs: {within} of {oursAll} of our columns within 100 mm of one of theirs ({100.0 * within / oursAll:F0}%); theirs -> ours: {theirsWithin} of {theirsAll} ({100.0 * theirsWithin / Math.Max(1, theirsAll):F0}%)");
            foreach (var g in yard.GroupBy(s => s.OursCompared is int c && c > 0 ? (100 * (s.OursWithin100 ?? 0) / c) / 25 * 25 : 0).OrderByDescending(g => g.Key))
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {g.Count()} set(s) with {g.Key}-{g.Key + 24}% of our columns within 100 mm: {string.Join(" ", g.Select(s => s.Job).Take(12))}{(g.Count() > 12 ? " ..." : "")}");
        }
        var sheets = r.Sheets;
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {sheets.Count} pages read: {sheets.Count(s => s.SheetType == "plan")} plans, {sheets.Count(s => s.SheetType == "failed")} failed; " +
            $"plans not placed on a grid: {sheets.Count(s => s.Placed == false)}");
        foreach (var g in sheets.Where(s => s.SheetType == "failed").GroupBy(s => Reason(s.Failure)).OrderByDescending(g => g.Count()).Take(5))
            sb.AppendLine(CultureInfo.InvariantCulture, $"    failed, {g.Count()} page(s): {g.Key}");
        return sb.ToString();
    }

    /// <summary>A message's stable part: the words before the first number or quoted name, so like failures group.</summary>
    internal static string Reason(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(none)";
        var sb = new StringBuilder();
        foreach (char c in message)
        {
            if (char.IsDigit(c) || c == '\'' || c == '"') break;
            sb.Append(c);
        }
        string r = sb.ToString().Trim();
        return r.Length == 0 ? message.Trim() : r;
    }

    // ---- CSV, both directions (the manifest's cache is these files) ----

    private static string Q(object? v) => v is null ? "" : "\"" + Convert.ToString(v, CultureInfo.InvariantCulture)!.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static readonly string SetHeader = "run_id,run_at_utc,tool_built_at_utc,job,category,set_kind,pdf,issue_date,issue_date_from_name,bytes,pages,plan_sheets,sheets_written,sheets_failed,sheets_not_plan,assembly_cards,storeys_read,has_model,model_error,sheets_placed,storeys_built,walls,columns,floors,storeys_with_plate,seconds,error,yardstick,yardstick_storeys,shared_storeys,frame_from_grids,frame_support,ours_compared,ours_median_mm,ours_within_100,theirs_compared,theirs_within_100,yardstick_note";
    private static readonly string SheetHeader = "run_id,job,page,sheet_number,sheet_type,title,level,scale_note,scale_denominator,slabs,columns,walls,lines,dxf_files,self_check,placed,storeys,flags,failure";

    public static void WriteSetCsv(string path, IReadOnlyList<SetRow> rows)
    {
        var lines = new List<string> { SetHeader };
        foreach (var s in rows)
            lines.Add(string.Join(",", Q(s.RunId), Q(s.RunAtUtc.ToString("O")), Q(s.ToolBuiltAtUtc.ToString("O")), Q(s.Job), Q(s.Category), Q(s.SetKind), Q(s.Pdf), Q(s.IssueDate), Q(s.IssueDateFromName), Q(s.Bytes),
                Q(s.Pages), Q(s.PlanSheets), Q(s.SheetsWritten), Q(s.SheetsFailed), Q(s.SheetsNotPlan), Q(s.AssemblyCards), Q(s.StoreysRead), Q(s.HasModel), Q(s.ModelError), Q(s.SheetsPlaced),
                Q(s.StoreysBuilt), Q(s.Walls), Q(s.Columns), Q(s.Floors), Q(s.StoreysWithPlate), Q(s.Seconds), Q(s.Error),
                Q(s.Yardstick), Q(s.YardstickStoreys), Q(s.SharedStoreys), Q(s.FrameFromGrids), Q(s.FrameSupport), Q(s.OursCompared), Q(s.OursMedianMm), Q(s.OursWithin100), Q(s.TheirsCompared), Q(s.TheirsWithin100), Q(s.YardstickNote)));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    public static void WriteSheetCsv(string path, IReadOnlyList<SheetRow> rows)
    {
        var lines = new List<string> { SheetHeader };
        foreach (var s in rows)
            lines.Add(string.Join(",", Q(s.RunId), Q(s.Job), Q(s.Page), Q(s.SheetNumber), Q(s.SheetType), Q(s.Title), Q(s.Level), Q(s.ScaleNote), Q(s.ScaleDenominator),
                Q(s.Slabs), Q(s.Columns), Q(s.Walls), Q(s.Lines), Q(s.DxfFiles), Q(s.SelfCheck), Q(s.Placed), Q(s.Storeys), Q(s.Flags), Q(s.Failure)));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static SetRow ReadSetRow(string path, Guid runId, DateTime runAt)
        => ParseSetRow(File.ReadAllLines(path)[1]) with { RunId = runId, RunAtUtc = runAt };

    /// <summary>Every set row of a ledger CSV (the analyzer's `ledger-sets.csv`, or a banked copy under docs/etabs-handoff/corpus/).</summary>
    public static IReadOnlyList<SetRow> ReadSets(string path) => File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0).Select(ParseSetRow).ToList();

    /// <summary>Every sheet row of a ledger CSV (`ledger-sheets.csv`).</summary>
    public static IReadOnlyList<SheetRow> ReadSheets(string path) => File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0).Select(l => ParseSheetRow(l, Guid.Empty)).ToList();

    private static SetRow ParseSetRow(string line)
    {
        var f = Csv.Parse(line);
        static int? I(string s) => s.Length == 0 ? null : int.Parse(s, CultureInfo.InvariantCulture);
        static string? S(string s) => s.Length == 0 ? null : s;
        Guid runId = Guid.TryParse(f[0], out var g) ? g : Guid.Empty;
        DateTime runAt = DateTime.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : default;
        return new SetRow(runId, runAt, DateTime.Parse(f[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), f[3], f[4], f[5], f[6], S(f[7]), bool.Parse(f[8]), long.Parse(f[9], CultureInfo.InvariantCulture),
            int.Parse(f[10], CultureInfo.InvariantCulture), int.Parse(f[11], CultureInfo.InvariantCulture), int.Parse(f[12], CultureInfo.InvariantCulture), int.Parse(f[13], CultureInfo.InvariantCulture),
            int.Parse(f[14], CultureInfo.InvariantCulture), int.Parse(f[15], CultureInfo.InvariantCulture), int.Parse(f[16], CultureInfo.InvariantCulture), bool.Parse(f[17]), S(f[18]), I(f[19]), I(f[20]), I(f[21]), I(f[22]), I(f[23]), I(f[24]),
            double.Parse(f[25], CultureInfo.InvariantCulture), S(f[26]),
            f.Count > 37 ? S(f[27]) : null, f.Count > 37 ? I(f[28]) : null, f.Count > 37 ? I(f[29]) : null, f.Count > 37 && f[30].Length > 0 ? bool.Parse(f[30]) : null, f.Count > 37 ? I(f[31]) : null,
            f.Count > 37 ? I(f[32]) : null, f.Count > 37 && f[33].Length > 0 ? double.Parse(f[33], CultureInfo.InvariantCulture) : null, f.Count > 37 ? I(f[34]) : null, f.Count > 37 ? I(f[35]) : null, f.Count > 37 ? I(f[36]) : null, f.Count > 37 ? S(f[37]) : null);
    }

    private static IEnumerable<SheetRow> ReadSheetRows(string path, Guid runId)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadAllLines(path).Skip(1))
            yield return ParseSheetRow(line, runId);
    }

    private static SheetRow ParseSheetRow(string line, Guid runId)
    {
        static int? I(string s) => s.Length == 0 ? null : int.Parse(s, CultureInfo.InvariantCulture);
        static string? S(string s) => s.Length == 0 ? null : s;
        var f = Csv.Parse(line);
        if (runId == Guid.Empty && Guid.TryParse(f[0], out var g)) runId = g;
        return new SheetRow(runId, f[1], int.Parse(f[2], CultureInfo.InvariantCulture), S(f[3]), f[4], S(f[5]), S(f[6]), S(f[7]), I(f[8]),
            int.Parse(f[9], CultureInfo.InvariantCulture), int.Parse(f[10], CultureInfo.InvariantCulture), int.Parse(f[11], CultureInfo.InvariantCulture), int.Parse(f[12], CultureInfo.InvariantCulture),
            S(f[13]), S(f[14]), f[15].Length == 0 ? null : bool.Parse(f[15]), S(f[16]), S(f[17]), S(f[18]));
    }

    /// <summary>A quoted-field CSV line back into its fields (the writer above's inverse).</summary>
    internal static class Csv
    {
        public static List<string> Parse(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }

    // ---- the ledger tables, when they exist ----

    /// <summary>
    /// Every row into analysis.IntakeSet / analysis.IntakeSheet. Returns what happened in one line:
    /// how many rows, or why nothing was written (no connection, tables not there — migration 083).
    /// </summary>
    public static string WriteLedger(RunResult r, string? connectionString)
    {
        connectionString ??= Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return "ledger not written: no KorStandards connection (KOR_ENGINEERINGTOOLS_STANDARDSDB)";
        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using (var check = new SqlCommand("SELECT CASE WHEN OBJECT_ID('analysis.IntakeSet','U') IS NULL OR OBJECT_ID('analysis.IntakeSheet','U') IS NULL THEN 0 ELSE 1 END", conn))
                if ((int)check.ExecuteScalar()! == 0) return "ledger not written: analysis.IntakeSet / IntakeSheet not there yet (migration 083)";

            using var tx = conn.BeginTransaction();
            int sets = 0, sheets = 0;
            using (var cmd = new SqlCommand(
                "INSERT INTO analysis.IntakeSet (RunId,RunAtUtc,ToolBuiltAtUtc,Job,Category,SetKind,Pdf,IssueDate,IssueDateFromName,Bytes,Pages,PlanSheets,SheetsWritten,SheetsFailed,SheetsNotPlan,AssemblyCards,StoreysRead,HasModel,ModelError,SheetsPlaced,StoreysBuilt,Walls,Columns,Floors,StoreysWithPlate,Seconds,Error," +
                "Yardstick,YardstickStoreys,SharedStoreys,FrameFromGrids,FrameSupport,OursCompared,OursMedianMm,OursWithin100,TheirsCompared,TheirsWithin100,YardstickNote) " +
                "VALUES (@RunId,@RunAtUtc,@ToolBuiltAtUtc,@Job,@Category,@SetKind,@Pdf,@IssueDate,@IssueDateFromName,@Bytes,@Pages,@PlanSheets,@SheetsWritten,@SheetsFailed,@SheetsNotPlan,@AssemblyCards,@StoreysRead,@HasModel,@ModelError,@SheetsPlaced,@StoreysBuilt,@Walls,@Columns,@Floors,@StoreysWithPlate,@Seconds,@Error," +
                "@Yardstick,@YardstickStoreys,@SharedStoreys,@FrameFromGrids,@FrameSupport,@OursCompared,@OursMedianMm,@OursWithin100,@TheirsCompared,@TheirsWithin100,@YardstickNote)", conn, tx))
            {
                foreach (var s in r.Sets)
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@RunId", s.RunId); cmd.Parameters.AddWithValue("@RunAtUtc", s.RunAtUtc); cmd.Parameters.AddWithValue("@ToolBuiltAtUtc", s.ToolBuiltAtUtc);
                    cmd.Parameters.AddWithValue("@Job", s.Job); cmd.Parameters.AddWithValue("@Category", s.Category); cmd.Parameters.AddWithValue("@SetKind", s.SetKind);
                    cmd.Parameters.AddWithValue("@Pdf", Trunc(s.Pdf, 400)); cmd.Parameters.AddWithValue("@IssueDate", (object?)s.IssueDate ?? DBNull.Value); cmd.Parameters.AddWithValue("@IssueDateFromName", s.IssueDateFromName);
                    cmd.Parameters.AddWithValue("@Bytes", s.Bytes); cmd.Parameters.AddWithValue("@Pages", s.Pages); cmd.Parameters.AddWithValue("@PlanSheets", s.PlanSheets);
                    cmd.Parameters.AddWithValue("@SheetsWritten", s.SheetsWritten); cmd.Parameters.AddWithValue("@SheetsFailed", s.SheetsFailed); cmd.Parameters.AddWithValue("@SheetsNotPlan", s.SheetsNotPlan);
                    cmd.Parameters.AddWithValue("@AssemblyCards", s.AssemblyCards); cmd.Parameters.AddWithValue("@StoreysRead", s.StoreysRead); cmd.Parameters.AddWithValue("@HasModel", s.HasModel);
                    cmd.Parameters.AddWithValue("@ModelError", (object?)Trunc(s.ModelError, 1000) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SheetsPlaced", (object?)s.SheetsPlaced ?? DBNull.Value); cmd.Parameters.AddWithValue("@StoreysBuilt", (object?)s.StoreysBuilt ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Walls", (object?)s.Walls ?? DBNull.Value); cmd.Parameters.AddWithValue("@Columns", (object?)s.Columns ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Floors", (object?)s.Floors ?? DBNull.Value); cmd.Parameters.AddWithValue("@StoreysWithPlate", (object?)s.StoreysWithPlate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Seconds", (float)s.Seconds); cmd.Parameters.AddWithValue("@Error", (object?)Trunc(s.Error, 1000) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Yardstick", (object?)Trunc(s.Yardstick, 400) ?? DBNull.Value); cmd.Parameters.AddWithValue("@YardstickStoreys", (object?)s.YardstickStoreys ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SharedStoreys", (object?)s.SharedStoreys ?? DBNull.Value); cmd.Parameters.AddWithValue("@FrameFromGrids", (object?)s.FrameFromGrids ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FrameSupport", (object?)s.FrameSupport ?? DBNull.Value); cmd.Parameters.AddWithValue("@OursCompared", (object?)s.OursCompared ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@OursMedianMm", s.OursMedianMm is double om ? (float)om : DBNull.Value); cmd.Parameters.AddWithValue("@OursWithin100", (object?)s.OursWithin100 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@TheirsCompared", (object?)s.TheirsCompared ?? DBNull.Value); cmd.Parameters.AddWithValue("@TheirsWithin100", (object?)s.TheirsWithin100 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@YardstickNote", (object?)Trunc(s.YardstickNote, 1000) ?? DBNull.Value);
                    sets += cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = new SqlCommand(
                "INSERT INTO analysis.IntakeSheet (RunId,Job,Page,SheetNumber,SheetType,Title,Level,ScaleNote,ScaleDenominator,Slabs,Columns,Walls,Lines,DxfFiles,SelfCheck,Placed,Storeys,Flags,Failure) " +
                "VALUES (@RunId,@Job,@Page,@SheetNumber,@SheetType,@Title,@Level,@ScaleNote,@ScaleDenominator,@Slabs,@Columns,@Walls,@Lines,@DxfFiles,@SelfCheck,@Placed,@Storeys,@Flags,@Failure)", conn, tx))
            {
                foreach (var s in r.Sheets)
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@RunId", s.RunId); cmd.Parameters.AddWithValue("@Job", s.Job); cmd.Parameters.AddWithValue("@Page", s.Page);
                    cmd.Parameters.AddWithValue("@SheetNumber", (object?)Trunc(s.SheetNumber, 32) ?? DBNull.Value); cmd.Parameters.AddWithValue("@SheetType", Trunc(s.SheetType, 32)!);
                    cmd.Parameters.AddWithValue("@Title", (object?)Trunc(s.Title, 200) ?? DBNull.Value); cmd.Parameters.AddWithValue("@Level", (object?)Trunc(s.Level, 64) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ScaleNote", (object?)Trunc(s.ScaleNote, 64) ?? DBNull.Value); cmd.Parameters.AddWithValue("@ScaleDenominator", (object?)s.ScaleDenominator ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Slabs", s.Slabs); cmd.Parameters.AddWithValue("@Columns", s.Columns); cmd.Parameters.AddWithValue("@Walls", s.Walls); cmd.Parameters.AddWithValue("@Lines", s.Lines);
                    cmd.Parameters.AddWithValue("@DxfFiles", (object?)Trunc(s.DxfFiles, 600) ?? DBNull.Value); cmd.Parameters.AddWithValue("@SelfCheck", (object?)Trunc(s.SelfCheck, 200) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Placed", (object?)s.Placed ?? DBNull.Value); cmd.Parameters.AddWithValue("@Storeys", (object?)Trunc(s.Storeys, 200) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Flags", (object?)Trunc(s.Flags, 1000) ?? DBNull.Value); cmd.Parameters.AddWithValue("@Failure", (object?)Trunc(s.Failure, 1000) ?? DBNull.Value);
                    sheets += cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return $"ledger: {sets} set row(s) and {sheets} sheet row(s) written to analysis.IntakeSet / IntakeSheet under run {r.RunId}";
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException)
        {
            return $"ledger not written: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
