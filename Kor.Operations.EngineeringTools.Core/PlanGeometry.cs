#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Pure pixel-geometry primitives for measuring quantities off a rasterized structural
    /// plan — the deterministic "Layer 1" of the stickfile → takeoff pipeline. No image or PDF
    /// dependency: callers decode a page (or crop) to luminance / RGB byte buffers and pass them
    /// in, which keeps this engine cross-platform and unit-testable with synthetic arrays.
    ///
    /// Validated 2026-06-27 against the Coronation Heights Tower 1 stickfile: the typical tower
    /// "concrete outline" plate measured 9,597 sq.ft from the drawing vs 9,800 in the engineer's
    /// QTO (−2.1%), with the elevator/stair core void excluded automatically (it is dark-hatched,
    /// so the interior-light fill skips it). Wall/column gray footprint measured +3.8%.
    ///
    /// The two primitives mirror how a careful estimator reads a plan:
    ///   • <see cref="MeasureEnclosedArea"/> — flood-fill the area enclosed by the heaviest closed
    ///     contour (the drawn concrete outline) → slab / plate area.
    ///   • <see cref="MeasureGrayFootprint"/> — area of the solid-gray-filled regions (walls and
    ///     columns are gray-filled on the outline) → vertical-element footprint (× storey height).
    /// </summary>
    public static class PlanGeometry
    {
        /// <summary>Result of an enclosed-area flood-fill, in pixels.</summary>
        /// <remarks>
        /// <see cref="InteriorLightPx"/> is the blank enclosed area (core voids already excluded,
        /// being dark-hatched). <see cref="InteriorDarkPx"/> is the enclosed text / hatch / outline
        /// band. The true plate area is bracketed by [Lower..Upper]; <see cref="InteriorLightPx"/>
        /// alone is the robust, slightly-conservative estimator validated on Coronation.
        /// </remarks>
        public readonly record struct EnclosedArea(long InteriorLightPx, long InteriorDarkPx)
        {
            /// <summary>Conservative area (blank slab only) — the validated estimator.</summary>
            public long LowerPx => InteriorLightPx;

            /// <summary>Upper bracket (blank + enclosed dark: text, hatch, outline band).</summary>
            public long UpperPx => InteriorLightPx + InteriorDarkPx;
        }

        /// <summary>
        /// Flood-fills the exterior over light pixels seeded from the <i>entire</i> border, then
        /// reports the enclosed region split into light (blank slab) and dark (text / hatch /
        /// outline) pixels. Seeding from the whole border — not one corner — is essential: grid
        /// lines partition the margin into compartments that a single-seed fill cannot cross.
        /// </summary>
        /// <param name="luminance">Row-major 8-bit luminance of the crop, length = width*height.</param>
        /// <param name="darkThreshold">
        /// Luminance below this is "dark structure". ~110 keeps the heavy concrete outline while
        /// dropping thin gray grid lines so they neither wall off the fill nor get counted.
        /// </param>
        /// <param name="sealHairlineGaps">
        /// Dilate the dark mask by 1px before filling, to seal anti-aliased hairline gaps in the
        /// outline that would otherwise leak the exterior fill into the slab interior.
        /// </param>
        public static EnclosedArea MeasureEnclosedArea(
            ReadOnlySpan<byte> luminance,
            int width,
            int height,
            int darkThreshold = 110,
            bool sealHairlineGaps = true)
        {
            ValidateBuffer(luminance.Length, width, height, nameof(luminance));

            var (dark, exterior) = ComputeDarkAndExterior(luminance, width, height, darkThreshold, sealHairlineGaps);

            long light = 0, darkInside = 0;
            for (int i = 0; i < dark.Length; i++)
            {
                if (exterior[i]) continue;
                if (dark[i]) darkInside++;
                else light++;
            }
            return new EnclosedArea(light, darkInside);
        }

        /// <summary>One enclosed light region (a candidate plate): its blank-pixel count and bounds.</summary>
        public readonly record struct EnclosedRegion(long LightPx, int MinX, int MinY, int MaxX, int MaxY)
        {
            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
        }

        /// <summary>
        /// Segments the crop into connected-component enclosed light regions, returned largest-first.
        /// NOTE: grid lines partition a single plate into one region per bay, so the largest region is
        /// only a bay, not the plate — callers wanting a whole plate from a loose/coarse vision box
        /// should use <see cref="MeasureEnclosedClusters"/>, which re-groups a plate's bays. This
        /// lower-level primitive exposes the raw regions (and is what the cluster pass builds on).
        /// A dark-hatched core void is an interior hole and does not create a region.
        /// </summary>
        /// <param name="minPixels">Drop components smaller than this (noise, dimension pockets).</param>
        public static IReadOnlyList<EnclosedRegion> MeasureEnclosedRegions(
            ReadOnlySpan<byte> luminance,
            int width,
            int height,
            int darkThreshold = 110,
            bool sealHairlineGaps = true,
            long minPixels = 1)
        {
            ValidateBuffer(luminance.Length, width, height, nameof(luminance));

            var (dark, exterior) = ComputeDarkAndExterior(luminance, width, height, darkThreshold, sealHairlineGaps);
            int n = width * height;
            var visited = new bool[n];
            var regions = new List<EnclosedRegion>();
            var q = new Queue<int>();

            for (int s = 0; s < n; s++)
            {
                if (exterior[s] || dark[s] || visited[s]) continue;
                visited[s] = true;
                q.Enqueue(s);
                long count = 0;
                int minX = width, minY = height, maxX = 0, maxY = 0;
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    count++;
                    int x = i % width, y = i / width;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    Visit(i - 1, x > 0);
                    Visit(i + 1, x < width - 1);
                    Visit(i - width, y > 0);
                    Visit(i + width, y < height - 1);

                    void Visit(int j, bool inBounds)
                    {
                        if (inBounds && !exterior[j] && !dark[j] && !visited[j])
                        {
                            visited[j] = true;
                            q.Enqueue(j);
                        }
                    }
                }
                if (count >= minPixels)
                    regions.Add(new EnclosedRegion(count, minX, minY, maxX, maxY));
            }

            // Largest-first; break ties by position (top-most, then left-most, then bottom/right) so
            // the ordering is a total order — callers that take [0] get a deterministic result even
            // when two disjoint regions share area and top-left corner.
            regions.Sort((a, b) =>
            {
                int c = b.LightPx.CompareTo(a.LightPx);
                if (c != 0) return c;
                c = a.MinY.CompareTo(b.MinY); if (c != 0) return c;
                c = a.MinX.CompareTo(b.MinX); if (c != 0) return c;
                c = a.MaxY.CompareTo(b.MaxY); return c != 0 ? c : a.MaxX.CompareTo(b.MaxX);
            });
            return regions;
        }

        /// <summary>One plate reconstructed from its grid-bay fragments: total blank area and bounds.</summary>
        public readonly record struct EnclosedCluster(long LightPx, int MinX, int MinY, int MaxX, int MaxY, int RegionCount)
        {
            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
        }

        /// <summary>
        /// Groups the enclosed light regions into plates and returns them largest-first. Grid lines
        /// fragment one plate into many bays (<see cref="MeasureEnclosedRegions"/> returns each bay
        /// separately), so the largest single bay badly undercounts; the total over ALL regions
        /// over-counts when a coarse crop box also clips a neighbouring plate. Clustering resolves
        /// both: regions whose bounding boxes lie within <paramref name="mergeGapPx"/> of each other
        /// are united (a plate's bays touch across the ~1px grid line), while a neighbouring plate —
        /// separated by a wide exterior gap that contains no regions — forms its own cluster. The
        /// largest cluster's summed area is the target plate, robust to a loose or two-plate box.
        /// Unlike morphological opening this never erodes the heavy outline, so it cannot leak.
        /// </summary>
        /// <param name="mergeGapPx">Max gap between region bounding boxes to treat them as one plate.</param>
        /// <param name="minPixels">Drop components smaller than this before clustering (noise).</param>
        public static IReadOnlyList<EnclosedCluster> MeasureEnclosedClusters(
            ReadOnlySpan<byte> luminance,
            int width,
            int height,
            int darkThreshold = 110,
            bool sealHairlineGaps = true,
            long minPixels = 1,
            int mergeGapPx = 6)
        {
            var regions = MeasureEnclosedRegions(luminance, width, height, darkThreshold, sealHairlineGaps, minPixels);
            int m = regions.Count;
            if (m == 0) return Array.Empty<EnclosedCluster>();

            // Union-find: merge two regions whose bounding boxes, expanded by mergeGapPx, overlap.
            var parent = new int[m];
            for (int i = 0; i < m; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

            int g = mergeGapPx;
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                {
                    var a = regions[i]; var b = regions[j];
                    bool near = a.MinX - g <= b.MaxX && b.MinX - g <= a.MaxX
                             && a.MinY - g <= b.MaxY && b.MinY - g <= a.MaxY;
                    if (near) Union(i, j);
                }

            var byRoot = new Dictionary<int, EnclosedCluster>();
            for (int i = 0; i < m; i++)
            {
                int root = Find(i);
                var r = regions[i];
                if (byRoot.TryGetValue(root, out var c))
                    byRoot[root] = new EnclosedCluster(
                        c.LightPx + r.LightPx,
                        Math.Min(c.MinX, r.MinX), Math.Min(c.MinY, r.MinY),
                        Math.Max(c.MaxX, r.MaxX), Math.Max(c.MaxY, r.MaxY),
                        c.RegionCount + 1);
                else
                    byRoot[root] = new EnclosedCluster(r.LightPx, r.MinX, r.MinY, r.MaxX, r.MaxY, 1);
            }

            var clusters = new List<EnclosedCluster>(byRoot.Values);
            clusters.Sort((a, b) =>
            {
                int c = b.LightPx.CompareTo(a.LightPx);
                if (c != 0) return c;
                c = a.MinY.CompareTo(b.MinY); if (c != 0) return c;
                c = a.MinX.CompareTo(b.MinX); if (c != 0) return c;
                c = a.MaxY.CompareTo(b.MaxY); return c != 0 ? c : a.MaxX.CompareTo(b.MaxX);
            });
            return clusters;
        }

        /// <summary>A slab-thickness callout placed on the plan, in this crop's pixel coordinates.</summary>
        public readonly record struct CalloutPx(double X, double Y, int Value);

        /// <summary>
        /// Splits the enclosed-light slab area by thickness ZONE. A floor is rarely one thickness — a
        /// tower plate is large "8&quot; SLAB" wings plus "12&quot; SLAB" zones at the core/transfer bays — yet
        /// pricing it at a single modal thickness under-counts the thicker zones. The drawing tells us
        /// where each thickness governs: the «N&quot; SLAB» callout sits inside its own zone. So every
        /// enclosed-light pixel in the plate is assigned to its NEAREST callout (a Voronoi partition of
        /// the slab by its own annotations), and the per-thickness pixel areas are returned. The caller
        /// turns these into an area-weighted effective thickness. The numbers come from the exact callout
        /// TEXT; vision/geometry only says WHERE — the synthesis-led split, applied to thickness.
        /// Pass ONLY the qualifying field callouts (a genuine multi-zone floor); a lone stray callout
        /// must be filtered out upstream, or it claims a Voronoi cell it does not own.
        /// </summary>
        /// <param name="callouts">Qualifying thickness callouts in crop pixel coords (≥1 required).</param>
        /// <param name="minX">Restrict the scan to the plate bounds (the located cluster's box).</param>
        public static IReadOnlyDictionary<int, long> ThicknessZoneFractions(
            ReadOnlySpan<byte> luminance,
            int width,
            int height,
            IReadOnlyList<CalloutPx> callouts,
            int minX,
            int minY,
            int maxX,
            int maxY,
            int darkThreshold = 110,
            bool sealHairlineGaps = true)
        {
            ValidateBuffer(luminance.Length, width, height, nameof(luminance));
            var result = new Dictionary<int, long>();
            if (callouts is null || callouts.Count == 0) return result;

            var (dark, exterior) = ComputeDarkAndExterior(luminance, width, height, darkThreshold, sealHairlineGaps);
            int x0 = Math.Max(0, minX), y0 = Math.Max(0, minY);
            int x1 = Math.Min(width - 1, maxX), y1 = Math.Min(height - 1, maxY);
            for (int y = y0; y <= y1; y++)
            {
                int row = y * width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    if (exterior[i] || dark[i]) continue;   // line-work / outside the plate — not concrete
                    double best = double.MaxValue;
                    int bestVal = callouts[0].Value;
                    for (int c = 0; c < callouts.Count; c++)
                    {
                        double dx = x - callouts[c].X, dy = y - callouts[c].Y;
                        double d = dx * dx + dy * dy;
                        if (d < best) { best = d; bestVal = callouts[c].Value; }
                    }
                    result.TryGetValue(bestVal, out var cur);
                    result[bestVal] = cur + 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Counts solid gray-filled footprint pixels. Walls and columns are filled with a light,
        /// near-neutral gray on the concrete-outline sheets; this isolates that fill from black
        /// line-work (below <paramref name="loThreshold"/>), white background (above
        /// <paramref name="hiThreshold"/>), and anything coloured (rejected by
        /// <paramref name="neutralTolerance"/>). Footprint × storey height = vertical concrete.
        /// </summary>
        /// <param name="r">Red plane of the crop, length = width*height.</param>
        /// <param name="g">Green plane of the crop.</param>
        /// <param name="b">Blue plane of the crop.</param>
        public static long MeasureGrayFootprint(
            ReadOnlySpan<byte> r,
            ReadOnlySpan<byte> g,
            ReadOnlySpan<byte> b,
            int width,
            int height,
            int loThreshold = 196,
            int hiThreshold = 228,
            int neutralTolerance = 24)
        {
            int n = width * height;
            ValidateBuffer(r.Length, width, height, nameof(r));
            if (g.Length != n) throw new ArgumentException("Green plane length mismatch.", nameof(g));
            if (b.Length != n) throw new ArgumentException("Blue plane length mismatch.", nameof(b));

            long gray = 0;
            for (int i = 0; i < n; i++)
            {
                int ri = r[i], gi = g[i], bi = b[i];
                double lum = 0.299 * ri + 0.587 * gi + 0.114 * bi;
                if (lum < loThreshold || lum > hiThreshold) continue;

                int max = Math.Max(ri, Math.Max(gi, bi));
                int min = Math.Min(ri, Math.Min(gi, bi));
                if (max - min <= neutralTolerance) gray++;
            }
            return gray;
        }

        /// <summary>One solid-gray-filled blob (a column or a length of wall): area and bounds/shape.</summary>
        public readonly record struct GrayComponent(long AreaPx, int MinX, int MinY, int MaxX, int MaxY)
        {
            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
            public long BBoxArea => (long)Width * Height;
            /// <summary>Fraction of the bounding box that is filled — a solid rectangle ≈ 1.</summary>
            public double Solidity => BBoxArea > 0 ? (double)AreaPx / BBoxArea : 0;
            /// <summary>Long-side / short-side of the bounding box — a column ≈ 1–3, a wall run is large.</summary>
            public double Elongation => Math.Max(Width, Height) / (double)Math.Max(1, Math.Min(Width, Height));
        }

        /// <summary>
        /// Connected-components of the solid-gray fill (same neutral-gray predicate as
        /// <see cref="MeasureGrayFootprint"/>), returned largest-first with bounds and shape. On a
        /// concrete-outline framing sheet the columns are discrete gray rectangles and the shear walls
        /// are gray runs, so segmenting the fill lets the caller split column vs wall footprint by
        /// shape/size rather than a hand-entered fraction. 8-connected so a fill broken by a thin
        /// dimension tick still reads as one blob.
        /// </summary>
        /// <param name="minPixels">Drop blobs smaller than this (anti-alias speckle, tick marks).</param>
        public static IReadOnlyList<GrayComponent> MeasureGrayComponents(
            ReadOnlySpan<byte> r,
            ReadOnlySpan<byte> g,
            ReadOnlySpan<byte> b,
            int width,
            int height,
            int loThreshold = 196,
            int hiThreshold = 228,
            int neutralTolerance = 24,
            long minPixels = 1)
        {
            int n = width * height;
            ValidateBuffer(r.Length, width, height, nameof(r));
            if (g.Length != n) throw new ArgumentException("Green plane length mismatch.", nameof(g));
            if (b.Length != n) throw new ArgumentException("Blue plane length mismatch.", nameof(b));

            var gray = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int ri = r[i], gi = g[i], bi = b[i];
                double lum = 0.299 * ri + 0.587 * gi + 0.114 * bi;
                if (lum < loThreshold || lum > hiThreshold) continue;
                int mx = Math.Max(ri, Math.Max(gi, bi)), mn = Math.Min(ri, Math.Min(gi, bi));
                if (mx - mn <= neutralTolerance) gray[i] = true;
            }

            var visited = new bool[n];
            var comps = new List<GrayComponent>();
            var q = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (!gray[s] || visited[s]) continue;
                visited[s] = true; q.Enqueue(s);
                long count = 0; int minX = width, minY = height, maxX = 0, maxY = 0;
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    count++;
                    int x = i % width, y = i / width;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int j = ny * width + nx;
                            if (gray[j] && !visited[j]) { visited[j] = true; q.Enqueue(j); }
                        }
                }
                if (count >= minPixels)
                    comps.Add(new GrayComponent(count, minX, minY, maxX, maxY));
            }

            comps.Sort((a, c) =>
            {
                int k = c.AreaPx.CompareTo(a.AreaPx);
                if (k != 0) return k;
                k = a.MinY.CompareTo(c.MinY); if (k != 0) return k;
                k = a.MinX.CompareTo(c.MinX); if (k != 0) return k;
                k = a.MaxY.CompareTo(c.MaxY); return k != 0 ? k : a.MaxX.CompareTo(c.MaxX);
            });
            return comps;
        }

        /// <summary>A vertical concrete element, told apart by the shape of its gray footprint.</summary>
        public enum VerticalKind { Column, Wall }

        /// <summary>
        /// Classifies a gray footprint blob as a column or a wall by shape. Columns are compact
        /// rectangles (a 18&quot;×48&quot; pier reads elongation ≈ 2.5 and a small footprint); walls are
        /// elongated runs (elongation ≥ <paramref name="elongationThreshold"/>) or simply too large in
        /// footprint to be a column (> <paramref name="maxColumnAreaPx"/>, which catches a stocky core
        /// pier). Validated on Coronation: columns measured elong 1.0–2.6, walls 5.6–36.8 — a clean gap
        /// at 4. The split is only used to pick a rebar density (wall vs column); concrete volume is the
        /// same footprint×height either way, so a mis-split is immaterial under BC-moderate (375 vs 385).
        /// Under a high-seismic profile (e.g. SD 500 vs 700) the densities diverge ~40%, so there the
        /// wall/column split should be treated as advisory and spot-checked.
        /// </summary>
        public static VerticalKind ClassifyVertical(GrayComponent c, long maxColumnAreaPx, double elongationThreshold = 4.0)
            => c.Elongation >= elongationThreshold || c.AreaPx > maxColumnAreaPx
                ? VerticalKind.Wall : VerticalKind.Column;

        /// <summary>
        /// Histogram of NEUTRAL (near-grey, |max−min| ≤ tolerance) pixels by luminance, in 16 bins of
        /// width 16. A diagnostic for discovering the actual fill tones on a sheet — e.g. light-grey
        /// columns vs a heavier-grey shear wall sit in different bins — so thresholds are chosen from
        /// evidence, not guessed.
        /// </summary>
        public static long[] NeutralLuminanceHistogram(
            ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b,
            int width, int height, int neutralTolerance = 24)
        {
            int n = width * height;
            ValidateBuffer(r.Length, width, height, nameof(r));
            if (g.Length != n) throw new ArgumentException("Green plane length mismatch.", nameof(g));
            if (b.Length != n) throw new ArgumentException("Blue plane length mismatch.", nameof(b));

            var bins = new long[16];
            for (int i = 0; i < n; i++)
            {
                int ri = r[i], gi = g[i], bi = b[i];
                int mx = Math.Max(ri, Math.Max(gi, bi)), mn = Math.Min(ri, Math.Min(gi, bi));
                if (mx - mn > neutralTolerance) continue;
                int lum = (int)(0.299 * ri + 0.587 * gi + 0.114 * bi);
                bins[Math.Clamp(lum / 16, 0, 15)]++;
            }
            return bins;
        }

        /// <summary>
        /// Length (metres) of the boundary between the EXTERIOR and the enclosed plate — the plate's
        /// outer contour, counted as exterior↔non-exterior 4-neighbour pixel edges × metres-per-pixel.
        /// On a below-grade plan this contour IS the perimeter/basement wall centreline (the wall is
        /// drawn as the plate boundary, which is exactly why the gray-fill footprint never sees it).
        /// Orthogonal walls measure near-exact; a diagonal run staircases up to ×1.41 — acceptable for
        /// a flagged estimate on predominantly rectilinear basements.
        /// </summary>
        public static double BoundaryMetres(
            ReadOnlySpan<byte> luminance, int width, int height, double metresPerPixel,
            int darkThreshold = 110, bool sealHairlineGaps = true)
        {
            ValidateBuffer(luminance.Length, width, height, nameof(luminance));
            if (metresPerPixel <= 0) throw new ArgumentOutOfRangeException(nameof(metresPerPixel));
            var (_, exterior) = ComputeDarkAndExterior(luminance, width, height, darkThreshold, sealHairlineGaps);
            int n = width * height;

            // The crop carries more than the plate — schedule tables, notes, north arrows — and every
            // one of them has a contour. Only the LARGEST connected non-exterior component is the plate;
            // measuring all contours read a parkade perimeter 5× long. Label components, keep the biggest.
            var comp = new int[n];           // 0 = unlabelled/exterior; else component id
            int nextId = 0, bestId = 0; long bestSize = 0;
            var q = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (exterior[s] || comp[s] != 0) continue;
                int id = ++nextId; long size = 0;
                comp[s] = id; q.Enqueue(s);
                while (q.Count > 0)
                {
                    int i = q.Dequeue(); size++;
                    int x = i % width, y = i / width;
                    Visit(i - 1, x > 0); Visit(i + 1, x < width - 1);
                    Visit(i - width, y > 0); Visit(i + width, y < height - 1);
                    void Visit(int j, bool inBounds)
                    { if (inBounds && !exterior[j] && comp[j] == 0) { comp[j] = id; q.Enqueue(j); } }
                }
                if (size > bestSize) { bestSize = size; bestId = id; }
            }
            if (bestId == 0) return 0;

            long edges = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if (comp[i] != bestId) continue;
                    // Count each exterior neighbour edge once (border pixels face implicit exterior).
                    if (x == 0 || exterior[i - 1]) edges++;
                    if (x == width - 1 || exterior[i + 1]) edges++;
                    if (y == 0 || exterior[i - width]) edges++;
                    if (y == height - 1 || exterior[i + width]) edges++;
                }
            }
            return edges * metresPerPixel;
        }

        /// <summary>Square metres for a pixel count at a given real-world metres-per-pixel.</summary>
        public static double SquareMetres(long pixels, double metresPerPixel)
        {
            if (metresPerPixel <= 0) throw new ArgumentOutOfRangeException(nameof(metresPerPixel));
            return pixels * metresPerPixel * metresPerPixel;
        }

        /// <summary>Square feet for a pixel count at a given real-world metres-per-pixel.</summary>
        public static double SquareFeet(long pixels, double metresPerPixel)
            => SquareMetres(pixels, metresPerPixel) * 10.763910416709722;

        /// <summary>One solid-after-closing region — a hatched concrete mat / deep footing.</summary>
        public readonly record struct HatchedRegion(long AreaPx, int MinX, int MinY, int MaxX, int MaxY)
        {
            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
            public int CentroidX => (MinX + MaxX) / 2;
            public int CentroidY => (MinY + MaxY) / 2;
        }

        /// <summary>
        /// Deterministically finds the cross-hatched concrete regions on a plan — the deep/core
        /// footings and mats drawn as closely-spaced diagonal line fill. The discriminator is LOCAL
        /// LINE DENSITY: inside a (2·<paramref name="windowRadius"/>+1)² window the cross-hatch packs
        /// many close parallel lines (high dark fraction), whereas grid lines, dimension strings and
        /// the heavy outline are sparse — so a density threshold isolates hatch from the line web that
        /// otherwise connects the whole sheet into one blob. Pixels whose windowed dark fraction ≥
        /// <paramref name="densityPercent"/>% form the hatch mask; its connected components (≥
        /// <paramref name="minPixels"/>) are the footing plan-areas — measured by geometry (repeatable)
        /// with the vision layer left only to read each one's DEPTH label. A small closing solidifies
        /// the mask first. Computed in O(n) via an integral image. Returned largest-first.
        /// </summary>
        /// <param name="windowRadius">Half-size of the density window in px (≈ a few hatch spacings).</param>
        /// <param name="densityPercent">Min % of the window that is diagonal-hatch to count (≈ 8–20).</param>
        /// <param name="minPixels">Drop blobs below this (text/dimension clutter, tick clusters).</param>
        /// <param name="maxRun">Dark runs longer than this (px) are orthogonal lines (grid/dim/outline),
        /// removed before the density test so only the short-run diagonal hatch is measured.</param>
        public static IReadOnlyList<HatchedRegion> MeasureHatchedRegions(
            ReadOnlySpan<byte> luminance,
            int width,
            int height,
            int darkThreshold = 110,
            int windowRadius = 10,
            double densityPercent = 12.0,
            long minPixels = 2000,
            int maxRun = 14)
        {
            ValidateBuffer(luminance.Length, width, height, nameof(luminance));
            int n = width * height;

            var dark = new bool[n];
            for (int i = 0; i < n; i++) dark[i] = luminance[i] < darkThreshold;

            // Orientation filter: keep a dark pixel only if BOTH its horizontal and vertical dark-run
            // lengths are short. A diagonal hatch line crosses each row/column in a few px (short runs);
            // a grid line, dimension string, or footing outline is a long orthogonal run (removed).
            var diag = new bool[n];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width;)
                {
                    if (!dark[row + x]) { x++; continue; }
                    int x2 = x; while (x2 < width && dark[row + x2]) x2++;
                    if (x2 - x <= maxRun) for (int xx = x; xx < x2; xx++) diag[row + xx] = true;
                    x = x2;
                }
            }
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height;)
                {
                    if (!dark[y * width + x]) { y++; continue; }
                    int y2 = y; while (y2 < height && dark[y2 * width + x]) y2++;
                    if (y2 - y > maxRun) for (int yy = y; yy < y2; yy++) diag[yy * width + x] = false; // long vertical run → drop
                    y = y2;
                }

            // Integral image of the diagonal-hatch mask for O(1) windowed density.
            int W1 = width + 1;
            var ii = new int[W1 * (height + 1)];
            for (int y = 0; y < height; y++)
            {
                int rowAbove = y * W1, row = (y + 1) * W1, baseL = y * width;
                int runSum = 0;
                for (int x = 0; x < width; x++)
                {
                    runSum += diag[baseL + x] ? 1 : 0;
                    ii[row + x + 1] = ii[rowAbove + x + 1] + runSum;
                }
            }

            int r = Math.Max(1, windowRadius);
            double frac = Math.Clamp(densityPercent, 0, 100) / 100.0;
            var mask = new bool[n];
            for (int y = 0; y < height; y++)
            {
                int y1 = Math.Max(0, y - r), y2 = Math.Min(height - 1, y + r);
                int rowOut = y * width;
                for (int x = 0; x < width; x++)
                {
                    int x1 = Math.Max(0, x - r), x2 = Math.Min(width - 1, x + r);
                    int win = (x2 - x1 + 1) * (y2 - y1 + 1);
                    int sum = ii[(y2 + 1) * W1 + (x2 + 1)] - ii[y1 * W1 + (x2 + 1)]
                            - ii[(y2 + 1) * W1 + x1] + ii[y1 * W1 + x1];
                    if (sum >= frac * win) mask[rowOut + x] = true;
                }
            }
            // Solidify: a single closing pass fills the thin within-hatch dips so each region is one blob.
            mask = Dilate(mask, width, height);
            mask = Erode(mask, width, height);

            var visited = new bool[n];
            var regions = new List<HatchedRegion>();
            var q = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (!mask[s] || visited[s]) continue;
                visited[s] = true; q.Enqueue(s);
                long count = 0; int minX = width, minY = height, maxX = 0, maxY = 0;
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    count++;
                    int x = i % width, y = i / width;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int j = ny * width + nx;
                            if (mask[j] && !visited[j]) { visited[j] = true; q.Enqueue(j); }
                        }
                }
                if (count >= minPixels)
                    regions.Add(new HatchedRegion(count, minX, minY, maxX, maxY));
            }

            regions.Sort((a, c) =>
            {
                int k = c.AreaPx.CompareTo(a.AreaPx);
                if (k != 0) return k;
                k = a.MinY.CompareTo(c.MinY); if (k != 0) return k;
                return a.MinX.CompareTo(c.MinX);
            });
            return regions;
        }

        // 4-neighbour erosion: a set pixel survives only if itself and all in-bounds orthogonal
        // neighbours are set. Out-of-bounds neighbours count as background (the border erodes inward).
        private static bool[] Erode(bool[] mask, int width, int height)
        {
            var outMask = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if (!mask[i]) continue;
                    bool keep = x > 0 && mask[i - 1]
                             && x < width - 1 && mask[i + 1]
                             && y > 0 && mask[i - width]
                             && y < height - 1 && mask[i + width];
                    if (keep) outMask[i] = true;
                }
            }
            return outMask;
        }

        // ── scale resolution ────────────────────────────────────────────────────────────────
        // Architectural scale: fractional inch (1/8"=1'-0", 3/16" = 1'-0") or whole inch
        // (1"=20'-0", common on foundation/site sheets). The "/denominator" is optional.
        private static readonly Regex ArchScale = new(
            @"(?<pn>\d+)\s*(?:/\s*(?<pd>\d+))?\s*""?\s*=\s*(?<ft>\d+)\s*'\s*(?:-\s*)?(?<in>\d+)?\s*""?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Engineering ratio scale, e.g. 1:100, 1:50.
        private static readonly Regex RatioScale = new(
            @"\b1\s*:\s*(?<n>\d+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const double FeetToMetres = 0.3048;
        private const double InchToMetres = 0.0254;

        /// <summary>
        /// Real-world metres represented by one rendered pixel, from a title-block scale note and
        /// the raster DPI the page was rendered at. Supports architectural notes ("1/8&quot;=1'-0&quot;")
        /// and engineering ratios ("1:100"). Returns null if the note cannot be parsed — the caller
        /// then falls back to grid-spacing calibration rather than guessing.
        /// </summary>
        public static double? MetresPerPixel(string? scaleNote, double renderDpi)
        {
            if (string.IsNullOrWhiteSpace(scaleNote) || renderDpi <= 0) return null;

            var arch = ArchScale.Match(scaleNote);
            if (arch.Success)
            {
                double pn = double.Parse(arch.Groups["pn"].Value, CultureInfo.InvariantCulture);
                double pd = arch.Groups["pd"].Success
                    ? double.Parse(arch.Groups["pd"].Value, CultureInfo.InvariantCulture)
                    : 1.0;
                if (pn <= 0 || pd <= 0) return null;
                double paperInches = pn / pd;
                double realFeet = double.Parse(arch.Groups["ft"].Value, CultureInfo.InvariantCulture)
                    + (arch.Groups["in"].Success && arch.Groups["in"].Value.Length > 0
                        ? double.Parse(arch.Groups["in"].Value, CultureInfo.InvariantCulture) / 12.0
                        : 0.0);
                if (paperInches <= 0 || realFeet <= 0) return null;
                double feetPerPaperInch = realFeet / paperInches;
                return feetPerPaperInch * FeetToMetres / renderDpi;
            }

            var ratio = RatioScale.Match(scaleNote);
            if (ratio.Success)
            {
                double factor = double.Parse(ratio.Groups["n"].Value, CultureInfo.InvariantCulture);
                if (factor <= 0) return null;
                // 1 paper inch represents <factor> real inches.
                return factor * InchToMetres / renderDpi;
            }

            return null;
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────
        // Builds the dark-structure mask (optionally gap-sealed) and flood-fills the exterior over
        // light pixels from the whole border. Shared by MeasureEnclosedArea/MeasureEnclosedRegions.
        private static (bool[] dark, bool[] exterior) ComputeDarkAndExterior(
            ReadOnlySpan<byte> luminance, int width, int height, int darkThreshold, bool sealHairlineGaps)
        {
            int n = width * height;
            var dark = new bool[n];
            for (int i = 0; i < n; i++)
                if (luminance[i] < darkThreshold) dark[i] = true;

            if (sealHairlineGaps)
                dark = Dilate(dark, width, height);

            var exterior = new bool[n];
            var queue = new Queue<int>();
            void Seed(int i)
            {
                if (!dark[i] && !exterior[i]) { exterior[i] = true; queue.Enqueue(i); }
            }
            for (int x = 0; x < width; x++) { Seed(x); Seed((height - 1) * width + x); }
            for (int y = 0; y < height; y++) { Seed(y * width); Seed(y * width + width - 1); }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % width;
                int y = i / width;
                if (x > 0) Visit(i - 1);
                if (x < width - 1) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y < height - 1) Visit(i + width);

                void Visit(int j)
                {
                    if (!dark[j] && !exterior[j]) { exterior[j] = true; queue.Enqueue(j); }
                }
            }
            return (dark, exterior);
        }

        private static bool[] Dilate(bool[] mask, int width, int height)
        {
            var outMask = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if (mask[i]) { outMask[i] = true; continue; }
                    if ((x > 0 && mask[i - 1]) ||
                        (x < width - 1 && mask[i + 1]) ||
                        (y > 0 && mask[i - width]) ||
                        (y < height - 1 && mask[i + width]))
                        outMask[i] = true;
                }
            }
            return outMask;
        }

        private static void ValidateBuffer(int length, int width, int height, string paramName)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (length != width * height)
                throw new ArgumentException(
                    $"Buffer length {length} != width*height {width * height}.", paramName);
        }
    }
}
