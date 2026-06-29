using System.Globalization;
using System.Text.Json;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Explicit usage for the new modes so a short/typo'd invocation reports help instead of falling
// through to the default CSV-diff path and failing with a confusing FileNotFound.
if (args.Length >= 1 && args[0].Equals("estimate", StringComparison.OrdinalIgnoreCase) && args.Length < 3)
{ Console.Error.WriteLine("Usage: takeoff estimate <config.json> <out.xlsx>"); return 1; }
if (args.Length >= 1 && args[0].Equals("measure", StringComparison.OrdinalIgnoreCase) && args.Length < 8)
{ Console.Error.WriteLine("Usage: takeoff measure <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [gray]"); return 1; }
if (args.Length >= 1 && args[0].Equals("vision-estimate", StringComparison.OrdinalIgnoreCase) && args.Length < 3)
{ Console.Error.WriteLine("Usage: takeoff vision-estimate <pages.json> <out.xlsx>"); return 1; }

// END-TO-END synthesis-led takeoff: classify each page, locate+measure slab plates, assemble via the
// EXISTING pipeline -> xlsx + total vs QTO. Usage: takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last]
if (args.Length >= 1 && args[0].Equals("vector-takeoff", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last] [scale]"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set."); return 2; }
    int? tkFirst = args.Length >= 5 && int.TryParse(args[4], out var tf) ? tf : null;
    int? tkLast  = args.Length >= 6 && int.TryParse(args[5], out var tl) ? tl : null;
    // Optional scale note (e.g. "1:100" for a metric set, "1/8\"=1'-0\"" imperial). Defaults to imperial.
    string tkScale = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6]) ? args[6] : "1/8\"=1'-0\"";

    // The whole measure→reconcile→price→synopsis spine now lives in Core (SlabTakeoffEngine) so the WPF
    // app runs it identically; this command is a thin host that supplies the AI + raster I/O and renders
    // the engine's note trace, totals, and orange synopsis exactly as before.
    var tkReq = new SlabTakeoffRequest(args[1], args[2], tkFirst, tkLast, Scale: tkScale);
    SlabTakeoffResult tkOut;
    try { tkOut = await SlabTakeoffEngine.RunAsync(tkReq, new CliPlanVision(), new CliPlanRaster()); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

    foreach (var n in tkOut.Notes) Console.WriteLine(n);
    File.WriteAllBytes(args[3], tkOut.Xlsx);
    Console.WriteLine($"\nPlates: {tkOut.Estimate.Plates.Count}   Slab concrete: {tkOut.TotalConcreteCuYd:N0} cu.yd   Rebar: {tkOut.TotalRebarLb:N0} lb");

    // PER-LEVEL — these are FIELD-SLAB volumes: plan area × plan thickness callout. "GRID" means the field
    // measurement is clean (area cross-checked by grid envelope + poché + peers, thickness from a real
    // callout); it does NOT mean the floor TOTAL is final. Built-up zones below the slab — drop panels, beams,
    // transfer build-up — are not drawn as plan callouts and are excluded, so a model/section QTO reads higher
    // on any floor that has them. (Proven on 31065: L4-North reads identically to L2-North on the plan yet is
    // 17% heavier in the model — no plan-level signal separates them.) "FLAG" = even the field measure is doubtful.
    Console.WriteLine($"\nPer-level FIELD-SLAB volume — GRID = field measure clean, FLAG = verify the measure by hand:");
    foreach (var pe in tkOut.Estimate.Plates.OrderByDescending(p => p.ConcreteTotalCuYd))
        Console.WriteLine($"  {(pe.Check.Confidence == TakeoffConfidence.High ? "GRID" : "FLAG")}  {pe.Plate.Level,-18} {pe.ConcreteTotalCuYd,8:N0} cy  ({pe.ConcretePerFloorCuYd,6:N0}/flr)  [{pe.Check.Confidence}]");
    Console.WriteLine("  NOTE: field-slab volumes — built-up zones below the slab (drops/beams/transfers) are NOT in plan");
    Console.WriteLine("        callouts and are excluded. Confirm built-up volume against the sections before trusting any floor total.");

    // SYNOPSIS — the on-screen "unsure areas" the product surfaces before export (and the future AI
    // crucible converses about). Every plate the diligence engine could not fully trust, with the reasons.
    Console.WriteLine($"\nSynopsis: {tkOut.Estimate.Plates.Count - tkOut.Synopsis.Count}/{tkOut.Estimate.Plates.Count} plates clear; {tkOut.Synopsis.Count} need review (orange):");
    foreach (var pe in tkOut.Synopsis.OrderByDescending(p => p.Check.HasCritical))
        foreach (var fl in pe.Check.Flags.Where(f => f.Severity != PlanFlagSeverity.Info))
            Console.WriteLine($"   [{fl.Severity}] {pe.Plate.Level,-16} {fl.Code,-20} {fl.Message}");

    // RESIDUAL — the plates nobody could resolve at all (NOT in the total). The honest other half of the
    // answer: what this takeoff does not cover, listed so it is never a silently dropped floor.
    if (tkOut.Residual.Count > 0)
    {
        Console.WriteLine($"\nResidual: {tkOut.Residual.Count} plate(s) UNRESOLVED — excluded from the total, finish by hand:");
        foreach (var rz in tkOut.Residual)
            Console.WriteLine($"   [{rz.Kind}] {rz.Label,-16} {rz.Note}");
    }
    Console.WriteLine($"\n(suspended-slab benchmark: 31044 Coronation = 20,208 cy net of the 4,287 mat)  ->  {args[3]}");
    return 0;
}

// Focused plate-locator derisk: synthesis returns the slab plate box, poché measures its area.
// Usage: takeoff vector-plate <pdf> <page> <png>
if (args.Length >= 1 && args[0].Equals("vector-plate", StringComparison.OrdinalIgnoreCase))
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

// Thickness-zoning derisk: locate the plate, then split its area by thickness ZONE (Voronoi by the
// «N" SLAB» callouts) and compare the zoned effective thickness to the single modal value.
// Usage: takeoff vector-zones <pdf> <png> <page> <modalThk>
if (args.Length >= 1 && args[0].Equals("vector-zones", StringComparison.OrdinalIgnoreCase))
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

// Layer-2 synthesis derisk: build one page's digest, send the EXACT facts to Claude, print the
// structured page takeoff. Usage: takeoff vector-synth <pdf> <page>
if (args.Length >= 1 && args[0].Equals("vector-synth", StringComparison.OrdinalIgnoreCase))
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

// Layer-1 digest: emit the per-page structured facts (lines, geometry regions, scale, wall bands) the
// synthesis reasons over. Usage: takeoff vector-digest <pdf> <out.json> [firstPage] [lastPage]
if (args.Length >= 1 && args[0].Equals("vector-digest", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-digest <pdf> <out.json> [firstPage] [lastPage]"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    int? first = args.Length >= 4 && int.TryParse(args[3], out var f) ? f : null;
    int? last  = args.Length >= 5 && int.TryParse(args[4], out var l) ? l : null;

    var digest = DrawingDigestBuilder.Build(args[1], first, last);
    var json = JsonSerializer.Serialize(digest, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(args[2], json);
    Console.WriteLine($"Digest: {digest.Pages.Count} page(s) of {digest.PageCount} -> {args[2]} ({json.Length:N0} chars)");
    foreach (var pg in digest.Pages)
        Console.WriteLine($"  p{pg.Page}: {pg.Lines.Count} lines, {pg.ClosedRegions.Count} regions, {pg.WallBands.Count} wall bands, scale '{pg.ScaleNote ?? "?"}'");
    return 0;
}

// Schedule grid reconstruction probe: from the native vector tokens, recover the level ladder and the
// thickness cells (each resolved to its level row). Usage: takeoff vector-sched <pdf> <page>
if (args.Length >= 1 && args[0].Equals("vector-sched", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-sched <pdf> <page>"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (!int.TryParse(args[2], out int schPage) || schPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

    var page = VectorPageReader.ReadPage(args[1], schPage);
    var ladder = ScheduleGridReader.ReadLevelLadder(page);
    var cells  = ScheduleGridReader.ReadThicknessCells(page);

    Console.WriteLine($"Level ladder ({ladder.Count} rows, top->bottom):");
    Console.WriteLine("  " + string.Join("  ", ladder.Select(r => r.Normalized)));
    Console.WriteLine($"Thickness cells ({cells.Count}):");
    foreach (var c in cells)
        Console.WriteLine($"    {c.ThicknessIn,2:F0}\" WALL  @ {c.Level,-6} (x={c.X:F0})");

    var bands = ScheduleGridReader.ReadWallBands(page);
    Console.WriteLine($"Wall bands ({bands.Count}) — mark: top..bottom = thickness:");
    foreach (var b in bands)
        Console.WriteLine($"    {b.Mark,-4} {b.LevelTop,-4}..{b.LevelBottom,-4} = {b.ThicknessIn:F0}\"");
    return 0;
}

// Vector front-end probe: read the NATIVE vector text + geometry of one drawing page straight from the
// PDF — no raster, no OCR — and print what came out. Proves the exact-data foundation of the takeoff.
// Usage: takeoff vector-dump <pdf> <page>
if (args.Length >= 1 && args[0].Equals("vector-dump", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-dump <pdf> <page>"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (!int.TryParse(args[2], out int pageNo) || pageNo < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

    var pc = VectorPageReader.ReadPage(args[1], pageNo);
    int closed = pc.Paths.Count(p => p.IsClosed);
    Console.WriteLine($"Page {pc.PageNumber}: {pc.WidthPts:F0}x{pc.HeightPts:F0} pts");
    Console.WriteLine($"  Text:     {pc.Words.Count} words (exact, no OCR)");
    Console.WriteLine($"  Geometry: {pc.Paths.Count} subpaths ({closed} closed / {pc.Paths.Count - closed} open)");

    Console.WriteLine("  Largest closed regions (candidate slabs/zones), bbox in pts:");
    foreach (var g in pc.Paths.Where(p => p.IsClosed).OrderByDescending(p => p.DiagonalLen).Take(6))
        Console.WriteLine($"    {g.Width:F0}x{g.Height:F0}  ({g.Points.Count} pts){(g.IsFilled ? " filled" : "")}");

    // Optional 4th arg: a substring filter — print every matching token with its position.
    if (args.Length >= 4)
    {
        string needle = args[3];
        var hits = pc.Words.Where(t => t.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"  Tokens containing \"{needle}\": {hits.Count}");
        foreach (var t in hits.Take(40))
            Console.WriteLine($"    \"{t.Text}\" @ {t.Cx:F0},{t.Cy:F0}");
    }
    else
    {
        Console.WriteLine("  Sample text tokens (text @ x,y):");
        foreach (var t in pc.Words.Take(12))
            Console.WriteLine($"    \"{t.Text}\" @ {t.Cx:F0},{t.Cy:F0}");
    }
    return 0;
}

// Deterministic-plate probe: can we find the slab plate from the WHOLE page with NO synthesis box?
// Run the enclosed-area clustering on the full render and dump the top clusters (area, position, size).
// If the slab is the dominant central cluster, area can be made synthesis-free. Usage:
//   takeoff vector-plate-auto <png>
if (args.Length >= 1 && args[0].Equals("vector-plate-auto", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff vector-plate-auto <png>"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PNG not found '{args[1]}'."); return 2; }

    var (iw, ih) = PlanRaster.ImageSize(args[1]);
    var img = PlanRaster.LoadCrop(args[1], 0, 0, iw, ih);
    double mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110) ?? 0;
    var clusters = PlanGeometry.MeasureEnclosedClusters(img.Lum, img.Width, img.Height);
    Console.WriteLine($"{Path.GetFileName(args[1])}: {img.Width}x{img.Height}px, {clusters.Count} clusters");
    Console.WriteLine("  Top clusters (area sqft | center fx,fy | size w%,h% | bays):");
    foreach (var c in clusters.Take(8))
    {
        double cx = ((c.MinX + c.MaxX) / 2.0) / img.Width, cy = ((c.MinY + c.MaxY) / 2.0) / img.Height;
        Console.WriteLine($"    {PlanGeometry.SquareFeet(c.LightPx, mpp),9:N0} sqft | fx={cx:F2} fy={cy:F2} | {(double)c.Width / img.Width:P0} x {(double)c.Height / img.Height:P0} | {c.RegionCount} bays");
    }
    return 0;
}

// Geometry probe: can the slab AREA come from vector geometry instead of pixels? Dump the largest paths
// (open + closed) by extent, with point count and shoelace area (as if closed), in sq.ft at 1/8"=1'-0".
// If the slab outline shows up as a big path (or a stitchable few), area can be exact. Usage:
//   takeoff vector-geom <pdf> <page>
if (args.Length >= 1 && args[0].Equals("vector-geom", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-geom <pdf> <page>"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (!int.TryParse(args[2], out int gPage) || gPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

    var pc = VectorPageReader.ReadPage(args[1], gPage);
    const double Ft2PerPt2 = 0.01234567901; // (1/72 in/pt * 8 ft/in)^2 at 1/8"=1'-0"
    static double Shoelace(IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0; int n = p.Count;
        for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
        return Math.Abs(a) / 2.0;
    }
    Console.WriteLine($"Page {gPage}: {pc.WidthPts:F0}x{pc.HeightPts:F0} pts, {pc.Paths.Count} subpaths");
    Console.WriteLine("  Largest subpaths by bbox extent (area = shoelace as-if-closed, sq.ft @ 1/8\"=1'-0\"):");
    foreach (var g in pc.Paths.OrderByDescending(p => p.Width * p.Height).Take(20))
        Console.WriteLine($"    {(g.IsClosed ? "C" : "o")}{(g.IsStroked ? "S" : " ")}{(g.IsFilled ? "F" : " ")} pts={g.Points.Count,4}  bbox={g.Width,6:F0}x{g.Height,6:F0}pt  area={Shoelace(g.Points) * Ft2PerPt2,9:N0} sqft");

    // Also: the union extent of all stroked geometry (a rough slab-envelope upper bound).
    var stroked = pc.Paths.Where(p => p.IsStroked && p.Points.Count >= 2).ToList();
    if (stroked.Count > 0)
    {
        double minX = stroked.Min(p => p.MinX), minY = stroked.Min(p => p.MinY);
        double maxX = stroked.Max(p => p.MaxX), maxY = stroked.Max(p => p.MaxY);
        Console.WriteLine($"  Stroked-geometry envelope: {(maxX - minX):F0}x{(maxY - minY):F0}pt = {(maxX - minX) * (maxY - minY) * Ft2PerPt2:N0} sqft (gross bound)");
    }
    return 0;
}

// EVIDENCE probe: compute EVERY candidate slab-AREA signal for one sheet, in the drawing's real scale,
// so we can see which signal is reliable per sheet type BEFORE building a cascade. No AI. Usage:
//   takeoff vector-signals <pdf> <page> [png] [scaleDenom=100] [dpi=110]
// Signals: (P) raster poché largest+sum, (poly) largest closed vector polygon, (fill) filled-region sum,
//          (env) stroked-geometry envelope, (grid) dimensioned structural-grid bubble envelope.
if (args.Length >= 1 && args[0].Equals("vector-signals", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-signals <pdf> <page> [png] [scaleDenom=100] [dpi=110]"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (!int.TryParse(args[2], out int sgPage) || sgPage < 1) { Console.Error.WriteLine("Page must be positive."); return 2; }
    string? sgPng = args.Length >= 4 && File.Exists(args[3]) ? args[3] : null;
    double scaleDenom = args.Length >= 5 && double.TryParse(args[4], out var sd) ? sd : 100.0;
    double dpi = args.Length >= 6 && double.TryParse(args[5], out var dp) ? dp : 110.0;

    // Real-world conversion at scale 1:scaleDenom. 1 pt = 1/72 in (paper); real = paper × scaleDenom.
    double mPerPt = scaleDenom * (0.0254 / 72.0);
    double ft2PerPt2 = (mPerPt * 3.28084) * (mPerPt * 3.28084);
    double mpp = scaleDenom * (0.0254 / dpi);   // metres per pixel of the render, real-world

    static double Shoelace(IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0; int n = p.Count;
        if (n < 3) return 0;
        for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
        return Math.Abs(a) / 2.0;
    }

    var pc = VectorPageReader.ReadPage(args[1], sgPage);
    Console.WriteLine($"Page {sgPage}: {pc.WidthPts:F0}x{pc.HeightPts:F0}pt, scale 1:{scaleDenom:F0}, {pc.Paths.Count} paths, {pc.Words.Count} words");
    Console.WriteLine($"  (1pt = {mPerPt:F4}m real; 1pt² = {ft2PerPt2:F4} sqft)");

    // (poly) largest closed vector polygon
    double polyMax = 0; var closed = pc.Paths.Where(p => p.IsClosed && p.Points.Count >= 3).ToList();
    foreach (var g in closed) polyMax = Math.Max(polyMax, Shoelace(g.Points) * ft2PerPt2);
    // (fill) filled-region sum
    var filled = closed.Where(p => p.IsFilled).ToList();
    double fillSum = filled.Sum(g => Shoelace(g.Points) * ft2PerPt2);
    // (env) stroked-geometry envelope (gross bound)
    var stroked = pc.Paths.Where(p => p.IsStroked && p.Points.Count >= 2).ToList();
    double env = 0;
    if (stroked.Count > 0)
        env = (stroked.Max(p => p.MaxX) - stroked.Min(p => p.MinX))
            * (stroked.Max(p => p.MaxY) - stroked.Min(p => p.MinY)) * ft2PerPt2;

    Console.WriteLine($"  poly (largest closed polygon):  {polyMax,10:N0} sqft   (closed paths: {closed.Count})");
    Console.WriteLine($"  fill (filled-region sum):       {fillSum,10:N0} sqft   (filled paths: {filled.Count})");
    Console.WriteLine($"  env  (stroked envelope, gross): {env,10:N0} sqft");

    // (grid) the dimensioned structural-grid envelope — now read by the Core StructuralGridReader (one
    // source of truth shared with the engine). Running it here verifies Core on the real sheets.
    var gf = StructuralGridReader.FromPage(pc);
    if (gf != null)
    {
        Console.WriteLine($"  grid X bubbles ({gf.XLabels.Count}): [{string.Join(",", gf.XLabels)}]  span {gf.XSpanPt:F0}pt = {gf.XSpanPt * mPerPt:F1}m");
        Console.WriteLine($"  grid Y bubbles ({gf.YLabels.Count}): [{string.Join(",", gf.YLabels)}]  span {gf.YSpanPt:F0}pt = {gf.YSpanPt * mPerPt:F1}m");
        Console.WriteLine($"  grid (envelope X×Y):            {gf.EnvelopeSqFt(scaleDenom),10:N0} sqft   multiPlan={gf.MultiPlan} usable={gf.IsUsable}");
    }
    else Console.WriteLine("  grid: no bubbles found");

    // (thk) slab-thickness callouts (metric mm): pair each "SLAB" token with the nearest number to its
    // left on the same row. The distribution (field 200 vs band 450/900) is what drives zoning on the
    // transfer levels that a single field thickness under-prices.
    var numRx = new System.Text.RegularExpressions.Regex(@"^\d{2,4}$");
    var slabToks = pc.Words.Where(t => t.Text.Equals("SLAB", StringComparison.OrdinalIgnoreCase)).ToList();
    var thkTally = new SortedDictionary<int, int>();
    foreach (var s in slabToks)
    {
        double sh = s.MaxY - s.MinY;
        var cand = pc.Words
            .Where(t => numRx.IsMatch(t.Text) && Math.Abs(t.Cy - s.Cy) < Math.Max(sh, 6) && t.Cx < s.Cx && s.Cx - t.Cx < 9 * Math.Max(sh, 6))
            .OrderByDescending(t => t.Cx).FirstOrDefault();
        if (!cand.Equals(default(VectorPageReader.TextToken)) && int.TryParse(cand.Text, out int mm) && mm >= 100 && mm <= 1200)
            thkTally[mm] = thkTally.GetValueOrDefault(mm) + 1;
    }
    Console.WriteLine($"  SLAB callouts (mm×n): {(thkTally.Count > 0 ? string.Join("  ", thkTally.OrderByDescending(k => k.Value).Select(k => $"{k.Key}×{k.Value}")) : "—")}");

    // (P) raster poché largest cluster + sum of top clusters (no AI box — full page)
    if (sgPng != null)
    {
        var (iw, ih) = PlanRaster.ImageSize(sgPng);
        var img = PlanRaster.LoadCrop(sgPng, 0, 0, iw, ih);
        var cl = PlanGeometry.MeasureEnclosedClusters(img.Lum, img.Width, img.Height);
        double pocheMax = cl.Count > 0 ? PlanGeometry.SquareFeet(cl[0].LightPx, mpp) : 0;
        double pocheSum = cl.Take(12).Sum(c => PlanGeometry.SquareFeet(c.LightPx, mpp));
        Console.WriteLine($"  poché largest cluster:          {pocheMax,10:N0} sqft   (of {cl.Count} clusters)");
        Console.WriteLine($"  poché sum top-12 clusters:      {pocheSum,10:N0} sqft");
    }
    return 0;
}

// Title-block probe: dump words by FONT SIZE (height) and normalized position, so we can see where the
// sheet title actually lives (corner? largest font?) vs stray cross-references. Usage:
//   takeoff vector-words <pdf> <page> [needle]
if (args.Length >= 1 && args[0].Equals("vector-words", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-words <pdf> <page> [needle]"); return 1; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
    if (!int.TryParse(args[2], out int wPage) || wPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

    var pc = VectorPageReader.ReadPage(args[1], wPage);
    double W = pc.WidthPts, H = pc.HeightPts;
    // Normalized position: fx = fraction across (0=left,1=right), fy = fraction up from BOTTOM (PDF y).
    string Pos(VectorPageReader.TextToken t) => $"fx={t.Cx / W:F2} fy={t.Cy / H:F2}";

    Console.WriteLine($"Page {wPage}: {W:F0}x{H:F0} pts, {pc.Words.Count} words");
    if (args.Length >= 4)
    {
        string needle = args[3];
        var hits = pc.Words.Where(t => t.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(t => t.Height).ToList();
        Console.WriteLine($"  \"{needle}\" matches by font height ({hits.Count}):");
        foreach (var t in hits.Take(40))
            Console.WriteLine($"    h={t.Height,5:F1}  {Pos(t)}  \"{t.Text}\"");
    }
    else
    {
        Console.WriteLine("  Top 30 words by font height (h=pts):");
        foreach (var t in pc.Words.OrderByDescending(t => t.Height).Take(30))
            Console.WriteLine($"    h={t.Height,5:F1}  {Pos(t)}  \"{t.Text}\"");
    }
    return 0;
}

// Vision Layer 2: the app reads the drawing itself. For each page, Claude classifies the sheet and
// locates the concrete-outline plates (level, count, element, thickness, normalized box); the Core
// geometry then measures the largest enclosed region in each box; the pipeline prices + reconciles.
// Usage: takeoff vision-estimate <pages.json> <out.xlsx>
if (args.Length >= 3 && args[0].Equals("vision-estimate", StringComparison.OrdinalIgnoreCase))
{
    VisionPagesConfig? cfg;
    try
    {
        cfg = JsonSerializer.Deserialize<VisionPagesConfig>(
            File.ReadAllText(args[1]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex) { Console.Error.WriteLine($"Could not read/parse pages config '{args[1]}': {ex.Message}"); return 2; }
    if (cfg is null || cfg.Pages is null || cfg.Pages.Count == 0) { Console.Error.WriteLine("Pages config has no pages."); return 2; }
    if (string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set — vision layer needs an Anthropic key."); return 2; }

    var vProfile = PlanProfile.ByName(cfg.Profile);
    var vPlates = new List<MeasuredPlate>();
    // Suspended slabs are collected here and reconciled building-wide AFTER all sheets are read, so
    // each physical floor is counted once regardless of how the set encodes its level/layout ranges.
    var pendingSlabs = new List<PendingSlab>();
    // Schedule cross-reference: vertical concrete priced from the SHEAR WALL + COLUMN schedules (the
    // estimator's source of truth) instead of plan poché pixels, when the config supplies the level
    // list. Wall bands/column bands accumulate across schedule sheets (Part 1 + Part 2); key-plan mark
    // lengths are read once (the core layout is constant up the height — summing sheets would double it).
    var wallBands = new List<ScheduleTakeoff.WallBand>();
    var colBands = new List<ScheduleTakeoff.ColumnBand>();
    var wallMarkLen = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    bool wallKeyPlanDone = false;
    // Building-wide guard: the same floor is often drawn on several sheets (multi-issue reprints,
    // formwork vs reinforcing copies, enlarged partials). Summing all of them multiply-counts the
    // structure, so the first sheet to claim a given (kind + set-of-level-labels) wins.
    var seenSheetSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pg in cfg.Pages)
    {
        string png = Path.IsPathRooted(pg.Png) ? pg.Png : Path.Combine(cfg.PngDir ?? "", pg.Png ?? "");
        if (!File.Exists(png)) { Console.Error.WriteLine($"  ! image not found '{png}', skipped."); continue; }
        double dpi = pg.Dpi ?? cfg.Dpi;

        SheetReading reading;
        try
        {
            byte[] small = PlanRaster.LoadDownscaledPng(png, 1500);
            string visionJson = await PlanVisionClient.ReadSheetJsonAsync(small);
            reading = PlanVisionParser.Parse(visionJson);
        }
        catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(png)}: vision failed: {ex.Message}, skipped."); continue; }

        Console.WriteLine($"  {Path.GetFileName(png)}: {reading.Kind}, scale '{reading.ScaleNote ?? "(none)"}', {reading.Plates.Count} plate(s)");

        // ── cross-reference the dimensioned schedules (the estimator's source for verticals) ────────
        // A core wall key plan gives each mark's length — read ONCE per building (the core layout is the
        // same up the height; re-summing it per sheet would multiply the wall concrete). The single read
        // is the noisiest input (vision estimates lengths and varies which marks it catches), so read it
        // a few times and take the MEDIAN length per mark over the union of marks seen — deterministic
        // enough to stop the wall total swinging run-to-run.
        if (reading.HasWallKeyPlan && !wallKeyPlanDone)
        {
            const int keyPlanReads = 3;
            var perMark = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            int okReads = 0;
            byte[] kpPng = PlanRaster.LoadDownscaledPng(png, 1600);
            for (int rd = 0; rd < keyPlanReads; rd++)
            {
                try
                {
                    using var kp = JsonDocument.Parse(await PlanVisionClient.ReadWallKeyPlanJsonAsync(kpPng));
                    // Within ONE read, two occurrences of a mark = the two core faces (sum); but drop an
                    // occurrence whose box centroid coincides with one already seen (a duplicate read).
                    var seenCentroids = new Dictionary<string, List<(double x, double y)>>(StringComparer.OrdinalIgnoreCase);
                    var thisRead = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in kp.RootElement.GetProperty("marks").EnumerateArray())
                    {
                        string mk = (m.GetProperty("mark").GetString() ?? "").Trim();
                        double len = m.TryGetProperty("lengthFt", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetDouble() : 0;
                        if (mk.Length == 0 || len <= 0) continue;
                        double cx = 0.5, cy = 0.5;
                        if (m.TryGetProperty("box", out var bx) && bx.ValueKind == JsonValueKind.Array)
                        {
                            var v = bx.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
                            if (v.Count >= 4) { cx = (v[0] + v[2]) / 2; cy = (v[1] + v[3]) / 2; }
                        }
                        if (!seenCentroids.TryGetValue(mk, out var cs)) seenCentroids[mk] = cs = new();
                        if (cs.Any(c => Math.Abs(c.x - cx) < 0.02 && Math.Abs(c.y - cy) < 0.02)) continue;
                        cs.Add((cx, cy));
                        thisRead[mk] = thisRead.TryGetValue(mk, out var e) ? e + len : len;
                    }
                    foreach (var kv in thisRead)
                    {
                        if (!perMark.TryGetValue(kv.Key, out var lst)) perMark[kv.Key] = lst = new();
                        lst.Add(kv.Value);
                    }
                    okReads++;
                }
                catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(png)}: key plan read {rd + 1} failed: {ex.Message}"); }
            }
            if (okReads > 0)
            {
                // Keep every mark seen in ANY read (union) — a key-plan mark is only ever PRICED if it also
                // appears in the wall schedule, so a one-off vision hallucination is filtered downstream and
                // dropping marks here would just under-count real walls. Use the MEDIAN length over the reads
                // that saw each mark to damp the per-mark length noise.
                foreach (var (mk, lens) in perMark)
                {
                    var sorted = lens.OrderBy(x => x).ToList();
                    wallMarkLen[mk] = sorted[sorted.Count / 2];   // median
                }
                wallKeyPlanDone = wallMarkLen.Count > 0;
                Console.WriteLine($"      core wall key plan: {wallMarkLen.Count} marks (median of {okReads} reads), {wallMarkLen.Values.Sum():0} ft total");
            }
        }

        if (reading.Kind == SheetKind.Schedule)
        {
            try
            {
                if (reading.ScheduleType == SheetScheduleType.WallSchedule)
                {
                    using var doc = JsonDocument.Parse(await PlanVisionClient.ReadWallScheduleJsonAsync(PlanRaster.LoadDownscaledPng(png, 1600)));
                    int n = 0;
                    foreach (var b in doc.RootElement.GetProperty("entries").EnumerateArray())
                    {
                        double t = b.TryGetProperty("thicknessIn", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetDouble() : 0;
                        if (t <= 0) continue;
                        wallBands.Add(new ScheduleTakeoff.WallBand(
                            (b.GetProperty("mark").GetString() ?? "").Trim(),
                            b.GetProperty("levelTop").GetString() ?? "", b.GetProperty("levelBottom").GetString() ?? "", t));
                        n++;
                    }
                    Console.WriteLine($"      wall schedule: {n} thickness bands");
                }
                else if (reading.ScheduleType == SheetScheduleType.ColumnSchedule)
                {
                    using var doc = JsonDocument.Parse(await PlanVisionClient.ReadColumnScheduleJsonAsync(PlanRaster.LoadDownscaledPng(png, 1600)));
                    int n = 0;
                    foreach (var b in doc.RootElement.GetProperty("entries").EnumerateArray())
                    {
                        double w = b.TryGetProperty("widthIn", out var wv) && wv.ValueKind == JsonValueKind.Number ? wv.GetDouble() : 0;
                        double d = b.TryGetProperty("depthIn", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : 0;
                        if (w <= 0) continue;
                        colBands.Add(new ScheduleTakeoff.ColumnBand(
                            (b.GetProperty("mark").GetString() ?? "").Trim(),
                            b.GetProperty("levelTop").GetString() ?? "", b.GetProperty("levelBottom").GetString() ?? "", w, d));
                        n++;
                    }
                    Console.WriteLine($"      column schedule: {n} size bands");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(png)}: schedule read failed: {ex.Message}"); }
            continue;   // a schedule sheet carries no plates to measure
        }

        if (reading.Kind != SheetKind.Framing && reading.Kind != SheetKind.Foundation) continue;

        // Skip a sheet that re-draws levels an earlier sheet already supplied (kind + level-label set).
        string sheetSig = reading.Kind + ":" + string.Join("|", reading.Plates
            .Select(p => System.Text.RegularExpressions.Regex.Replace((p.Level ?? "").Trim().ToUpperInvariant(), @"\s+", " "))
            .Where(s => s.Length > 0)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal));
        if (reading.Plates.Count > 0 && !seenSheetSignatures.Add(sheetSig))
        { Console.Error.WriteLine($"  · {Path.GetFileName(png)}: levels already taken from an earlier sheet — duplicate, skipped."); continue; }

        double? mppSheet = PlanGeometry.MetresPerPixel(reading.ScaleNote, dpi);
        double? mpp = mppSheet ?? PlanGeometry.MetresPerPixel(cfg.Scale, dpi);
        if (mpp is null) { Console.Error.WriteLine($"  ! {Path.GetFileName(png)}: no usable scale, skipped."); continue; }
        // Confirmed only when THIS sheet's own scale note parsed. A non-null but unparseable note that
        // fell back to the config scale is NOT confirmation — let the pipeline flag SCALE_UNCONFIRMED.
        bool scaleConfirmed = mppSheet.HasValue;

        int fullW, fullH;
        try { (fullW, fullH) = PlanRaster.ImageSize(png); }
        catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(png)}: cannot read image size: {ex.Message}, skipped."); continue; }

        // Vertical-element footprints already claimed on THIS sheet (full-image centroids), so a column
        // sitting in the overlap of two plate boxes is counted once, not under both plates.
        var claimedVertical = new List<(int x, int y)>();
        // Thickened zones read on THIS sheet (full-image centroid, area, total thickness, confidence),
        // attached after the plate loop to the slab whose box contains them.
        var sheetThickenings = new List<(int cx, int cy, double areaSqFt, double totalThkIn, double conf)>();
        int slabStartIdx = pendingSlabs.Count;
        foreach (var pl in reading.Plates)
        {
            // The vision layer returns a degenerate (zero-area) box when it could not locate a plate;
            // skip it rather than crop a bogus region. The parser never defaults to the whole sheet.
            if (pl.NormX1 <= pl.NormX0 || pl.NormY1 <= pl.NormY0)
            { Console.Error.WriteLine($"  ! {pl.Level}: no usable box from vision, skipped."); continue; }

            // Vision-estimate measures slab/foundation plates only. Walls/columns are gray-fill, which
            // isn't box-confined (it would sum a clipped neighbour's gray) — route those to the
            // deterministic `estimate` mode with a tight human crop instead of measuring them here.
            if (pl.Element is TakeoffElementType.Wall or TakeoffElementType.Column)
            { Console.Error.WriteLine($"  ! {pl.Level} {pl.Element}: vision wall/column not supported (use 'estimate' mode), skipped."); continue; }

            const double pad = 0.025;
            int x0 = (int)((pl.NormX0 - pad) * fullW), y0 = (int)((pl.NormY0 - pad) * fullH);
            int x1 = (int)((pl.NormX1 + pad) * fullW), y1 = (int)((pl.NormY1 + pad) * fullH);
            PlanRaster.Crop crop;
            try { crop = PlanRaster.LoadCrop(png, x0, y0, x1, y1); }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {pl.Level}: crop failed: {ex.Message}, skipped."); continue; }

            // Map the UNPADDED vision box into crop-local pixels (LoadCrop clamps the origin to >=0).
            int cox = Math.Clamp(x0, 0, fullW), coy = Math.Clamp(y0, 0, fullH);
            int bx0 = Math.Clamp((int)(pl.NormX0 * fullW) - cox, 0, crop.Width);
            int by0 = Math.Clamp((int)(pl.NormY0 * fullH) - coy, 0, crop.Height);
            int bx1 = Math.Clamp((int)(pl.NormX1 * fullW) - cox, 0, crop.Width);
            int by1 = Math.Clamp((int)(pl.NormY1 * fullH) - coy, 0, crop.Height);

            // Cluster the crop into plates. mergeGapPx scales with render DPI (grid lines are a fixed
            // PAPER width → more pixels at higher DPI); minPixels drops sub-½-sq.ft specks and text
            // counters so a note string can't chain two plates into one cluster.
            int mergeGap = Math.Max(4, (int)Math.Round(dpi * 0.05));
            long minPx = Math.Max(1L, (long)(0.5 / (mpp.Value * mpp.Value * 10.763910416709722)));
            var clusters = PlanGeometry.MeasureEnclosedClusters(
                crop.Lum, crop.Width, crop.Height, minPixels: minPx, mergeGapPx: mergeGap);

            // The plate is the LARGEST cluster lying predominantly inside the vision box (bbox ≥60%
            // overlapped). Honouring the box is what defeats the "bigger neighbour" trap: a neighbour
            // the padded box merely clipped contributes only a partial fragment, which loses to the
            // target's full plate; small in-box strays (dimension tables, legends) lose too. We do NOT
            // silently sum secondary clusters — a clipped neighbour can also look "in-box". Instead we
            // FLAG when a comparable second region is present, and let a human resolve the box.
            long px = 0, second = 0;
            foreach (var c in clusters)
            {
                long bboxArea = (long)c.Width * c.Height;
                long ix = Math.Max(0L, Math.Min(c.MaxX, bx1 - 1) - Math.Max(c.MinX, bx0) + 1);
                long iy = Math.Max(0L, Math.Min(c.MaxY, by1 - 1) - Math.Max(c.MinY, by0) + 1);
                double ratio = bboxArea > 0 ? (double)(ix * iy) / bboxArea : 0;
                if (ratio < 0.6) continue;                    // not predominantly inside this plate's box
                if (px == 0) px = c.LightPx;                  // largest in-box (clusters are sorted largest-first)
                else if (second == 0) second = c.LightPx;     // next-largest in-box, for the ambiguity check
            }
            bool ambiguous = false;
            if (px == 0 && clusters.Count > 0) { px = clusters[0].LightPx; ambiguous = true; } // box missed the plate
            if (px == 0) { Console.Error.WriteLine($"  ! {pl.Level}: no enclosed region found, skipped."); continue; }
            if (ambiguous)
                Console.Error.WriteLine($"  ~ {pl.Level}: vision box did not cleanly enclose a plate — used largest region, VERIFY.");
            else if (second >= px * 0.5)
                Console.Error.WriteLine($"  ~ {pl.Level}: a comparable second region is inside the box — possible clipped neighbour or multi-part plate, VERIFY box.");

            double areaSqFt = PlanGeometry.SquareFeet(px, mpp.Value);
            double thickness = pl.ThicknessIn ?? 0;          // 0 => pipeline flags THK_UNRESOLVED

            // A thickened zone (drop panel / built-up transfer) is measured here but held: it has no
            // floor count of its own — it rides the slab it sits on, and is priced as its depth ABOVE
            // that slab's nominal so the field slab underneath is never counted twice. Defer to the
            // post-loop attach, which subtracts the owning slab's nominal thickness.
            if (pl.Element is TakeoffElementType.DropPanel)
            {
                if (thickness <= 0)
                { Console.Error.WriteLine($"  ~ {pl.Level} thickening: no readable depth, skipped."); continue; }
                int tcx = cox + (bx0 + bx1) / 2, tcy = coy + (by0 + by1) / 2;
                sheetThickenings.Add((tcx, tcy, areaSqFt, thickness, pl.Confidence));
                Console.WriteLine($"      {pl.Level} thickening {thickness:0.#}\" total: {areaSqFt:N0} sq.ft (conf {pl.Confidence:0.00})");
                continue;
            }

            // Slab-on-grade thickness is usually a note off the footings sheet; fall back to the config
            // default so the SOG isn't silently dropped (still flagged if no default is configured).
            if (thickness <= 0 && cfg.SogThicknessIn > 0 && pl.Element == TakeoffElementType.Foundation
                && pl.Level.IndexOf("SOG", StringComparison.OrdinalIgnoreCase) >= 0)
                thickness = cfg.SogThicknessIn;

            // Foundations (footings / SOG / mats) are built ONCE — emit directly with count 1. Suspended
            // slabs are deferred to the building-wide floor reconciliation that fixes their counts below.
            if (pl.Element is not TakeoffElementType.Slab)
            {
                vPlates.Add(new MeasuredPlate(pl.Level, pl.Element, pl.Variant, areaSqFt, thickness, Math.Max(1, pl.Count), "", scaleConfirmed));
                Console.WriteLine($"      {pl.Level} {pl.Element} {thickness:0.#}\" x1: {areaSqFt:N0} sq.ft (conf {pl.Confidence:0.00})");
                continue;
            }

            // ── derive the vertical concrete (walls + columns) co-located on this slab ────────────
            // Solid-gray fill inside the SAME plate outline; the slab area above is gross (spans under
            // them) so this is additional concrete. Only when the box was trusted and a storey height
            // is known. Footprints captured now, priced at the reconciled floor count below.
            double wallSqFt = 0, colSqFt = 0;
            double storeyIn = pg.StoreyHeightIn ?? cfg.StoreyHeightIn;
            if (ambiguous || second >= px * 0.5)
                Console.Error.WriteLine($"  ~ {pl.Level}: box not trusted — wall/column NOT measured for this plate.");
            else if (storeyIn <= 0)
                Console.Error.WriteLine($"  ~ {pl.Level}: no storey height set — wall/column concrete NOT measured (set storeyHeightIn).");
            else
            {
                try
                {
                    double sqftPerPx = mpp.Value * mpp.Value * 10.763910416709722;
                    long colMinPx = Math.Max(20L, (long)(0.2 / sqftPerPx));   // drop sub-0.2-sq.ft gray speckle
                    long colMaxPx = (long)(25.0 / sqftPerPx);                 // a column footprint caps ~25 sq.ft; bigger ⇒ wall
                    int dedupeTolPx = Math.Max(8, (int)(0.4572 / mpp.Value));  // ~18" — same physical column across overlapping boxes
                    var grayComps = PlanGeometry.MeasureGrayComponents(
                        crop.R, crop.G, crop.B, crop.Width, crop.Height, minPixels: colMinPx);
                    long wallPx = 0, colPx = 0; int nWall = 0, nCol = 0;
                    foreach (var gc in grayComps)
                    {
                        int gcx = (gc.MinX + gc.MaxX) / 2, gcy = (gc.MinY + gc.MaxY) / 2;
                        if (gcx < bx0 || gcx > bx1 || gcy < by0 || gcy > by1) continue;  // outside this plate's box
                        int fx = cox + gcx, fy = coy + gcy;                              // full-sheet centroid
                        bool already = false;
                        foreach (var (cxr, cyr) in claimedVertical)
                            if (Math.Abs(cxr - fx) <= dedupeTolPx && Math.Abs(cyr - fy) <= dedupeTolPx) { already = true; break; }
                        if (already) continue;                                           // already counted under an overlapping plate
                        claimedVertical.Add((fx, fy));
                        if (PlanGeometry.ClassifyVertical(gc, colMaxPx) == PlanGeometry.VerticalKind.Wall)
                        { wallPx += gc.AreaPx; nWall++; }
                        else { colPx += gc.AreaPx; nCol++; }
                    }
                    wallSqFt = PlanGeometry.SquareFeet(wallPx, mpp.Value);
                    colSqFt = PlanGeometry.SquareFeet(colPx, mpp.Value);
                }
                catch (Exception ex) { Console.Error.WriteLine($"  ~ {pl.Level}: vertical measurement failed: {ex.Message}, slab kept."); }
            }
            pendingSlabs.Add(new PendingSlab(pl.Level, pl.Variant, areaSqFt, thickness, pl.Confidence, scaleConfirmed, wallSqFt, colSqFt, storeyIn,
                cox + bx0, coy + by0, cox + bx1, coy + by1));
            Console.WriteLine($"      {pl.Level} Slab {thickness:0.#}\": {areaSqFt:N0} sq.ft (+ wall {wallSqFt:N0} / col {colSqFt:N0}) (conf {pl.Confidence:0.00})");
        }

        // Attach this sheet's thickened zones to the slab whose box contains each one (else the first
        // slab on the sheet). The added depth is the zone's total thickness minus that slab's nominal,
        // so a thickening over a 10" field slab priced at 16" total adds only its extra 6" of concrete.
        foreach (var th in sheetThickenings)
        {
            PendingSlab? owner = null;
            for (int si = slabStartIdx; si < pendingSlabs.Count; si++)
            {
                var ps = pendingSlabs[si];
                if (th.cx >= ps.BoxX0 && th.cx <= ps.BoxX1 && th.cy >= ps.BoxY0 && th.cy <= ps.BoxY1) { owner = ps; break; }
            }
            owner ??= slabStartIdx < pendingSlabs.Count ? pendingSlabs[slabStartIdx] : null;
            if (owner is null)
            { Console.Error.WriteLine($"  ~ thickening on a sheet with no suspended slab — dropped (not a floor plate)."); continue; }
            double added = th.totalThkIn - owner.ThicknessIn;
            if (added <= 0)
            { Console.Error.WriteLine($"  ~ {owner.Level} thickening {th.totalThkIn:0.#}\" ≤ slab {owner.ThicknessIn:0.#}\" — no added concrete, dropped."); continue; }
            owner.Thickenings.Add(new Thickening(added, th.areaSqFt, th.conf));
        }
    }

    // ── schedule-driven verticals: replace the gray-fill estimate when the schedules were read ──────
    // When the config supplies the building's ordered level list, price shear walls from the wall
    // schedule + key plan and columns from the column schedule (the dimensioned source of truth). In
    // that case the per-floor gray-fill wall/column footprints are SUPPRESSED below to avoid double-
    // counting; without a level list (or schedules), the gray-fill estimate stands.
    var levelList = cfg.Levels is { Count: > 0 } ? cfg.Levels : null;
    double[]? storeyInArr = null;
    if (levelList != null)
    {
        storeyInArr = new double[levelList.Count];
        var heightMap = new Dictionary<string, double>(StringComparer.Ordinal);
        if (cfg.StoreyHeightInByLevel != null)
            foreach (var kv in cfg.StoreyHeightInByLevel)
                heightMap[ScheduleTakeoff.NormalizeLevel(kv.Key)] = kv.Value;
        for (int i = 0; i < levelList.Count; i++)
            storeyInArr[i] = heightMap.TryGetValue(ScheduleTakeoff.NormalizeLevel(levelList[i]), out var h) && h > 0
                ? h : cfg.StoreyHeightIn;
    }
    // H4 guard: a level list with no usable storey height would price every wall/column to zero — don't
    // activate the schedule path (and silently delete the gray-fill); keep the gray-fill estimate.
    bool hasStorey = storeyInArr != null && storeyInArr.Any(h => h > 0);
    bool useSchedWalls = levelList != null && hasStorey && wallMarkLen.Count > 0 && wallBands.Count > 0;
    bool useSchedCols = levelList != null && hasStorey && colBands.Count > 0;
    if (levelList != null && !hasStorey && (wallBands.Count > 0 || colBands.Count > 0))
        Console.Error.WriteLine("  ! schedules read but no storey height set (storeyHeightIn) — keeping gray-fill verticals.");

    // Compute the schedule verticals up front so the reconciliation loop below can suppress the gray-fill
    // ONLY on the levels the schedule actually priced — uncovered levels (e.g. an upper tower whose Part-2
    // schedule isn't in the set, or basement/perimeter walls the core schedule omits) keep the gray-fill
    // fallback instead of silently vanishing.
    ScheduleTakeoff.ScheduleResult? wallRes = useSchedWalls && storeyInArr != null
        ? ScheduleTakeoff.ComputeWall(levelList!, storeyInArr, wallMarkLen, wallBands) : null;
    ScheduleTakeoff.ScheduleResult? colRes = useSchedCols && storeyInArr != null
        ? ScheduleTakeoff.ComputeColumn(levelList!, storeyInArr, colBands) : null;
    var coveredWallLevels = wallRes is null ? new HashSet<string>()
        : wallRes.PerLevel.Where(p => p.FootprintSqFt > 0).Select(p => ScheduleTakeoff.NormalizeLevel(p.Level)).ToHashSet();
    var coveredColLevels = colRes is null ? new HashSet<string>()
        : colRes.PerLevel.Where(p => p.FootprintSqFt > 0).Select(p => ScheduleTakeoff.NormalizeLevel(p.Level)).ToHashSet();

    // A slab band is "schedule-covered" for an element when the level it represents was priced by the
    // schedule — there the gray-fill is suppressed; elsewhere it stands. Match both the slab's RAW label
    // (so "P1 MEZZ" matches a schedule "P1 MEZZ") and its parsed floors (so a band "L17-28" matches the
    // schedule's per-level "L17"…"L28") — otherwise a label variant keeps gray-fill ON a covered level
    // and double-counts it against the schedule.
    bool SlabCovered(string level, HashSet<string> covered)
    {
        if (covered.Count == 0) return false;
        if (covered.Contains(ScheduleTakeoff.NormalizeLevel(level))) return true;
        return BuildingRollup.ParseFloors(level).Any(f => covered.Contains(ScheduleTakeoff.NormalizeLevel(f)));
    }

    int grayWallsKept = 0, grayColsKept = 0;

    // ── building-wide floor reconciliation ──────────────────────────────────────────────────────
    // Count each physical floor's suspended slab exactly once. Parse every slab's level label into the
    // floors it represents, assign each floor to one owning plate, and price it at that owned count —
    // so overlapping "LAYOUT APPLIES TO" sets and outline/reinforcing copies collapse, while clean
    // bands are untouched. Walls/columns inherit their slab's reconciled count.
    var slabRefs = new List<BuildingRollup.SlabRef>(pendingSlabs.Count);
    for (int i = 0; i < pendingSlabs.Count; i++)
        slabRefs.Add(new BuildingRollup.SlabRef(i, pendingSlabs[i].Level, pendingSlabs[i].AreaSqFt, pendingSlabs[i].Confidence, pendingSlabs[i].ThicknessIn));
    var ownedFloors = BuildingRollup.AssignSlabFloors(slabRefs);
    int keptSlabs = 0, droppedSlabs = 0;
    for (int i = 0; i < pendingSlabs.Count; i++)
    {
        var s = pendingSlabs[i];
        int eff = ownedFloors.TryGetValue(i, out var e) ? e : 1;
        if (eff <= 0)
        { droppedSlabs++; Console.Error.WriteLine($"  · {s.Level}: floor(s) already owned by a more specific or re-issued sheet — dropped."); continue; }
        keptSlabs++;
        vPlates.Add(new MeasuredPlate(s.Level, TakeoffElementType.Slab, s.Variant, s.AreaSqFt, s.ThicknessIn, eff, "", s.ScaleConfirmed));
        // Gray-fill walls/columns are the fallback estimate — kept only where the schedule did NOT price
        // this level, so covered levels use the schedule and uncovered levels don't silently lose concrete.
        if (s.WallSqFt > 0 && !SlabCovered(s.Level, coveredWallLevels))
        { vPlates.Add(new MeasuredPlate(s.Level, TakeoffElementType.Wall, "shear", s.WallSqFt, s.StoreyIn, eff, "", s.ScaleConfirmed)); grayWallsKept++; }
        if (s.ColSqFt > 0 && !SlabCovered(s.Level, coveredColLevels))
        { vPlates.Add(new MeasuredPlate(s.Level, TakeoffElementType.Column, null, s.ColSqFt, s.StoreyIn, eff, "", s.ScaleConfirmed)); grayColsKept++; }
        foreach (var th in s.Thickenings)   // drop panels / built-up zones: added depth over the slab, same floor count
            vPlates.Add(new MeasuredPlate(s.Level, TakeoffElementType.DropPanel, "thickening", th.AreaSqFt, th.AddedDepthIn, eff, "", s.ScaleConfirmed));
    }
    Console.WriteLine($"Floor reconciliation: {keptSlabs} slab plate(s) kept, {droppedSlabs} dropped as duplicate/superseded.");

    // Emit the schedule-driven verticals (one plate per covered level, count 1 — bands already expanded).
    if (wallRes != null)
    {
        foreach (var lf in wallRes.PerLevel)
            if (lf.FootprintSqFt > 0)
                vPlates.Add(new MeasuredPlate(lf.Level, TakeoffElementType.Wall, "shear", lf.FootprintSqFt, lf.StoreyIn, 1, "", true));
        Console.WriteLine($"Schedule walls: {wallRes.TotalCuYd:N0} cu.yd over {coveredWallLevels.Count}/{levelList!.Count} levels "
            + $"({wallRes.MarksPriced} marks, {wallRes.BandsApplied} bands applied, {wallRes.BandsSkipped} skipped); "
            + $"{grayWallsKept} slab(s) kept gray-fill walls on uncovered levels.");
    }
    if (colRes != null)
    {
        foreach (var lf in colRes.PerLevel)
            if (lf.FootprintSqFt > 0)
                vPlates.Add(new MeasuredPlate(lf.Level, TakeoffElementType.Column, null, lf.FootprintSqFt, lf.StoreyIn, 1, "", true));
        Console.WriteLine($"Schedule columns: {colRes.TotalCuYd:N0} cu.yd over {coveredColLevels.Count}/{levelList!.Count} levels "
            + $"({colRes.MarksPriced} marks, {colRes.BandsApplied} bands applied, {colRes.BandsSkipped} skipped); "
            + $"{grayColsKept} slab(s) kept gray-fill columns on uncovered levels.");
    }

    if (vPlates.Count == 0) { Console.Error.WriteLine("No measurable plates from vision."); return 2; }

    var vResult = PlanEstimatePipeline.Run(vPlates, vProfile);
    var vComputed = StructuralTakeoffService.Compute(vResult.TakeoffInputs, vProfile.ToImperialDensityTable());
    var vModel = new StructuralTakeoffReportModel(cfg.Project ?? "", cfg.Name ?? "", cfg.Issue ?? "", DateTime.UtcNow, vComputed);
    File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(vModel));

    Console.WriteLine($"Profile: {vProfile.Name}   Plates: {vResult.Plates.Count}");
    Console.WriteLine($"Concrete: {vResult.TotalConcreteCuYd:N0} cu.yd   Reinforcing: {vComputed.TotalRebarWeight:N0} lb");
    Console.WriteLine($"Diligence: {vResult.CriticalCount} critical, {vResult.ReviewCount} to review");
    foreach (var pe in vResult.Plates)
        foreach (var f in pe.Check.Flags)
            Console.WriteLine($"  [{f.Severity}] {pe.Plate.Level} {pe.Plate.Element}: {f.Message}");
    Console.WriteLine($"Wrote {args[2]}");
    return 0;
}

// Full stickfile → takeoff estimate. Reads a building config (the plate map a human or the
// vision layer produces), measures each plate off its rasterized sheet with the Core geometry
// engine, prices + reconciles via the pipeline, and writes the same orange-celled takeoff xlsx
// the app produces — plus a diligence report of everything it could not fully trust.
// Usage: takeoff estimate <config.json> <out.xlsx>
if (args.Length >= 3 && args[0].Equals("estimate", StringComparison.OrdinalIgnoreCase))
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

// Layer-1 geometry probe: measure a plate area (flood-fill) or a wall/column footprint
// (gray-fill) off one rasterized plan crop. Validates the engine and is the pipeline's
// measurement step. Usage: takeoff measure <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [gray]
if (args.Length >= 8 && args[0].Equals("measure", StringComparison.OrdinalIgnoreCase))
{
    var ic = CultureInfo.InvariantCulture;
    if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
        !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
        !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
        !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
        !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
    { Console.Error.WriteLine("measure: crop coords must be integers and dpi a number."); return 2; }
    string note = args[7];
    double? mpp = PlanGeometry.MetresPerPixel(note, dpi);
    if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{note}'."); return 2; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }

    PlanRaster.Crop crop;
    try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
    catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }
    bool gray = args.Length > 8 && args[8].Equals("gray", StringComparison.OrdinalIgnoreCase);
    if (gray)
    {
        long px = PlanGeometry.MeasureGrayFootprint(crop.R, crop.G, crop.B, crop.Width, crop.Height);
        Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}");
        Console.WriteLine($"gray footprint : {px:N0} px = {PlanGeometry.SquareFeet(px, mpp.Value):N0} sq.ft");
    }
    else
    {
        var a = PlanGeometry.MeasureEnclosedArea(crop.Lum, crop.Width, crop.Height);
        var clusters = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
        long largest = clusters.Count > 0 ? clusters[0].LightPx : 0;
        Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  clusters={clusters.Count}");
        Console.WriteLine($"enclosed lo(light)  : {a.LowerPx,11:N0} px = {PlanGeometry.SquareFeet(a.LowerPx, mpp.Value):N0} sq.ft");
        Console.WriteLine($"enclosed hi(+dark)  : {a.UpperPx,11:N0} px = {PlanGeometry.SquareFeet(a.UpperPx, mpp.Value):N0} sq.ft");
        Console.WriteLine($"largest cluster     : {largest,11:N0} px = {PlanGeometry.SquareFeet(largest, mpp.Value):N0} sq.ft");
    }
    return 0;
}

// Gray-fill diagnostic: dump the neutral-tone histogram + the gray connected-components (with shape)
// of a crop, so wall/column fill thresholds and the shape split are chosen from evidence, not guessed.
// Usage: takeoff graycomp <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [lo] [hi]
if (args.Length >= 8 && args[0].Equals("graycomp", StringComparison.OrdinalIgnoreCase))
{
    var ic = CultureInfo.InvariantCulture;
    if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
        !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
        !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
        !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
        !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
    { Console.Error.WriteLine("graycomp: crop coords must be integers and dpi a number."); return 2; }
    double? mpp = PlanGeometry.MetresPerPixel(args[7], dpi);
    if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{args[7]}'."); return 2; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
    int lo = args.Length > 8 && int.TryParse(args[8], NumberStyles.Integer, ic, out int loV) ? loV : 196;
    int hi = args.Length > 9 && int.TryParse(args[9], NumberStyles.Integer, ic, out int hiV) ? hiV : 228;

    PlanRaster.Crop crop;
    try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
    catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }

    double sqftPer(long p) => PlanGeometry.SquareFeet(p, mpp.Value);
    var histo = PlanGeometry.NeutralLuminanceHistogram(crop.R, crop.G, crop.B, crop.Width, crop.Height);
    Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  band=[{lo},{hi}]");
    Console.WriteLine("neutral-tone histogram (lum bin -> px):");
    for (int bi = 0; bi < histo.Length; bi++)
        if (histo[bi] > 0) Console.WriteLine($"  {bi * 16,3}-{bi * 16 + 15,3}: {histo[bi],10:N0}");

    var comps = PlanGeometry.MeasureGrayComponents(crop.R, crop.G, crop.B, crop.Width, crop.Height, lo, hi, minPixels: 30);
    long total = 0; foreach (var c in comps) total += c.AreaPx;
    Console.WriteLine($"gray band total: {total:N0} px = {sqftPer(total):N1} sq.ft across {comps.Count} comp(s)");
    Console.WriteLine("top components (area sq.ft, WxH px, solidity, elongation):");
    for (int k = 0; k < Math.Min(20, comps.Count); k++)
    {
        var c = comps[k];
        Console.WriteLine($"  {sqftPer(c.AreaPx),7:N1}  {c.Width,4}x{c.Height,-4}  sol={c.Solidity:0.00}  elong={c.Elongation:0.0}");
    }
    return 0;
}

// Hatched-footing detector diagnostic: deterministically find the cross-hatched concrete mats /
// deep footings on a crop and list their measured areas. Used to calibrate density / minSqFt.
// Usage: takeoff hatch <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [densityPct] [minSqFt] [winR]
if (args.Length >= 8 && args[0].Equals("hatch", StringComparison.OrdinalIgnoreCase))
{
    var ic = CultureInfo.InvariantCulture;
    if (!int.TryParse(args[2], NumberStyles.Integer, ic, out int x0) ||
        !int.TryParse(args[3], NumberStyles.Integer, ic, out int y0) ||
        !int.TryParse(args[4], NumberStyles.Integer, ic, out int x1) ||
        !int.TryParse(args[5], NumberStyles.Integer, ic, out int y1) ||
        !double.TryParse(args[6], NumberStyles.Float, ic, out double dpi))
    { Console.Error.WriteLine("hatch: crop coords must be integers and dpi a number."); return 2; }
    double? mpp = PlanGeometry.MetresPerPixel(args[7], dpi);
    if (mpp is null) { Console.Error.WriteLine($"Unparseable scale note '{args[7]}'."); return 2; }
    if (!File.Exists(args[1])) { Console.Error.WriteLine($"Image not found '{args[1]}'."); return 2; }
    double densityPct = args.Length > 8 && double.TryParse(args[8], NumberStyles.Float, ic, out double dn) ? dn : 18.0;
    double minSqFt = args.Length > 9 && double.TryParse(args[9], NumberStyles.Float, ic, out double ms) ? ms : 10.0;
    int winR = args.Length > 10 && int.TryParse(args[10], NumberStyles.Integer, ic, out int wr) ? wr : 10;

    PlanRaster.Crop crop;
    try { crop = PlanRaster.LoadCrop(args[1], x0, y0, x1, y1); }
    catch (Exception ex) { Console.Error.WriteLine($"Could not load/crop '{args[1]}': {ex.Message}"); return 2; }
    long minPx = (long)(minSqFt / (mpp.Value * mpp.Value * 10.763910416709722));
    var regions = PlanGeometry.MeasureHatchedRegions(crop.Lum, crop.Width, crop.Height, windowRadius: winR, densityPercent: densityPct, minPixels: minPx);
    long tot = 0; foreach (var r in regions) tot += r.AreaPx;
    Console.WriteLine($"crop {crop.Width}x{crop.Height}  mpp={mpp:0.000000}  density={densityPct}%  winR={winR}  minSqFt={minSqFt}");
    Console.WriteLine($"{regions.Count} hatched region(s), total {PlanGeometry.SquareFeet(tot, mpp.Value):N0} sq.ft:");
    for (int k = 0; k < Math.Min(25, regions.Count); k++)
    {
        var r = regions[k];
        Console.WriteLine($"  {PlanGeometry.SquareFeet(r.AreaPx, mpp.Value),7:N0} sq.ft  {r.Width,4}x{r.Height,-4}  @({r.CentroidX},{r.CentroidY})");
    }
    return 0;
}

// Debug: dump a SHEAR WALL SCHEDULE reading (wall thickness per mark per level).
// Usage: takeoff wallsched <png>
if (args.Length >= 2 && args[0].Equals("wallsched", StringComparison.OrdinalIgnoreCase))
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

// Debug: dump a CORE WALL KEY PLAN reading (each wall mark's centreline length).
// Usage: takeoff wallplan <png>
if (args.Length >= 2 && args[0].Equals("wallplan", StringComparison.OrdinalIgnoreCase))
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

// Core-wall concrete the estimator's way: cross-reference the CORE WALL KEY PLAN (each mark's length)
// with the SHEAR WALL SCHEDULE (each mark's thickness per level band), priced over a level list.
// Usage: takeoff wallconcrete <keyplan.png> <schedule.png> <levels.json>
//   levels.json: { "storeyHeightFt": 10.5, "levels": ["LEVEL 20", ... , "P7"] }  (top to bottom)
if (args.Length >= 4 && args[0].Equals("wallconcrete", StringComparison.OrdinalIgnoreCase))
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

// Single-issue absolute takeoff from one concrete schedule CSV (faithful to the app's
// GenerateTakeoff_Click: Import -> Compute -> BuildXlsx).
// Usage: takeoff single <schedule.csv> <out.xlsx> [wbs] [name] [issue] [imperial]
if (args.Length >= 3 && args[0].Equals("single", StringComparison.OrdinalIgnoreCase))
{
    var sInputs = StructuralTakeoffCsvImporter.Import(File.ReadAllText(args[1]));
    bool imp = args.Length > 6 && args[6].Equals("imperial", StringComparison.OrdinalIgnoreCase);
    var table = imp ? StructuralDensityTable.KorImperialDefault : StructuralDensityTable.KorMetricDefault;
    var sResult = StructuralTakeoffService.Compute(sInputs, table);
    var sModel = new StructuralTakeoffReportModel(
        args.Length > 3 ? args[3] : "", args.Length > 4 ? args[4] : "",
        args.Length > 5 ? args[5] : "", DateTime.UtcNow, sResult);
    File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(sModel));
    string vU = imp ? "cu.yd" : "m3", wU = imp ? "lb" : "kg";
    Console.WriteLine($"Rows: {sInputs.Count}   Concrete: {sResult.TotalConcreteVolume:N0} {vU}   Reinforcing: {sResult.TotalRebarWeight:N0} {wU}");
    Console.WriteLine($"Wrote {args[2]}");
    return 0;
}

// Visual-markup mode (faithful to the app's GenerateOverlay_Click): on-drawing red/green markup.
// Usage: takeoff overlay <before.pdf> <after.pdf> <out.pdf> [name] [beforeLabel] [afterLabel] [imperial]
if (args.Length >= 4 && args[0].Equals("overlay", StringComparison.OrdinalIgnoreCase))
{
    string oname = args.Length > 4 ? args[4] : string.Empty;
    string obl   = args.Length > 5 ? args[5] : "Before";
    string oal   = args.Length > 6 ? args[6] : "After";
    var ounit = (args.Length > 7 && args[7].Equals("imperial", StringComparison.OrdinalIgnoreCase))
        ? UnitSystem.Imperial : UnitSystem.Metric;
    var obytes = RebarOverlayGenerator.Build(args[1], args[2], oname, obl, oal, ounit);
    File.WriteAllBytes(args[3], obytes);
    using (var doc = UglyToad.PdfPig.PdfDocument.Open(obytes))
        Console.WriteLine($"Markup pages: {doc.NumberOfPages}");
    Console.WriteLine($"Wrote {args[3]}");
    return 0;
}

// Rebar change-detection mode: takeoff rebar <before.pdf> <after.pdf> <out.xlsx> [name] [beforeLabel] [afterLabel]
if (args.Length >= 4 && args[0].Equals("rebar", StringComparison.OrdinalIgnoreCase))
{
    var bPages = PdfPageTextReader.ReadPages(args[1]);
    var aPages = PdfPageTextReader.ReadPages(args[2]);
    string rname = args.Length > 4 ? args[4] : string.Empty;
    string rbl = args.Length > 5 ? args[5] : "Before";
    string ral = args.Length > 6 ? args[6] : "After";
    var rr = RebarChangeService.Compare(bPages, aPages, rbl, ral);

    // "Can't-read" guard: matched real sheets but read ZERO reinforcing call-outs ⇒ the set's annotation
    // grammar wasn't recognised. That is NOT "no change" — refuse to emit a falsely-reassuring report.
    if (rr.SheetsCompared >= 3 && rr.TotalCalloutsRead == 0)
    {
        Console.Error.WriteLine(
            $"ABORT: compared {rr.SheetsCompared} sheets but read 0 reinforcing call-outs — this set's " +
            "call-out style was not recognised, so a change cannot be detected. This is NOT a 'no change' " +
            "result. No report written.");
        return 3;
    }

    if (args.Length >= 9) // ... beforeCsv afterCsv -> full takeoff + change
    {
        var dens = RebarDensityTable.Default;
        Dictionary<string, double> Vols(string csv)
        {
            var d = new Dictionary<string, double>();
            foreach (var l in TakeoffCsvImporter.Import(File.ReadAllText(csv), dens))
            {
                string k = l.ElementType switch
                {
                    TakeoffElementType.Wall => "Wall",
                    TakeoffElementType.Column => "Column",
                    TakeoffElementType.Foundation => "Foundation",
                    _ => "Slab"
                };
                d[k] = d.GetValueOrDefault(k) + l.ConcreteM3;
            }
            return d;
        }
        var corr = RebarWeightEstimator.Corroborate(aPages);
        var intB = RebarWeightEstimator.CalloutIntensity(bPages);
        var intA = RebarWeightEstimator.CalloutIntensity(aPages);
        var weight = RebarWeightEstimator.Estimate(Vols(args[7]), Vols(args[8]),
            RebarWeightEstimator.DefaultDensities, corr, rbl, ral, intB, intA);

        // Optional sheet-area CSV (Sheet,SlabAreaM2[,WallAreaM2]) prices the field-grid changes.
        Dictionary<string, double>? slabAreas = null, wallAreas = null;
        if (args.Length >= 10 && File.Exists(args[9]))
        {
            slabAreas = new(); wallAreas = new();
            foreach (var line in File.ReadAllLines(args[9]).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 2 || string.IsNullOrWhiteSpace(p[0])) continue;
                if (double.TryParse(p[1], out var sa)) slabAreas[p[0].Trim()] = sa;
                if (p.Length >= 3 && double.TryParse(p[2], out var wa)) wallAreas[p[0].Trim()] = wa;
            }
        }
        var priced = RebarGridPricer.Compare(bPages, aPages, slabAreas, wallAreas, rbl, ral);
        File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildFull(rr, weight, rname, priced));
        Console.WriteLine($"Rebar weight: {weight.TotalBefore:N0} t -> {weight.TotalAfter:N0} t (delta {weight.TotalDelta:+0.0;-0.0;0})");
        Console.WriteLine($"Field-grid changes: {priced.Changes.Count} ({priced.PricedCount} priced, {priced.UnpricedCount} need area)");
        foreach (var c in priced.Changes)
            Console.WriteLine($"  {c.Sheet,-11} {c.Kind,-18} {(c.Before?.Display ?? "—"),-16} -> {(c.After?.Display ?? "—"),-16} ΔAs {c.DeltaAsKgPerM2,6:+0.00;-0.00} kg/m²" +
                (c.DeltaLb.HasValue ? $"  = {c.DeltaLb,8:+#,##0;-#,##0} lb on {c.AreaM2:#,##0} m²" : "  (area needed)"));
    }
    else
    {
        File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildXlsx(rr, rname));
    }

    Console.WriteLine($"Sheets compared {rr.SheetsCompared}, changed {rr.SheetsChanged} " +
                      $"(content {rr.ContentChanged}, new {rr.NewSheets}, removed {rr.RemovedSheets})");
    foreach (var s in rr.Sheets.Where(s => s.Status != RebarChangeStatus.Unchanged))
        Console.WriteLine($"  {s.Sheet,-11} {s.Status,-12} net {s.NetDelta,+3} : {string.Join(", ", s.Added.Concat(s.Removed))}");

    // Bar-list WEIGHT (qty × length × CSA mass) — the lb number a manual rebar comparison produces. Only
    // printed where the set actually uses bar-list call-outs; an intensity-only set (Rory's 31065) weighs
    // 0 and the section is suppressed, so that path's output is unchanged.
    {
        var bSheets = RebarCalloutExtractor.Extract(bPages).ToDictionary(s => s.Sheet);
        var aSheets = RebarCalloutExtractor.Extract(aPages).ToDictionary(s => s.Sheet);
        double tb = 0, ta = 0; int unweigh = 0;
        var rows = new List<(string Sheet, double B, double A)>();
        foreach (var sh in bSheets.Keys.Union(aSheets.Keys).OrderBy(x => x))
        {
            var wb = bSheets.TryGetValue(sh, out var sbc) ? RebarBarListWeigher.Weigh(sbc.Callouts) : default;
            var wa = aSheets.TryGetValue(sh, out var sac) ? RebarBarListWeigher.Weigh(sac.Callouts) : default;
            if (wb.WeightLb <= 0 && wa.WeightLb <= 0 && wb.UnweighableCallouts == 0 && wa.UnweighableCallouts == 0) continue;
            rows.Add((sh, wb.WeightLb, wa.WeightLb));
            tb += wb.WeightLb; ta += wa.WeightLb; unweigh += wb.UnweighableCallouts + wa.UnweighableCallouts;
        }
        if (tb > 0 || ta > 0)
        {
            Console.WriteLine($"\nBar-list rebar weight (qty×length×CSA mass; readable quantity-bearing call-outs only):");
            Console.WriteLine($"  {"Sheet",-11} {rbl,13} {ral,13} {"Δ lb",13}");
            foreach (var r in rows.OrderByDescending(r => System.Math.Abs(r.A - r.B)))
                Console.WriteLine($"  {r.Sheet,-11} {r.B,13:N0} {r.A,13:N0} {r.A - r.B,13:+#,##0;-#,##0;0}");
            Console.WriteLine($"  {"TOTAL",-11} {tb,13:N0} {ta,13:N0} {ta - tb,13:+#,##0;-#,##0;0}");
            Console.WriteLine($"  NOTE: a call-out-SUM estimate, not a full per-element model (no mat-by-area / hooks / studrails / stirrups). Unweighable continuous call-outs skipped: {unweigh}.");
        }
    }
    Console.WriteLine($"Wrote {args[3]}");
    return 0;
}

// Drives the REAL takeoff engine: two concrete-schedule CSVs -> issue delta -> xlsx + docx.
// Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]
if (args.Length < 3)
{
    Console.WriteLine("Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]");
    return 1;
}

string beforePath = args[0];
string afterPath  = args[1];
string outBase    = args[2];
string wbs1        = args.Length > 3 ? args[3] : string.Empty;
string projectName = args.Length > 4 ? args[4] : string.Empty;
string beforeLabel = args.Length > 5 ? args[5] : "Before";
string afterLabel  = args.Length > 6 ? args[6] : "After";

var densities = RebarDensityTable.Default;
var before = TakeoffCsvImporter.Import(File.ReadAllText(beforePath), densities);
var after  = TakeoffCsvImporter.Import(File.ReadAllText(afterPath), densities);

var diff = TakeoffDiffService.Compare(before, after);
var model = new TakeoffReportModel(wbs1, projectName, beforeLabel, afterLabel, DateTime.UtcNow, diff);

File.WriteAllBytes(outBase + ".xlsx", TakeoffReportGenerator.BuildXlsx(model));
File.WriteAllBytes(outBase + ".docx", TakeoffReportGenerator.BuildDocx(model));

Console.WriteLine($"Concrete delta: {diff.TotalConcreteDeltaM3:N1} m3   " +
                  $"Rebar delta: {diff.TotalRebarDeltaTonnes:N1} t   " +
                  $"Formwork delta: {diff.TotalFormworkDeltaM2:N1} m2");
if (diff.AddedLevels.Count > 0) Console.WriteLine("Added levels: " + string.Join(", ", diff.AddedLevels));
if (diff.RemovedLevels.Count > 0) Console.WriteLine("Removed levels: " + string.Join(", ", diff.RemovedLevels));
Console.WriteLine($"Wrote {outBase}.xlsx and {outBase}.docx");
return 0;

// Decodes a rasterized plan page (PNG) and crops a region into the pixel buffers the Core
// PlanGeometry engine consumes. Crop box is [x0,x1) × [y0,y1), clamped to the image.
// The CLI's concrete I/O for the Core engine: the Anthropic-backed vision calls and the ImageSharp
// raster decode. The WPF app will register its own equivalents — the engine depends on neither.
sealed class CliPlanVision : IPlanVision
{
    public Task<string> SynthesizePageAsync(string pageJson, CancellationToken ct = default)
        => PlanVisionClient.SynthesizePageAsync(pageJson);
    public Task<string> LocatePlateAsync(string pageJson, byte[] downscaledPng, CancellationToken ct = default, string? feedback = null)
        => PlanVisionClient.LocatePlateAsync(pageJson, downscaledPng, feedback);
    public Task<string> ApportionThicknessAsync(byte[] plateCropPng, IReadOnlyList<int> thicknessesIn, CancellationToken ct = default, string? feedback = null)
        => PlanVisionClient.ApportionThicknessJsonAsync(plateCropPng, thicknessesIn, feedback);
}

sealed class CliPlanRaster : IPlanRaster
{
    public (int Width, int Height) ImageSize(string path) => PlanRaster.ImageSize(path);
    public byte[] LoadDownscaledPng(string path, int maxEdge) => PlanRaster.LoadDownscaledPng(path, maxEdge);
    public byte[] LoadCropPng(string path, int x0, int y0, int x1, int y1, int maxEdge) => PlanRaster.LoadCropPng(path, x0, y0, x1, y1, maxEdge);
    public RasterCrop LoadCrop(string path, int x0, int y0, int x1, int y1)
    {
        var c = PlanRaster.LoadCrop(path, x0, y0, x1, y1);
        return new RasterCrop(c.Lum, c.Width, c.Height);
    }
}

static class PlanRaster
{
    public readonly record struct Crop(byte[] Lum, byte[] R, byte[] G, byte[] B, int Width, int Height);

    public static Crop LoadCrop(string path, int x0, int y0, int x1, int y1)
    {
        using var img = Image.Load<Rgb24>(path);
        x0 = Math.Clamp(x0, 0, img.Width);
        x1 = Math.Clamp(x1, 0, img.Width);
        y0 = Math.Clamp(y0, 0, img.Height);
        y1 = Math.Clamp(y1, 0, img.Height);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) throw new ArgumentException($"Empty crop after clamping: {w}x{h}.");

        var lum = new byte[w * h];
        var r = new byte[w * h];
        var g = new byte[w * h];
        var b = new byte[w * h];
        img.ProcessPixelRows(accessor =>
        {
            for (int yy = 0; yy < h; yy++)
            {
                var srcRow = accessor.GetRowSpan(y0 + yy);
                for (int xx = 0; xx < w; xx++)
                {
                    Rgb24 px = srcRow[x0 + xx];
                    int i = yy * w + xx;
                    r[i] = px.R; g[i] = px.G; b[i] = px.B;
                    lum[i] = (byte)(0.299 * px.R + 0.587 * px.G + 0.114 * px.B);
                }
            }
        });
        return new Crop(lum, r, g, b, w, h);
    }

    public static TakeoffElementType ParseElement(string raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "wall" => TakeoffElementType.Wall,
        "beam" or "framing" => TakeoffElementType.Beam,
        "column" => TakeoffElementType.Column,
        "foundation" or "footing" or "mat" => TakeoffElementType.Foundation,
        "droppanel" or "drop" => TakeoffElementType.DropPanel,
        _ => TakeoffElementType.Slab,
    };

    // Full image pixel dimensions (for mapping a vision normalized box back to full-res pixels).
    public static (int Width, int Height) ImageSize(string path)
    {
        var info = Image.Identify(path);
        return (info.Width, info.Height);
    }

    // A PNG re-encoded with its long edge capped at maxEdge — sent to the vision API to keep token
    // cost down. Measurement always runs on the full-res image; normalized boxes are resolution-free.
    public static byte[] LoadDownscaledPng(string path, int maxEdge)
    {
        using var img = Image.Load<Rgb24>(path);
        int longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            double s = (double)maxEdge / longEdge;
            img.Mutate(c => c.Resize(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s))));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    // A PNG of just one plate's pixel box (long edge capped at maxEdge) — a focused image for a targeted
    // vision call so the model sees that plate alone, not the whole sheet.
    public static byte[] LoadCropPng(string path, int x0, int y0, int x1, int y1, int maxEdge)
    {
        using var img = Image.Load<Rgb24>(path);
        x0 = Math.Clamp(x0, 0, img.Width); x1 = Math.Clamp(x1, 0, img.Width);
        y0 = Math.Clamp(y0, 0, img.Height); y1 = Math.Clamp(y1, 0, img.Height);
        int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        img.Mutate(c => c.Crop(new SixLabors.ImageSharp.Rectangle(x0, y0, w, h)));
        int longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            double s = (double)maxEdge / longEdge;
            img.Mutate(c => c.Resize(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s))));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}

// Pages config for `vision-estimate`: project metadata + the rasterized sheets to read.
sealed class VisionPagesConfig
{
    public string? Project { get; set; }
    public string? Name { get; set; }
    public string? Issue { get; set; }
    public string Profile { get; set; } = "BC-moderate";
    public double Dpi { get; set; } = 110;
    public string Scale { get; set; } = "1/8\"=1'-0\"";   // fallback if the title-block scale is illegible
    public string? PngDir { get; set; }
    public double StoreyHeightIn { get; set; }            // floor-to-floor height for wall/column concrete; 0 = unknown
    public double SogThicknessIn { get; set; }            // slab-on-grade thickness when not legible on the footings sheet; 0 = leave unresolved
    // Ordered building levels (top → bottom), e.g. ["LEVEL 46", … , "P7"]. When present, vertical
    // concrete is priced from the wall/column SCHEDULES over these levels instead of plan poché pixels.
    public List<string>? Levels { get; set; }
    public Dictionary<string, double>? StoreyHeightInByLevel { get; set; }  // per-level overrides (inches); else StoreyHeightIn
    public List<VisionPage> Pages { get; set; } = new();
}

sealed class VisionPage
{
    public string Png { get; set; } = "";
    public double? Dpi { get; set; }
    public double? StoreyHeightIn { get; set; }           // overrides config StoreyHeightIn for this sheet's level band
}

// A suspended-slab measurement held back until the whole building is read, so its floor count can be
// reconciled across overlapping/duplicate sheets (BuildingRollup.AssignSlabFloors). Co-located wall and
// column footprints ride along and inherit the slab's reconciled count, as do thickened zones (drop
// panels / built-up transfer) detected on the same sheet, which are priced as their depth ABOVE the
// nominal slab so the field slab is never double-counted. BoxX0..BoxY1 are full-sheet pixels of the
// plate outline, used to attach a sheet's thickenings to the slab whose box contains them.
sealed record PendingSlab(
    string Level, string? Variant, double AreaSqFt, double ThicknessIn, double Confidence,
    bool ScaleConfirmed, double WallSqFt, double ColSqFt, double StoreyIn,
    int BoxX0, int BoxY0, int BoxX1, int BoxY1)
{
    public List<Thickening> Thickenings { get; } = new();
}

// A local slab thickening (drop panel / thickened band / built-up transfer zone): its area and the
// concrete depth ADDED above the nominal slab. Priced as a DropPanel plate at the owning slab's count.
sealed record Thickening(double AddedDepthIn, double AreaSqFt, double Confidence);

// Building config for the `estimate` mode: project metadata + the per-plate map (which sheet, which
// crop, how to measure it, the read thickness/height, and how many identical floors it covers).
sealed class EstimateConfig
{
    public string? Project { get; set; }
    public string? Name { get; set; }
    public string? Issue { get; set; }
    public string Profile { get; set; } = "BC-moderate";
    public double Dpi { get; set; } = 110;
    public string Scale { get; set; } = "1/8\"=1'-0\"";
    public string? PngDir { get; set; }
    public List<PlateConfig> Plates { get; set; } = new();
}

sealed class PlateConfig
{
    public string Level { get; set; } = "";
    public string Element { get; set; } = "Slab";
    public string? Variant { get; set; }
    public string Png { get; set; } = "";
    public int[] Crop { get; set; } = new int[4];     // x0, y0, x1, y1
    public string? Scale { get; set; }                // overrides config Scale
    public double? Dpi { get; set; }                  // overrides config Dpi
    public bool Gray { get; set; }                    // measure gray footprint (walls/cols) vs enclosed area
    public double DimensionIn { get; set; }           // slab/mat thickness, or wall/column storey height
    public int Count { get; set; } = 1;               // identical floors this plate stands in for
    public double AreaFraction { get; set; } = 1.0;   // split a gray footprint into wall vs column share
    public string? Grade { get; set; }
    public bool ScaleConfirmed { get; set; } = true;
    public double? RebarLbPerCyOverride { get; set; }
}
