// The takeoff verb `wallsched`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Debug: dump a SHEAR WALL SCHEDULE reading (wall thickness per mark per level).
// Usage: takeoff wallsched <png>
internal static class WallschedVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("wallsched", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
        byte[] small = PlanRaster.LoadDownscaledPng(args[1], 1600);
        string json = await PlanVisionClient.ReadWallScheduleJsonAsync(small);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        Console.WriteLine($"{entries.GetArrayLength()} wall-schedule bands:");
        foreach (var e in entries.EnumerateArray())
            Console.WriteLine($"  {e.GetProperty("mark").GetString(),-5} {e.GetProperty("levelTop").GetString(),-8}→{e.GetProperty("levelBottom").GetString(),-8} : {e.GetProperty("thicknessIn").GetDouble():0.#}\"");
        return 0;
    }
}
