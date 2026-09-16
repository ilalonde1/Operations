// GROUND TRUTH: what the PDF itself draws in a region, below every reader — every long axis-aligned
// run of the page's own paths, grouped by the line it sits on. Settles "is the drafter's line actually
// there?" before a reader is blamed: on 2026-09-10 it showed 31168's tower slab edge IS drawn, 31,586 mm
// a side in three collinear pieces, while the DXF held none of it — the wall reader was eating it.
// Ported from docs/etabs-handoff/pdf_lines.py (WP2, 2026-09-11); reads the page through
// VectorPageReader, the same extraction every reader starts from (extraction is not the weak link, §30).
// Coordinates are PDF points with y UP (the reader's frame, as vector-words prints them).
//   takeoff vector-lines <pdf> <page> [--region x0 y0 x1 y1] [--min-pt 20] [--scale 96] [--pens] [--long]
internal static class VectorLinesVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-lines", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-lines <pdf> <page> [--region x0 y0 x1 y1] [--min-pt 20] [--scale 96] [--pens] [--long]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int page) || page < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }
        double x0 = double.NegativeInfinity, y0 = double.NegativeInfinity, x1 = double.PositiveInfinity, y1 = double.PositiveInfinity;
        double minPt = 20, scale = 96;
        bool pens = args.Any(a => a.Equals("--pens", StringComparison.OrdinalIgnoreCase));
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--region", StringComparison.OrdinalIgnoreCase) && i + 4 < args.Length)
            {
                x0 = Num(args[++i]); y0 = Num(args[++i]); x1 = Num(args[++i]); y1 = Num(args[++i]);
            }
            else if (args[i].Equals("--min-pt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) minPt = Num(args[++i]);
            else if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) scale = Num(args[++i]);
        }
        double mm = scale * PdfToSafeConstants.PointsToMm;     // one point on the paper, in drawing millimetres at this scale

        var pc = VectorPageReader.ReadPage(args[1], page);
        // --long (2026-09-16, the tendon characterisation's second column): EVERY long segment on the page, at any angle,
        // one line each - its ends, its length, its pen, and at each end the nearest small filled shape within 12 pt (an
        // arrowhead is a filled triangle) and the nearest word within 40 pt - so a tendon (a long line ending at an
        // arrowhead labelled in KIPS) and a slab edge (a long line ending at another edge) are told apart from the page,
        // line by line, before any rule is written. Sorted longest first.
        if (args.Any(a => a.Equals("--long", StringComparison.OrdinalIgnoreCase)))
        {
            var fills = pc.Paths.Where(p => p.IsFilled && !p.IsClipping && p.MaxX - p.MinX <= 20 && p.MaxY - p.MinY <= 20).ToList();
            var rows = new List<(double Len, string Line)>();
            foreach (var path in pc.Paths)
            {
                if (path.IsClipping || path.IsFilled || !path.IsStroked || path.Points.Count != 2) continue;
                var a = path.Points[0]; var b = path.Points[1];
                if (a.X < x0 || a.X > x1 || a.Y < y0 || a.Y > y1) continue;
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (len < minPt) continue;
                string End((double X, double Y) e)
                {
                    var fill = fills.Where(f => Math.Abs((f.MinX + f.MaxX) / 2 - e.X) <= 12 && Math.Abs((f.MinY + f.MaxY) / 2 - e.Y) <= 12).OrderBy(f => Math.Abs((f.MinX + f.MaxX) / 2 - e.X) + Math.Abs((f.MinY + f.MaxY) / 2 - e.Y)).FirstOrDefault();
                    var word = pc.Words.Where(w => Math.Abs(w.Cx - e.X) <= 40 && Math.Abs(w.Cy - e.Y) <= 40).OrderBy(w => Math.Abs(w.Cx - e.X) + Math.Abs(w.Cy - e.Y)).Take(2).Select(w => w.Text).ToList();
                    return (fill.Points is { Count: > 0 } ? $"fill {fill.Points.Count}pt {fill.MaxX - fill.MinX:0}x{fill.MaxY - fill.MinY:0}" : "-") + (word.Count > 0 ? " \"" + string.Join(" ", word) + "\"" : "");
                }
                double angle = Math.Abs(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
                rows.Add((len, $"  ({a.X,7:0},{a.Y,7:0})-({b.X,7:0},{b.Y,7:0}) {len * mm,8:0} mm {angle,5:0}° w{path.LineWidth,4:0.0} #{path.Color.R:X2}{path.Color.G:X2}{path.Color.B:X2}  A: {End(a),-28} B: {End(b)}"));
            }
            Console.WriteLine($"{Path.GetFileName(args[1])} p{page}: {rows.Count} stroked two-point segments >= {minPt:0} pt ({minPt * mm:0} mm at 1:{scale:0}), longest first; at each end the nearest small fill (an arrowhead) and word:");
            foreach (var (_, line) in rows.OrderByDescending(r => r.Len)) Console.WriteLine(line);
            return 0;
        }
        var hor = new SortedDictionary<double, List<(double A, double B)>>();
        var ver = new SortedDictionary<double, List<(double A, double B)>>();
        // --pens (2026-09-16, the tendon characterisation's first column): the long axis-aligned runs by the pen that drew
        // them - line width and colour - with how many distinct lines each pen draws and their total length. A tendon
        // is drawn with a pen; whether it is a pen of its own, the page says here before any rule is written.
        var byPen = new Dictionary<(double Width, string Colour), (int Runs, double Length, HashSet<double> Lines)>();
        int n = 0;
        foreach (var path in pc.Paths)
        {
            if (path.IsClipping) continue;
            var pts = path.Points;
            int edges = path.IsClosed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < edges; k++)
            {
                var a = pts[k]; var b = pts[(k + 1) % pts.Count];
                if (a.X < x0 || a.X > x1 || a.Y < y0 || a.Y > y1) continue;
                n++;
                bool h = Math.Abs(a.Y - b.Y) < 0.3 && Math.Abs(a.X - b.X) >= minPt, v = Math.Abs(a.X - b.X) < 0.3 && Math.Abs(a.Y - b.Y) >= minPt;
                if (h) Add(hor, Math.Round(a.Y, 1), Math.Min(a.X, b.X), Math.Max(a.X, b.X));
                if (v) Add(ver, Math.Round(a.X, 1), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
                if (pens && (h || v))
                {
                    var key = (Math.Round(path.LineWidth * 2) / 2, $"#{path.Color.R:X2}{path.Color.G:X2}{path.Color.B:X2}" + (path.IsFilled ? " filled" : ""));
                    var e = byPen.TryGetValue(key, out var got) ? got : (Runs: 0, Length: 0.0, Lines: new HashSet<double>());
                    e.Lines.Add(h ? Math.Round(a.Y, 0) : -Math.Round(a.X, 0));
                    byPen[key] = (e.Runs + 1, e.Length + (h ? Math.Abs(a.X - b.X) : Math.Abs(a.Y - b.Y)), e.Lines);
                }
            }
        }
        if (pens)
        {
            Console.WriteLine($"{Path.GetFileName(args[1])} p{page}: {n} segments in region; axis-aligned runs >= {minPt:0} pt by the pen that drew them:");
            Console.WriteLine($"  {"width pt",8} {"colour",-16} {"runs",6} {"lines",6} {"total mm",10}");
            foreach (var (k, e) in byPen.OrderByDescending(kv => kv.Value.Length))
                Console.WriteLine($"  {k.Width,8:0.0} {k.Colour,-16} {e.Runs,6} {e.Lines.Count,6} {e.Length * mm,10:0}");
            return 0;
        }

        Console.WriteLine($"{Path.GetFileName(args[1])} p{page}: {n} segments in region; horizontals >= {minPt:0} pt ({minPt * mm:0} mm at 1:{scale:0}), by y (PDF y up):");
        foreach (var (y, spans) in hor) Print("y", y, spans, mm);
        Console.WriteLine();
        Console.WriteLine("verticals, by x:");
        foreach (var (x, spans) in ver) Print("x", x, spans, mm);
        return 0;

        static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static void Add(SortedDictionary<double, List<(double A, double B)>> d, double at, double a, double b)
        {
            if (!d.TryGetValue(at, out var spans)) d[at] = spans = [];
            spans.Add((a, b));
        }
        // a grid line drawn as dashes shows as many short runs with gaps between (31168's 2026-09-10
        // reissue draws its numbered axes so — 87 runs on one x), so the runs are listed, not just summed
        static void Print(string axis, double at, List<(double A, double B)> spans, double mm)
        {
            spans.Sort();
            double total = spans.Sum(s => s.B - s.A);
            Console.WriteLine($"  {axis}={at,8:0.0}  {spans.Count,3} run(s)  total {total,7:0.0} pt = {total * mm,8:0} mm   {spans[0].A:0}..{spans[^1].B:0}");
            Console.WriteLine("        " + string.Join("  ", spans.Select(s => $"[{s.A:0},{s.B:0}]={(s.B - s.A) * mm:0}mm")));
        }
    }
}
