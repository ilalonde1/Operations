// The takeoff verb `vision-estimate`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class VisionEstimateUsageVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vision-estimate", StringComparison.OrdinalIgnoreCase) && args.Length < 3;

    public static int Run(string[] args)
    {
    Console.Error.WriteLine("Usage: takeoff vision-estimate <pages.json> <out.xlsx>"); return 1;
    }
}
