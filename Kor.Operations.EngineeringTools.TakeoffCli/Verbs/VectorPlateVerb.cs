// The takeoff verb `vector-plate`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Focused plate-locator derisk: synthesis returns the slab plate box, poché measures its area.
// Usage: takeoff vector-plate <pdf> <page> <png>
internal static class VectorPlateVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-plate", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff vector-plate <pdf> <page> <png>"); return 1; }
        if (!File.Exists(args[1]) || !File.Exists(args[3])) { Console.Error.WriteLine("PDF or PNG not found."); return 2; }
        if (!int.TryParse(args[2], out int plPage) || plPage < 1) { Console.Error.WriteLine("Page must be positive."); return 2; }
        if (string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set."); return 2; }

        var pd = DrawingDigestBuilder.Build(args[1], plPage, plPage).Pages[0];
        string dj = JsonSerializer.Serialize(pd, new JsonSerializerOptions { WriteIndented = false });
        string r = await PlanVisionClient.LocatePlateAsync(dj, PlanRaster.LoadDownscaledPng(args[3], 1600));
        var re = JsonSerializer.Deserialize<JsonElement>(r);
        Console.WriteLine(JsonSerializer.Serialize(re, new JsonSerializerOptions { WriteIndented = true }));
        if (re.TryGetProperty("slabBox", out var sb2) && sb2.ValueKind == JsonValueKind.Array)
        {
            var bb = sb2.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
            if (bb.Count >= 4)
            {
                var (iw, ih) = PlanRaster.ImageSize(args[3]);
                var crop = PlanRaster.LoadCrop(args[3], (int)(Math.Min(bb[0], bb[2]) * iw), (int)(Math.Min(bb[1], bb[3]) * ih),
                                                         (int)(Math.Max(bb[0], bb[2]) * iw), (int)(Math.Max(bb[1], bb[3]) * ih));
                double mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110) ?? 0;
                var cl = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
                Console.WriteLine($"  poché slab area in box: {PlanGeometry.SquareFeet(cl.Count > 0 ? cl[0].LightPx : 0, mpp):N0} sq.ft ({crop.Width}x{crop.Height}px, {cl.Count} clusters)");
            }
        }
        return 0;
    }
}
