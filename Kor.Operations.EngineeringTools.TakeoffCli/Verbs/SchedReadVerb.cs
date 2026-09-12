// The takeoff verb `sched-read`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// kind = wall | column | keyplan.  Usage: takeoff sched-read <kind> <sheet.png>
internal static class SchedReadVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("sched-read", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (!File.Exists(args[2])) { Console.Error.WriteLine($"Not found '{args[2]}'."); return 2; }
        var spng = PlanRaster.LoadDownscaledPng(args[2], 1600);
        string sjson = args[1].ToLowerInvariant() switch
        {
            "column" or "col" => await PlanVisionClient.ReadColumnScheduleJsonAsync(spng),
            "colcount" or "count" => await PlanVisionClient.ReadColumnCountsJsonAsync(spng),
            "keyplan" or "key" => await PlanVisionClient.ReadWallKeyPlanJsonAsync(spng),
            _ => await PlanVisionClient.ReadWallScheduleJsonAsync(spng),
        };
        Console.WriteLine(sjson);
        return 0;
    }
}
