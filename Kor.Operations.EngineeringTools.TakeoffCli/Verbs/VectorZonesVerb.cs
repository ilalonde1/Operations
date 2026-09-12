// The takeoff verb `vector-zones`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Thickness-zoning derisk: locate the plate, then split its area by thickness ZONE (Voronoi by the
// «N" SLAB» callouts) and compare the zoned effective thickness to the single modal value.
// Usage: takeoff vector-zones <pdf> <png> <page> <modalThk>
internal static class VectorZonesVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-zones", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 5) { Console.Error.WriteLine("Usage: takeoff vector-zones <pdf> <png> <page> <modalThk>"); return 1; }
        if (!File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("PDF or PNG not found."); return 2; }
        if (!int.TryParse(args[3], out int zPage) || zPage < 1) { Console.Error.WriteLine("Page must be positive."); return 2; }
        if (!int.TryParse(args[4], out int zModal) || zModal <= 0) { Console.Error.WriteLine("modalThk must be positive."); return 2; }
        if (string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set."); return 2; }

        var zPd = DrawingDigestBuilder.Build(args[1], zPage, zPage).Pages[0];
        string zdj = JsonSerializer.Serialize(zPd, new JsonSerializerOptions { WriteIndented = false });
        using var zld = JsonDocument.Parse(await PlanVisionClient.LocatePlateAsync(zdj, PlanRaster.LoadDownscaledPng(args[2], 1600)));
        if (!zld.RootElement.TryGetProperty("slabBox", out var zsb) || zsb.ValueKind != JsonValueKind.Array) { Console.Error.WriteLine("no plate box."); return 2; }
        var zbb = zsb.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
        if (zbb.Count < 4) { Console.Error.WriteLine("malformed plate box."); return 2; }

        var (ziw, zih) = PlanRaster.ImageSize(args[2]);
        int zx0 = (int)(Math.Min(zbb[0], zbb[2]) * ziw), zy0 = (int)(Math.Min(zbb[1], zbb[3]) * zih);
        int zx1 = (int)(Math.Max(zbb[0], zbb[2]) * ziw), zy1 = (int)(Math.Max(zbb[1], zbb[3]) * zih);
        var zcrop = PlanRaster.LoadCrop(args[2], zx0, zy0, zx1, zy1);
        double zmpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110) ?? 0;
        var zcl = PlanGeometry.MeasureEnclosedClusters(zcrop.Lum, zcrop.Width, zcrop.Height);
        if (zcl.Count == 0) { Console.Error.WriteLine("no clusters in box."); return 2; }
        double zArea = PlanGeometry.SquareFeet(zcl[0].LightPx, zmpp);

        // Read callouts WITH positions, map PDF pts -> crop px, keep only those inside the located plate box.
        var zPage2 = VectorPageReader.ReadPage(args[1], zPage);
        var zCallouts = SlabThicknessZoner.ReadCallouts(zPage2);
        Console.WriteLine($"p{zPage}: plate {zArea:N0} sqft, modal {zModal}\"; callouts: " +
            string.Join(", ", zCallouts.GroupBy(c => c.ValueIn).OrderBy(g => g.Key).Select(g => $"{g.Key}\"x{g.Count()}")));
        var zQual = SlabThicknessZoner.QualifyingValues(zCallouts, zModal);
        Console.WriteLine($"  qualifying zones: {string.Join(", ", zQual.OrderBy(v => v).Select(v => v + "\""))}");

        var zPx = zCallouts
            .Where(c => zQual.Contains(c.ValueIn))
            .Select(c => new PlanGeometry.CalloutPx(
                c.Cx / zPage2.WidthPts * ziw - zx0,
                (zPage2.HeightPts - c.Cy) / zPage2.HeightPts * zih - zy0,
                c.ValueIn))
            .Where(c => c.X >= 0 && c.X < zcrop.Width && c.Y >= 0 && c.Y < zcrop.Height)
            .ToList();
        var zFrac = PlanGeometry.ThicknessZoneFractions(zcrop.Lum, zcrop.Width, zcrop.Height, zPx,
            zcl[0].MinX, zcl[0].MinY, zcl[0].MaxX, zcl[0].MaxY);
        long zTot = zFrac.Values.Sum();
        foreach (var kv in zFrac.OrderBy(k => k.Key))
            Console.WriteLine($"    {kv.Key}\" zone: {(zTot > 0 ? 100.0 * kv.Value / zTot : 0):N0}% of plate ({PlanGeometry.SquareFeet(kv.Value, zmpp):N0} sqft)");
        double zEff = SlabThicknessZoner.EffectiveThicknessIn(zFrac, zModal);
        double zVolModal = zArea * zModal / 12.0 / 27.0;
        double zVolZoned = zArea * zEff / 12.0 / 27.0;
        Console.WriteLine($"  effective thickness {zEff:N2}\" vs modal {zModal}\"  ->  {zVolModal:N0} cy -> {zVolZoned:N0} cy/floor ({(zVolModal > 0 ? 100.0 * (zVolZoned - zVolModal) / zVolModal : 0):+0.0;-0.0}%)");
        return 0;
    }
}
