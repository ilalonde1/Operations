// The takeoff verb `vision-estimate`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Vision Layer 2: the app reads the drawing itself. For each page, Claude classifies the sheet and
// locates the concrete-outline plates (level, count, element, thickness, normalized box); the Core
// geometry then measures the largest enclosed region in each box; the pipeline prices + reconciles.
// Usage: takeoff vision-estimate <pages.json> <out.xlsx>
internal static class VisionEstimateVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("vision-estimate", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
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
}
