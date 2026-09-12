// The takeoff verb `wallplan`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Debug: dump a CORE WALL KEY PLAN reading (each wall mark's centreline length).
// Usage: takeoff wallplan <png>
internal static class WallplanVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("wallplan", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
        byte[] small = PlanRaster.LoadDownscaledPng(args[1], 1600);
        string json = await PlanVisionClient.ReadWallKeyPlanJsonAsync(small);
        using var doc = JsonDocument.Parse(json);
        var marks = doc.RootElement.GetProperty("marks");
        double tot = 0;
        Console.WriteLine($"{marks.GetArrayLength()} wall marks:");
        foreach (var m in marks.EnumerateArray())
        {
            double len = m.GetProperty("lengthFt").GetDouble();
            tot += len;
            Console.WriteLine($"  {m.GetProperty("mark").GetString(),-5} : {len,6:0.#} ft");
        }
        Console.WriteLine($"total core wall plan length: {tot:0.#} ft");
        return 0;
    }
}
