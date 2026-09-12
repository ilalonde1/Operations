// The takeoff verb `wallconcrete`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Usage: takeoff wallconcrete <keyplan.png> <schedule.png> <levels.json>
//   levels.json: { "storeyHeightFt": 10.5, "levels": ["LEVEL 20", ... , "P7"] }  (top to bottom)
internal static class WallconcreteVerb
{
    public static bool Matches(string[] args) => args.Length >= 4 && args[0].Equals("wallconcrete", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        foreach (var p in new[] { args[1], args[2], args[3] })
            if (!File.Exists(p)) { Console.Error.WriteLine($"Not found '{p}'."); return 2; }

        static string Norm(string s)
        {
            string n = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpperInvariant(), @"\s+", " ");
            // Strip zero-padding in numbers so 'LEVEL 08' == 'LEVEL 8', 'L01' == 'L1'.
            return System.Text.RegularExpressions.Regex.Replace(n, @"0*(\d+)", "$1");
        }

        var lvlDoc = JsonDocument.Parse(File.ReadAllText(args[3]));
        double storeyFt = lvlDoc.RootElement.TryGetProperty("storeyHeightFt", out var sh) ? sh.GetDouble() : 10.5;
        var levels = lvlDoc.RootElement.GetProperty("levels").EnumerateArray().Select(e => Norm(e.GetString() ?? "")).ToList();
        var levelIdx = new Dictionary<string, int>();
        for (int i = 0; i < levels.Count; i++) levelIdx[levels[i]] = i;
        int IndexOfLevel(string label) // FLOOR/BASE => bottom of the list
        {
            string n = Norm(label);
            if (n is "FLOOR" or "BASE" or "FND" or "FOUNDATION") return levels.Count - 1;
            return levelIdx.TryGetValue(n, out var i) ? i : -1;
        }

        Console.Error.WriteLine("Reading core wall key plan…");
        string kpJson = await PlanVisionClient.ReadWallKeyPlanJsonAsync(PlanRaster.LoadDownscaledPng(args[1], 1600));
        Console.Error.WriteLine("Reading shear wall schedule…");
        string schJson = await PlanVisionClient.ReadWallScheduleJsonAsync(PlanRaster.LoadDownscaledPng(args[2], 1600));

        // mark -> total plan length per floor (sum each occurrence; the same mark can label both core faces)
        var lenByMark = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in JsonDocument.Parse(kpJson).RootElement.GetProperty("marks").EnumerateArray())
        {
            string mk = (m.GetProperty("mark").GetString() ?? "").Trim();
            double len = m.TryGetProperty("lengthFt", out var l) ? l.GetDouble() : 0;
            if (mk.Length == 0 || len <= 0) continue;
            lenByMark[mk] = lenByMark.TryGetValue(mk, out var e) ? e + len : len;
        }

        // mark -> per-level thickness (inches), expanding each schedule band over the level list
        var thkByMarkLevel = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        int bandsApplied = 0, bandsSkipped = 0;
        foreach (var b in JsonDocument.Parse(schJson).RootElement.GetProperty("entries").EnumerateArray())
        {
            string mk = (b.GetProperty("mark").GetString() ?? "").Trim();
            double thk = b.GetProperty("thicknessIn").GetDouble();
            int top = IndexOfLevel(b.GetProperty("levelTop").GetString() ?? "");
            int bot = IndexOfLevel(b.GetProperty("levelBottom").GetString() ?? "");
            if (mk.Length == 0 || thk <= 0 || top < 0 || bot < 0) { bandsSkipped++; continue; }
            if (top > bot) (top, bot) = (bot, top);
            if (!thkByMarkLevel.TryGetValue(mk, out var arr)) thkByMarkLevel[mk] = arr = new double[levels.Count];
            for (int i = top; i <= bot; i++) arr[i] = thk;
            bandsApplied++;
        }

        // Price only marks present in BOTH the key plan and the schedule (the real, dimensioned shear walls).
        double totalCy = 0; int pricedMarks = 0;
        var rows = new List<(string mark, double len, double avgThk, int floors, double cy)>();
        foreach (var (mk, len) in lenByMark.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!thkByMarkLevel.TryGetValue(mk, out var thks)) continue;
            double cy = 0; int floors = 0; double thkSum = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                if (thks[i] <= 0) continue;
                cy += len * (thks[i] / 12.0) * storeyFt / 27.0;
                floors++; thkSum += thks[i];
            }
            if (floors == 0) continue;
            totalCy += cy; pricedMarks++;
            rows.Add((mk, len, thkSum / floors, floors, cy));
        }

        Console.WriteLine($"Levels: {levels.Count} (top {levels.FirstOrDefault()} → bottom {levels.LastOrDefault()}), storey {storeyFt} ft");
        Console.WriteLine($"Schedule bands: {bandsApplied} applied, {bandsSkipped} skipped (level not in list)");
        Console.WriteLine($"Priced {pricedMarks} marks present in BOTH key plan and schedule:");
        Console.WriteLine($"  {"mark",-6} {"len ft",7} {"avg thk",8} {"floors",7} {"cy",9}");
        foreach (var r in rows.OrderByDescending(r => r.cy))
            Console.WriteLine($"  {r.mark,-6} {r.len,7:0.#} {r.avgThk,7:0.#}\" {r.floors,7} {r.cy,9:N0}");
        Console.WriteLine($"TOTAL core shear-wall concrete: {totalCy:N0} cu.yd");
        return 0;
    }
}
