// GROUND TRUTH: what the PDF itself draws in a region, below every reader — every long axis-aligned
// run of the page's own paths, grouped by the line it sits on. Settles "is the drafter's line actually
// there?" before a reader is blamed: on 2026-09-10 it showed 31168's tower slab edge IS drawn, 31,586 mm
// a side in three collinear pieces, while the DXF held none of it — the wall reader was eating it.
// Ported from docs/etabs-handoff/pdf_lines.py (WP2, 2026-09-11); reads the page through
// VectorPageReader, the same extraction every reader starts from (extraction is not the weak link, §30).
// Coordinates are PDF points with y UP (the reader's frame, as vector-words prints them).
//   takeoff vector-lines <pdf> <page> [--region x0 y0 x1 y1] [--min-pt 20] [--scale 96] [--pens]
internal static class VectorLinesVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-lines", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-lines <pdf> <page> [--region x0 y0 x1 y1] [--min-pt 20] [--scale 96] [--pens]"); return 1; }
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
