// The takeoff verb `model-yardstick`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// A MODEL AGAINST THE ENGINEER'S OWN, column by column: the positional check the counts never give
// (ModelYardstick, ported 2026-09-11 from columns_vs_yardstick.py). Frames matched by grid name,
// storeys by full name then stripped, residuals BOTH ways, every storey listed.
// Usage: takeoff model-yardstick <model.e2k> <yardstick.e2k>
internal static class ModelYardstickVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("model-yardstick", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("Both .e2k files must exist."); return 1; }
        Console.Write(ModelYardstick.Summary(ModelYardstick.Compare(args[1], args[2])));
        return 0;
    }
}
