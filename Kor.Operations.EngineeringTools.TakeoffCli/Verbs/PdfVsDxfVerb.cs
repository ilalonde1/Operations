// The takeoff verb `pdf-vs-dxf`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE DIFFERENTIAL AGAINST GROUND TRUTH WE OWN. A job with both a stick-file PDF and a Revit DXF
// export of the same sheets: sheet by sheet, what the PDF side emits against what the DXF side
// reads from the same drawing. See PdfVersusDxf for what it covers and does not (position, yet).
// Usage: takeoff pdf-vs-dxf <pdf> <dxfFolder> --scale N [--rules-db <conn>]
internal static class PdfVsDxfVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-vs-dxf", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff pdf-vs-dxf <pdf> <dxfFolder> --scale N [--rules-db <conn>]"); return 1; }
        string vdPdf = args[1], vdFolder = args[2];
        int vdScale = 0; string? vdRules = null; bool vdViews = false;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out vdScale);
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) vdRules = args[++i];
            else if (args[i].Equals("--views", StringComparison.OrdinalIgnoreCase)) vdViews = true;
        }
        if (vdScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }
        var (vdOptions, vdRulesSource) = PdfIntakeOptions.For(vdRules);
        PdfVersusDxf.Result vd;
        try { vd = PdfVersusDxf.Compare(vdPdf, vdFolder, vdScale, vdOptions); }
        catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 2; }

        Console.WriteLine($"{Path.GetFileName(vdPdf)} 1:{vdScale} vs {vdFolder}   page index from {vd.PageIndexSource}   PDF rules: {vdRulesSource}   DXF rules: compiled defaults in the drawing's unit");
        Console.WriteLine();
        // A Revit export writes several views per sheet (S2.22.1_1, _2, …) and the PDF page carries them
        // all, so the comparison is per SHEET: one PDF page against the sum of its views.
        var bySheet = vd.Pairs.GroupBy(p => p.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Sheet: g.Key, Page: g.First().Page, Title: g.First().PageTitle, Views: g.Count(), IsPlan: g.First().IsPlan, SheetType: g.First().SheetType,
                          PdfSlabs: g.First().PdfSlabs, PdfCols: g.First().PdfColumns, PdfLines: g.First().PdfLines, PdfWalls: g.First().PdfWalls,
                          DxfWalls: g.Sum(p => p.DxfWalls), DxfCols: g.Sum(p => p.DxfColumns), DxfSlabs: g.Sum(p => p.DxfSlabs), DxfOpenings: g.Sum(p => p.DxfOpenings)))
            .OrderBy(s => s.Page).ToList();
        Console.WriteLine("sheet      page views  title                 |  PDF: slabs  cols  lines  walls |  DXF: walls  cols  slabs  openings |  cols Δ");
        foreach (var s in bySheet)
        {
            string title = s.Title.Length > 20 ? s.Title[..20] : s.Title;
            Console.WriteLine($"{s.Sheet,-10} {s.Page,4} {s.Views,5}  {title,-20} |  {s.PdfSlabs,10} {s.PdfCols,5} {s.PdfLines,6} {s.PdfWalls,6} |  {s.DxfWalls,10} {s.DxfCols,5} {s.DxfSlabs,6} {s.DxfOpenings,9} |  {s.PdfCols - s.DxfCols,+6}" +
                              (s.IsPlan ? "" : $"   ({s.SheetType}: not compared)"));
            if (!vdViews) continue;
            // One row per DXF view under its sheet, so a typical plan drawn once on the page and exported
            // once per level (and once more "for reinforcing plan") is seen for what it is.
            foreach (var v in vd.Pairs.Where(p => p.SheetNumber.Equals(s.Sheet, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.DxfFile, StringComparer.OrdinalIgnoreCase))
            {
                string view = Path.GetFileNameWithoutExtension(v.DxfFile);
                int cut = view.IndexOf(s.Sheet, StringComparison.OrdinalIgnoreCase);
                if (cut >= 0) view = view[(cut + s.Sheet.Length)..].TrimStart('_');
                if (view.Length > 44) view = view[..44];
                Console.WriteLine($"{"",-10} {"",4} {"",5}  {"",-20} |  {"",10} {"",5} {"",6} {"",6} |  {v.DxfWalls,10} {v.DxfColumns,5} {v.DxfSlabs,6} {v.DxfOpenings,9} |         {view,-44}  walls: {v.DxfWallLayers}");
            }
        }
        Console.WriteLine();
        // Only plans are compared: a section sheet's cut poché against a section view's says nothing about the intake.
        var plans = bySheet.Where(s => s.IsPlan).ToList();
        int colsAgree = plans.Count(s => s.PdfCols == s.DxfCols);
        int wallsWithinTwo = plans.Count(s => Math.Abs(s.PdfWalls - s.DxfWalls) <= 2);
        Console.WriteLine($"{bySheet.Count} sheet(s) matched from {vd.Pairs.Count} DXF view(s), {plans.Count} of them plans; {vd.Unmatched.Count} DXF file(s) with no PDF page (kept views carry no sheet number).");
        bySheet = plans;
        Console.WriteLine($"Totals over compared plan sheets — PDF: slabs {bySheet.Sum(s => s.PdfSlabs)}, columns {bySheet.Sum(s => s.PdfCols)}, walls {bySheet.Sum(s => s.PdfWalls)} (within two of the DXF on {wallsWithinTwo} of {bySheet.Count} sheets)  |  " +
                          $"DXF: walls {bySheet.Sum(s => s.DxfWalls)}, columns {bySheet.Sum(s => s.DxfCols)}, slabs {bySheet.Sum(s => s.DxfSlabs)}  |  " +
                          $"column counts equal on {colsAgree} of {bySheet.Count} sheets");
        foreach (var u in vd.Unmatched.Take(12)) Console.WriteLine($"  unmatched: {u}");
        if (vd.Unmatched.Count > 12) Console.WriteLine($"  … and {vd.Unmatched.Count - 12} more");
        return 0;
    }
}
