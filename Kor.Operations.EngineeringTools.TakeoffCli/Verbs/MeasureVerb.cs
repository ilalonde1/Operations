// The takeoff verb `measure`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Layer-1 geometry probe: measure a plate area (flood-fill) or a wall/column footprint
// (gray-fill) off one rasterized plan crop. Validates the engine and is the pipeline's
// measurement step. Usage: takeoff measure <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [gray]
internal static class MeasureVerb
{
    public static bool Matches(string[] args) => args.Length >= 8 && args[0].Equals("measure", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var ic = CultureInfo.InvariantCulture;
        if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
            !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
            !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
            !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
            !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
        { Console.Error.WriteLine("measure: crop coords must be integers and dpi a number."); return 2; }
        string note = args[7];
        double? mpp = PlanGeometry.MetresPerPixel(note, dpi);
        if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{note}'."); return 2; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }

        PlanRaster.Crop crop;
        try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }
        bool gray = args.Length > 8 && args[8].Equals("gray", StringComparison.OrdinalIgnoreCase);
        if (gray)
        {
            long px = PlanGeometry.MeasureGrayFootprint(crop.R, crop.G, crop.B, crop.Width, crop.Height);
            Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}");
            Console.WriteLine($"gray footprint : {px:N0} px = {PlanGeometry.SquareFeet(px, mpp.Value):N0} sq.ft");
        }
        else
        {
            var a = PlanGeometry.MeasureEnclosedArea(crop.Lum, crop.Width, crop.Height);
            var clusters = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
            long largest = clusters.Count > 0 ? clusters[0].LightPx : 0;
            Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  clusters={clusters.Count}");
            Console.WriteLine($"enclosed lo(light)  : {a.LowerPx,11:N0} px = {PlanGeometry.SquareFeet(a.LowerPx, mpp.Value):N0} sq.ft");
            Console.WriteLine($"enclosed hi(+dark)  : {a.UpperPx,11:N0} px = {PlanGeometry.SquareFeet(a.UpperPx, mpp.Value):N0} sq.ft");
            Console.WriteLine($"largest cluster     : {largest,11:N0} px = {PlanGeometry.SquareFeet(largest, mpp.Value):N0} sq.ft");
        }
        return 0;
    }
}
