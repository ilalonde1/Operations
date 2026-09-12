// The takeoff verb `vector-synth`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Layer-2 synthesis derisk: build one page's digest, send the EXACT facts to Claude, print the
// structured page takeoff. Usage: takeoff vector-synth <pdf> <page>
internal static class VectorSynthVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-synth", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-synth <pdf> <page>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int synPage) || synPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }
        if (string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set."); return 2; }

        var pd = DrawingDigestBuilder.Build(args[1], synPage, synPage).Pages[0];
        string digestJson = JsonSerializer.Serialize(pd, new JsonSerializerOptions { WriteIndented = false });
        // Optional 4th arg: a rendered PNG to fuse for the slab-area judgment.
        string result;
        if (args.Length >= 4 && File.Exists(args[3]))
        {
            Console.WriteLine($"p{synPage}: digest {digestJson.Length:N0} chars + image {Path.GetFileName(args[3])} -> synthesizing...");
            result = await PlanVisionClient.SynthesizePageWithImageAsync(digestJson, PlanRaster.LoadDownscaledPng(args[3], 1600));
        }
        else
        {
            Console.WriteLine($"p{synPage}: digest {digestJson.Length:N0} chars -> synthesizing...");
            result = await PlanVisionClient.SynthesizePageAsync(digestJson);
        }
        var resEl = JsonSerializer.Deserialize<JsonElement>(result);
        Console.WriteLine(JsonSerializer.Serialize(resEl, new JsonSerializerOptions { WriteIndented = true }));

        // If the synthesis gave a slab plate box and we have the rendered image, MEASURE the area via poché.
        if (args.Length >= 4 && File.Exists(args[3]) && resEl.TryGetProperty("slabBox", out var sb) && sb.ValueKind == JsonValueKind.Array)
        {
            var b = sb.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
            if (b.Count >= 4)
            {
                var (iw, ih) = PlanRaster.ImageSize(args[3]);
                int px0 = (int)(Math.Min(b[0], b[2]) * iw), py0 = (int)(Math.Min(b[1], b[3]) * ih);
                int px1 = (int)(Math.Max(b[0], b[2]) * iw), py1 = (int)(Math.Max(b[1], b[3]) * ih);
                var crop = PlanRaster.LoadCrop(args[3], px0, py0, px1, py1);
                double mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110) ?? 0;
                var clusters = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
                long largest = clusters.Count > 0 ? clusters[0].LightPx : 0;
                Console.WriteLine($"  poché slab area in box: {PlanGeometry.SquareFeet(largest, mpp):N0} sq.ft (box {crop.Width}x{crop.Height}px, {clusters.Count} clusters)");
            }
        }
        return 0;
    }
}
