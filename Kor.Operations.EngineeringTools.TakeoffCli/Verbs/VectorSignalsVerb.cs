// The takeoff verb `vector-signals`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// EVIDENCE probe: compute EVERY candidate slab-AREA signal for one sheet, in the drawing's real scale,
// so we can see which signal is reliable per sheet type BEFORE building a cascade. No AI. Usage:
//   takeoff vector-signals <pdf> <page> [png] [scaleDenom=100] [dpi=110]
// Signals: (P) raster poché largest+sum, (poly) largest closed vector polygon, (fill) filled-region sum,
//          (env) stroked-geometry envelope, (grid) dimensioned structural-grid bubble envelope.
internal static class VectorSignalsVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-signals", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-signals <pdf> <page> [png] [scaleDenom=100] [dpi=110]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int sgPage) || sgPage < 1) { Console.Error.WriteLine("Page must be positive."); return 2; }
        string? sgPng = args.Length >= 4 && File.Exists(args[3]) ? args[3] : null;
        double scaleDenom = args.Length >= 5 && double.TryParse(args[4], out var sd) ? sd : 100.0;
        double dpi = args.Length >= 6 && double.TryParse(args[5], out var dp) ? dp : 110.0;

        // Real-world conversion at scale 1:scaleDenom. 1 pt = 1/72 in (paper); real = paper × scaleDenom.
        double mPerPt = scaleDenom * (0.0254 / 72.0);
        double ft2PerPt2 = (mPerPt * 3.28084) * (mPerPt * 3.28084);
        double mpp = scaleDenom * (0.0254 / dpi);   // metres per pixel of the render, real-world

        static double Shoelace(IReadOnlyList<(double X, double Y)> p)
        {
            double a = 0; int n = p.Count;
            if (n < 3) return 0;
            for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
            return Math.Abs(a) / 2.0;
        }

        var pc = VectorPageReader.ReadPage(args[1], sgPage);
        Console.WriteLine($"Page {sgPage}: {pc.WidthPts:F0}x{pc.HeightPts:F0}pt, scale 1:{scaleDenom:F0}, {pc.Paths.Count} paths, {pc.Words.Count} words");
        Console.WriteLine($"  (1pt = {mPerPt:F4}m real; 1pt² = {ft2PerPt2:F4} sqft)");

        // (poly) largest closed vector polygon
        double polyMax = 0; var closed = pc.Paths.Where(p => p.IsClosed && p.Points.Count >= 3).ToList();
        foreach (var g in closed) polyMax = Math.Max(polyMax, Shoelace(g.Points) * ft2PerPt2);
        // (fill) filled-region sum
        var filled = closed.Where(p => p.IsFilled).ToList();
        double fillSum = filled.Sum(g => Shoelace(g.Points) * ft2PerPt2);
        // (env) stroked-geometry envelope (gross bound)
        var stroked = pc.Paths.Where(p => p.IsStroked && p.Points.Count >= 2).ToList();
        double env = 0;
        if (stroked.Count > 0)
            env = (stroked.Max(p => p.MaxX) - stroked.Min(p => p.MinX))
                * (stroked.Max(p => p.MaxY) - stroked.Min(p => p.MinY)) * ft2PerPt2;

        Console.WriteLine($"  poly (largest closed polygon):  {polyMax,10:N0} sqft   (closed paths: {closed.Count})");
        Console.WriteLine($"  fill (filled-region sum):       {fillSum,10:N0} sqft   (filled paths: {filled.Count})");
        Console.WriteLine($"  env  (stroked envelope, gross): {env,10:N0} sqft");

        // (grid) the dimensioned structural-grid envelope — now read by the Core StructuralGridReader (one
        // source of truth shared with the engine). Running it here verifies Core on the real sheets.
        var gf = StructuralGridReader.FromPage(pc);
        if (gf != null)
        {
            Console.WriteLine($"  grid X bubbles ({gf.XLabels.Count}): [{string.Join(",", gf.XLabels)}]  span {gf.XSpanPt:F0}pt = {gf.XSpanPt * mPerPt:F1}m");
            Console.WriteLine($"  grid Y bubbles ({gf.YLabels.Count}): [{string.Join(",", gf.YLabels)}]  span {gf.YSpanPt:F0}pt = {gf.YSpanPt * mPerPt:F1}m");
            Console.WriteLine($"  grid (envelope X×Y):            {gf.EnvelopeSqFt(scaleDenom),10:N0} sqft   multiPlan={gf.MultiPlan} usable={gf.IsUsable}");
        }
        else Console.WriteLine("  grid: no bubbles found");

        // (circles) bubble-shaped path stats — evidence for the circle-primary grid detector: how the set
        // actually draws its bubbles (closed? point count? radial spread?), so the detector's geometry
        // rules are chosen from sheets, not guessed.
        var round = pc.Paths.Where(p =>
            p.Width >= 8 && p.Width <= 40 && p.Height >= 8 && p.Height <= 40
            && Math.Abs(p.Width - p.Height) <= 0.25 * Math.Max(p.Width, p.Height)
            && p.Points.Count >= 3).ToList();
        Console.WriteLine($"  circle-ish paths (8-40pt, square bbox): {round.Count}  (closed: {round.Count(p => p.IsClosed)})");
        int trueCircles = 0, oneDigit = 0, oneLetter = 0, multiTok = 0, zeroTok = 0;
        var digRx = new System.Text.RegularExpressions.Regex(@"^\d{1,2}$");
        var letRx = new System.Text.RegularExpressions.Regex(@"^[A-Z]$");
        var sampleLines = new List<string>();
        foreach (var p in round)
        {
            double ccx = (p.MinX + p.MaxX) / 2, ccy = (p.MinY + p.MaxY) / 2, rr = (p.Width + p.Height) / 4;
            double dmin = double.MaxValue, dmax = 0;
            foreach (var (px, py) in p.Points)
            {
                double d = Math.Sqrt((px - ccx) * (px - ccx) + (py - ccy) * (py - ccy)) / rr;
                dmin = Math.Min(dmin, d); dmax = Math.Max(dmax, d);
            }
            bool isCircle = dmin >= 0.75 && dmax <= 1.25;
            if (!isCircle) continue;
            trueCircles++;
            var inTok = pc.Words.Where(t =>
                (t.Cx - ccx) * (t.Cx - ccx) + (t.Cy - ccy) * (t.Cy - ccy) <= rr * rr * 0.81).ToList();
            if (inTok.Count == 0) zeroTok++;
            else if (inTok.Count > 1) multiTok++;
            else if (digRx.IsMatch(inTok[0].Text.Trim())) oneDigit++;
            else if (letRx.IsMatch(inTok[0].Text.Trim())) oneLetter++;
            if (inTok.Count == 1 && (digRx.IsMatch(inTok[0].Text.Trim()) || letRx.IsMatch(inTok[0].Text.Trim())))
                sampleLines.Add($"    [{inTok[0].Text.Trim(),3}] fx={ccx / pc.WidthPts:F2} fy={ccy / pc.HeightPts:F2} ⌀{p.Width:F0}");
            else if (sampleLines.Count < 40 && inTok.Count > 0)
                sampleLines.Add($"    ({string.Join("|", inTok.Select(t => t.Text).Take(3)),6}) fx={ccx / pc.WidthPts:F2} fy={ccy / pc.HeightPts:F2} ⌀{p.Width:F0} (not a label)");
        }
        Console.WriteLine($"  true circles: {trueCircles}  → 1-digit {oneDigit}, 1-letter {oneLetter}, multi-token {multiTok}, empty {zeroTok}");
        foreach (var ln in sampleLines.Take(40)) Console.WriteLine(ln);

        // (thk) slab-thickness callouts (metric mm): pair each "SLAB" token with the nearest number to its
        // left on the same row. The distribution (field 200 vs band 450/900) is what drives zoning on the
        // transfer levels that a single field thickness under-prices.
        var numRx = new System.Text.RegularExpressions.Regex(@"^\d{2,4}$");
        var slabToks = pc.Words.Where(t => t.Text.Equals("SLAB", StringComparison.OrdinalIgnoreCase)).ToList();
        var thkTally = new SortedDictionary<int, int>();
        foreach (var s in slabToks)
        {
            double sh = s.MaxY - s.MinY;
            var cand = pc.Words
                .Where(t => numRx.IsMatch(t.Text) && Math.Abs(t.Cy - s.Cy) < Math.Max(sh, 6) && t.Cx < s.Cx && s.Cx - t.Cx < 9 * Math.Max(sh, 6))
                .OrderByDescending(t => t.Cx).FirstOrDefault();
            if (!cand.Equals(default(VectorPageReader.TextToken)) && int.TryParse(cand.Text, out int mm) && mm >= 100 && mm <= 1200)
                thkTally[mm] = thkTally.GetValueOrDefault(mm) + 1;
        }
        Console.WriteLine($"  SLAB callouts (mm×n): {(thkTally.Count > 0 ? string.Join("  ", thkTally.OrderByDescending(k => k.Value).Select(k => $"{k.Key}×{k.Value}")) : "—")}");

        // (P) raster poché largest cluster + sum of top clusters (no AI box — full page)
        if (sgPng != null)
        {
            var (iw, ih) = PlanRaster.ImageSize(sgPng);
            var img = PlanRaster.LoadCrop(sgPng, 0, 0, iw, ih);
            var cl = PlanGeometry.MeasureEnclosedClusters(img.Lum, img.Width, img.Height);
            double pocheMax = cl.Count > 0 ? PlanGeometry.SquareFeet(cl[0].LightPx, mpp) : 0;
            double pocheSum = cl.Take(12).Sum(c => PlanGeometry.SquareFeet(c.LightPx, mpp));
            Console.WriteLine($"  poché largest cluster:          {pocheMax,10:N0} sqft   (of {cl.Count} clusters)");
            Console.WriteLine($"  poché sum top-12 clusters:      {pocheSum,10:N0} sqft");
        }
        return 0;
    }
}
