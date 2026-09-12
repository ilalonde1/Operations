// The takeoff verb `model-diff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// WHAT A SECOND MODEL LOST OR GAINED AGAINST A FIRST, storey by storey, with positions - the
// differential behind "byte-identical, or what moved" (ModelDiff, ported 2026-09-11 from
// members_diff.py and plate_diff.py). Frames by grid label, else the modal displacement (a GUESS, said).
// Usage: takeoff model-diff <before.e2k> <after.e2k>
internal static class ModelDiffVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("model-diff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("Both .e2k files must exist."); return 1; }
        var mdResult = ModelDiff.Compare(args[1], args[2]);
        Console.Write(ModelDiff.Report(mdResult));
        return mdResult.ByteIdentical ? 0 : 2;
    }
}
