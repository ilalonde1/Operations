// The takeoff verb `pdf-takeoff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// TAKE OFF a drawing PDF's structure to DXF, so a job whose plans arrive only as a stick file can
// enter the same pipeline as one that arrives as CAD: pdf-takeoff -> dxf-to-etabs -> publish.
//
// ⚠ READS THE DRAWING BY DEFAULT, NOT THE MARKUP. PdfToSafe was built for the Bluebeam workflow,
// where the engineer's redlines ARE the model and the page underneath is the architect's noise, and
// its filter defaulted that way with no caller anywhere able to change it. So a CLEAN ISSUED SET —
// which carries no markup at all — read as completely empty and said nothing about why. 31130-01's
// stick file is exactly that: 60 pages, every one carrying thousands of subpaths, every one
// returning nothing. Pass --markup for the old behaviour.
//
// ⚠ The sheet border, title block and schedule tables are page content too, and come out as slabs
// and beams. They are NOT filtered here — window the DXF, or drop those layers downstream.
//
// The scale is OFFERED, never assumed: with no --scale this reports what the sheet itself states
// and stops, because a wrong denominator renders identically and is wrong by a constant.
// Usage: takeoff pdf-takeoff <pdf> <out.dxf> [--page N] [--pages A-B] [--scale 96] [--markup] [--kor-layers] [--rules-db <conn>]
internal static class PdfTakeoffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-takeoff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff pdf-takeoff <pdf> <out.dxf> [--page N] [--pages A-B] [--scale 96] [--markup] [--kor-layers] [--rules-db <conn>]"); return 1; }
        string ptPdf = args[1], ptOut = args[2];
        if (!File.Exists(ptPdf)) { Console.Error.WriteLine($"PDF not found '{ptPdf}'."); return 2; }

        int ptFirst = 1, ptLast = 1, ptScale = 0;
        bool ptMarkup = false, ptKor = false;
        string? ptRulesDb = null;
        for (int i = 3; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--markup", StringComparison.OrdinalIgnoreCase)) ptMarkup = true;
            // The numbers that decide what the linework becomes, from the job's own rules rather than
            // this build's defaults. Without it nothing changes -- the defaults ARE what it did before.
            else if (a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                ptRulesDb = args[++i];
            // Name the layers the way the Revit bridge does, so dxf-to-etabs reads this file with no
            // per-source options and a PDF-derived plan stops being a special case.
            else if (a.Equals("--kor-layers", StringComparison.OrdinalIgnoreCase)) ptKor = true;
            else if (a.Equals("--page", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            { int.TryParse(args[++i], out ptFirst); ptLast = ptFirst; }
            else if (a.Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out ptScale);
            else if (a.Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string[] span = args[++i].Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (span.Length >= 1) int.TryParse(span[0], out ptFirst);
                ptLast = span.Length >= 2 && int.TryParse(span[1], out int pl) ? pl : ptFirst;
            }
        }
        if (ptFirst < 1) ptFirst = 1;
        if (ptLast < ptFirst) ptLast = ptFirst;

        // No --scale: say what the sheet states and stop. Assuming it is how a model ends up 4% out
        // with nothing about the number looking wrong.
        if (ptScale <= 0)
        {
            using var scaleDoc = UglyToad.PdfPig.PdfDocument.Open(ptPdf);
            string? stated = DrawingIntake.ReadSheet(scaleDoc, ptFirst,
                new IntakeRequest(null, PdfIntakeOptions.Default), DocumentFacts.From(scaleDoc)).ScaleNote;
            Console.Error.WriteLine(stated is null
                ? $"No --scale given, and page {ptFirst} states no machine-readable scale (takeoff scale-scan says the same). "
                  + "The set's documented fallback is 1/8\" = 1'-0\", so --scale 96 — but confirm it against the sheet before a model is built on it."
                : $"No --scale given. Page {ptFirst} states \"{stated}\". Pass --scale <denominator> to confirm it (1/8\" = 1'-0\" is 96).");
            return 2;
        }

        var (ptOptions, ptRulesSource) = PdfIntakeOptions.For(ptRulesDb);

        Console.WriteLine($"{Path.GetFileName(ptPdf)}  1:{ptScale}  pages {ptFirst}-{ptLast}  " +
                          $"reading {(ptMarkup ? "MARKUP only" : "the drawing")}  rules: {ptRulesSource}");
        Console.WriteLine();
        Console.WriteLine("page   raw  annot   slabs  columns   walls  footings   lines   file");

        // THE ROUTE IS ONE CALL IN CORE (PdfOnlyBuild.WriteSheets, 2026-09-11): the verb prints what it
        // always printed, and the six-set test and the corpus analyzer build a set the same way.
        var ptResult = PdfOnlyBuild.WriteSheets(ptPdf, ptOut, ptFirst, ptLast, ptScale, ptMarkup, ptKor, ptOptions,
            onSheet: s =>
            {
                if (s.Failure is not null) { Console.WriteLine($"{s.Page,4}   FAILED  {s.Failure}"); return; }
                if (!s.IsPlan) { Console.WriteLine($"{s.Page,4}  {s.SheetType,-18} not a plan sheet; no DXF written"); return; }
                string file = s.DxfFiles.Count == 1 ? s.DxfFiles[0] : s.DxfFiles.Count == 0 ? "" : $"{s.DxfFiles.Count} views: {string.Join(" | ", s.DxfFiles)}";
                Console.WriteLine($"{s.Page,4} {s.RawPaths,5}  {s.AnnotationPaths,5}   {s.Slabs,5}  {s.Columns,7}   {s.Walls,5}  {s.Footings,8}   {s.Lines,5}   {file}{s.SelfCheck}");
            },
            onAssemblies: a =>
                Console.WriteLine($"assembly schedule: {a.Count} card(s) — {a.Count(x => x.IsStructural)} structural, " +
                                  $"{a.Count(x => x.Material == AssemblySchedule.Material.Stud)} stud; walls tagged with these codes are typed, partitions go to KOR_PARTITION"));

        Console.WriteLine();
        Console.WriteLine($"{ptResult.Written} DXF written, {ptResult.Empty} page(s) empty, {ptResult.NotPlan} page(s) not plan sheets.");
        if (ptResult.DimensionStrings > 0)
            Console.WriteLine($"{ptResult.DimensionStrings} wall(s) the two-face reader offered were dimension strings - a length written along them - and were not written.");
        if (ptResult.PatternCells > 0)
            Console.WriteLine($"{ptResult.PatternCells} column-sized shape(s) stood edge to edge in runs of three or more of a size - the cells of a fill pattern, not columns - and were not written.");
        if (ptResult.Assemblies.Count > 0)
            Console.WriteLine($"wall types: {ptResult.Tags} tag(s) on the plans; {ptResult.Typed} wall(s) typed, of which {ptResult.Partitions} partition(s) sent to KOR_PARTITION (not modelled); {ptResult.NotWalls} untagged on plans that tag their walls, so not walls (KOR_PARTITION); {ptResult.Untagged} untagged on plans that do not tag, modelled as drawn.");
        if (ptResult.Empty > 0 && ptMarkup)
            Console.WriteLine("  Empty in --markup mode means the page carries no Bluebeam markup. Drop --markup to read the drawing itself.");
        return ptResult.Written > 0 ? 0 : 3;
    }
}
