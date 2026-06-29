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

        // Digit bubble rows hug the top/bottom edges; letters can be anywhere down the plan sides (an
        // L-shaped plate splits A–D and E–F across two x-columns), so letters are NOT margin-banded.
        private const double DigitMarginFy = 0.15;   // within 15% of the top OR bottom edge
        private const double TitleBlockFx = 0.95;    // ignore single letters inside the right-corner title block

        public static GridFrame? FromPage(VectorPageReader.PageContent? page)
        {
            if (page is null || page.Words.Count == 0) return null;
            double w = page.WidthPts, h = page.HeightPts;
            if (w <= 0 || h <= 0) return null;

            // ── X grid (digits): isolate the single most-populated margin ROW, keep its dominant-height
            //    bubbles, then take the widest evenly-spaced cluster (a 2nd cluster ⇒ a 2nd plan). ──
            var marginDigits = page.Words
                .Where(t => DigitRx.IsMatch(t.Text.Trim()) && (t.Cy / h > 1 - DigitMarginFy || t.Cy / h < DigitMarginFy))
                .ToList();
            var row = marginDigits
                .GroupBy(t => Math.Round(t.Cy / 12.0)).OrderByDescending(g => g.Count())
                .FirstOrDefault()?.ToList() ?? new List<VectorPageReader.TextToken>();
            row = KeepDominantHeight(row);
            var (xLabels, xSpan, xMin, xMax, xClusters) = DominantCluster(row, t => t.Cx);

            // ── Y grid (letters): dominant-height single letters outside the title block; widest cluster. ──
            var allLetters = page.Words
                .Where(t => LetterRx.IsMatch(t.Text.Trim()) && t.Cx / w < TitleBlockFx)
                .ToList();
            allLetters = KeepDominantHeight(allLetters);
            // top→bottom: Cy is up-from-bottom, so order the labels by DESCENDING Cy.
            var (yLabels, ySpan, yMin, yMax, yClusters) = DominantCluster(allLetters, t => t.Cy, descendingLabels: true);

            if (xLabels.Count == 0 && yLabels.Count == 0) return null;
            return new GridFrame(xLabels, yLabels, xSpan, ySpan, MultiPlan: xClusters > 1 || yClusters > 1)
            {
                XMinPt = xMin, XMaxPt = xMax, YMinPt = yMin, YMaxPt = yMax,
            };
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
            bool descendingLabels = false)
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
