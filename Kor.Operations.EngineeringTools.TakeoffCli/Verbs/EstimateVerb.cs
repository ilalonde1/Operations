// The takeoff verb `estimate`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Full stickfile → takeoff estimate. Reads a building config (the plate map a human or the
// vision layer produces), measures each plate off its rasterized sheet with the Core geometry
// engine, prices + reconciles via the pipeline, and writes the same orange-celled takeoff xlsx
// the app produces — plus a diligence report of everything it could not fully trust.
// Usage: takeoff estimate <config.json> <out.xlsx>
internal static class EstimateVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("estimate", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        EstimateConfig? json;
        try
        {
            json = JsonSerializer.Deserialize<EstimateConfig>(
                File.ReadAllText(args[1]),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) { Console.Error.WriteLine($"Could not read/parse config '{args[1]}': {ex.Message}"); return 2; }
        if (json is null) { Console.Error.WriteLine("Config parsed to null."); return 2; }
        if (json.Plates is null || json.Plates.Count == 0) { Console.Error.WriteLine("Config has no plates."); return 2; }

        var profile = PlanProfile.ByName(json.Profile);
        var plates = new List<MeasuredPlate>();
        foreach (var pc in json.Plates)
        {
            string tag = $"{pc.Level} {pc.Element}";
            double dpi = pc.Dpi ?? json.Dpi;
            string note = pc.Scale ?? json.Scale;
            double? mpp = PlanGeometry.MetresPerPixel(note, dpi);
            if (mpp is null) { Console.Error.WriteLine($"  ! {tag}: unparseable scale '{note}', skipped."); continue; }
            if (pc.Crop is not { Length: 4 }) { Console.Error.WriteLine($"  ! {tag}: crop must be [x0,y0,x1,y1], skipped."); continue; }
            if (!(pc.AreaFraction > 0)) { Console.Error.WriteLine($"  ! {tag}: areaFraction must be > 0, skipped."); continue; }

            string png = Path.IsPathRooted(pc.Png) ? pc.Png : Path.Combine(json.PngDir ?? "", pc.Png ?? "");
            if (!File.Exists(png)) { Console.Error.WriteLine($"  ! {tag}: image not found '{png}', skipped."); continue; }

            PlanRaster.Crop crop;
            try { crop = PlanRaster.LoadCrop(png, pc.Crop[0], pc.Crop[1], pc.Crop[2], pc.Crop[3]); }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {tag}: could not load/crop '{png}': {ex.Message}, skipped."); continue; }

            long px = pc.Gray
                ? PlanGeometry.MeasureGrayFootprint(crop.R, crop.G, crop.B, crop.Width, crop.Height)
                : PlanGeometry.MeasureEnclosedArea(crop.Lum, crop.Width, crop.Height).LowerPx;
            double areaSqFt = PlanGeometry.SquareFeet(px, mpp.Value) * pc.AreaFraction;

            plates.Add(new MeasuredPlate(
                pc.Level, PlanRaster.ParseElement(pc.Element), pc.Variant,
                areaSqFt, pc.DimensionIn, pc.Count, pc.Grade ?? "", pc.ScaleConfirmed, pc.RebarLbPerCyOverride));
        }
        if (plates.Count == 0) { Console.Error.WriteLine("No measurable plates after validation."); return 2; }

        var result = PlanEstimatePipeline.Run(plates, profile);
        var computed = StructuralTakeoffService.Compute(result.TakeoffInputs, profile.ToImperialDensityTable());
        var eModel = new StructuralTakeoffReportModel(json.Project ?? "", json.Name ?? "", json.Issue ?? "", DateTime.UtcNow, computed);
        File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(eModel));

        Console.WriteLine($"Profile: {profile.Name}   Plates: {result.Plates.Count}");
        Console.WriteLine($"Concrete: {result.TotalConcreteCuYd:N0} cu.yd   Reinforcing: {computed.TotalRebarWeight:N0} lb   ({computed.TotalRebarWeight / 2000:N0} tons)");
        Console.WriteLine($"Diligence: {result.CriticalCount} critical, {result.ReviewCount} to review");
        foreach (var pe in result.Plates)
            foreach (var f in pe.Check.Flags)
                Console.WriteLine($"  [{f.Severity}] {pe.Plate.Level} {pe.Plate.Element}: {f.Message}");
        Console.WriteLine($"Wrote {args[2]}");
        return 0;
    }
}
