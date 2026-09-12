// The takeoff verb `measure`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class MeasureUsageVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("measure", StringComparison.OrdinalIgnoreCase) && args.Length < 8;

    public static int Run(string[] args)
    {
    Console.Error.WriteLine("Usage: takeoff measure <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [gray]"); return 1;
    }
}
