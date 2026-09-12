// The takeoff verb `e2k-storeys`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff e2k-storeys <model.e2k> — the model's storeys top → bottom with their heights, the
// ground truth an elevation sheet's level ladder is measured against (elev-scan).
internal static class E2kStoreysVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("e2k-storeys", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var sdoc = E2kDocument.Load(args[1]);
        var storeys = sdoc.ReadStories().OrderByDescending(s => s.Elevation).ToList();
        Console.WriteLine($"{Path.GetFileName(args[1])}: {storeys.Count} storey(s), elevations in the model's units");
        foreach (var s in storeys)
        {
            double h = s.Elevation - s.ElevationBelow;
            Console.WriteLine($"  {s.Name,-14} elev {s.Elevation,10:0.##}   height {h,8:0.##}   ({h * 25.4,7:0} mm if inches)");
        }
        return 0;
    }
}
