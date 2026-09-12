// The takeoff verb `vector-geom`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Geometry probe: can the slab AREA come from vector geometry instead of pixels? Dump the largest paths
// (open + closed) by extent, with point count and shoelace area (as if closed), in sq.ft at 1/8"=1'-0".
// If the slab outline shows up as a big path (or a stitchable few), area can be exact. Usage:
//   takeoff vector-geom <pdf> <page>
internal static class VectorGeomVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-geom", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-geom <pdf> <page>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int gPage) || gPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var pc = VectorPageReader.ReadPage(args[1], gPage);
        const double Ft2PerPt2 = 0.01234567901; // (1/72 in/pt * 8 ft/in)^2 at 1/8"=1'-0"
        static double Shoelace(IReadOnlyList<(double X, double Y)> p)
        {
            double a = 0; int n = p.Count;
            for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
            return Math.Abs(a) / 2.0;
        }
        Console.WriteLine($"Page {gPage}: {pc.WidthPts:F0}x{pc.HeightPts:F0} pts, {pc.Paths.Count} subpaths");
        Console.WriteLine("  Largest subpaths by bbox extent (area = shoelace as-if-closed, sq.ft @ 1/8\"=1'-0\"):");
        foreach (var g in pc.Paths.OrderByDescending(p => p.Width * p.Height).Take(20))
            Console.WriteLine($"    {(g.IsClosed ? "C" : "o")}{(g.IsStroked ? "S" : " ")}{(g.IsFilled ? "F" : " ")} pts={g.Points.Count,4}  bbox={g.Width,6:F0}x{g.Height,6:F0}pt  area={Shoelace(g.Points) * Ft2PerPt2,9:N0} sqft");

        // Also: the union extent of all stroked geometry (a rough slab-envelope upper bound).
        var stroked = pc.Paths.Where(p => p.IsStroked && p.Points.Count >= 2).ToList();
        if (stroked.Count > 0)
        {
            double minX = stroked.Min(p => p.MinX), minY = stroked.Min(p => p.MinY);
            double maxX = stroked.Max(p => p.MaxX), maxY = stroked.Max(p => p.MaxY);
            Console.WriteLine($"  Stroked-geometry envelope: {(maxX - minX):F0}x{(maxY - minY):F0}pt = {(maxX - minX) * (maxY - minY) * Ft2PerPt2:N0} sqft (gross bound)");
        }
        return 0;
    }
}
