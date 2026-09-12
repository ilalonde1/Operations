// The takeoff verb `vector-plate-auto`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Deterministic-plate probe: can we find the slab plate from the WHOLE page with NO synthesis box?
// Run the enclosed-area clustering on the full render and dump the top clusters (area, position, size).
// If the slab is the dominant central cluster, area can be made synthesis-free. Usage:
//   takeoff vector-plate-auto <png>
internal static class VectorPlateAutoVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-plate-auto", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff vector-plate-auto <png>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PNG not found '{args[1]}'."); return 2; }

        var (iw, ih) = PlanRaster.ImageSize(args[1]);
        var img = PlanRaster.LoadCrop(args[1], 0, 0, iw, ih);
        double mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110) ?? 0;
        var clusters = PlanGeometry.MeasureEnclosedClusters(img.Lum, img.Width, img.Height);
        Console.WriteLine($"{Path.GetFileName(args[1])}: {img.Width}x{img.Height}px, {clusters.Count} clusters");
        Console.WriteLine("  Top clusters (area sqft | center fx,fy | size w%,h% | bays):");
        foreach (var c in clusters.Take(8))
        {
            double cx = ((c.MinX + c.MaxX) / 2.0) / img.Width, cy = ((c.MinY + c.MaxY) / 2.0) / img.Height;
            Console.WriteLine($"    {PlanGeometry.SquareFeet(c.LightPx, mpp),9:N0} sqft | fx={cx:F2} fy={cy:F2} | {(double)c.Width / img.Width:P0} x {(double)c.Height / img.Height:P0} | {c.RegionCount} bays");
        }
        return 0;
    }
}
