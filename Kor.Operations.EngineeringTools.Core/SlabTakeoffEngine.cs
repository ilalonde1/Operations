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

    /// <summary>Why a plate could not be priced and is therefore NOT in the total — the honest residual the
    /// three-phase pipeline owes alongside the answer. <see cref="ResidualKind.AreaUnresolved"/>: no grid and
    /// AI could not locate the plate; <see cref="ResidualKind.ThicknessUnresolved"/>: the plate measured but no
    /// thickness callout, sibling or same-class peer gives it a depth. <see cref="AreaSqFt"/> carries whatever
    /// partial measure exists (the area, for a thickness residual) so the human can finish it by hand.</summary>
    public enum ResidualKind { AreaUnresolved, ThicknessUnresolved }
    public sealed record SlabResidual(string Label, ResidualKind Kind, double? AreaSqFt, string Note);

    /// <summary>The whole-building suspended-slab takeoff: the priced/reconciled plates, the rendered
    /// xlsx, the totals, the human-readable measurement trace (what the CLI used to print), the
    /// SYNOPSIS — every priced plate the diligence engine could not fully trust, which the app surfaces as the
    /// "unsure areas (orange)" panel — and the RESIDUAL: the plates nobody could resolve at all, excluded from
    /// the total and listed so the answer is honest about what it does NOT cover (never a silently dropped floor).</summary>
    public sealed record SlabTakeoffResult(
        PlanEstimateResult Estimate,
        byte[] Xlsx,
        double TotalConcreteCuYd,
        double TotalRebarLb,
        IReadOnlyList<string> Notes,
        IReadOnlyList<PlateEstimate> Synopsis,
        IReadOnlyList<SlabResidual> Residual);

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

        /// <summary>The structural CLASS a plate belongs to for a thickness peer-estimate: its tower cardinal
        /// if it has one, else the leading letters of its level token (a parkade "P3"→"P", a roof "ROOF"). A
        /// missing field-thickness is estimated only from the SAME class — a parkade mat is judged by other
        /// parkade levels, never by a thin tower floor — so the substitute depth is structurally plausible.</summary>
        private static string ClassKey(RawPlate r)
        {
            var tw = TowerOf(r.Label);
            if (tw != null) return tw;
            var m = System.Text.RegularExpressions.Regex.Match(r.LevelBase ?? "", "[A-Za-z]+");
            return m.Success ? m.Value.ToUpperInvariant() : "?";
        }

        private static double Median(IEnumerable<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }

        // How many times a still-failing AI unknown is re-asked (with grounded feedback) before it is left as
        // an honest orange residual — 1 first pass + up to 2 honing passes.
        private const int MaxAiPasses = 3;

        /// <summary>The expected plate size for <paramref name="r"/> from the resolved floors within a few
        /// levels of it in the SAME tower — the deterministic sanity bound a phase-2 AI locate hones against
        /// (a setback floor is judged by its setback neighbours, a podium floor by its podium neighbours).
        /// 0 when the plate has no near, resolved, grid-backed neighbours to judge by.</summary>
        private static double NearLevelMedian(RawPlate r, IReadOnlyList<RawPlate> all)
        {
            if (!r.RepLevel.HasValue) return 0;
            string? tw = TowerOf(r.Label);
            var near = all.Where(x => !ReferenceEquals(x, r) && x.RepLevel.HasValue
                             && Math.Abs(x.RepLevel!.Value - r.RepLevel!.Value) <= 3
                             && TowerOf(x.Label) == tw && !x.NeedsLocate && x.Area >= DegenerateFloorSqFt)
                            .Select(x => x.Area).ToList();
            return near.Count > 0 ? Median(near) : 0;
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

        // Words a page's text layer yields — split each digest line on whitespace. The cheap signal that
        // separates a vector drawing (hundreds–thousands of words: grids, dims, callouts) from a flattened
        // image page (≈0).
        private static int WordsOnPage(PageDigest p) =>
            p.Lines.Sum(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

        /// <summary>Cheap, no-AI readability verdict for a built digest — does this set carry the vector text
        /// the takeoff needs, or is it a scanned/flattened image set the tool is blind to? Public so a host
        /// can pre-check a bid PDF before committing to a full run.</summary>
        public static PdfReadability AssessReadability(DrawingDigest digest)
        {
            ArgumentNullException.ThrowIfNull(digest);
            return PdfReadabilityAssessor.Assess(digest.Pages.Select(WordsOnPage).ToList());
        }

        /// <summary>Build the digest and assess readability in one cheap step (text only — no raster, no AI).</summary>
        public static PdfReadability AssessReadability(string pdfPath, int? firstPage = null, int? lastPage = null)
            => AssessReadability(DrawingDigestBuilder.Build(pdfPath, firstPage, lastPage));

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

            // PRE-FLIGHT READABILITY — refuse a scanned/flattened set NOW, before any raster or AI spend, so
            // the tool never bluffs a number off a drawing it cannot actually read. (Costs nothing: the text
            // layer is already in the digest.)
            var readability = AssessReadability(tkDigest);
            if (!readability.Readable)
                throw new PdfNotReadableException(readability.Reason);

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

                // (a) LOCATE — no readable grid: ask AI for the plate box, MEASURE the poché, and HONE. The
                //     deterministic measurement is the ground truth a re-pass corrects against: if the box
                //     measured far from the plate's nearest-level neighbours, tell the model exactly that and
                //     ask again (≤ MaxAiPasses). Keep the best attempt; converge within band → trust, else flag.
                if (p.NeedsLocate && page != null)
                {
                    var (iw, ih) = raster.ImageSize(p.Png);
                    string pj = JsonSerializer.Serialize(page, new JsonSerializerOptions { WriteIndented = false });
                    double expect = NearLevelMedian(p, tkRaw);
                    (int X0, int Y0, int X1, int Y1, double Area, double Fill, int Clusters)? best = null;
                    double bestErr = double.MaxValue;
                    string? feedback = null;
                    for (int pass = 0; pass < MaxAiPasses; pass++)
                    {
                        try
                        {
                            using var ld = JsonDocument.Parse(await vision.LocatePlateAsync(pj, raster.LoadDownscaledPng(p.Png, 1600), ct, feedback));
                            if (!ld.RootElement.TryGetProperty("slabBox", out var sbx) || sbx.ValueKind != JsonValueKind.Array) break;
                            var bb = sbx.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToList();
                            if (bb.Count < 4) break;
                            int bx0 = Math.Clamp((int)(Math.Min(bb[0], bb[2]) * iw), 0, iw - 1), by0 = Math.Clamp((int)(Math.Min(bb[1], bb[3]) * ih), 0, ih - 1);
                            int bx1 = Math.Clamp((int)(Math.Max(bb[0], bb[2]) * iw), bx0 + 1, iw), by1 = Math.Clamp((int)(Math.Max(bb[1], bb[3]) * ih), by0 + 1, ih);
                            var (area, fill, clusters) = MeasurePoche(raster, p.Png, bx0, by0, bx1, by1, tkMpp);
                            double err = expect > 0 ? Math.Abs(area - expect) / expect : (area >= 500 ? 0 : 1);
                            if (area >= 500 && err < bestErr) { best = (bx0, by0, bx1, by1, area, fill, clusters); bestErr = err; }
                            if (area >= 500 && (expect <= 0 || err <= 0.4)) break;   // converged within band (or no peer to judge by)
                            feedback = expect > 0
                                ? $"Your bounding box measured about {area:N0} sqft of slab, but the floors within a few levels of this one measure about {expect:N0} sqft, so this plate should be a similar size. Your box was {(area < expect ? "TOO SMALL — it grabbed only part of the plate" : "TOO LARGE — it grabbed neighbouring plans, schedules or the title block")}. Return a corrected box around the WHOLE floor-slab plate."
                                : $"Your bounding box measured only about {area:N0} sqft, which is implausibly small for a floor slab. Return a box around the WHOLE floor-slab plate, excluding the title block and notes.";
                        }
                        catch (Exception ex) { notes.Add($"  ! {p.Label}: AI locate pass {pass + 1} failed: {ex.Message}"); break; }
                    }
                    if (best is { } b)
                    {
                        var consensus = SlabAreaReconciler.Reconcile(null, tkScaleDenom, b.Area);   // no grid → poché stands alone
                        p.Area = consensus.AreaSqFt; p.FillRatio = b.Fill; p.ClusterCount = b.Clusters;
                        p.Poche = b.Area; p.Basis = consensus.Basis; p.AreaFlags = consensus.Flags;
                        p.Cx0 = b.X0; p.Cy0 = b.Y0; p.Cx1 = b.X1; p.Cy1 = b.Y1; p.HasBox = true; p.NeedsLocate = false;
                        notes.Add($"  ⤷ {p.Label}: AI located plate → {b.Area:N0} sqft" +
                                  (expect > 0 ? $" (peers ~{expect:N0}; {(bestErr <= 0.4 ? "converged" : $"best of {MaxAiPasses}, still off — flagged")})." : "."));
                    }
                    else if (expect > 0)
                    {
                        // AI could not locate the plate after honing, but it is a KNOWN floor whose nearest-level
                        // neighbours are resolved — estimate its area from them (flagged orange) rather than drop
                        // a real floor. Uses the plate's own neighbours, never the answer key; the human verifies.
                        p.Area = expect; p.Basis = AreaBasis.PocheOnly; p.HasBox = false; p.NeedsLocate = false;
                        p.AreaFlags = new[] { new PlanFlag(PlanFlagSeverity.Review, "AREA_ESTIMATED_PEERS",
                            $"AI could not locate this plate; its area was estimated from its nearest-level neighbours ({expect:N0} sqft) — verify against the drawing.") };
                        notes.Add($"  ⤷ {p.Label}: AI could not locate — area ESTIMATED from nearest-level peers ({expect:N0} sqft, flagged).");
                    }
                    else notes.Add($"  ! {p.Label}: AI could not locate a plausible plate and has no near peers — residual unknown.");
                }

                // (b) THICKNESS SPLIT — drop bands: ask the area % per called-out thickness, then HONE. A
                //     thickened transfer plate's effective depth MUST exceed its field slab (the drawing calls
                //     out deeper bands); if the split came back at/below the field, the model missed the bands —
                //     re-ask, naming the deeper callouts to find (≤ MaxAiPasses). Grounded in the drawing's own
                //     callouts, never the answer key; converge above field → trust, else keep best + flag.
                if (p.NeedsThicknessSplit && p.HasBox && p.Callouts.Count >= 2)
                {
                    int field = p.Callouts.Min();
                    double bestEff = 0;
                    string? feedback = null;
                    for (int pass = 0; pass < MaxAiPasses; pass++)
                    {
                        try
                        {
                            var cropPng = raster.LoadCropPng(p.Png, p.Cx0, p.Cy0, p.Cx1, p.Cy1, 1200);
                            using var sd = JsonDocument.Parse(await vision.ApportionThicknessAsync(cropPng, p.Callouts, ct, feedback));
                            double eff = EffectiveFromSplit(sd.RootElement, p.Callouts);
                            if (eff > bestEff) bestEff = eff;
                            if (eff > field + 0.5) break;   // found real thickening → accept
                            feedback = $"Your split put almost all of the area at the thin {field}\" field slab and missed the thickened zones. The drawing EXPLICITLY calls out deeper bands at {string.Join("\", ", p.Callouts.Where(c => c > field))}\" on THIS plate — find those hatched/outlined thickened regions and give each a realistic share of the plan area. The effective depth must come out greater than {field}\".";
                        }
                        catch (Exception ex) { notes.Add($"  ! {p.Label}: AI apportion pass {pass + 1} failed: {ex.Message}"); break; }
                    }
                    if (bestEff > 0)
                    {
                        p.ZonedThk = bestEff; p.NeedsThicknessSplit = false;
                        notes.Add($"  ⤷ {p.Label}: AI apportioned bands [{string.Join("/", p.Callouts)}\"] → effective {bestEff:N1}\"" +
                                  (bestEff > field + 0.5 ? "." : $" (≈ field after {MaxAiPasses} passes — flagged)."));
                    }
                    else notes.Add($"  ! {p.Label}: AI thickness split unusable — kept field thickness (flagged).");
                }
            }

            // (c) THICKNESS — plate measured, but the field-slab reader found NO callout (a parkade mat, a
            //     slab-on-grade, a podium raft — depths stated in forms that reader deliberately skips). This
            //     is resolved deterministically, NOT by AI: the invariant is that a thickness never originates
            //     from the model, only from exact text or a real peer. Two ordered passes so a peer-estimate
            //     sees every text-recovered sibling: (1) re-read each plate's own pooled text with the wider
            //     mat/SOG recogniser; (2) anything still missing → the nearest same-class resolved plate's
            //     depth (flagged orange); (3) no peer at all → an honest residual the pricing pass drops.
            var thkUnknowns = tkRaw.Where(p => p.NeedsThickness).ToList();
            if (thkUnknowns.Count > 0) notes.Add($"phase 2c: {thkUnknowns.Count} plate(s) missing a field-thickness callout → deterministic recovery.");

            double PeerClassThickness(RawPlate r)
            {
                string cls = ClassKey(r);
                var same = tkRaw.Where(x => !ReferenceEquals(x, r) && x.Thk > 0 && ClassKey(x) == cls).ToList();
                if (same.Count == 0) return 0;
                // Nearest level within the class when both are levelled; else the class's modal depth.
                if (r.RepLevel.HasValue && same.Any(x => x.RepLevel.HasValue))
                    return same.Where(x => x.RepLevel.HasValue)
                               .OrderBy(x => Math.Abs(x.RepLevel!.Value - r.RepLevel!.Value)).First().Thk;
                return same.GroupBy(x => x.Thk).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            }

            foreach (var p in thkUnknowns)
            {
                ct.ThrowIfCancellationRequested();
                var pool = tkPooled.TryGetValue(p.Key, out var pl) ? pl : (IEnumerable<string>)Array.Empty<string>();
                if (SlabThicknessReader.RecoverStructuralDepthIn(pool) is int rt && rt > 0)
                {
                    p.Thk = rt; p.NeedsThickness = false;
                    notes.Add($"  ⤷ {p.Label}: thickness {rt}\" recovered from a mat/SOG/raft callout the field-slab reader skips.");
                }
            }
            foreach (var p in thkUnknowns.Where(p => p.NeedsThickness))
            {
                double peer = PeerClassThickness(p);
                if (peer > 0)
                {
                    p.Thk = peer; p.NeedsThickness = false;
                    p.AreaFlags = p.AreaFlags.Concat(new[] { new PlanFlag(PlanFlagSeverity.Review, "THICKNESS_ESTIMATED_PEERS",
                        $"No slab-thickness callout on this plate; depth estimated from the nearest {ClassKey(p)} plate ({peer:N0}\") — verify against the drawing.") }).ToArray();
                    notes.Add($"  ⤷ {p.Label}: no thickness callout — depth ESTIMATED {peer:N0}\" from nearest {ClassKey(p)} peer (flagged).");
                }
                else notes.Add($"  ! {p.Label}: no thickness callout and no same-class peer — RESIDUAL unknown, excluded from concrete.");
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
            // this one (shared with the phase-2 locate honing). A whole-tower median is podium-dominated, so
            // it both mis-condemns a legitimately small SETBACK floor and would substitute a wrong (too-large)
            // value. Falls back to the tower median when a floor has no near neighbours.
            double NearPeerMedian(RawPlate r)
            {
                double near = NearLevelMedian(r, tkRaw);
                return near > 0 ? near : PeerMedianFor(r);
            }

            var tkPlates = new List<MeasuredPlate>();
            var residual = new List<SlabResidual>();
            foreach (var r in tkRaw)
            {
                // A plate whose area nobody could resolve (AI locate failed) is the honest residual — list it,
                // exclude it from the total, never invent a number for it.
                if (r.Area <= 0 || r.NeedsLocate)
                {
                    notes.Add($"  ! {r.Label}: area unresolved (no grid, AI could not locate) — RESIDUAL unknown, excluded from total.");
                    residual.Add(new SlabResidual(r.Label, ResidualKind.AreaUnresolved, null,
                        "No structural grid and AI could not locate the plate — area unresolved, excluded from the total."));
                    continue;
                }

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
                if (thk <= 0)
                {
                    notes.Add($"  ! {r.Label}: no thickness for level {r.LevelBase} (no callout, no sibling) — RESIDUAL unknown, excluded from concrete.");
                    residual.Add(new SlabResidual(r.Label, ResidualKind.ThicknessUnresolved, r.Area,
                        $"Area {r.Area:N0} sqft measured, but no thickness callout, sibling or same-class peer gives a depth — excluded from concrete."));
                    continue;
                }

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

            return new SlabTakeoffResult(tkResult, xlsx, tkResult.TotalConcreteCuYd, tkComputed.TotalRebarWeight, notes, synopsis, residual);
        }
    }
}
