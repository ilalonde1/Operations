// The takeoff verb `model-yardstick`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// A MODEL AGAINST THE ENGINEER'S OWN, column by column: the positional check the counts never give
// (ModelYardstick, ported 2026-09-11 from columns_vs_yardstick.py). Frames matched by grid name,
// storeys by full name then stripped, residuals BOTH ways, every storey listed.
// Usage: takeoff model-yardstick <model.e2k> <yardstick.e2k> [--pairs [storey]] [--openings [storey]]
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
        // --openings [storey]: every opening on the shared storeys, ours and hers in our frame, with the nearest of the other
        // side's - the raw material behind the openings figure (2026-09-18 13:00: what IS a 0.5 x 1 m of hers we have not?)
        int ao = Array.FindIndex(args, a => a.Equals("--openings", StringComparison.OrdinalIgnoreCase));
        if (ao >= 0)
        {
            string? st = ao + 1 < args.Length && !args[ao + 1].StartsWith("--", StringComparison.Ordinal) ? args[ao + 1] : null;
            var orows = c.OpeningRows.Where(r => st is null || r.Storey.Equals(st, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"{orows.Count} of {c.OpeningRows.Count} openings{(st is null ? "" : $" on {st}")} (storey, ours/hers, centre x y in our frame mm, plan box m, nearest of the other side's on the storey mm; hers: covered by one of ours, on a plate of ours):");
            foreach (var r in orows.OrderBy(r => r.Storey, StringComparer.Ordinal).ThenBy(r => r.Ours ? 0 : 1).ThenBy(r => r.X).ThenBy(r => r.Y))
                Console.WriteLine($"  {r.Storey,-8} {(r.Ours ? "ours" : "hers"),-4} ({r.X,9:N0}, {r.Y,9:N0})  {r.W / 1000,5:0.0} x {r.H / 1000,5:0.0} m  nearest {(double.IsNaN(r.NearestMm) ? "   none" : r.NearestMm.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).PadLeft(7))}{(r.Ours ? "" : $"  {(r.Covered ? "covered" : "       ")} {(r.OnOurPlate ? "on our plate" : "OFF our plate")}")}");
            return 0;
        }
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
