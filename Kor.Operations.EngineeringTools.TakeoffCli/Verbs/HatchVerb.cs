// The takeoff verb `hatch`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Hatched-footing detector diagnostic: deterministically find the cross-hatched concrete mats /
// deep footings on a crop and list their measured areas. Used to calibrate density / minSqFt.
// Usage: takeoff hatch <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [densityPct] [minSqFt] [winR]
internal static class HatchVerb
{
    public static bool Matches(string[] args) => args.Length >= 8 && args[0].Equals("hatch", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var ic = CultureInfo.InvariantCulture;
        if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
            !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
            !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
            !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
            !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
        { Console.Error.WriteLine("hatch: crop coords must be integers and dpi a number."); return 2; }
        double? mpp = PlanGeometry.MetresPerPixel(args[7], dpi);
        if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{args[7]}'."); return 2; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
        double densityPct = args.Length > 8 && double.TryParse(args[8], NumberStyles.Float, ic, out double dn) ? dn : 18.0;
        double minSqFt = args.Length > 9 && double.TryParse(args[9], NumberStyles.Float, ic, out double ms) ? ms : 10.0;
        int winR = args.Length > 10 && int.TryParse(args[10], NumberStyles.Integer, ic, out int wr) ? wr : 10;

        PlanRaster.Crop crop;
        try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }
        long minPx = (long)(minSqFt / (mpp.Value * mpp.Value * 10.763910416709722));
        var regions = PlanGeometry.MeasureHatchedRegions(crop.Lum, crop.Width, crop.Height, windowRadius: winR, densityPercent: densityPct, minPixels: minPx);
        long tot = 0; foreach (var r in regions) tot += r.AreaPx;
        Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  density={densityPct}%  winR={winR}  minSqFt={minSqFt}");
        Console.WriteLine($"{regions.Count} hatched region(s), total {PlanGeometry.SquareFeet(tot, mpp.Value):N0} sq.ft:");
        for (int k = 0; k < Math.Min(25, regions.Count); k++)
        {
            var r = regions[k];
            Console.WriteLine($"  {PlanGeometry.SquareFeet(r.AreaPx, mpp.Value),7:N0} sq.ft  {r.Width,4}x{r.Height,-4}  @({r.CentroidX},{r.CentroidY})");
        }
        return 0;
    }
}
