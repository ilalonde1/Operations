// The takeoff verb `graycomp`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Gray-fill diagnostic: dump the neutral-tone histogram + the gray connected-components (with shape)
// of a crop, so wall/column fill thresholds and the shape split are chosen from evidence, not guessed.
// Usage: takeoff graycomp <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [lo] [hi]
internal static class GraycompVerb
{
    public static bool Matches(string[] args) => args.Length >= 8 && args[0].Equals("graycomp", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var ic = CultureInfo.InvariantCulture;
        if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
            !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
            !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
            !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
            !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
        { Console.Error.WriteLine("graycomp: crop coords must be integers and dpi a number."); return 2; }
        double? mpp = PlanGeometry.MetresPerPixel(args[7], dpi);
        if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{args[7]}'."); return 2; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
        int lo = args.Length > 8 && int.TryParse(args[8], NumberStyles.Integer, ic, out int loV) ? loV : 196;
        int hi = args.Length > 9 && int.TryParse(args[9], NumberStyles.Integer, ic, out int hiV) ? hiV : 228;

        PlanRaster.Crop crop;
        try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }

        double sqftPer(long p) => PlanGeometry.SquareFeet(p, mpp.Value);
        var histo = PlanGeometry.NeutralLuminanceHistogram(crop.R, crop.G, crop.B, crop.Width, crop.Height);
        Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  band=[{lo},{hi}]");
        Console.WriteLine("neutral-tone histogram (lum bin -> px):");
        for (int bi = 0; bi < histo.Length; bi++)
            if (histo[bi] > 0) Console.WriteLine($"  {bi * 16,3}-{bi * 16 + 15,3}: {histo[bi],10:N0}");

        var comps = PlanGeometry.MeasureGrayComponents(crop.R, crop.G, crop.B, crop.Width, crop.Height, lo, hi, minPixels: 30);
        long total = 0; foreach (var c in comps) total += c.AreaPx;
        Console.WriteLine($"gray band total: {total:N0} px = {sqftPer(total):N1} sq.ft across {comps.Count} comp(s)");
        Console.WriteLine("top components (area sq.ft, WxH px, solidity, elongation):");
        for (int k = 0; k < Math.Min(20, comps.Count); k++)
        {
            var c = comps[k];
            Console.WriteLine($"  {sqftPer(c.AreaPx),7:N1}  {c.Width,4}x{c.Height,-4}  sol={c.Solidity:0.00}  elong={c.Elongation:0.0}");
        }
        return 0;
    }
}
