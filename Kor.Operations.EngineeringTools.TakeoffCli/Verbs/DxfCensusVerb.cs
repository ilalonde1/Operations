// The takeoff verb `dxf-census`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE DIFFERENTIAL'S EYES. Which layers moved between two folders of DXFs written from the same pages.
// Usage: takeoff dxf-census <beforeDir> <afterDir> [--only LAYER,LAYER]   (exit 2 when another layer moved)
internal static class DxfCensusVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("dxf-census", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string? only = null;
        for (int i = 3; i < args.Length; i++)
            if (args[i].Equals("--only", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) only = args[++i];
        if (!Directory.Exists(args[1]) || !Directory.Exists(args[2])) { Console.Error.WriteLine("Both folders must exist."); return 1; }
        var censusDiffs = DxfLayerCensus.Compare(args[1], args[2]);
        foreach (var line in DxfLayerCensus.Report(censusDiffs)) Console.WriteLine(line);
        if (only is null) return 0;
        var allowed = new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        var outside = censusDiffs.SelectMany(d => d.Changed.Keys).Where(l => !allowed.Contains(l)).Distinct().OrderBy(l => l).ToList();
        var missing = censusDiffs.Where(d => d.MissingBefore || d.MissingAfter).Select(d => d.Name).ToList();
        if (outside.Count == 0 && missing.Count == 0) { Console.WriteLine($"only {only} moved, as intended."); return 0; }
        Console.Error.WriteLine($"a layer outside --only moved: {string.Join(", ", outside)}" + (missing.Count > 0 ? $"; files on one side only: {string.Join(", ", missing)}" : ""));
        return 2;
    }
}
