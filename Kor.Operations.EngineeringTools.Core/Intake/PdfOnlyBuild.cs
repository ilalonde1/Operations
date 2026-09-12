#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// The PDF-only route to a model, as ONE call: every page of a set through the one ingestion point
/// (<see cref="DrawingIntake.ReadSheet"/>), the plan sheets written as named views on the office's
/// layers, the set's storeys read off its elevations, and the composer run on the result. This is
/// what <c>takeoff pdf-takeoff</c>, <c>pdf-levels</c> and <c>dxf-to-etabs</c> do in a row, and what
/// the harness script chained by hand until 2026-09-11; now the verbs, the six-set test and the
/// corpus analyzer share it, so a set built by any of them is built the same way.
/// </summary>
/// <remarks>
/// WHAT THIS IS NOT YET: the in-memory handoff (completion plan WP4) — the plan geometry still goes
/// to the composer as scratch DXF on disk, exactly as the verbs wrote it, so that the six banked
/// models stay byte-identical while the orchestration moves into Core. The DXF folder is the
/// contract until WP4 replaces it with <c>PlanGeometrySet</c>s in memory.
/// </remarks>
public static class PdfOnlyBuild
{
    /// <summary>One page's outcome at the ingestion point: what it was, what it held, what was written from it.</summary>
    public sealed record SheetOutcome(
        int Page, string? SheetNumber, string SheetType, string? Title, string? Level, string? ScaleNote, int? ScaleDenominator,
        int RawPaths, int AnnotationPaths, int Slabs, int Columns, int Walls, int Footings, int Lines,
        IReadOnlyList<string> DxfFiles, string SelfCheck, string? Failure)
    {
        public bool IsPlan => SheetType == "plan";
        public bool Written => DxfFiles.Count > 0;
    }

    /// <summary>The set's sheets, and the totals the verb prints under its table.</summary>
    public sealed record SheetsResult(
        IReadOnlyList<SheetOutcome> Sheets, IReadOnlyList<AssemblySchedule.Assembly> Assemblies,
        int Written, int Empty, int NotPlan, int Failed,
        int DimensionStrings, int PatternCells, int Tags, int Typed, int Partitions, int NotWalls, int Untagged);

    /// <summary>
    /// Every page from <paramref name="first"/> to <paramref name="last"/> read and, when it is a plan
    /// with structure on it, written as DXF view(s) beside <paramref name="outDxf"/> — one file per
    /// view in range mode (more than one page), the single file named by <paramref name="outDxf"/>
    /// otherwise. <paramref name="onSheet"/> sees each outcome as it lands, in page order.
    /// </summary>
    public static SheetsResult WriteSheets(
        string pdf, string outDxf, int first, int last, int scale, bool markup, bool korLayers,
        PdfIntakeOptions options, Action<SheetOutcome>? onSheet = null, Action<IReadOnlyList<AssemblySchedule.Assembly>>? onAssemblies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdf);
        ArgumentNullException.ThrowIfNull(options);
        if (first < 1) first = 1;
        if (last < first) last = first;
        bool range = last > first;
        string dir = Path.GetDirectoryName(Path.GetFullPath(outDxf)) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(outDxf);
        Directory.CreateDirectory(dir);

        using var doc = PdfDocument.Open(pdf);
        var facts = DocumentFacts.From(doc);
        // the set's assembly schedule, read once (step 32), so every plan's walls can take their tags (step 33)
        IReadOnlyList<AssemblySchedule.Assembly> assemblies = [];
        try { assemblies = AssemblySchedule.ReadSet(pdf, options.AssemblyStructuralWords, options.AssemblyPartitionWords); } catch { }
        if (assemblies.Count > 0) onAssemblies?.Invoke(assemblies);
        var request = new IntakeRequest(scale, options, markup, assemblies);

        var sheets = new List<SheetOutcome>();
        int written = 0, empty = 0, notPlan = 0, failed = 0;
        int typed = 0, partitions = 0, untagged = 0, tags = 0, notWalls = 0, dimensionStrings = 0, patternCells = 0;
        for (int p = first; p <= last; p++)
        {
            SheetRecord record;
            try { record = DrawingIntake.ReadSheet(doc, p, request, facts); }
            catch (Exception ex)
            {
                failed++;
                var failure = new SheetOutcome(p, null, "failed", null, null, null, null, 0, 0, 0, 0, 0, 0, 0, [], "", $"{ex.GetType().Name}: {ex.Message}");
                sheets.Add(failure); onSheet?.Invoke(failure);
                continue;
            }

            var geo = record.Geometry;
            string? title = record.BookmarkTitle;
            // A DXF PLAN IS WRITTEN FROM A PLAN SHEET. The classifier runs on every page and the record
            // keeps what it found; a section's cut-wall poché or a detail's outline is not a storey and
            // does not go to the model. S3.01 on 31168 yielded 36 "walls" before this (brief 17).
            if (record.SheetType != "plan")
            {
                notPlan++;
                var other = new SheetOutcome(p, record.SheetNumber, record.SheetType, title, record.Level, record.ScaleNote, record.ScaleDenominator,
                    geo.RawPathCount, record.Context.AnnotationPaths, geo.Slabs.Count, geo.Columns.Count, geo.Walls.Count, geo.Footings.Count, geo.Lines.Count, [], "", null);
                sheets.Add(other); onSheet?.Invoke(other);
                continue;
            }

            int found = geo.Slabs.Count + geo.Columns.Count + geo.Walls.Count + geo.Lines.Count;
            dimensionStrings += record.Context.DimensionStringsReadAsWalls;   // a dimension string is not a wall (step 35), on every set
            patternCells += geo.PatternCells.Count;                             // a pattern's cells are not columns (step 37)
            if (assemblies.Count > 0)
            {
                tags += geo.WallTypeTags.Count;
                typed += geo.WallTypeCodes.Count(c => c is not null);
                for (int wi = 0; wi < geo.Walls.Count; wi++)
                {
                    if (wi < geo.WallIsDimensionString.Count && geo.WallIsDimensionString[wi]) continue;   // counted on its own line
                    bool isTyped = wi < geo.WallTypeCodes.Count && geo.WallTypeCodes[wi] is not null;
                    bool outOfModel = wi < geo.WallIsPartition.Count && geo.WallIsPartition[wi];
                    if (isTyped && outOfModel) partitions++;
                    else if (!isTyped && outOfModel) notWalls++;
                    else if (!isTyped) untagged++;
                }
            }

            var files = new List<string>();
            if (found > 0)
            {
                // Named as the office's export names a view — sheet number, view index, title — so the
                // DXF-to-ETABS reader takes the sheet's storeys from the name (intake step 15); and a
                // sheet drawing two plans side by side is written as two views (intake step 26)
                if (range)
                {
                    foreach (var part in SheetViews.Parts(record, $"{stem}-p{p:00}"))
                    {
                        var pg = part.Geometry;
                        if (pg.Slabs.Count + pg.Columns.Count + pg.Walls.Count + pg.Lines.Count == 0) continue;
                        DxfExporter.Export(pg, Path.Combine(dir, part.FileName), korLayers: korLayers);
                        files.Add(part.FileName);
                        written++;
                    }
                }
                else
                {
                    string dxf = Path.GetFullPath(outDxf);
                    DxfExporter.Export(geo, dxf, korLayers: korLayers);
                    files.Add(Path.GetFileName(dxf));
                    written++;
                }
            }
            else empty++;

            // THE SHEET CHECKED AGAINST ITSELF. The geometry and the schedule are the same facts drawn
            // twice and read here by entirely separate code, so agreement between them is evidence that
            // needs no reference model — which is what every other gate in this repo requires, and why
            // none of them can say anything about the first sheet of a new job.
            string agree = record.ColumnAgreementError is { } error ? $"  (self-check unavailable: {error})" : "";
            if (record.ColumnAgreement is { } check)
            {
                // Coverage uses the drawing's labels; precision exposes excess emitted columns.
                agree = check.LabelsOnThePlan > 0
                    ? $"  cover {check.MatchedToTheirOwnMark}/{check.LabelsOnThePlan} labelled"
                      + $", emitted {check.ColumnsFound} ({check.Precision:0.0}x)"
                    : $"  {check.SizesDeclaredSomewhere}/{check.ColumnsFound} cols declared";
                if (check.MarksDeclaredButNeverFound.Count > 0)
                    agree += $"; unplaced {string.Join(",", check.MarksDeclaredButNeverFound)}";
            }

            var outcome = new SheetOutcome(p, record.SheetNumber, record.SheetType, title, record.Level, record.ScaleNote, record.ScaleDenominator,
                geo.RawPathCount, record.Context.AnnotationPaths, geo.Slabs.Count, geo.Columns.Count, geo.Walls.Count, geo.Footings.Count, geo.Lines.Count,
                files, agree, null);
            sheets.Add(outcome); onSheet?.Invoke(outcome);
        }

        return new SheetsResult(sheets, assemblies, written, empty, notPlan, failed, dimensionStrings, patternCells, tags, typed, partitions, notWalls, untagged);
    }

    /// <summary>The set's storeys as the levels file dxf-to-etabs takes in place of a reference model (the pdf-levels verb's file).</summary>
    public static SetStoreys.Chain WriteLevels(string pdf, string levelsCsv, PdfIntakeOptions options, out SetStoreys.Table table)
    {
        ArgumentNullException.ThrowIfNull(options);
        table = SetStoreys.Read(pdf, options.LevelLabelWords, options.LevelNameWords);
        var chain = SetStoreys.Levels(table);
        var lines = new List<string> { "# unit: mm", "# level,elevation mm — from the stick file's wall elevations; the lowest stated level is 0" };
        foreach (var l in chain.Levels) lines.Add($"{l.Name},{l.ElevationMm:0}");
        File.WriteAllLines(levelsCsv, lines);
        return chain;
    }

    /// <summary>What one set became: its sheets, its storeys, and the model or the reason there is none.</summary>
    public sealed record BuildOutcome(
        string Pdf, int Pages, SheetsResult Sheets, SetStoreys.Chain? Levels, string? LevelsError,
        DxfToEtabsReport? Model, string? ModelError, string OutputE2k, TimeSpan Elapsed);

    /// <summary>
    /// The whole route for one set into <paramref name="workDir"/>: <c>dxf/</c> (the scratch views),
    /// <c>levels.csv</c>, <c>out.e2k</c>. A set the tool cannot use is an ordinary outcome carried in
    /// <see cref="BuildOutcome.ModelError"/>, not an exception — the corpus has hundreds of them.
    /// </summary>
    public static BuildOutcome Build(string pdf, string workDir, int scale, PdfIntakeOptions options, string? rulesConnection = null,
        Action<SheetOutcome>? onSheet = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        string dxfDir = Path.Combine(workDir, "dxf");
        Directory.CreateDirectory(dxfDir);
        int pages;
        using (var doc = PdfDocument.Open(pdf)) pages = doc.NumberOfPages;

        string stem = Path.GetFileNameWithoutExtension(pdf);
        var sheets = WriteSheets(pdf, Path.Combine(dxfDir, stem + ".dxf"), 1, pages, scale, markup: false, korLayers: true, options, onSheet);

        string levelsCsv = Path.Combine(workDir, "levels.csv");
        SetStoreys.Chain? chain = null;
        string? levelsError = null;
        try { chain = WriteLevels(pdf, levelsCsv, options, out _); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException or ArgumentException) { levelsError = $"{ex.GetType().Name}: {ex.Message}"; }

        string outE2k = Path.Combine(workDir, "out.e2k");
        DxfToEtabsReport? model = null;
        string? modelError = null;
        if (sheets.Written > 0 && chain is not null && chain.Levels.Count > 0)
        {
            try
            {
                model = DxfToEtabsService.Run(new DxfToEtabsRequest
                {
                    DxfFolder = dxfDir,
                    ReferenceE2k = string.Empty,
                    OutputE2k = outE2k,
                    LevelsFile = levelsCsv,
                    LevelsUnit = "mm",
                    Classification = new PlanClassificationOptions(),
                    Compose = new ComposeOptions { IncludeFloors = true, InferMissingFloors = false, MembersRiseToStoreyAbove = true },
                    RuleSettingsConnection = rulesConnection,
                    RequireRuleSettings = true,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
            {
                // A drawing set this tool cannot use is an ordinary outcome, not a defect in the tool.
                modelError = $"Cannot build a model from these inputs: {ex.Message}";
            }
            // the composer's report beside the model, as dxf-to-etabs prints it: the WHY for every count in the ledger
            var report = new List<string>();
            if (model is not null)
            {
                report.Add($"Sheets read   : {model.SheetsRead}   placed: {model.SheetsPlaced}   set on the grid by name: {model.SheetsSetOnGridByName.Count}");
                report.Add($"Storeys built : {model.SavedModel.Storeys.Count}   Walls: {model.SavedModel.Walls}   Columns: {model.SavedModel.Columns}   Floors: {model.SavedModel.Floors}");
                report.AddRange(model.Warnings.Select(w => "  - " + w));
            }
            else report.Add(modelError ?? "");
            File.WriteAllLines(Path.Combine(workDir, "report.txt"), report);
        }
        else modelError = sheets.Written == 0 ? "no plan sheet with structure on it" : levelsError ?? "no storeys read off the elevations";

        return new BuildOutcome(pdf, pages, sheets, chain, levelsError, model, modelError, outE2k, watch.Elapsed);
    }
}
