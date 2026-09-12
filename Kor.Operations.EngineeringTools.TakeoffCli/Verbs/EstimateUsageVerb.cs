// The takeoff verb `estimate`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Explicit usage for the new modes so a short/typo'd invocation reports help instead of falling
// through to the default CSV-diff path and failing with a confusing FileNotFound.
internal static class EstimateUsageVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("estimate", StringComparison.OrdinalIgnoreCase) && args.Length < 3;

    public static int Run(string[] args)
    {
    Console.Error.WriteLine("Usage: takeoff estimate <config.json> <out.xlsx>"); return 1;
    }
}
