// The takeoff verb `intake-baseline`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE BASELINE. The thirteen plan DXFs of the five stick files, to a folder named for the step, for dxf-census.
// Usage: takeoff intake-baseline <stickFilesDir> <outDir>
internal static class IntakeBaselineVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("intake-baseline", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!Directory.Exists(args[1])) { Console.Error.WriteLine($"Stick-file folder not found '{args[1]}'."); return 1; }
        foreach (var line in IntakeBaseline.Write(args[1], args[2])) Console.WriteLine(line);
        return 0;
    }
}
