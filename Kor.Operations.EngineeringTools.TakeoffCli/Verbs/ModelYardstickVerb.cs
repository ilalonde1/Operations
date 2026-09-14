// The takeoff verb `model-yardstick`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// A MODEL AGAINST THE ENGINEER'S OWN, column by column: the positional check the counts never give
// (ModelYardstick, ported 2026-09-11 from columns_vs_yardstick.py). Frames matched by grid name,
// storeys by full name then stripped, residuals BOTH ways, every storey listed.
// Usage: takeoff model-yardstick <model.e2k> <yardstick.e2k> [--pairs [storey]]
//   --pairs: every judged column of ours with the offset to the nearest of hers (mm, after the frame) and both
//   sections - the raw material behind the medians, for looking at what a 300 mm residual IS (2026-09-14).
internal static class ModelYardstickVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("model-yardstick", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("Both .e2k files must exist."); return 1; }
        var c = ModelYardstick.Compare(args[1], args[2]);
        Console.Write(ModelYardstick.Summary(c));
        int at = Array.FindIndex(args, a => a.Equals("--pairs", StringComparison.OrdinalIgnoreCase));
        if (at < 0) return 0;
        string? storey = at + 1 < args.Length ? args[at + 1] : null;
        var rows = c.Pairs.Where(p => storey is null || p.Storey.Equals(storey, StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"{rows.Count} of {c.Pairs.Count} judged columns{(storey is null ? "" : $" on {storey}")} (storey, ours at x y, our section -> her section, offset dx dy, distance):");
        foreach (var p in rows.OrderBy(p => p.Storey, StringComparer.Ordinal).ThenBy(p => p.X).ThenBy(p => p.Y))
            Console.WriteLine($"  {p.Storey,-8} ({p.X,9:N0}, {p.Y,9:N0})  {p.OurSection,-20} -> {p.TheirSection,-22} ({p.Dx,6:N0}, {p.Dy,6:N0})  {Math.Sqrt(p.Dx * p.Dx + p.Dy * p.Dy),6:N0}");
        return 0;
    }
}
