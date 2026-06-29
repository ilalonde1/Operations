#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>What the app's "Generate takeoff" button needs: the drawing set, where its pages are
    /// rendered, and the read parameters. Defaults match the validated pipeline (1/8&quot;=1'-0&quot; @ 110 dpi,
    /// BC-moderate density, single-thickness-per-level to match the answer-key methodology).</summary>
    public sealed record SlabTakeoffRequest(
        string PdfPath,
        string PngDir,
        int? FirstPage = null,
        int? LastPage = null,
        string Scale = "1/8\"=1'-0\"",
        double Dpi = 110,
        string ProfileName = "BC-moderate",
        bool ApplyZoning = false);

    /// <summary>The whole-building suspended-slab takeoff: the priced/reconciled plates, the rendered
    /// xlsx, the totals, the human-readable measurement trace (what the CLI used to print), and the
    /// SYNOPSIS — every plate the diligence engine could not fully trust, which the app surfaces as the
    /// "unsure areas (orange)" panel before export and the AI crucible later converses about.</summary>
    public sealed record SlabTakeoffResult(
        PlanEstimateResult Estimate,
        byte[] Xlsx,
        double TotalConcreteCuYd,
        double TotalRebarLb,
        IReadOnlyList<string> Notes,
        IReadOnlyList<PlateEstimate> Synopsis);

    /// <summary>
    /// The takeoff engine, lifted OUT of the CLI so it runs identically from the WPF app: render a drawing
    /// set "like an estimator" → measured/reconciled/priced suspended-slab concrete + the orange-flag
    /// synopsis. The only AI is <see cref="IPlanVision"/> (classify a sheet, locate the plate — never reads
    /// a digit); the only image I/O is <see cref="IPlanRaster"/>; everything else is the deterministic Core
    /// spine. It returns DATA (no Console, no file write beyond the xlsx bytes it hands back) so any host
    /// can render it. The CLI's <c>vector-takeoff</c> command is now a thin caller of this method.
    /// </summary>
    public static class SlabTakeoffEngine
    {
        /// <summary>One plate's mutable working state between measurement and pricing: the reconciled area
        /// (and the raw grid-net/poché/basis it came from, so the sibling adjudicator can re-decide), plus
        /// the identity, thickness and quality diagnostics the pricing pass needs.</summary>
        private sealed class RawPlate
        {
            public string Label = "", LevelBase = "", Key = "";
            public int? RepLevel;
            public double Area, Thk, ZonedThk, FillRatio, GridNet, Poche;
            public int ClusterCount;
            public AreaBasis Basis;
            public IReadOnlyList<PlanFlag> AreaFlags = Array.Empty<PlanFlag>();
        }

        private static readonly System.Text.RegularExpressions.Regex TowerRx = new(@"\b(NORTH|SOUTH|EAST|WEST)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>The tower a plate belongs to (its label's cardinal), or null for a podium/parkade plate
        /// that belongs to no tower. Peer comparisons MUST stay within a tower: two towers have genuinely
        /// different footprints (here north ≈15k sqft vs south ≈8.7k), so a same-level cross-tower comparison
        /// would mislabel a good north floor "large" and a good south floor "small", and could wrongly
        /// "correct" a real north plate against the smaller south one.</summary>
        private static string? TowerOf(string label)
        {
            var m = TowerRx.Match(label ?? "");
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static double Median(IEnumerable<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }

        // A real tower-floor slab is never this small; an area below it is a degenerate locate box (a few
        // hundred sqft), NOT a setback floor that is merely smaller than the podium-level floors. Used both
        // to trigger the degenerate substitution and to exclude bad boxes from the per-tower typical median.
        private const double DegenerateFloorSqFt = 1500;

        public static async Task<SlabTakeoffResult> RunAsync(
            SlabTakeoffRequest req, IPlanVision vision, IPlanRaster raster, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(vision);
            ArgumentNullException.ThrowIfNull(raster);
            if (!File.Exists(req.PdfPath)) throw new FileNotFoundException("PDF not found.", req.PdfPath);

            var notes = new List<string>();
            double tkMpp = PlanGeometry.MetresPerPixel(req.Scale, req.Dpi) ?? 0;
            var tkProfile = PlanProfile.ByName(req.ProfileName);

            // Raw per-plate measurements (area is the reconciled grid/poché consensus; thickness may be
            // missing from synthesis and is reconciled across a level's match-line halves below before
            // pricing). A mutable class (not a tuple) so the cross-tower sibling adjudication pass below can
            // override a plate's area/flags after every plate — and its siblings — have been measured.
            var tkRaw = new List<RawPlate>();
            // The grid envelope and the poché are read in the SAME effective scale (derived from tkMpp), so
            // the reconciler compares like with like regardless of the absolute scale note.
            double tkScaleDenom = tkMpp > 0 ? tkMpp * req.Dpi / 0.0254 : 100.0;

            var tkMeasured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // keys with a SUCCESSFUL slab plate
            // Pooled text per canonical (level, zone) key — across ALL its sheets incl. the deduped
            // reinforcing ones — so a typical-floor band stated only on a sibling sheet still drives the multiplier.
            var tkPooled = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var tkDigest = DrawingDigestBuilder.Build(req.PdfPath, req.FirstPage, req.LastPage);
            notes.Add($"vector-takeoff: {tkDigest.Pages.Count} page(s) in range, classifying...");
            foreach (var page in tkDigest.Pages)
            {
                ct.ThrowIfCancellationRequested();
                string pj = JsonSerializer.Serialize(page, new JsonSerializerOptions { WriteIndented = false });
                string kind;
                try { using var cd = JsonDocument.Parse(await vision.SynthesizePageAsync(pj, ct)); kind = cd.RootElement.TryGetProperty("sheetKind", out var sk) && sk.ValueKind == JsonValueKind.String ? sk.GetString()! : "other"; }
                catch (Exception ex) { notes.Add($"  ! p{page.Page}: classify failed: {ex.Message}"); continue; }

                if (kind != "floor_plan" && kind != "foundation_plan") { notes.Add($"  p{page.Page}: {kind} (skip)"); continue; }

                // Canonical identity off the exact title block. One slab per (level, zone): the same
                // floor-half drawn twice (framing plan + reinforcing plan) must NOT be measured twice. Dedupe
                // BEFORE the locate call so duplicate sheets cost nothing. First sheet of an identity wins
                // (framing precedes reinforcing); the dropped sheet is a rebar/detail source, not a 2nd slab.
                if (page.Title is { } t0)
                {
                    // Pool every sheet's text under its canonical key BEFORE the dup-skip, so the floor-band
                    // note on a reinforcing sheet (which we skip for measurement) still feeds the multiplier.
                    if (!tkPooled.TryGetValue(t0.Key, out var pool)) { pool = new List<string>(); tkPooled[t0.Key] = pool; }
                    pool.AddRange(page.Lines);
                    // Skip ONLY once this key has a SUCCESSFUL measurement — so if the first sheet of a level
                    // is a reinforcing/penthouse sheet that fails to locate, a later framing sheet still measures.
                    if (tkMeasured.Contains(t0.Key)) { notes.Add($"  p{page.Page}: {t0.Display} — dup of an already-counted plan, slab not double-counted (skip)"); continue; }
                }

                string png = Path.Combine(req.PngDir, $"p-{page.Page:D2}.png");
                if (!File.Exists(png)) { notes.Add($"  ! p{page.Page}: render {png} missing, skipped."); continue; }
                try
                {
                    using var ld = JsonDocument.Parse(await vision.LocatePlateAsync(pj, raster.LoadDownscaledPng(png, 1600), ct));
                    var root = ld.RootElement;
                    // Prefer the exact title-block level over the synthesised one; title is deterministic.
                    string lvl = page.Title?.Display
                        ?? (root.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.String ? lv.GetString()! : $"p{page.Page}");
                    double thk = root.TryGetProperty("slabThicknessIn", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetDouble() : 0;
                    if (!root.TryGetProperty("slabBox", out var sbx) || sbx.ValueKind != JsonValueKind.Array) { notes.Add($"  ! {lvl}: no plate box, skipped."); continue; }
                    var bb = sbx.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
                    if (bb.Count < 4) { notes.Add($"  ! {lvl}: malformed plate box, skipped."); continue; }

                    var (iw, ih) = raster.ImageSize(png);
                    int cx0 = (int)(Math.Min(bb[0], bb[2]) * iw), cy0 = (int)(Math.Min(bb[1], bb[3]) * ih);
                    var crop = raster.LoadCrop(png, cx0, cy0,
                                                    (int)(Math.Max(bb[0], bb[2]) * iw), (int)(Math.Max(bb[1], bb[3]) * ih));
                    var cl = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
                    double area = PlanGeometry.SquareFeet(cl.Count > 0 ? cl[0].LightPx : 0, tkMpp);
                    if (area < 500) { notes.Add($"  ! {lvl}: slab area {area:N0} sqft implausibly small, skipped."); continue; }

                    // Measurement-quality diagnostic: how DENSELY the measured plate fills its own extent. A
                    // well-sealed plate fills its bounding box (a solid slab); a leaky/open boundary (the L01
                    // podium) leaves a sparse skeleton spanning a large bbox -> low fill -> under-measured.
                    // This separates good from bad, unlike enclosed/located-box (which only measures margin).
                    double fillRatio = cl.Count > 0 && cl[0].Width > 0 && cl[0].Height > 0
                        ? (double)cl[0].LightPx / ((double)cl[0].Width * cl[0].Height)
                        : double.NaN;

                    // Keep the plate even if thickness is missing — area is solid; thickness is reconciled
                    // below from the sibling match-line half of the same level (depth is constant across the line).
                    string levelBase = page.Title?.Level ?? lvl;
                    string key = page.Title?.Key ?? lvl;
                    // Representative storey for the typical-floor band: numeric or L-prefixed levels only
                    // (a tower's "13"/"L13"). P-parkade, basements, roof, mezzanines are 1:1 -> no multiplier.
                    string lvlTok = page.Title?.Level ?? "";
                    string lvlDigits = lvlTok.StartsWith("L", StringComparison.OrdinalIgnoreCase) && lvlTok.Length > 1 ? lvlTok.Substring(1) : lvlTok;
                    int? repLevel = int.TryParse(lvlDigits, out var rl) ? rl : (int?)null;

                    // Thickness ZONING (numbers from exact text, geometry only locates): a floor is rarely one
                    // thickness. Where the drawing genuinely calls out multiple field thicknesses (a tower's 8"
                    // wings + 12" core), split the measured area by nearest callout and price an area-weighted
                    // effective thickness. OFF by default: the answer-key QTO models each level at a SINGLE
                    // thickness, so Voronoi zoning over-weights localized callouts ~12%. Capability stays
                    // available (probe `vector-zones`, Core tests) for a future zone-resolved answer key.
                    double zonedThk = 0;
                    if (req.ApplyZoning && repLevel.HasValue && cl.Count > 0 && SlabThicknessReader.DominantThicknessIn(page.Lines) is int perPageModal)
                    {
                        var pageWP = VectorPageReader.ReadPage(req.PdfPath, page.Page);
                        var callouts = SlabThicknessZoner.ReadCallouts(pageWP);
                        var qual = SlabThicknessZoner.QualifyingValues(callouts, perPageModal);
                        if (qual.Count > 1)
                        {
                            var calloutPx = callouts.Where(c => qual.Contains(c.ValueIn))
                                .Select(c => new PlanGeometry.CalloutPx(
                                    c.Cx / pageWP.WidthPts * iw - cx0,
                                    (pageWP.HeightPts - c.Cy) / pageWP.HeightPts * ih - cy0,
                                    c.ValueIn))
                                .Where(c => c.X >= 0 && c.X < crop.Width && c.Y >= 0 && c.Y < crop.Height)
                                .ToList();
                            var zonePx = PlanGeometry.ThicknessZoneFractions(crop.Lum, crop.Width, crop.Height,
                                calloutPx, cl[0].MinX, cl[0].MinY, cl[0].MaxX, cl[0].MaxY);
                            zonedThk = SlabThicknessZoner.EffectiveThicknessIn(zonePx, perPageModal);
                            long zt = zonePx.Values.Sum();
                            if (zt > 0)
                                notes.Add($"  z {lvl}: multi-thickness slab " +
                                    string.Join(" + ", zonePx.OrderByDescending(k => k.Value).Select(k => $"{100.0 * k.Value / zt:N0}%@{k.Key}\"")) +
                                    $" -> effective {zonedThk:N1}\" (FLAG: zoned).");
                        }
                    }

                    // ── the hopper's AREA convergence: the structural-grid envelope (stable anchor) is
                    //    reconciled with the poché (independent cross-check). The grid is the primary number
                    //    where it reads; the poché confirms it or, when it has leaked/grabbed wrong, flags it.
                    var grid = StructuralGridReader.FromPage(VectorPageReader.ReadPage(req.PdfPath, page.Page));
                    var consensus = SlabAreaReconciler.Reconcile(grid, tkScaleDenom, area);
                    double plateArea = consensus.AreaSqFt > 0 ? consensus.AreaSqFt : area;

                    tkRaw.Add(new RawPlate
                    {
                        Label = lvl, LevelBase = levelBase, Key = key, RepLevel = repLevel,
                        Area = plateArea, Thk = thk, ZonedThk = zonedThk, FillRatio = fillRatio,
                        ClusterCount = cl.Count, GridNet = consensus.GridNetSqFt ?? 0,
                        Poche = consensus.PocheSqFt ?? area, Basis = consensus.Basis, AreaFlags = consensus.Flags,
                    });
                    if (page.Title is { } tk) tkMeasured.Add(tk.Key);   // this key now has a real measured plate
                    string gridNote = grid is { IsUsable: true }
                        ? $"grid[{string.Join("", grid.XLabels.Take(1))}..{string.Join("", grid.XLabels.TakeLast(1))}×{string.Join("", grid.YLabels.Take(1))}..{string.Join("", grid.YLabels.TakeLast(1))}]"
                        : "grid[—]";
                    notes.Add($"  p{page.Page}: {lvl,-18} slab {plateArea,7:N0} sqft x {(thk > 0 ? thk + "\"" : "?\" (reconcile)"),-12} {consensus.Basis,-18} {gridNote} [poché {area,6:N0} fill {(double.IsNaN(fillRatio) ? 0 : fillRatio):0.00}]");
                }
                catch (Exception ex) { notes.Add($"  ! p{page.Page}: locate/measure failed: {ex.Message}"); }
            }

            if (tkRaw.Count == 0) throw new InvalidOperationException("No slab plates measured.");

            // ── the hopper's SECOND peg: within-tower outlier adjudication. A grid≫poché plate that
            //    Reconcile resolved by trusting the grid is re-checked against the CONFIRMED typical floors
            //    of its OWN tower. The same tower's floors share a footprint, so when this plate's grid
            //    balloons past the tower typical (a setback floor caught at full podium width) while its
            //    poché matches the typical, the grid grabbed too much — the poché is the better number.
            //    Per-tower, NOT same-level-cross-tower: the two towers differ in size, so comparing a north
            //    floor to its south namesake would wrongly shrink a real north plate. Deterministic, no AI.
            foreach (var grp in tkRaw.Where(r => r.RepLevel.HasValue && TowerOf(r.Label) != null)
                                     .GroupBy(r => TowerOf(r.Label)!))
            {
                var trustworthy = grp.Where(r => r.Basis is AreaBasis.GridConfirmed or AreaBasis.GridOnly)
                                     .Select(r => r.Area).ToList();
                if (trustworthy.Count == 0) continue;
                double peerMedian = Median(trustworthy);
                foreach (var r in grp.Where(r => r.Basis == AreaBasis.GridPocheDisagree))
                {
                    var resolved = SlabAreaReconciler.ResolveAgainstPeers(
                        new AreaConsensus(r.Area, r.Basis, r.GridNet, r.Poche, r.AreaFlags), peerMedian);
                    if (resolved.Basis != r.Basis)
                    {
                        notes.Add($"  ⇄ {r.Label}: grid {r.GridNet:N0} sqft is a {r.GridNet / peerMedian:P0} outlier vs the {grp.Key} tower typical ({peerMedian:N0}); using poché {r.Poche:N0} (FLAG: grid grabbed podium-width bubbles).");
                        r.Area = resolved.AreaSqFt; r.Basis = resolved.Basis; r.AreaFlags = resolved.Flags;
                    }
                }
            }

            // PRIMARY thickness: the drawing's own slab callout ("10\" SLAB" / metric "200 SLAB"), read
            // deterministically from the exact text pooled across a level's sheets — stable run-to-run.
            var tkDetThk = tkRaw.GroupBy(r => r.LevelBase, StringComparer.OrdinalIgnoreCase).ToDictionary(
                g => g.Key,
                g => SlabThicknessReader.DominantThicknessIn(
                        g.Select(r => r.Key).Distinct().SelectMany(k => tkPooled.TryGetValue(k, out var p) ? p : Enumerable.Empty<string>())),
                StringComparer.OrdinalIgnoreCase);

            // FALLBACK thickness: a level's modal synthesised value, for any level whose drawing has no callout.
            var tkThkByLevel = tkRaw.Where(r => r.Thk > 0)
                .GroupBy(r => r.LevelBase)
                .ToDictionary(g => g.Key,
                              g => g.GroupBy(r => r.Thk).OrderByDescending(t => t.Count()).ThenByDescending(t => t.Key).First().Key,
                              StringComparer.OrdinalIgnoreCase);

            // Tower typical plans tile the building: each governs a contiguous stack from above the previous
            // typical level up to its own. Boundaries are the representative levels read off the title block,
            // so floors no band explicitly names still get counted by the plan below them (no orphan floors).
            var towerReps = tkRaw.Where(r => r.RepLevel.HasValue).Select(r => r.RepLevel!.Value).Distinct().OrderBy(x => x).ToList();
            int topBandTop = towerReps.Count > 0 ? towerReps[^1] : 0;
            if (towerReps.Count > 0)
            {
                string topKey = tkRaw.First(r => r.RepLevel == towerReps[^1]).Key;
                if (tkPooled.TryGetValue(topKey, out var topPool))
                {
                    var bands = FloorMultiplier.Bands(topPool);
                    if (bands.Count > 0) topBandTop = Math.Max(topBandTop, bands.Max(b => b.High));
                }
            }
            var tileCounts = FloorMultiplier.TileTowerCounts(towerReps, topBandTop);

            // Degenerate-box guard + peer-area flags work off the PER-TOWER median floor area (two towers
            // have different footprints, so a global median would mislabel a good north floor "large" and a
            // good south floor "small"). Plates with no tower (podium/parkade) share a global fallback.
            // The typical-floor median excludes degenerate bad boxes so a couple of 200-sqft mislocates don't
            // drag the "typical" down (and so the substitute value below is a real floor, not a half-bad one).
            double globalTowerMedian = Median(tkRaw.Where(r => r.RepLevel.HasValue && r.Area >= DegenerateFloorSqFt).Select(r => r.Area));
            var medianByTower = tkRaw.Where(r => r.RepLevel.HasValue && r.Area >= DegenerateFloorSqFt && TowerOf(r.Label) != null)
                .GroupBy(r => TowerOf(r.Label)!)
                .ToDictionary(g => g.Key, g => Median(g.Select(r => r.Area)), StringComparer.OrdinalIgnoreCase);
            double PeerMedianFor(RawPlate r) =>
                TowerOf(r.Label) is string tw && medianByTower.TryGetValue(tw, out var m) && m > 0 ? m : globalTowerMedian;

            var tkPlates = new List<MeasuredPlate>();
            foreach (var r in tkRaw)
            {
                double thk = r.Thk;   // synthesised read
                var thkSource = ThicknessSource.None;
                // A genuinely multi-thickness plate prices at its own area-weighted zoned thickness — it has
                // explicit callouts for every zone (that is why it qualified), so it needs no sibling reconcile.
                if (r.ZonedThk > 0)
                {
                    thk = r.ZonedThk; thkSource = ThicknessSource.Callout;
                }
                else if (tkDetThk.TryGetValue(r.LevelBase, out var det) && det.HasValue)
                {
                    if (thk > 0 && Math.Abs(thk - det.Value) > 0.5)
                        notes.Add($"  ~ {r.Label}: thickness {det.Value}\" from slab callout (synthesis said {thk}\").");
                    thk = det.Value; thkSource = ThicknessSource.Callout;
                }
                else if (thk <= 0 && tkThkByLevel.TryGetValue(r.LevelBase, out var inferred))
                { thk = inferred; thkSource = ThicknessSource.SynthesisFallback; notes.Add($"  ~ {r.Label}: thickness inherited {thk}\" from level {r.LevelBase} (synthesis fallback, no callout)."); }
                else if (thk > 0) thkSource = ThicknessSource.SynthesisFallback;   // the page's own synth read, no callout pooled
                if (thk <= 0) { notes.Add($"  ! {r.Label}: no thickness for level {r.LevelBase} (no callout, no sibling), FLAGGED + excluded from concrete."); continue; }

                // Parkade/roof and any non-numeric level are 1:1; tower levels take their tiled floor count.
                int floors = r.RepLevel is int rep && tileCounts.TryGetValue(rep, out var c) ? c : 1;
                if (floors > 1) notes.Add($"  x {r.Label}: typical plan -> {floors} physical floors (FLAG: inferred contiguous stack, levels {r.RepLevel - floors + 1}-{r.RepLevel}).");

                double area = r.Area;
                double towerMedianArea = PeerMedianFor(r);
                bool degenerate = false;
                // A degenerate locate box is implausibly small for ANY tower floor (a few hundred sqft) — NOT
                // a setback floor that is legitimately smaller than the podium-level floors. So the trigger is
                // an ABSOLUTE floor or a tiny fraction (<15%) of the tower typical; and a GridConfirmed plate
                // (grid and poché already agree on its area) is never condemned — that is the trustworthy case.
                if (r.RepLevel.HasValue && r.Basis != AreaBasis.GridConfirmed && towerMedianArea > 0
                    && (area < DegenerateFloorSqFt || area < 0.15 * towerMedianArea))
                {
                    notes.Add($"  ! {r.Label}: area {area:N0} sqft implausible vs {(TowerOf(r.Label) ?? "tower")} median {towerMedianArea:N0} — substituting median (FLAG: degenerate locate box).");
                    area = towerMedianArea; degenerate = true;
                }
                // Peer-area ratio: a tower's own typical floors should be similar, so a plate far from its
                // tower median is a likely locate error. Only tower floors have true peers; others (parkade
                // halves, mezz, roof) are not directly comparable -> NaN (no peer flag).
                double peerRatio = r.RepLevel.HasValue && towerMedianArea > 0 ? area / towerMedianArea : double.NaN;
                tkPlates.Add(new MeasuredPlate(r.Label, TakeoffElementType.Slab, "suspended", area, thk, floors,
                    FillRatio: r.FillRatio, ClusterCount: r.ClusterCount, ThicknessSource: thkSource,
                    DegenerateBox: degenerate, PeerAreaRatio: peerRatio, ExtraFlags: r.AreaFlags));
            }

            if (tkPlates.Count == 0) throw new InvalidOperationException("No priceable slab plates (no thickness anywhere).");

            var tkResult = PlanEstimatePipeline.Run(tkPlates, tkProfile);
            var tkComputed = StructuralTakeoffService.Compute(tkResult.TakeoffInputs, tkProfile.ToImperialDensityTable());
            var tkModel = new StructuralTakeoffReportModel(Path.GetFileNameWithoutExtension(req.PdfPath), "Vector takeoff", "", DateTime.UtcNow, tkComputed);
            byte[] xlsx = StructuralTakeoffReportGenerator.BuildXlsx(tkModel);

            // SYNOPSIS — every plate the diligence engine could not fully trust (drives the app's "unsure
            // areas (orange)" panel and, later, what the AI crucible converses about).
            var synopsis = tkResult.Plates.Where(p => p.Check.Confidence != TakeoffConfidence.High).ToList();

            return new SlabTakeoffResult(tkResult, xlsx, tkResult.TotalConcreteCuYd, tkComputed.TotalRebarWeight, notes, synopsis);
        }
    }
}
