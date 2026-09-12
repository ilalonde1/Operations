// The takeoff verb `set-diff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// REISSUE IMPACT (foundation §3.1): what changed between two issues of a set, sheet by sheet, as
// objects. Both issues are read through the one reader; sheets pair by sheet number; the new
// issue is set on the old one's grid by name before anything is called a move.
// Usage: takeoff set-diff <old.pdf> <new.pdf> --scale N [--sheet S2.02] [--rules-db <conn>]
internal static class SetDiffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("set-diff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff set-diff <old.pdf> <new.pdf> --scale N [--sheet S2.02] [--rules-db <conn>]"); return 1; }
        string sdOld = args[1], sdNew = args[2];
        int sdScale = 0; string? sdSheet = null, sdRules = null, sdOverlay = null;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out sdScale);
            else if (args[i].Equals("--sheet", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) sdSheet = args[++i];
            else if (args[i].Equals("--overlay", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) sdOverlay = args[++i];
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) sdRules = args[++i];
        }
        if (!File.Exists(sdOld) || !File.Exists(sdNew)) { Console.Error.WriteLine("Both PDFs must exist."); return 2; }
        if (sdScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }
        var (sdOptions, sdRulesSource) = PdfIntakeOptions.For(sdRules);

        static List<SheetObjects> ReadSet(string pdf, int scale, PdfIntakeOptions options)
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
            var facts = DocumentFacts.From(doc);
            var request = new IntakeRequest(scale, options);
            var sheets = new List<SheetObjects>();
            for (int p = 1; p <= doc.NumberOfPages; p++)
            {
                try { sheets.Add(SheetObjects.From(DrawingIntake.ReadSheet(doc, p, request, facts))); }
                catch (Exception ex) { Console.Error.WriteLine($"{Path.GetFileName(pdf)} p{p}: {ex.GetType().Name}: {ex.Message}"); }
            }
            return sheets;
        }

        var oldSet = ReadSet(sdOld, sdScale, sdOptions);
        var newSet = ReadSet(sdNew, sdScale, sdOptions);
        Console.WriteLine($"{Path.GetFileName(sdOld)} ({oldSet.Count} pages) -> {Path.GetFileName(sdNew)} ({newSet.Count} pages)   1:{sdScale}   rules: {sdRulesSource}");
        Console.WriteLine();

        // pair by sheet number, first occurrence; a page with no number is reported, not compared
        SheetObjects? First(List<SheetObjects> set, string number) => set.FirstOrDefault(s => number.Equals(s.SheetNumber, StringComparison.OrdinalIgnoreCase));
        var numbers = oldSet.Concat(newSet).Where(s => s.SheetNumber is not null).Select(s => s.SheetNumber!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => First(oldSet, n)?.PageNumber ?? int.MaxValue).ThenBy(n => First(newSet, n)?.PageNumber ?? int.MaxValue).ToList();
        var deltas = new List<(string Number, SheetObjects? Old, SheetObjects? New, SheetDiff.SheetDelta? Delta)>();
        foreach (string number in numbers)
        {
            var o = First(oldSet, number); var n = First(newSet, number);
            deltas.Add((number, o, n, o is not null && n is not null ? SheetDiff.Compare(o, n) : null));
        }

        // THE ENGINEER OPENS A PICTURE, NOT A LIST. Every changed sheet painted on the OLD issue's page —
        // where everything in the delta already sits, the new issue having been set on the old grid —
        // green for added, red for removed, orange for moved or changed, cyan for a grid axis moved.
        if (sdOverlay is not null)
        {
            Directory.CreateDirectory(sdOverlay);
            const int dpi = 40;
            var green = new Rgba32(0, 160, 60); var redC = new Rgba32(220, 40, 40); var orange = new Rgba32(240, 140, 0); var cyan = new Rgba32(0, 170, 190); var greyC = new Rgba32(120, 120, 120);
            int painted = 0;
            foreach (var (number, o, n, d) in deltas)
            {
                if (d is null || d.Changes == 0 || o is null) continue;
                using var img = PlanPdfRenderer.RenderPage(sdOld, d.OldPage, dpi);
                double mmToPt = 1.0 / (sdScale * PdfToSafeConstants.PointsToMm), px = dpi / 72.0;
                (int X, int Y) P(double xMm, double yMm) => ((int)Math.Round(xMm * mmToPt * px), (int)Math.Round((o.PageHeightPts - yMm * mmToPt) * px));
                void Plot(int x, int y, Rgba32 c, int w)
                {
                    for (int dx = -w; dx <= w; dx++) for (int dy = -w; dy <= w; dy++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < img.Width && yy < img.Height) img[xx, yy] = c;
                    }
                }
                void Line((int X, int Y) a, (int X, int Y) b, Rgba32 c, int w)
                {
                    int steps = Math.Max(1, Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
                    for (int s = 0; s <= steps; s++) { double f = (double)s / steps; Plot((int)Math.Round(a.X + (b.X - a.X) * f), (int)Math.Round(a.Y + (b.Y - a.Y) * f), c, w); }
                }
                void Poly(IReadOnlyList<(double X, double Y)> pts, Rgba32 c, int w)
                {
                    for (int i = 1; i < pts.Count; i++) Line(P(pts[i - 1].X, pts[i - 1].Y), P(pts[i].X, pts[i].Y), c, w);
                    if (pts.Count > 2) Line(P(pts[^1].X, pts[^1].Y), P(pts[0].X, pts[0].Y), c, w);
                }
                void Box((double X, double Y) at, double halfMm, Rgba32 c, int w)
                    => Poly(new[] { (at.X - halfMm, at.Y - halfMm), (at.X + halfMm, at.Y - halfMm), (at.X + halfMm, at.Y + halfMm), (at.X - halfMm, at.Y + halfMm) }, c, w);

                foreach (var c in d.ColumnsAdded) Box(c, 450, green, 2);
                foreach (var c in d.ColumnsRemoved) Box(c, 450, redC, 2);
                foreach (var m in d.ColumnsMoved) { Line(P(m.From.X, m.From.Y), P(m.To.X, m.To.Y), orange, 2); Box(m.To, 450, orange, 2); }
                foreach (var r in d.ColumnsResized) Box(r.At, 600, orange, 1);
                foreach (var w in d.WallsAdded) Poly(w.Outline, green, 2);
                foreach (var w in d.WallsRemoved) Poly(w.Outline, redC, 2);
                foreach (var w in d.WallsChanged) { Poly(w.From.Outline, orange, 1); Poly(w.To.Outline, orange, 2); }
                foreach (var f in d.FootingsAdded) Poly(f.Outline, green, 2);
                foreach (var f in d.FootingsRemoved) Poly(f.Outline, redC, 2);
                foreach (var f in d.FootingsChanged) Poly(f.To.Outline, orange, 2);
                foreach (var k in d.KindChanges) Box(k.At, 500, greyC, 1);
                double pageWmm = img.Width / px / mmToPt, pageHmm = o.PageHeightPts / mmToPt;
                foreach (var g in d.GridMoved)
                    Poly(g.Vertical ? new[] { (g.ToMm, 0.0), (g.ToMm, pageHmm) } : new[] { (0.0, g.ToMm), (pageWmm, g.ToMm) }, cyan, 1);

                string file = Path.Combine(sdOverlay, SheetDxfName.Sanitise($"{number}-changes") + ".png");
                img.SaveAsPng(file);
                painted++;
                Console.WriteLine($"{number}: {d.Changes} change(s) painted on the old issue's p{d.OldPage} -> {file}");
            }
            Console.WriteLine($"{painted} sheet(s) painted. Green added, red removed, orange moved or changed, cyan a grid axis moved, grey a member re-read.");
            Console.WriteLine();
        }

        if (sdSheet is not null)
        {
            foreach (string wanted in sdSheet.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var one = deltas.FirstOrDefault(d => d.Number.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (one.Delta is null) { Console.WriteLine($"{wanted}: not in both issues."); continue; }
                Console.WriteLine($"{one.Number}  old p{one.Delta.OldPage} -> new p{one.Delta.NewPage}   {one.Delta.FrameNote}");
                Console.WriteLine($"  same: columns {one.Delta.ColumnsSame}, walls {one.Delta.WallsSame}, footings {one.Delta.FootingsSame}; grid axes old {one.Old!.GridAxes.Count}, new {one.New!.GridAxes.Count}");
                foreach (string line in one.Delta.Lines()) Console.WriteLine("  " + line);
                Console.WriteLine($"  {one.Delta.Changes} change(s)");
                Console.WriteLine();
            }
            return 0;
        }

        Console.WriteLine("sheet      old  new  type       | cols  =   +   -   ->  size | walls  =   +   -  chg | ftgs  =   +   -  chg | grid +  -  -> | storeys | sched | frame");
        int totalChanges = 0;
        foreach (var (number, o, n, d) in deltas)
        {
            if (d is null)
            {
                Console.WriteLine($"{number,-10} {(o?.PageNumber.ToString() ?? "-"),4} {(n?.PageNumber.ToString() ?? "-"),4}  {(o ?? n)!.SheetType,-10} | {(o is null ? "only in the new issue" : "only in the old issue")}");
                continue;
            }
            totalChanges += d.Changes;
            string frame = d.FrameNote.StartsWith("new issue set", StringComparison.Ordinal) ? "by name" : "page";
            Console.WriteLine($"{number,-10} {d.OldPage,4} {d.NewPage,4}  {o!.SheetType,-10} | " +
                              $"{d.ColumnsSame,7} {d.ColumnsAdded.Count,3} {d.ColumnsRemoved.Count,3} {d.ColumnsMoved.Count,4} {d.ColumnsResized.Count,5} | " +
                              $"{d.WallsSame,7} {d.WallsAdded.Count,3} {d.WallsRemoved.Count,3} {d.WallsChanged.Count,4} | " +
                              $"{d.FootingsSame,6} {d.FootingsAdded.Count,3} {d.FootingsRemoved.Count,3} {d.FootingsChanged.Count,4} | " +
                              $"{d.GridAdded.Count,6} {d.GridRemoved.Count,2} {d.GridMoved.Count,3} | " +
                              $"{d.StoreysAdded.Count + d.StoreysRemoved.Count + d.StoreysChanged.Count,7} | " +
                              $"{d.ScheduleChanges.Count + d.ScheduleRowsAdded.Count + d.ScheduleRowsRemoved.Count,5} | {frame}");
        }
        Console.WriteLine();
        int unnumberedOld = oldSet.Count(s => s.SheetNumber is null), unnumberedNew = newSet.Count(s => s.SheetNumber is null);
        Console.WriteLine($"{deltas.Count(d => d.Delta is not null)} sheet(s) in both issues, {deltas.Count(d => d.Old is null)} only in the new, {deltas.Count(d => d.New is null)} only in the old; " +
                          $"{unnumberedOld} + {unnumberedNew} page(s) with no sheet number not compared. {totalChanges} object change(s) in all; --sheet <number> lists one sheet's.");
        return 0;
    }
}
