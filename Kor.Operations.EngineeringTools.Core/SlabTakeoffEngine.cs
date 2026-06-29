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
    /// The takeoff engine, lifted OUT of the CLI so it runs identically from the WPF app. It is a THREE-PHASE
    /// pipeline:
    ///   1. DETERMINISTIC pass (no AI) — every page/plate: identity (title block), area (grid envelope ⟷
    ///      poché), field thickness (slab callouts). It resolves everything the drawing gives up for free and
    ///      records what it CAN'T as an explicit unknown on the plate — never guessing, never silently dropping.
    ///   2. AI resolves the UNKNOWNS — one targeted, data-attached question per unknown ("locate this
    ///      garbled-grid plate", "apportion these 200/900/450 drop bands"). A clean set makes ZERO calls.
    ///   3. SYNTHESIS — merge the deterministic results and the AI answers, run the cross-checks (within-tower
    ///      adjudication, tiling), price, and report the totals plus the honest residual (what nobody resolved).
    /// The only AI is <see cref="IPlanVision"/>; the only image I/O is <see cref="IPlanRaster"/>; everything
    /// else is the deterministic Core spine. It returns DATA so any host (CLI today, WPF next) can render it.
    /// </summary>
    public static class SlabTakeoffEngine
    {
        /// <summary>One plate's mutable working state across the three phases: the reconciled area (and the raw
        /// grid-net/poché/basis it came from, so the sibling adjudicator can re-decide), the identity, thickness
        /// and quality diagnostics the pricing pass needs, the phase-2 context (page/render/box/callouts) for a
        /// targeted AI call, and the explicit unknowns the deterministic pass could not resolve.</summary>
        private sealed class RawPlate
        {
            public string Label = "", LevelBase = "", Key = "";
            public int? RepLevel;
            public double Area, Thk, ZonedThk, FillRatio, GridNet, Poche;
            public int ClusterCount;
            public AreaBasis Basis;
            public IReadOnlyList<PlanFlag> AreaFlags = Array.Empty<PlanFlag>();

            // Phase-2 context: where the plate is (page + render + pixel box) so a targeted AI call can be
            // re-issued against just this plate, and the distinct thickness callouts (inches) the drawing
            // states (field + any deeper drop bands) that an apportionment question hands to the model.
            public int Page; public string Png = "";
            public int Cx0, Cy0, Cx1, Cy1; public bool HasBox;
            public IReadOnlyList<int> Callouts = Array.Empty<int>();

            // The explicit unknowns this plate carries out of the deterministic pass (phase 1) — the work
            // list phase 2 hands to AI. Cleared as each is resolved; whatever remains is the honest residual.
            public bool NeedsLocate, NeedsThicknessSplit, NeedsThickness;
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

        /// <summary>Crop a rendered page to a pixel box and run the slab poché over it: returns the enclosed
        /// plate area (sqft at <paramref name="mpp"/>), how densely it fills its own extent, and the cluster
        /// count. The one place area is measured, shared by the deterministic grid-box pass and the phase-2
        /// AI-located pass so they measure identically.</summary>
        private static (double Area, double Fill, int Clusters) MeasurePoche(
            IPlanRaster raster, string png, int x0, int y0, int x1, int y1, double mpp)
        {
            var crop = raster.LoadCrop(png, x0, y0, x1, y1);
            var cl = PlanGeometry.MeasureEnclosedClusters(crop.Lum, crop.Width, crop.Height);
            double area = PlanGeometry.SquareFeet(cl.Count > 0 ? cl[0].LightPx : 0, mpp);
            double fill = cl.Count > 0 && cl[0].Width > 0 && cl[0].Height > 0
                ? (double)cl[0].LightPx / ((double)cl[0].Width * cl[0].Height) : double.NaN;
            return (area, fill, cl.Count);
        }

        /// <summary>The area-weighted effective thickness (inches) from an AI drop-band apportionment —
        /// JSON <c>{ "fractions": [ { "thicknessIn", "areaPct" } ] }</c>. Trusts only thicknesses the drawing
        /// actually called out (the <paramref name="callouts"/> we sent); ignores any the model invents, and
        /// returns 0 (caller keeps the field thickness) if the answer is unusable.</summary>
        private static double EffectiveFromSplit(JsonElement root, IReadOnlyList<int> callouts)
        {
            if (!root.TryGetProperty("fractions", out var fr) || fr.ValueKind != JsonValueKind.Array) return 0;
            double wsum = 0, psum = 0;
            foreach (var f in fr.EnumerateArray())
            {
                if (!f.TryGetProperty("thicknessIn", out var tEl) || !f.TryGetProperty("areaPct", out var pEl)) continue;
                double t = tEl.ValueKind == JsonValueKind.Number ? tEl.GetDouble() : 0;
                double p = pEl.ValueKind == JsonValueKind.Number ? pEl.GetDouble() : 0;
                if (t <= 0 || p <= 0) continue;
                // Snap to the nearest called-out thickness so a slightly-off value still counts; reject a
                // value far from every callout (a hallucinated depth) rather than letting it skew the average.
                int near = callouts.OrderBy(c => Math.Abs(c - t)).First();
                if (Math.Abs(near - t) > 3) continue;
                wsum += near * p; psum += p;
            }
            return psum > 0 ? wsum / psum : 0;
        }

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
            var tkRaw = new List<RawPlate>();
            // The grid envelope and the poché are read in the SAME effective scale (derived from tkMpp), so
            // the reconciler compares like with like regardless of the absolute scale note.
            double tkScaleDenom = tkMpp > 0 ? tkMpp * req.Dpi / 0.0254 : 100.0;

            var tkMeasured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // keys with a resolved area
            // Pooled text per canonical (level, zone) key — across ALL its sheets incl. the deduped
            // reinforcing ones — so a typical-floor band stated only on a sibling sheet still drives the multiplier.
            var tkPooled = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var tkDigest = DrawingDigestBuilder.Build(req.PdfPath, req.FirstPage, req.LastPage);
            notes.Add($"vector-takeoff: {tkDigest.Pages.Count} page(s) in range.");

            // ════════════════════ PHASE 1 — DETERMINISTIC PASS (no AI) ════════════════════
            // Resolve every number the drawing gives up for free; record what it can't as an explicit unknown
            // ON the plate (NeedsLocate / NeedsThicknessSplit / NeedsThickness). Nothing is guessed and nothing
            // is silently dropped — a bad-box floor is KEPT as a listed unknown for phase 2 to resolve.
            notes.Add("phase 1: deterministic pass (title + grid + slab callouts) ...");
            foreach (var page in tkDigest.Pages)
            {
                ct.ThrowIfCancellationRequested();

                // Deterministic classify: a measurable slab plan is a LEVELLED sheet that shows a structural
                // grid envelope or a slab callout. Details/schedules/sections have neither → skipped for free.
                var vp = VectorPageReader.ReadPage(req.PdfPath, page.Page);
                var grid = StructuralGridReader.FromPage(vp);
                int? pageThk = SlabThicknessReader.DominantThicknessIn(page.Lines);
                bool leveled = page.Title?.Level is { Length: > 0 };
                if (!(leveled && (grid?.IsLocatable == true || pageThk.HasValue)))
                { notes.Add($"  p{page.Page}: {page.Title?.Display ?? "untitled"} — not a measurable slab plan (skip)"); continue; }

                // Canonical identity off the exact title block; pool every sheet's text (even deduped ones), and
                // skip a duplicate only once the identity already has a RESOLVED plate.
                if (page.Title is { } t0)
                {
                    if (!tkPooled.TryGetValue(t0.Key, out var pool)) { pool = new List<string>(); tkPooled[t0.Key] = pool; }
                    pool.AddRange(page.Lines);
                    if (tkMeasured.Contains(t0.Key)) { notes.Add($"  p{page.Page}: {t0.Display} — dup of an already-counted plan, slab not double-counted (skip)"); continue; }
                }

                string png = Path.Combine(req.PngDir, $"p-{page.Page:D2}.png");
                if (!File.Exists(png)) { notes.Add($"  ! p{page.Page}: render {png} missing, skipped."); continue; }

                string lvl = page.Title?.Display ?? $"p{page.Page}";
                string levelBase = page.Title?.Level ?? lvl;
                string key = page.Title?.Key ?? lvl;
                string lvlTok = page.Title?.Level ?? "";
                string lvlDigits = lvlTok.StartsWith("L", StringComparison.OrdinalIgnoreCase) && lvlTok.Length > 1 ? lvlTok.Substring(1) : lvlTok;
                int? repLevel = int.TryParse(lvlDigits, out var rl) ? rl : (int?)null;
                var (iw, ih) = raster.ImageSize(png);

                var plate = new RawPlate
                {
                    Label = lvl, LevelBase = levelBase, Key = key, RepLevel = repLevel,
                    Thk = pageThk ?? 0, Page = page.Page, Png = png,
                };

                // Distinct thickness callouts (inches): the field slab + any deeper drop bands. A band deeper
                // than the field (≥ field+6" and ≥1.4×) means a thickened transfer/podium plate the field
                // thickness alone under-prices → an explicit thickness-split unknown for AI to apportion.
                var distinctThk = SlabThicknessZoner.ReadCallouts(vp).Select(c => c.ValueIn)
                                    .Where(v => v > 0).Distinct().OrderBy(v => v).ToList();
                int fieldThk = plate.Thk > 0 ? (int)Math.Round(plate.Thk) : (distinctThk.Count > 0 ? distinctThk[0] : 0);
                // The apportionment set is the FIELD slab plus only DEEPER drop bands. A thinner stray callout
                // (a misread "5" SLAB, a topping/stair note) would drag the area-weighted depth the WRONG way,
                // so anything below the field thickness is excluded before the question goes to AI.
                var apportion = distinctThk.Where(v => v >= fieldThk).Distinct().OrderBy(v => v).ToList();
                plate.Callouts = apportion;
                bool hasDropBands = apportion.Count >= 2 && apportion.Any(v => v >= fieldThk + 6 && v >= 1.4 * fieldThk);

                // Deterministic locate + area: the grid bubble box → padded poché crop. No readable grid (or a
                // box that yields an implausible poché) → leave the area unresolved and flag NeedsLocate.
                if (grid?.IsLocatable == true)
                {
                    int cx0 = (int)(grid.XMinPt / vp.WidthPts * iw), cx1 = (int)(grid.XMaxPt / vp.WidthPts * iw);
                    int cy0 = (int)((vp.HeightPts - grid.YMaxPt) / vp.HeightPts * ih), cy1 = (int)((vp.HeightPts - grid.YMinPt) / vp.HeightPts * ih);
                    // The grid box is tight to the gridlines (≈ the slab edge); pad it outward so the poché's
                    // exterior flood seeds in the paper margin, not inside the slab (else the poché collapses).
                    int padX = (int)(0.10 * (cx1 - cx0)), padY = (int)(0.10 * (cy1 - cy0));
                    cx0 = Math.Clamp(cx0 - padX, 0, iw - 1); cx1 = Math.Clamp(cx1 + padX, cx0 + 1, iw);
                    cy0 = Math.Clamp(cy0 - padY, 0, ih - 1); cy1 = Math.Clamp(cy1 + padY, cy0 + 1, ih);
                    plate.Cx0 = cx0; plate.Cy0 = cy0; plate.Cx1 = cx1; plate.Cy1 = cy1; plate.HasBox = true;

                    var (area, fill, clusters) = MeasurePoche(raster, png, cx0, cy0, cx1, cy1, tkMpp);
                    if (area >= 500)
                    {
                        var consensus = SlabAreaReconciler.Reconcile(grid, tkScaleDenom, area);
                        plate.Area = consensus.AreaSqFt > 0 ? consensus.AreaSqFt : area;
                        plate.FillRatio = fill; plate.ClusterCount = clusters;
                        plate.GridNet = consensus.GridNetSqFt ?? 0; plate.Poche = consensus.PocheSqFt ?? area;
                        plate.Basis = consensus.Basis; plate.AreaFlags = consensus.Flags;
                    }
                    else plate.NeedsLocate = true;   // the grid box gave a bad poché — hand the locate to AI
                }
                else plate.NeedsLocate = true;       // no readable grid — hand the locate to AI

                if (hasDropBands) plate.NeedsThicknessSplit = true;
                else if (fieldThk <= 0) plate.NeedsThickness = true;

                tkRaw.Add(plate);
                if (!plate.NeedsLocate && page.Title is { } tk) tkMeasured.Add(tk.Key);
                string need = string.Join("+", new[]
                {
                    plate.NeedsLocate ? "locate" : null,
                    plate.NeedsThicknessSplit ? "thk-split" : null,
                    plate.NeedsThickness ? "thk" : null,
                }.Where(s => s != null));
                notes.Add($"  p{page.Page}: {lvl,-16} {(plate.Area > 0 ? $"{plate.Area,7:N0} sqft" : "  (area pending)")}  " +
                          $"thk {(plate.Thk > 0 ? plate.Thk + "\"" : "?"),-4} {plate.Basis,-16}{(need.Length > 0 ? "  → UNKNOWN[" + need + "]" : "")}");
            }

            // A level's first sheet may have been an unreadable-grid reinforcing plan (NeedsLocate) that a
            // later framing sheet then measured cleanly — keep ONE plate per identity, preferring the resolved.
            tkRaw = tkRaw.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.FirstOrDefault(p => !p.NeedsLocate) ?? g.First()).ToList();

            if (tkRaw.Count == 0) throw new InvalidOperationException("No slab plates measured.");

            // ════════════════════ PHASE 2 — AI RESOLVES THE UNKNOWNS (one targeted call each) ════════════════════
            // Only flagged plates reach here; a fully deterministic set makes ZERO calls. Each call is a
            // specific question with the plate's own data attached — never a per-page sweep.
            var unknowns = tkRaw.Where(p => p.NeedsLocate || p.NeedsThicknessSplit || p.NeedsThickness).ToList();
            if (unknowns.Count > 0) notes.Add($"phase 2: {unknowns.Count} unknown(s) → targeted AI.");
            foreach (var p in unknowns)
            {
                ct.ThrowIfCancellationRequested();
                var page = tkDigest.Pages.FirstOrDefault(pg => pg.Page == p.Page);

                // (a) LOCATE — no readable grid: ask AI for the plate box, then measure the poché deterministically.
                if (p.NeedsLocate && page != null)
                {
                    try
                    {
                        var (iw, ih) = raster.ImageSize(p.Png);
                        string pj = JsonSerializer.Serialize(page, new JsonSerializerOptions { WriteIndented = false });
                        using var ld = JsonDocument.Parse(await vision.LocatePlateAsync(pj, raster.LoadDownscaledPng(p.Png, 1600), ct));
                        var root = ld.RootElement;
                        if (root.TryGetProperty("slabBox", out var sbx) && sbx.ValueKind == JsonValueKind.Array)
                        {
                            var bb = sbx.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
                            if (bb.Count >= 4)
                            {
                                int bx0 = Math.Clamp((int)(Math.Min(bb[0], bb[2]) * iw), 0, iw - 1), by0 = Math.Clamp((int)(Math.Min(bb[1], bb[3]) * ih), 0, ih - 1);
                                int bx1 = Math.Clamp((int)(Math.Max(bb[0], bb[2]) * iw), bx0 + 1, iw), by1 = Math.Clamp((int)(Math.Max(bb[1], bb[3]) * ih), by0 + 1, ih);
                                var (area, fill, clusters) = MeasurePoche(raster, p.Png, bx0, by0, bx1, by1, tkMpp);
                                if (area >= 500)
                                {
                                    var consensus = SlabAreaReconciler.Reconcile(null, tkScaleDenom, area);   // no grid → poché stands alone
                                    p.Area = consensus.AreaSqFt; p.FillRatio = fill; p.ClusterCount = clusters;
                                    p.Poche = area; p.Basis = consensus.Basis; p.AreaFlags = consensus.Flags;
                                    p.Cx0 = bx0; p.Cy0 = by0; p.Cx1 = bx1; p.Cy1 = by1; p.HasBox = true; p.NeedsLocate = false;
                                    notes.Add($"  ⤷ {p.Label}: AI located plate → {area:N0} sqft (no grid).");
                                }
                                else notes.Add($"  ! {p.Label}: AI-located box still measured {area:N0} sqft — left as residual unknown.");
                            }
                        }
                    }
                    catch (Exception ex) { notes.Add($"  ! {p.Label}: AI locate failed: {ex.Message}"); }
                }

                // (b) THICKNESS SPLIT — drop bands: ask AI the area % per called-out thickness; compute the
                //     area-weighted effective depth deterministically from its answer.
                if (p.NeedsThicknessSplit && p.HasBox && p.Callouts.Count >= 2)
                {
                    try
                    {
                        var cropPng = raster.LoadCropPng(p.Png, p.Cx0, p.Cy0, p.Cx1, p.Cy1, 1200);
                        using var sd = JsonDocument.Parse(await vision.ApportionThicknessAsync(cropPng, p.Callouts, ct));
                        double eff = EffectiveFromSplit(sd.RootElement, p.Callouts);
                        if (eff > 0)
                        {
                            p.ZonedThk = eff; p.NeedsThicknessSplit = false;
                            notes.Add($"  ⤷ {p.Label}: AI apportioned drop bands [{string.Join("/", p.Callouts)}\"] → effective {eff:N1}\".");
                        }
                        else notes.Add($"  ! {p.Label}: AI thickness split unusable — kept field thickness (flagged).");
                    }
                    catch (Exception ex) { notes.Add($"  ! {p.Label}: AI thickness apportion failed: {ex.Message}"); }
                }
            }

            // ════════════════════ PHASE 3 — SYNTHESIS (merge + cross-check + price) ════════════════════
            notes.Add("phase 3: synthesis (cross-checks + tiling + pricing) ...");

            // The hopper's within-tower outlier adjudication: a grid≫poché plate whose grid balloons past its
            // OWN tower's typical floor (a setback caught at full podium width) while its poché matches that
            // typical — use the poché. Per-tower, NOT same-level-cross-tower (the two towers differ in size).
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

            // Degenerate-box guard + peer-area flags work off the PER-TOWER median floor area (two towers have
            // different footprints). The typical-floor median excludes degenerate bad boxes so a couple of
            // 200-sqft mislocates don't drag the "typical" down (and so the substitute value is a real floor).
            double globalTowerMedian = Median(tkRaw.Where(r => r.RepLevel.HasValue && r.Area >= DegenerateFloorSqFt).Select(r => r.Area));
            var medianByTower = tkRaw.Where(r => r.RepLevel.HasValue && r.Area >= DegenerateFloorSqFt && TowerOf(r.Label) != null)
                .GroupBy(r => TowerOf(r.Label)!)
                .ToDictionary(g => g.Key, g => Median(g.Select(r => r.Area)), StringComparer.OrdinalIgnoreCase);
            double PeerMedianFor(RawPlate r) =>
                TowerOf(r.Label) is string tw && medianByTower.TryGetValue(tw, out var m) && m > 0 ? m : globalTowerMedian;

            // The LOCAL peer for a degenerate-area check: same-tower resolved floors within a few levels of
            // this one. A whole-tower median is podium-dominated, so it both mis-condemns a legitimately small
            // SETBACK floor (19 NORTH ≈ its neighbour 18 NORTH, not the 15k podium) and would substitute a
            // wrong (too-large) value. Nearest-level siblings keep podium compared to podium and setback to
            // setback. Falls back to the tower median when a floor has no near neighbours.
            double NearPeerMedian(RawPlate r)
            {
                if (!r.RepLevel.HasValue) return PeerMedianFor(r);
                string? tw = TowerOf(r.Label);
                var near = tkRaw.Where(x => !ReferenceEquals(x, r) && x.RepLevel.HasValue
                                 && Math.Abs(x.RepLevel!.Value - r.RepLevel!.Value) <= 3
                                 && TowerOf(x.Label) == tw && !x.NeedsLocate && x.Area >= DegenerateFloorSqFt)
                                .Select(x => x.Area).ToList();
                return near.Count > 0 ? Median(near) : PeerMedianFor(r);
            }

            var tkPlates = new List<MeasuredPlate>();
            foreach (var r in tkRaw)
            {
                // A plate whose area nobody could resolve (AI locate failed) is the honest residual — list it,
                // exclude it from the total, never invent a number for it.
                if (r.Area <= 0 || r.NeedsLocate)
                { notes.Add($"  ! {r.Label}: area unresolved (no grid, AI could not locate) — RESIDUAL unknown, excluded from total."); continue; }

                double thk = r.Thk;   // the page's field-callout read
                var thkSource = ThicknessSource.None;
                // An AI-apportioned transfer plate prices at its area-weighted effective depth — it covers
                // every called-out band, so it needs no sibling reconcile.
                if (r.ZonedThk > 0)
                {
                    thk = r.ZonedThk; thkSource = ThicknessSource.Callout;
                }
                else if (tkDetThk.TryGetValue(r.LevelBase, out var det) && det.HasValue)
                {
                    if (thk > 0 && Math.Abs(thk - det.Value) > 0.5)
                        notes.Add($"  ~ {r.Label}: thickness {det.Value}\" from slab callout (page read {thk}\").");
                    thk = det.Value; thkSource = ThicknessSource.Callout;
                }
                else if (thk <= 0 && tkThkByLevel.TryGetValue(r.LevelBase, out var inferred))
                { thk = inferred; thkSource = ThicknessSource.SynthesisFallback; notes.Add($"  ~ {r.Label}: thickness inherited {thk}\" from level {r.LevelBase} (sibling fallback, no callout)."); }
                else if (thk > 0) thkSource = ThicknessSource.SynthesisFallback;
                if (thk <= 0) { notes.Add($"  ! {r.Label}: no thickness for level {r.LevelBase} (no callout, no sibling) — RESIDUAL unknown, excluded from concrete."); continue; }

                // Parkade/roof and any non-numeric level are 1:1; tower levels take their tiled floor count.
                int floors = r.RepLevel is int rep && tileCounts.TryGetValue(rep, out var c) ? c : 1;
                if (floors > 1) notes.Add($"  x {r.Label}: typical plan -> {floors} physical floors (FLAG: inferred contiguous stack, levels {r.RepLevel - floors + 1}-{r.RepLevel}).");

                double area = r.Area;
                double towerMedianArea = PeerMedianFor(r);
                double nearMedian = NearPeerMedian(r);
                bool degenerate = false;
                // A degenerate locate is implausibly small for the floor it claims to be. Two cases substitute
                // the NEAREST-level peer: an ABSOLUTE floor (a few hundred sqft — bad for any floor), and a
                // NO-GRID plate (poché located by AI, no envelope cross-check) that came in under half its
                // nearest-level siblings — almost certainly a sub-region grab. A GRID-BACKED plate is never
                // condemned by the relative test: its grid envelope is trustworthy even for a legitimately
                // small setback floor (18 NORTH at 5,320), so only the absolute floor applies to it.
                bool noGridCrossCheck = r.Basis is AreaBasis.PocheOnly or AreaBasis.Unresolved;
                if (r.RepLevel.HasValue && r.Basis != AreaBasis.GridConfirmed && nearMedian > 0
                    && (area < DegenerateFloorSqFt || (noGridCrossCheck && area < 0.5 * nearMedian)))
                {
                    notes.Add($"  ! {r.Label}: area {area:N0} sqft implausible vs nearest-level {(TowerOf(r.Label) ?? "tower")} peers {nearMedian:N0} — substituting (FLAG: degenerate locate box).");
                    area = nearMedian; degenerate = true;
                }
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
