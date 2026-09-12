// The takeoff verb `vector-takeoff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// END-TO-END synthesis-led takeoff: classify each page, locate+measure slab plates, assemble via the
// EXISTING pipeline -> xlsx + total vs QTO. Usage: takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last]
internal static class VectorTakeoffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-takeoff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        // Spend controls. Vision answers are CACHED under <pngDir>/.vision-cache keyed by the exact
        // request, so re-running a set replays them: $0 spent, byte-identical numbers. --fresh discards
        // the stored answers (a deliberate new sample — this SPENDS); --deterministic makes no vision
        // calls at all (unpriceable pieces become flags/residuals) — the free regression mode.
        bool tkDeterministic = args.Any(a => a.Equals("--deterministic", StringComparison.OrdinalIgnoreCase));
        bool tkFresh = args.Any(a => a.Equals("--fresh", StringComparison.OrdinalIgnoreCase));
        args = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last] [scale] [heightsJson] [--deterministic] [--fresh]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!tkDeterministic && string.IsNullOrWhiteSpace(PlanVisionClient.ApiKey)) { Console.Error.WriteLine("KOR_ANTHROPIC_KEY not set (or run with --deterministic)."); return 2; }
        int? tkFirst = args.Length >= 5 && int.TryParse(args[4], out var tf) ? tf : null;
        int? tkLast  = args.Length >= 6 && int.TryParse(args[5], out var tl) ? tl : null;
        // Optional scale note OVERRIDE (e.g. "1:100" metric, "1/8\"=1'-0\"" imperial). Absent → each sheet
        // is measured at the scale ITS title block states (SheetScaleReader); imperial fallback, flagged.
        string? tkScale = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6]) ? args[6] : null;

        // Optional storey-height file (clean-at-source): { "storeyHeightFt": 10.5, "byLevel": { "P1": 13, "LEVEL 1": 12, ... } }
        // — real floor-to-floor heights (FEET) from the architectural set. byLevel prices each named level's verticals
        // exactly; storeyHeightFt sets the typical fallback for the rest. Absent → the engine's 10.5ft default, flagged.
        double tkStoreyIn = 126; Dictionary<string, double>? tkHeights = null;
        if (args.Length >= 8 && File.Exists(args[7]))
        {
            try
            {
                using var hd = JsonDocument.Parse(File.ReadAllText(args[7]));
                if (hd.RootElement.TryGetProperty("storeyHeightFt", out var sh) && sh.TryGetDouble(out var shv) && shv > 0) tkStoreyIn = shv * 12;
                if (hd.RootElement.TryGetProperty("byLevel", out var bl) && bl.ValueKind == JsonValueKind.Object)
                {
                    tkHeights = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in bl.EnumerateObject()) if (p.Value.TryGetDouble(out var ft) && ft > 0) tkHeights[p.Name] = ft * 12;  // feet → inches
                }
                Console.WriteLine($"Storey heights: typical {tkStoreyIn / 12:0.0}ft{(tkHeights is { Count: > 0 } ? $" + {tkHeights.Count} per-level from {Path.GetFileName(args[7])}" : "")}.");
            }
            catch (Exception ex) { Console.Error.WriteLine($"Heights file '{args[7]}' unreadable ({ex.Message}); using typical {tkStoreyIn / 12:0.0}ft."); }
        }

        // The whole measure→reconcile→price→synopsis spine now lives in Core (SlabTakeoffEngine) so the WPF
        // app runs it identically; this command is a thin host that supplies the AI + raster I/O and renders
        // the engine's note trace, totals, and orange synopsis exactly as before.
        var tkReq = new SlabTakeoffRequest(args[1], args[2], tkFirst, tkLast, Scale: tkScale,
            StoreyHeightIn: tkStoreyIn, StoreyHeightInByLevel: tkHeights);

        // Render the PDF's pages to p-NN.png at the request's render dpi if they aren't already there, so the
        // tool is self-contained — no separate rasterizing step, no cryptic "no rendered images" death.
        try
        {
            int made = PlanPdfRenderer.RenderMissing(tkReq.PdfPath, tkReq.PngDir, tkReq.Dpi, tkFirst, tkLast);
            if (made > 0) Console.WriteLine($"Rendered {made} page(s) to {tkReq.PngDir} @ {tkReq.Dpi:0} dpi.");
        }
        catch (Exception ex) { Console.Error.WriteLine($"PDF render failed: {ex.Message}"); return 2; }

        if (!tkDeterministic)
        {
            string tkCacheDir = Path.Combine(args[2], ".vision-cache");
            if (tkFresh && Directory.Exists(tkCacheDir)) Directory.Delete(tkCacheDir, true);
            PlanVisionClient.CacheDir = tkCacheDir;
        }

        SlabTakeoffResult tkOut;
        try { tkOut = await SlabTakeoffEngine.RunAsync(tkReq, tkDeterministic ? new NoPlanVision() : new CliPlanVision(), new CliPlanRaster()); }
        catch (PdfNotReadableException ex)
        {
            // The set is unreadable — say so plainly and abort BEFORE pretending a number. Distinct exit code 3.
            Console.Error.WriteLine($"CANNOT READ THIS SET — {ex.Message}");
            return 3;
        }
        catch (SlabTakeoffNothingPricedException ex)
        {
            // Nothing priced — print the engine's full phase trace FIRST, so the failure is diagnosable
            // (which pages classified, which plates failed locate/thickness), then the verdict.
            foreach (var n in ex.Notes) Console.WriteLine(n);
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

        foreach (var n in tkOut.Notes) Console.WriteLine(n);
        File.WriteAllBytes(args[3], tkOut.Xlsx);
        // The engine total now includes the deterministic gray-fill walls + columns (priced per plate), so break
        // it down by element rather than calling it slab-only.
        double wallCyGeom = tkOut.Estimate.Plates.Where(p => p.Plate.Element == TakeoffElementType.Wall).Sum(p => p.ConcreteTotalCuYd);
        double colCyGeom  = tkOut.Estimate.Plates.Where(p => p.Plate.Element == TakeoffElementType.Column).Sum(p => p.ConcreteTotalCuYd);
        double slabCy     = tkOut.TotalConcreteCuYd - wallCyGeom - colCyGeom;
        Console.WriteLine($"\nPlates: {tkOut.Estimate.Plates.Count}   Concrete: {tkOut.TotalConcreteCuYd:N0} cu.yd "
            + $"(slab/fdn {slabCy:N0} + walls {wallCyGeom:N0} + columns {colCyGeom:N0})   Rebar: {tkOut.TotalRebarLb:N0} lb");

        // PER-LEVEL — these are FIELD-SLAB volumes: plan area × plan thickness callout. "GRID" means the field
        // measurement is clean (area cross-checked by grid envelope + poché + peers, thickness from a real
        // callout); it does NOT mean the floor TOTAL is final. Built-up zones below the slab — drop panels, beams,
        // transfer build-up — are not drawn as plan callouts and are excluded, so a model/section QTO reads higher
        // on any floor that has them. (Proven on 31065: L4-North reads identically to L2-North on the plan yet is
        // 17% heavier in the model — no plan-level signal separates them.) "FLAG" = even the field measure is doubtful.
        // SLAB plates only — Estimate.Plates also carries the gray-fill wall/column plates, and printing
        // them unlabelled under a "FIELD-SLAB" heading reads as triple-counted slab (the list would sum
        // ~50% over the priced slab line above). Element totals are in the header; walls/columns detail
        // is in the xlsx.
        Console.WriteLine($"\nPer-level FIELD-SLAB volume — GRID = field measure clean, FLAG = verify the measure by hand:");
        foreach (var pe in tkOut.Estimate.Plates.Where(p => p.Plate.Element == TakeoffElementType.Slab)
                                                .OrderByDescending(p => p.ConcreteTotalCuYd))
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
        // ── WHOLE-BUILDING: add COLUMN + WALL concrete from the schedule sheets ───────────────────────────
        // Auto-detect each column-schedule and shear-wall-schedule sheet, read it (vision), and price via the
        // existing ComputeColumn / ComputeWall. Columns: sizes filled down the deterministic level ladder.
        // Walls: thickness bands × mark length from the key plan on the same sheet. BALLPARK for now — one
        // column per mark and a typical storey height; per-mark COUNTS and real storey heights are the next
        // increment. Flagged as such, never presented as final. Footings follow the same pattern.
        try
        {
            var tkDig = DrawingDigestBuilder.Build(args[1], tkFirst, tkLast);
            double typicalStoreyIn = tkStoreyIn;   // typical fallback; per-level heights below when supplied
            var schedHeights = SlabTakeoffEngine.NormalizeHeightMap(tkHeights);
            double StoreyOf(string lvl) => SlabTakeoffEngine.ResolveStoreyHeightIn(lvl, schedHeights, tkStoreyIn).Inches;
            double colCuYd = 0, wallCuYd = 0; int colSheets = 0, colMarks = 0, wallSheets = 0, wallMarks = 0;
            // Per-sheet schedule column results, with the sheet's tower identity — the composition below
            // makes the SCHEDULE the priced source for the floors it covers (validated closest to the
            // model), with gray-fill only filling floors no schedule covers.
            var colSched = new List<(int Page, string? Tower, ScheduleTakeoff.ScheduleResult Res)>();

            // A single schedule/key-plan vision call is fragile: it occasionally returns empty or an outlier,
            // and the wall total hangs on it — one empty response ZEROES a whole tower (31065 p53 read 0 cy one
            // run, 1,053 the next). So each sheet is read N times and the MEDIAN priced result is taken: a lone
            // empty/outlier read can no longer zero or swing the sheet. Reads that come back empty are dropped,
            // not counted as zero. Spread (min..max) is printed so the remaining read-to-read variance is visible.
            const int VisionReads = 3;

            async Task<(ScheduleTakeoff.ScheduleResult res, int distinctMarks, double lo, double hi)?>
                ReadColumnSheetAsync(byte[] png, List<string> ladder, List<double> storeys)
            {
                var got = new List<(ScheduleTakeoff.ScheduleResult res, int marks)>();
                for (int i = 0; i < VisionReads; i++)
                {
                    // Each read salts the cache differently: the median WANTS independent samples, and a
                    // replayed run then reproduces all N of them (median included) without spending.
                    PlanVisionClient.CacheSalt = i;
                    var counts = ScheduleConcreteReader.ColumnCounts(await PlanVisionClient.ReadColumnCountsJsonAsync(png));
                    var cbands = ScheduleConcreteReader.ColumnBands(await PlanVisionClient.ReadColumnScheduleJsonAsync(png), ladder, counts);
                    if (cbands.Count == 0) continue;
                    var res = ScheduleTakeoff.ComputeColumn(ladder, storeys, cbands);
                    if (res.TotalCuYd > 0) got.Add((res, counts.Count > 0 ? counts.Count : res.MarksPriced));
                }
                PlanVisionClient.CacheSalt = 0;
                if (got.Count == 0) return null;
                got = got.OrderBy(g => g.res.TotalCuYd).ToList();
                var mid = got[got.Count / 2];
                return (mid.res, mid.marks, got[0].res.TotalCuYd, got[^1].res.TotalCuYd);
            }

            async Task<(ScheduleTakeoff.ScheduleResult res, double lo, double hi)?>
                ReadWallSheetAsync(byte[] png, List<string> ladder, List<double> storeys)
            {
                var got = new List<ScheduleTakeoff.ScheduleResult>();
                for (int i = 0; i < VisionReads; i++)
                {
                    PlanVisionClient.CacheSalt = i;   // independent sample per read; all N replay from cache
                    var wbands = ScheduleConcreteReader.WallBands(await PlanVisionClient.ReadWallScheduleJsonAsync(png));
                    var wlen = ScheduleConcreteReader.WallLengthsByMark(await PlanVisionClient.ReadWallKeyPlanJsonAsync(png));
                    if (wbands.Count == 0 || wlen.Count == 0) continue;
                    var res = ScheduleTakeoff.ComputeWall(ladder, storeys, wlen, wbands);
                    if (res.TotalCuYd > 0) got.Add(res);
                }
                PlanVisionClient.CacheSalt = 0;
                if (got.Count == 0) return null;
                got = got.OrderBy(r => r.TotalCuYd).ToList();
                return (got[got.Count / 2], got[0].TotalCuYd, got[^1].TotalCuYd);
            }

            foreach (var pg in tkDig.Pages)
            {
                if (tkDeterministic) break;   // no vision: columns price from gray-fill alone; schedule reads skipped
                string ppng = Path.Combine(args[2], $"p-{pg.Page:D2}.png");
                if (!File.Exists(ppng)) continue;

                // A schedule sheet is identified by its TITLE BLOCK, not a page-wide text scan: a general-notes
                // sheet that mentions "shear wall schedule" in prose must not be read as one (31065 p3 read as
                // 150 cy of phantom wall before this gate). HasScheduleTitle requires the phrase at title size
                // on the right edge.
                var page = VectorPageReader.ReadPage(args[1], pg.Page);
                List<string> Ladder() => ScheduleGridReader.ReadLevelLadder(page)
                                            .OrderByDescending(r => r.Y).Select(r => r.RawLabel).ToList();

                // COLUMN schedule sheet
                if (SheetTitleReader.HasScheduleTitle(page, "COLUMN"))
                {
                    var ladder = Ladder();
                    if (ladder.Count >= 3)
                    {
                        var cpngB = PlanRaster.LoadDownscaledPng(ppng, 1600);
                        var got = await ReadColumnSheetAsync(cpngB, ladder, ladder.Select(StoreyOf).ToList());
                        if (got is { } c)
                        {
                            colCuYd += c.res.TotalCuYd; colSheets++; colMarks += c.res.MarksPriced;
                            string despaced = string.Concat(string.Join(" ", pg.Lines).ToUpperInvariant().Where(ch => !char.IsWhiteSpace(ch)));
                            string? tower = despaced.Contains("NORTHTOWER") ? "NORTH" : despaced.Contains("SOUTHTOWER") ? "SOUTH"
                                          : despaced.Contains("EASTTOWER") ? "EAST" : despaced.Contains("WESTTOWER") ? "WEST" : null;
                            colSched.Add((pg.Page, tower, c.res));
                            string spread = c.hi > c.lo ? $" [{VisionReads} reads {c.lo:N0}..{c.hi:N0}]" : "";
                            Console.WriteLine($"  column schedule p{pg.Page}{(tower is null ? "" : $" ({tower} tower)")}: {c.res.MarksPriced} columns ({c.distinctMarks} marks) over {ladder.Count} levels -> {c.res.TotalCuYd:N0} cy{spread}");
                        }
                        else Console.WriteLine($"  column schedule p{pg.Page}: vision returned nothing usable over {VisionReads} reads (skipped, not counted as 0)");
                    }
                }

                // SHEAR-WALL schedule sheet (schedule + key plan live on the same sheet)
                if (SheetTitleReader.HasScheduleTitle(page, "SHEAR"))
                {
                    var ladder = Ladder();
                    if (ladder.Count >= 3)
                    {
                        var png = PlanRaster.LoadDownscaledPng(ppng, 1600);
                        var got = await ReadWallSheetAsync(png, ladder, ladder.Select(StoreyOf).ToList());
                        if (got is { } wv)
                        {
                            var wres = wv.res;
                            wallCuYd += wres.TotalCuYd; wallSheets++; wallMarks += wres.MarksPriced;
                            string spread = wv.hi > wv.lo ? $" [{VisionReads} reads {wv.lo:N0}..{wv.hi:N0}]" : "";
                            Console.WriteLine($"  wall schedule p{pg.Page}: {wres.MarksPriced} marks priced ({wres.BandsApplied} bands) -> {wres.TotalCuYd:N0} cy{spread}");
                        }
                        else Console.WriteLine($"  wall schedule p{pg.Page}: vision returned nothing usable over {VisionReads} reads (skipped, not counted as 0)");
                    }
                }
            }

            // ── SCHEDULE-FIRST COLUMNS ────────────────────────────────────────────────────────────────
            // The column SCHEDULE states every column's true size; the gray-fill footprint only infers it
            // (and over-reads: fills, symbols and stocky wall ends masquerade as columns — +127% vs the
            // model on the validation building, where the schedule read landed within 18%). So wherever a
            // same-tower column schedule covers a floor, the schedule is the priced source and the engine's
            // gray-fill column row is REPLACED; gray-fill prices only the floors no schedule covers. Each
            // floor's columns come from exactly one source; when no schedule is readable, nothing changes.
            var engineInputs = tkOut.Estimate.TakeoffInputs.ToList();
            var schedColInputs = new List<StructuralTakeoffInput>();
            double schedColCy = 0, keptGrayColCy = 0;

            // Engine label -> (tower, floor keys). "6-18 NORTH (x13)" is the band form the engine emits.
            (string? Tower, List<string> Floors) ParseLabel(string label)
            {
                string u = label.ToUpperInvariant();
                string? tw = u.Contains("NORTH") ? "NORTH" : u.Contains("SOUTH") ? "SOUTH"
                           : u.Contains("EAST") ? "EAST" : u.Contains("WEST") ? "WEST" : null;
                var band = System.Text.RegularExpressions.Regex.Match(u, @"^(\d+)\s*-\s*(\d+)\b");
                if (band.Success)
                {
                    int lo = int.Parse(band.Groups[1].Value), hi = int.Parse(band.Groups[2].Value);
                    return (tw, Enumerable.Range(lo, hi - lo + 1).Select(i => $"L{i}").ToList());
                }
                return (tw, new List<string> { SlabTakeoffEngine.NormalizeLevelKey(label) });
            }

            if (colSched.Count > 0)
            {
                // Floors each tower's schedule actually priced (normalized, e.g. "L19", "P1").
                var covered = new Dictionary<string, HashSet<string>>();
                foreach (var (_, tower, res) in colSched)
                {
                    var set = covered.TryGetValue(tower ?? "", out var s) ? s : covered[tower ?? ""] = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var lf in res.PerLevel.Where(l => l.ConcreteCuYd > 0))
                        set.Add(ScheduleTakeoff.NormalizeLevel(lf.Level));
                }

                // Engine column rows: replaced when EVERY floor they price is schedule-covered for their
                // tower; kept (and their floors reserved) otherwise — a floor is never priced twice.
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "TOWER|FLOOR" kept by gray-fill
                var keptEngine = new List<StructuralTakeoffInput>();
                foreach (var inp in engineInputs)
                {
                    if (inp.Element != TakeoffElementType.Column) { keptEngine.Add(inp); continue; }
                    var (tw, floors) = ParseLabel(inp.Level);
                    bool allCovered = covered.TryGetValue(tw ?? "", out var set) && floors.All(set.Contains);
                    if (allCovered) continue;                                   // schedule replaces this row
                    keptEngine.Add(inp);
                    keptGrayColCy += inp.ConcreteVolume;
                    foreach (var f in floors) reserved.Add($"{tw}|{f}");
                }
                engineInputs = keptEngine;

                // Schedule rows for every covered floor not reserved by a kept gray-fill row, labelled to
                // match the engine's level rows so the workbook stays one row per level.
                var engineLabels = tkOut.Estimate.TakeoffInputs.Select(i => i.Level).Distinct()
                    .Select(l => (Label: l, Parsed: ParseLabel(l))).ToList();
                var byLabel = new Dictionary<string, double>();
                foreach (var (_, tower, res) in colSched)
                    foreach (var lf in res.PerLevel.Where(l => l.ConcreteCuYd > 0))
                    {
                        string f = ScheduleTakeoff.NormalizeLevel(lf.Level);
                        // A kept gray-fill row with NO tower (parkade "P1") prices the whole floor plate —
                        // a tower schedule reaching into that floor must not add its columns on top.
                        if (reserved.Contains($"{tower}|{f}") || reserved.Contains($"|{f}")) continue;
                        string label = engineLabels.FirstOrDefault(e =>
                            (e.Parsed.Tower ?? "") == (tower ?? "") && e.Parsed.Floors.Contains(f)).Label
                            ?? (tower is null ? f : $"{f} {tower}");
                        byLabel[label] = byLabel.GetValueOrDefault(label) + lf.ConcreteCuYd;
                    }
                foreach (var kv in byLabel)
                {
                    schedColInputs.Add(new StructuralTakeoffInput(kv.Key, TakeoffElementType.Column, "schedule", kv.Value));
                    schedColCy += kv.Value;
                }
            }

            // FOUNDATIONS — deterministic, from the drawing's own FOUNDATION SCHEDULE (mark → L×W×D DEEP)
            // × the mark placements counted on the foundation plans, outside the table. Spread footings are
            // priced directly. STRIP footings (two dims) run CONTINUOUSLY under the basement walls — the
            // schedule itself says "BOTTOM CONT." — so each mark's LENGTH is its share of the plan's outer
            // contour, split by nearest mark (the same Voronoi-by-annotation principle as the thickness
            // zones); width × depth stay the schedule's exact text. Flagged: the contour staircases on
            // diagonals, includes matchline edges, and misses interior strip runs — verify against the plan.
            // No contour or no placements → the mark stays a NAMED residual, never silently dropped.
            double fdnCy = 0;
            var fdnInputs = new List<StructuralTakeoffInput>();
            var fdnBreakdown = new List<string>();
            var stripMarks = new List<string>();
            foreach (var pg in tkDig.Pages)
            {
                string ds = string.Concat(string.Join(" ", pg.Lines).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
                if (!ds.Contains("FOUNDATIONSCHEDULE") && !ds.Contains("FOOTINGSCHEDULE")) continue;
                var fpPage = VectorPageReader.ReadPage(args[1], pg.Page);
                var (ftypes, tableBox) = FootingScheduleReader.ReadSchedule(fpPage);
                if (ftypes.Count == 0) continue;
                // a mark in a note or legend is a mention, not a placement (brief 21)
                var fpPositions = FootingScheduleReader.PlacementPositions(fpPage, ftypes, tableBox, SheetFurniture.On(fpPage, PlanAgreesWithItsSchedule.DefaultToleranceMm));
                var placements = fpPositions.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.OrdinalIgnoreCase);
                string flevel = SheetTitleReader.FromPage(fpPage)?.Display ?? "FOUNDATION";

                // Strip runs: contour metres per strip mark, from the page's own crop. One contour pass
                // per page, all strip marks together (they compete for the same perimeter).
                var stripTypes = ftypes.Where(t => !t.IsSpread && t.WidthMm > 0 && t.DepthMm > 0
                                                && fpPositions.GetValueOrDefault(t.Mark) is { Count: > 0 }).ToList();
                var stripLenFt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (stripTypes.Count > 0)
                {
                    string spng = Path.Combine(args[2], $"p-{pg.Page:D2}.png");
                    string? fScale = tkScale ?? SheetScaleReader.FromPage(fpPage);
                    double? fMpp = PlanGeometry.MetresPerPixel(fScale ?? "1/8\"=1'-0\"", tkReq.Dpi);
                    if (File.Exists(spng) && fMpp is double fmv)
                    {
                        var fcrop = PlanRaster.LoadCrop(spng, 0, 0, int.MaxValue / 2, int.MaxValue / 2);
                        // All strip-mark positions, PDF pts → crop px; remember which mark owns each index.
                        var pts = new List<(double X, double Y)>();
                        var owner = new List<string>();
                        foreach (var st in stripTypes)
                            foreach (var (mx, my) in fpPositions[st.Mark])
                            {
                                pts.Add((mx / fpPage.WidthPts * fcrop.Width,
                                         (fpPage.HeightPts - my) / fpPage.HeightPts * fcrop.Height));
                                owner.Add(st.Mark);
                            }
                        var (_, byMark) = PlanGeometry.BoundaryMetresByNearestMark(
                            fcrop.Lum, fcrop.Width, fcrop.Height, fmv, pts);
                        foreach (var kv in byMark)
                            stripLenFt[owner[kv.Key]] = stripLenFt.GetValueOrDefault(owner[kv.Key]) + kv.Value * 3.2808399;
                    }
                }

                foreach (var ft in ftypes)
                {
                    int n = placements.GetValueOrDefault(ft.Mark);
                    if (!ft.IsSpread)
                    {
                        if (n == 0) continue;
                        if (stripLenFt.TryGetValue(ft.Mark, out var lenFt) && lenFt > 0)
                        {
                            double scy = (lenFt / 3.2808399 * 1000) * ft.WidthMm * ft.DepthMm / 1e9 * 1.30795;
                            fdnCy += scy;
                            fdnInputs.Add(new StructuralTakeoffInput(flevel, TakeoffElementType.Foundation, "strip footing", scy));
                            fdnBreakdown.Add($"    p{pg.Page} {flevel,-10} {ft.Mark,-4} strip {lenFt,5:N0} ft x {ft.WidthMm:0}x{ft.DepthMm:0} = {scy,6:N0} cy (FLAGGED - contour run, verify)");
                        }
                        else stripMarks.Add($"{ft.Mark} x{n} (p{pg.Page})");
                        continue;
                    }
                    if (n == 0) continue;
                    double cy = n * ft.VolumeCuYdEach;
                    fdnCy += cy;
                    fdnInputs.Add(new StructuralTakeoffInput(flevel, TakeoffElementType.Foundation, "spread footing", cy));
                    fdnBreakdown.Add($"    p{pg.Page} {flevel,-10} {ft.Mark,-4} x{n,3} @ {ft.LengthMm:0}x{ft.WidthMm:0}x{ft.DepthMm:0} = {cy,7:N0} cy");
                }
            }
            // HATCHED MATS (core/pit footings) — the drawing convention: a deep mat is drawn as a
            // cross-hatched region with its own "#### DEEP ... FOOTING" note. Deterministic pairing:
            // hatched regions (PlanGeometry.MeasureHatchedRegions) are priced ONLY when a DEEP note sits
            // within reach of the region; hatch without a depth note (hairpin extents, poché) is ignored,
            // a note without hatch stays residual. Area × noted depth, flagged for review.
            double matCy = 0; var matBreakdown = new List<string>();
            try
            {
                var deepRe = new System.Text.RegularExpressions.Regex(@"^(\d{3,4})$");
                foreach (var pg in tkDig.Pages)
                {
                    string ds3 = string.Concat(string.Join(" ", pg.Lines).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
                    if (!ds3.Contains("FOUNDATIONSCHEDULE") && !ds3.Contains("FOOTINGSCHEDULE")) continue;
                    string mpng = Path.Combine(args[2], $"p-{pg.Page:D2}.png");
                    if (!File.Exists(mpng)) continue;
                    var mpage = VectorPageReader.ReadPage(args[1], pg.Page);
                    // This sheet's own scale (title block), unless the operator overrode it — same precedence
                    // as the engine, so the mats and the slabs are measured in the same world. An assumed
                    // scale is SAID here (the engine flags its plates; the mats only have this trace).
                    string? matScale = tkScale ?? SheetScaleReader.FromPage(mpage);
                    double? matMpp = PlanGeometry.MetresPerPixel(matScale ?? "1/8\"=1'-0\"", tkReq.Dpi);
                    if (matMpp is not double mv)
                    { Console.Error.WriteLine($"  ! hatched-mat p{pg.Page}: scale note unresolvable — page skipped, quantify its mats by hand."); continue; }
                    if (matScale is null)
                        Console.WriteLine($"  ~ hatched-mat p{pg.Page}: no stated scale on this sheet — mats measured at the assumed 1/8\"=1'-0\" (verify).");
                    var crop = PlanRaster.LoadCrop(mpng, 0, 0, int.MaxValue / 2, int.MaxValue / 2);
                    var regions = PlanGeometry.MeasureHatchedRegions(crop.Lum, crop.Width, crop.Height);
                    if (regions.Count == 0) continue;
                    string mlevel = SheetTitleReader.FromPage(mpage)?.Display ?? "FOUNDATION";
                    // "1800 DEEP" note positions, mapped into render pixels.
                    var deepNotes = new List<(double Px, double Py, int Mm)>();
                    foreach (var w in mpage.Words)
                    {
                        if (!w.Text.StartsWith("DEEP", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var n in mpage.Words)
                        {
                            var m = deepRe.Match(n.Text.Trim().Replace(",", ""));
                            if (!m.Success || Math.Abs(n.Cy - w.Cy) > 7 || n.Cx >= w.Cx || w.Cx - n.Cx > 80) continue;
                            int mm = int.Parse(m.Groups[1].Value);
                            if (mm < 500 || mm > 4000) continue;   // a MAT depth; slab callouts are shallower
                            deepNotes.Add((n.Cx / mpage.WidthPts * crop.Width,
                                           (mpage.HeightPts - n.Cy) / mpage.HeightPts * crop.Height, mm));
                        }
                    }
                    if (deepNotes.Count == 0) continue;
                    foreach (var rg in regions)
                    {
                        double areaSqFt = PlanGeometry.SquareFeet(rg.AreaPx, mv);
                        if (areaSqFt < 100) continue;              // a mat, not a hatch speck
                        // The note's leader lands beside the region — pair within 1.5 region-widths.
                        double reach = 1.5 * Math.Max(rg.Width, rg.Height);
                        var near = deepNotes.Where(d2 =>
                                d2.Px >= rg.MinX - reach && d2.Px <= rg.MaxX + reach &&
                                d2.Py >= rg.MinY - reach && d2.Py <= rg.MaxY + reach)
                            .OrderBy(d2 => Math.Abs(d2.Px - rg.CentroidX) + Math.Abs(d2.Py - rg.CentroidY))
                            .ToList();
                        if (near.Count == 0) continue;
                        double cy = areaSqFt * (near[0].Mm / 304.8) / 27.0;
                        matCy += cy;
                        fdnInputs.Add(new StructuralTakeoffInput(mlevel, TakeoffElementType.Foundation, "hatched mat", cy));
                        matBreakdown.Add($"    p{pg.Page} {mlevel,-10} hatched mat {areaSqFt,6:N0} sqft x {near[0].Mm}mm DEEP = {cy,6:N0} cy (FLAGGED - verify extent/depth)");
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"  (hatched-mat takeoff skipped: {ex.Message})"); }
            fdnCy += matCy;

            // Name every foundation element the takeoff does NOT price, so the residual is a checklist,
            // not a shrug: strip footings (lengths are plan geometry) and any core/pit mats the notes call out.
            var unpriced = new List<string>();
            if (stripMarks.Count > 0) unpriced.Add($"strip footings {string.Join(", ", stripMarks)} (lengths on plan)");
            // Point the core-footing residual at a FOUNDATION PLAN page (where the hatched mat is drawn),
            // not at whichever notes sheet mentions the phrase first.
            if (matCy == 0)
                foreach (var pg in tkDig.Pages)
                {
                    string ds2 = string.Concat(string.Join(" ", pg.Lines).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
                    if (ds2.Contains("COREFOOTING") && ds2.Contains("FOUNDATIONPLAN"))
                    { unpriced.Add($"core footing (p{pg.Page} — hatched mat, depth in plan note)"); break; }
                }
            string footingNote = fdnInputs.Count > 0
                ? $"NOT priced, quantify by hand: {string.Join("; ", unpriced.DefaultIfEmpty("nothing further found"))}. Spread footings ARE priced above."
                : "footings: no machine-readable footing schedule found — if the set has spread/strip footings, quantify them by hand. Parkade slabs ARE counted above.";

            // CALL-OUT REBAR CROSS-CHECK — per level, the sum of the quantity-bearing reinforcing call-outs
            // readable on that level's sheets (count × length × CSA mass, via the same grammar the rebar
            // change tool uses). An independent second opinion on the density-based reinforcing column;
            // never a bar list (mats-by-area, ties and continuous bars carry no computable weight).
            var calloutLb = new Dictionary<string, double>();
            try
            {
                using var cdoc = UglyToad.PdfPig.PdfDocument.Open(args[1]);
                var cPages = Kor.Operations.EngineeringTools.RebarChange.RebarPdfReader.Read(cdoc, UnitSystem.Metric)
                    .ToDictionary(p => p.Num);
                var engineLbl = tkOut.Estimate.TakeoffInputs.Select(i => i.Level).Distinct()
                    .Select(l => (Label: l, P: ParseLabel(l))).ToList();
                foreach (var pg in tkDig.Pages)
                {
                    if (pg.Title is null || !cPages.TryGetValue(pg.Page, out var cp)) continue;
                    double lb = cp.Callouts
                        .Select(h => Kor.Operations.EngineeringTools.RebarChange.RebarBarListWeigher.KeyWeightLb(h.Key))
                        .Where(w => w.HasValue).Sum(w => w!.Value);
                    if (lb <= 0) continue;
                    string floor = SlabTakeoffEngine.NormalizeLevelKey(pg.Title.Level);
                    string? tw = pg.Title.Zone;
                    string label = engineLbl.FirstOrDefault(e => (e.P.Tower ?? "") == (tw ?? "") && e.P.Floors.Contains(floor)).Label
                                ?? engineLbl.FirstOrDefault(e => e.P.Floors.Contains(floor)).Label
                                ?? pg.Title.Display;
                    calloutLb[label] = calloutLb.GetValueOrDefault(label) + lb;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"  (call-out rebar cross-check skipped: {ex.Message})"); }

            // Rebuild the workbook whenever the composition changed anything: engine rows (with replaced
            // gray-fill column rows removed) + schedule column rows + foundation rows, recomputed so the
            // xlsx matches the console total. The basis/caveat text states exactly what each source was.
            if (fdnInputs.Count > 0 || schedColInputs.Count > 0 || calloutLb.Count > 0)
            {
                var combined = engineInputs.Concat(schedColInputs).Concat(fdnInputs).ToList();
                var wbComputed = StructuralTakeoffService.Compute(combined, PlanProfile.BcModerate.ToImperialDensityTable());
                var wbModel = new StructuralTakeoffReportModel(Path.GetFileNameWithoutExtension(args[1]), "Vector takeoff", "", DateTime.UtcNow, wbComputed,
                    ConcreteBasis: "Concrete is MEASURED OFF THE DRAWINGS — a drawing takeoff, not model geometry. Slabs: poché + grid cross-check. Columns: the drawing's column schedules × key-plan counts × storey height (gray-fill footprint only where no schedule covers a floor). Walls: gray-fill footprints × storey height (+ flagged below-grade perimeter walls from the plate contour). Spread footings: the foundation schedule × counted plan marks. Transfer/built-up zones below slabs are NOT in plan callouts; verify transfer-prone levels against the sections.",
                    FoundationNote: footingNote,
                    CalloutRebarLbByLevel: calloutLb.Count > 0 ? calloutLb : null);
                File.WriteAllBytes(args[3], StructuralTakeoffReportGenerator.BuildXlsx(wbModel));
            }

            // The priced whole-building number (slab + deterministic gray-fill walls/columns) already came from the
            // engine and is in the xlsx. The schedule reads below are an INDEPENDENT CROSS-CHECK only — never added,
            // so the noisy schedule×key-plan path can't swing or zero the answer; it just offers a second opinion.
            double colFinalCy = schedColInputs.Count > 0 ? schedColCy + keptGrayColCy : colCyGeom;
            double wbTotal = slabCy + wallCyGeom + colFinalCy + fdnCy;
            Console.WriteLine($"\nWHOLE-BUILDING (per-level storey heights where supplied, else typical {typicalStoreyIn / 12:0.0}ft — see 'storey heights' note above):");
            Console.WriteLine($"  slab (incl. mats)    {slabCy,8:N0} cy");
            Console.WriteLine($"  walls   (gray-fill)  {wallCyGeom,8:N0} cy");
            if (schedColInputs.Count > 0)
                Console.WriteLine($"  columns              {colFinalCy,8:N0} cy  (schedule-first: {schedColCy:N0} from schedules"
                    + (keptGrayColCy > 0 ? $" + {keptGrayColCy:N0} gray-fill on uncovered floors)" : ")"));
            else
                Console.WriteLine($"  columns (gray-fill)  {colFinalCy,8:N0} cy  (no readable column schedule — footprint fallback)");
            if (fdnInputs.Count > 0)
            {
                Console.WriteLine($"  foundations          {fdnCy,8:N0} cy  (spread: schedule × counted marks; mats: hatch × DEEP note)");
                foreach (var line in fdnBreakdown) Console.WriteLine(line);
                foreach (var line in matBreakdown) Console.WriteLine(line);
            }
            Console.WriteLine($"  {"":21}--------");
            Console.WriteLine($"  TOTAL   {wbTotal,8:N0} cy   (in {args[3]})");
            Console.WriteLine($"  RESIDUAL: {footingNote}");
            Console.WriteLine($"  cross-check (NOT added — independent second opinions):");
            if (schedColInputs.Count > 0) Console.WriteLine($"    columns: gray-fill footprint {colCyGeom:N0} cy  vs schedule-first {colFinalCy:N0} cy above");
            if (wallSheets > 0) Console.WriteLine($"    walls:   schedule ~{wallCuYd:N0} cy ({wallMarks} marks / {wallSheets} sheet(s))  vs gray-fill {wallCyGeom:N0} cy above");
        }
        catch (Exception ex) { Console.Error.WriteLine($"  (vertical cross-check skipped: {ex.Message})"); }

        // SPEND TRACE — always say what this run cost and what it replayed, so a surprise invoice is
        // impossible: fresh API calls are the only spend; cache replays and --deterministic are $0.
        Console.WriteLine(tkDeterministic
            ? "  vision: DISABLED (--deterministic) — 0 API calls, $0."
            : $"  vision: {PlanVisionClient.CacheMisses} fresh API call(s) SPENT, {PlanVisionClient.CacheHits} replayed from cache ({Path.Combine(args[2], ".vision-cache")}).");

        // EXTENTS SIDECAR — the measured per-level slab plate areas, written beside the workbook so the
        // rebar change tool can price intensity changes (ΔAs × area) from OUR measurement instead of
        // leaving every area cell blank for a human. The fusion seam; see RebarExtents.
        try
        {
            var extents = tkOut.Estimate.Plates
                .Where(p => p.Plate.Element == TakeoffElementType.Slab && p.Plate.AreaSqFt > 0)
                .GroupBy(p => p.Plate.Level)
                .Select(g => new { label = g.Key, slabSqFtPerFloor = Math.Round(g.Max(p => p.Plate.AreaSqFt)) })
                .ToList();
            string extPath = args[3] + ".extents.json";
            File.WriteAllText(extPath, System.Text.Json.JsonSerializer.Serialize(
                new { levels = extents }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Extents sidecar (per-level slab areas for the rebar ΔAs pricer) -> {extPath}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"  (extents sidecar skipped: {ex.Message})"); }

        Console.WriteLine($"\n(suspended-slab benchmark: 31044 Coronation = 20,208 cy net of the 4,287 mat)  ->  {args[3]}");
        return 0;
    }
}
