#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The structural grid an estimator reads off a plan: the labelled gridlines (digits 1,2,3… across;
    /// letters A,B,C… down) whose bubbles bound the slab plate. The bubble SPAN is the plate's plan
    /// envelope — proven the most reliable AREA signal on a vector plan: it is STABLE run-to-run (the grid
    /// is fixed), unlike the raster poché which wanders, and it works where the poché leaks or the geometry
    /// is shattered. The digit labels also name the tower for free (e.g. grids 1–8 = north, 9–13 = south).
    /// </summary>
    public sealed record GridFrame(
        IReadOnlyList<string> XLabels,   // digit gridlines, left→right
        IReadOnlyList<string> YLabels,   // letter gridlines, top→bottom
        double XSpanPt,                  // bubble span across the plate, PDF points
        double YSpanPt,                  // bubble span down the plate, PDF points
        bool MultiPlan)                  // the sheet carries >1 plan (a repeated label run) — span is ONE plan's
    {
        // Absolute bubble-box extents in PDF points (y-up origin, the VectorPageReader convention). The grid
        // SPAN gives the area; the BOX gives the plate's location, so the poché can be measured inside the
        // grid-derived rectangle WITHOUT a paid AI locate call. 0 when the box was not populated (e.g. a grid
        // hand-built in a test from spans only) — callers must check IsLocatable before using it.
        public double XMinPt { get; init; }
        public double XMaxPt { get; init; }
        public double YMinPt { get; init; }
        public double YMaxPt { get; init; }

        /// <summary>The gross plate envelope in square feet at scale 1:<paramref name="scaleDenom"/>
        /// (e.g. 100 for a metric 1:100 sheet). Paper points → real units → square feet.</summary>
        public double EnvelopeSqFt(double scaleDenom)
        {
            double ftPerPt = scaleDenom * (0.0254 / 72.0) * 3.28084;   // 1 paper-pt in real feet
            return XSpanPt * YSpanPt * (ftPerPt * ftPerPt);
        }

        /// <summary>True if the grid was read with enough bubbles on both axes to trust the envelope.</summary>
        public bool IsUsable => XLabels.Count >= 2 && YLabels.Count >= 2 && XSpanPt > 0 && YSpanPt > 0;

        /// <summary>True if the absolute plate box is populated, so the poché can be cropped from the grid
        /// (the deterministic replacement for the AI locate call).</summary>
        public bool IsLocatable => IsUsable && XMaxPt > XMinPt && YMaxPt > YMinPt;
    }

    /// <summary>
    /// Reads the <see cref="GridFrame"/> off a vector plan page (Layer 1, NO AI). The bubbles are isolated
    /// from the dense plan body by three proven filters that generalise across sheet layouts: they sit in
    /// the page MARGINS, they share a consistent LARGE font (the bubble height — interior dimensions/notes
    /// are smaller), and they are COLLINEAR and roughly evenly spaced (so an off-interval stray is trimmed,
    /// and a sheet carrying two plans shows up as two clusters → <see cref="GridFrame.MultiPlan"/>).
    /// </summary>
    public static class StructuralGridReader
    {
        private static readonly Regex DigitRx = new(@"^\d{1,2}$", RegexOptions.Compiled);
        private static readonly Regex LetterRx = new(@"^[A-Z]$", RegexOptions.Compiled);
        // Circle-confirmed letter labels may carry a tower/wing suffix ("AS" = gridline A, south run) —
        // allow 1–2 letters INSIDE a bubble, where prose can't reach. The page-wide fallback keeps the
        // strict single letter (two-letter words are everywhere in open text).
        private static readonly Regex GridLetterRx = new(@"^[A-Z]{1,2}$", RegexOptions.Compiled);

        // Digit bubble rows hug the top/bottom edges; letters can be anywhere down the plan sides (an
        // L-shaped plate splits A–D and E–F across two x-columns), so letters are NOT margin-banded.
        private const double DigitMarginFy = 0.15;   // within 15% of the top OR bottom edge
        private const double TitleBlockFx = 0.95;    // ignore single letters inside the right-corner title block

        public static GridFrame? FromPage(VectorPageReader.PageContent? page)
        {
            if (page is null || page.Words.Count == 0) return null;
            double w = page.WidthPts, h = page.HeightPts;
            if (w <= 0 || h <= 0) return null;

            // ── PRIMARY: the label sits INSIDE a drawn CIRCLE — the universal bubble convention. The
            //    margin/font heuristics below are the fallback, and they are SHAPED by the sets they were
            //    built on (proven on 31065; on Coronation the "most-populated margin row" caught dimension
            //    digits [6,50] and the dominant-height letters caught stray capitals [T,N,D,E,T]). A
            //    circle-contained single label is unambiguous on any set whose bubbles survived to vector.
            //    Detail/section markers are circles too but carry TWO stacked tokens (number over sheet
            //    ref) — excluded by the one-token rule; rectangles fail the radial circle test.
            //    AXIS IS DECIDED BY THE RUN'S ORIENTATION, NEVER THE LABEL CLASS: 31065 runs digits
            //    across the top and letters down the side; Coronation runs letters ACROSS and digits
            //    DOWN. A horizontal run of bubbles labels the X axis whatever its alphabet.
            var bubbles = CircleCandidates(page.Paths);
            var digitBubbles = LabelsInBubbles(page.Words, bubbles, DigitRx);
            var letterBubbles = LabelsInBubbles(page.Words, bubbles, GridLetterRx)
                .Where(t => t.Cx / w < TitleBlockFx).ToList();

            // Best horizontal run (tokens sharing a row) and best vertical run (sharing a column),
            // each taken from whichever label class supplies more bubbles. A lone circled digit in the
            // plan body (a detail number that lost its ref) cannot join a run.
            var xTokens = Longest(LargestRun(digitBubbles, t => t.Cy, t => t.Cx),
                                  LargestRun(letterBubbles, t => t.Cy, t => t.Cx));
            var yTokens = Longest(LargestRun(digitBubbles, t => t.Cx, t => t.Cy),
                                  LargestRun(letterBubbles, t => t.Cx, t => t.Cy));
            bool xFromBubbles = xTokens.Count >= 2, yFromBubbles = yTokens.Count >= 2;
            // The two axes must not claim the SAME bubbles (a single run reads as both "best horizontal"
            // and "best vertical" when the other orientation is empty) — the wider-spread claim wins.
            if (xTokens.Count >= 2 && yTokens.Count >= 2 && xTokens.Intersect(yTokens).Any())
            {
                double xSpread = xTokens.Max(t => t.Cx) - xTokens.Min(t => t.Cx);
                double ySpread = yTokens.Max(t => t.Cy) - yTokens.Min(t => t.Cy);
                if (xSpread >= ySpread) yTokens = new List<VectorPageReader.TextToken>();
                else xTokens = new List<VectorPageReader.TextToken>();
            }

            // ── FALLBACK per axis (<2 circle-confirmed bubbles): the original margin/font read. ──
            if (xTokens.Count < 2)
            {
                var marginDigits = page.Words
                    .Where(t => DigitRx.IsMatch(t.Text.Trim()) && (t.Cy / h > 1 - DigitMarginFy || t.Cy / h < DigitMarginFy))
                    .ToList();
                xTokens = marginDigits
                    .GroupBy(t => Math.Round(t.Cy / 12.0)).OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.ToList() ?? new List<VectorPageReader.TextToken>();
                xTokens = KeepDominantHeight(xTokens);
            }
            if (yTokens.Count < 2)
            {
                var allLetters = page.Words
                    .Where(t => LetterRx.IsMatch(t.Text.Trim()) && t.Cx / w < TitleBlockFx)
                    .ToList();
                yTokens = KeepDominantHeight(allLetters);
            }

            // Bubble-sourced runs merge across a wide bay: a label sequence that CONTINUES over the gap
            // (…7,8 | 9,10… with no repeats) is one plan with a wider bay at the tower seam; only a
            // REPEATED run ([2,3,4,5 | 2,3,4,5]) is a second plan. The noisy fallback tokens keep the
            // plain gap split — merging noise would over-extend the envelope.
            var (xLabels, xSpan, xMin, xMax, xClusters) = DominantCluster(xTokens, t => t.Cx, mergeContinuations: xFromBubbles);
            // top→bottom: Cy is up-from-bottom, so order the labels by DESCENDING Cy.
            var (yLabels, ySpan, yMin, yMax, yClusters) = DominantCluster(yTokens, t => t.Cy, descendingLabels: true, mergeContinuations: yFromBubbles);

            if (xLabels.Count == 0 && yLabels.Count == 0) return null;
            return new GridFrame(xLabels, yLabels, xSpan, ySpan, MultiPlan: xClusters > 1 || yClusters > 1)
            {
                XMinPt = xMin, XMaxPt = xMax, YMinPt = yMin, YMaxPt = yMax,
            };
        }

        // Bubble geometry: a closed, circle-shaped path 8–40 pt across. The circle test is radial
        // uniformity — every path point sits near one radius from the bbox centre (a square's corners
        // sit 1.41× its edge midpoints, well outside the band).
        private const double BubbleMinDiaPt = 8, BubbleMaxDiaPt = 40;

        private static List<(double Cx, double Cy, double R)> CircleCandidates(IReadOnlyList<VectorPageReader.GeomPath> paths)
        {
            var found = new List<(double, double, double)>();
            if (paths is null) return found;
            foreach (var p in paths)
            {
                double dw = p.Width, dh = p.Height;
                if (dw < BubbleMinDiaPt || dw > BubbleMaxDiaPt || dh < BubbleMinDiaPt || dh > BubbleMaxDiaPt) continue;
                // A Bézier circle survives to vector as just its 4 on-curve anchors (+ the closing
                // repeat) — all exactly ON the radius; a rectangle's 4 corners sit at 1.41r. So the
                // radial band alone separates them, and the point-count floor stays at 4.
                if (Math.Abs(dw - dh) > 0.25 * Math.Max(dw, dh)) continue;
                if (p.Points is null || p.Points.Count < 4) continue;
                double cx = (p.MinX + p.MaxX) / 2, cy = (p.MinY + p.MaxY) / 2, r = (dw + dh) / 4;
                bool circle = true;
                foreach (var (px, py) in p.Points)
                {
                    double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    if (d < 0.75 * r || d > 1.25 * r) { circle = false; break; }
                }
                if (circle) found.Add((cx, cy, r));
            }
            return found;
        }

        // The single label token centred inside a bubble. A circle containing more than one token is a
        // detail/section marker (number over sheet reference), never a gridline — skipped whole. The
        // survivors are then filtered to the MODAL bubble diameter: one plan's gridline bubbles share a
        // size, and the miniature key-plan bubbles (⌀18 vs the main plan's ⌀32) must never join a run.
        private static List<VectorPageReader.TextToken> LabelsInBubbles(
            IReadOnlyList<VectorPageReader.TextToken> words,
            List<(double Cx, double Cy, double R)> bubbles,
            Regex labelRx)
        {
            var found = new List<(VectorPageReader.TextToken Tok, double R)>();
            foreach (var (bx, by, br) in bubbles)
            {
                VectorPageReader.TextToken? label = null;
                int inside = 0;
                foreach (var t in words)
                {
                    double dx = t.Cx - bx, dy = t.Cy - by;
                    if (dx * dx + dy * dy > br * br * 0.81) continue;   // centred within 0.9 r
                    inside++;
                    if (inside > 1) { label = null; break; }
                    if (labelRx.IsMatch(t.Text.Trim())) label = t;
                }
                if (label is { } l) found.Add((l, br));
            }
            if (found.Count == 0) return new List<VectorPageReader.TextToken>();
            double modalR = found.GroupBy(f => Math.Round(f.R))
                                 .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                                 .First().Key;
            return found.Where(f => f.R >= 0.8 * modalR && f.R <= 1.25 * modalR)
                        .Select(f => f.Tok).ToList();
        }

        private static List<VectorPageReader.TextToken> Longest(
            List<VectorPageReader.TextToken> a, List<VectorPageReader.TextToken> b) => a.Count >= b.Count ? a : b;

        // A real gridline run stretches along its axis (gridlines sit metres apart); a franken-group of
        // side-by-side neighbours has near-zero spread and must never qualify.
        private const double MinRunSpreadPt = 3 * BubbleMaxDiaPt;

        // The gridline RUN: tokens grouped by the perpendicular coordinate (digit bubbles share a row;
        // letter bubbles a column), gap-clustered (not fixed buckets — a run whose jitter straddles a
        // bucket boundary must not split). A group QUALIFIES as a run only when it actually spreads
        // along the run axis. The largest qualifying group anchors; every other qualifying group whose
        // labels are ENTIRELY new joins it — an L-shaped plate splits its letters across two columns
        // (E–G at the matchline edge, A–D further right), and both describe the one grid. A group
        // repeating an accumulated label is ANOTHER plan's grid (a partial ramp plan carries its own
        // "4") — excluded.
        private static List<VectorPageReader.TextToken> LargestRun(
            List<VectorPageReader.TextToken> tokens,
            Func<VectorPageReader.TextToken, double> perpCoord,
            Func<VectorPageReader.TextToken, double> runCoord)
        {
            if (tokens.Count <= 1) return new List<VectorPageReader.TextToken>();
            var ts = tokens.OrderBy(perpCoord).ToList();
            var groups = new List<List<VectorPageReader.TextToken>>();
            var cur = new List<VectorPageReader.TextToken> { ts[0] };
            for (int i = 1; i < ts.Count; i++)
            {
                if (perpCoord(ts[i]) - perpCoord(ts[i - 1]) > 1.5 * BubbleMaxDiaPt) { groups.Add(cur); cur = new(); }
                cur.Add(ts[i]);
            }
            groups.Add(cur);

            var qualifying = groups
                .Where(g => g.Count >= 2 && g.Max(runCoord) - g.Min(runCoord) >= MinRunSpreadPt)
                .OrderByDescending(g => g.Count)
                .ToList();
            if (qualifying.Count == 0) return new List<VectorPageReader.TextToken>();

            var run = new List<VectorPageReader.TextToken>(qualifying[0]);
            var seen = new HashSet<string>(qualifying[0].Select(t => t.Text.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var g in qualifying.Skip(1))
            {
                var labels = g.Select(t => t.Text.Trim()).ToList();
                if (labels.Any(seen.Contains)) continue;
                // Two gridlines never share a coordinate: a joining group whose tokens land on TOP of
                // accumulated ones (two full bubble ROWS pairing up column-wise into a fake vertical
                // run) is another row of the same axis, not a continuation of this run.
                if (g.Any(t => run.Any(r => Math.Abs(runCoord(r) - runCoord(t)) < 0.5 * BubbleMaxDiaPt))) continue;
                run.AddRange(g);
                foreach (var l in labels) seen.Add(l);
            }
            return run;
        }

        /// <summary>Grid bubbles share one large font; keep tokens near the dominant (modal) height so
        /// interior dimension/note digits and letters are dropped.</summary>
        private static List<VectorPageReader.TextToken> KeepDominantHeight(List<VectorPageReader.TextToken> ts)
        {
            if (ts.Count == 0) return ts;
            double mode = ts.Select(t => Math.Round(t.MaxY - t.MinY)).Where(x => x > 0)
                            .GroupBy(x => x).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                            .Select(g => (double?)g.Key).FirstOrDefault() ?? 0;
            if (mode <= 0) return ts;
            return ts.Where(t => { double th = t.MaxY - t.MinY; return th >= 0.8 * mode && th <= 1.25 * mode; }).ToList();
        }

        /// <summary>
        /// Cluster the tokens along one coordinate by gap (a gap &gt; 2× the median splits a cluster), then
        /// return the WIDEST cluster's labels (in reading order) and its span, plus the cluster count. A
        /// count &gt; 1 means the sheet carries more than one plan; we report the dominant single plan.
        /// </summary>
        private static (IReadOnlyList<string> Labels, double Span, double Min, double Max, int Clusters) DominantCluster(
            IReadOnlyList<VectorPageReader.TextToken> tokens,
            Func<VectorPageReader.TextToken, double> coord,
            bool descendingLabels = false,
            bool mergeContinuations = false)
        {
            var ts = tokens.OrderBy(coord).ToList();
            if (ts.Count < 2)
                return (ts.Select(t => t.Text.Trim()).ToList(), 0,
                        ts.Count > 0 ? coord(ts[0]) : 0, ts.Count > 0 ? coord(ts[0]) : 0, ts.Count > 0 ? 1 : 0);

            var gaps = new List<double>();
            for (int i = 1; i < ts.Count; i++) gaps.Add(coord(ts[i]) - coord(ts[i - 1]));
            var sortedGaps = gaps.Where(x => x > 0).OrderBy(x => x).ToList();
            double med = sortedGaps.Count > 0 ? sortedGaps[sortedGaps.Count / 2] : 1;
            if (med <= 0) med = 1;

            var clusters = new List<List<VectorPageReader.TextToken>>();
            var cur = new List<VectorPageReader.TextToken> { ts[0] };
            for (int i = 1; i < ts.Count; i++)
            {
                if (coord(ts[i]) - coord(ts[i - 1]) > 2.0 * med) { clusters.Add(cur); cur = new(); }
                cur.Add(ts[i]);
            }
            clusters.Add(cur);

            // A wide bay is not a second plan: when the labels CONTINUE across the gap without a repeat
            // (…7,8 | 9,10…), the clusters are one gridline run split by the tower seam — merge them.
            // A repeated label across the gap ([2,3,4,5 | 2,3,4,5]) is genuinely another plan — kept split.
            if (mergeContinuations && clusters.Count > 1)
            {
                for (int i = clusters.Count - 2; i >= 0; i--)
                {
                    var left = clusters[i].Select(t => t.Text.Trim());
                    var right = clusters[i + 1].Select(t => t.Text.Trim());
                    if (!left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any())
                    {
                        clusters[i].AddRange(clusters[i + 1]);
                        clusters.RemoveAt(i + 1);
                    }
                }
            }

            var best = clusters.OrderByDescending(c => c.Count).ThenByDescending(c => coord(c[^1]) - coord(c[0])).First();
            var ordered = descendingLabels ? best.OrderByDescending(coord) : best.OrderBy(coord);
            var labels = ordered.Select(t => t.Text.Trim()).ToList();
            double lo = best.Min(coord), hi = best.Max(coord);
            double span = hi - lo;
            // Only a SUBSTANTIAL second cluster (≥3 bubbles = another plan) counts as multi-plan; a lone
            // off-interval stray forms a 1-token cluster and is ignored (the dominant cluster excludes it).
            int plans = clusters.Count(c => c.Count >= 3);
            return (labels, span, lo, hi, plans);
        }
    }
}
