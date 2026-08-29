#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): the drawing scale an estimator reads off the title block's SCALE field.
    /// Every measured quantity is proportional to the scale SQUARED, so assuming a scale the sheet does
    /// not state is a silent systematic error — a metric set drawn 1:100 but measured at the imperial
    /// default 1/8&quot;=1'-0&quot; (1:96) under-prices every area by ~8%. This reads the stated value
    /// from where it lives (the "SCALE" label in the right-edge title block, value on the same baseline)
    /// so the takeoff measures at the sheet's own scale; the caller falls back — flagged — only when no
    /// sheet states one.
    ///
    /// Read by POSITION like <see cref="SheetTitleReader"/>: viewport captions elsewhere on the sheet
    /// ("SCALE: 1:50" under a stair detail) sit left of the title-block region and are not consulted.
    /// "AS NOTED"/"NTS" values do not parse and correctly yield null. When a title block carries several
    /// SCALE fields that parse to DIFFERENT values the sheet is ambiguous — also null, never a guess.
    /// </summary>
    public static class SheetScaleReader
    {
        // Same title-block region convention as SheetTitleReader: the right edge of the sheet — but the
        // SCALE metadata field specifically lives in the BOTTOM corner of the block (with project no.,
        // date, drawn-by). The height cut keeps viewport/detail captions drawn higher in the right-hand
        // details column ("SCALE: 1:20" under a stair section) from masquerading as the sheet scale.
        private const double TitleRegionMinFx = 0.80;
        private const double ScaleFieldMaxFy  = 0.35;   // bottom third (PDF y is up from the bottom)
        // The value sits just right of its label; reach a little further for wide imperial notes.
        private const double ValueReachPt = 220.0;
        private const double BaselineTolPt = 6.0;

        public readonly record struct ScaleNote(
            string Note,
            double MetresPerPixel,
            double FractionX,
            double FractionY);

        /// <summary>
        /// The scale note stated in the page's title block ("1 : 100", "1/8&quot; = 1'-0&quot;"), or null
        /// when the title block states none, states an unparseable one (AS NOTED), or states conflicting
        /// values. Validated by <see cref="PlanGeometry.MetresPerPixel"/> before being returned, so a
        /// non-null result is always convertible.
        /// </summary>
        public static string? FromPage(VectorPageReader.PageContent? page)
        {
            if (page is null || page.WidthPts <= 0 || page.HeightPts <= 0 || page.Words.Count == 0) return null;
            double w = page.WidthPts, h = page.HeightPts;

            var region = page.Words.Where(t => t.Cx / w >= TitleRegionMinFx).ToList();
            if (region.Count == 0) return null;

            var values = new List<double>();
            string? note = null;
            foreach (var label in region)
            {
                if (!label.Text.TrimStart().StartsWith("SCALE", StringComparison.OrdinalIgnoreCase)) continue;
                if (label.Cy / h > ScaleFieldMaxFy) continue;   // not the bottom-corner metadata field

                // The candidate note: any text after "SCALE:" inside the label token itself, then the
                // same-baseline words to its right, left→right.
                string inline = label.Text.TrimStart();
                int cut = "SCALE".Length;
                while (cut < inline.Length && (inline[cut] == ':' || char.IsWhiteSpace(inline[cut]))) cut++;
                // Same baseline, centred right of the label (tolerating slightly overlapping boxes from
                // kerning), within reach. A duplicate overprinted label at the same position is excluded.
                string tail = string.Join(" ", region
                    .Where(t => Math.Abs(t.Cy - label.Cy) <= BaselineTolPt
                                && t.Cx > label.Cx + 1
                                && t.MinX <= label.MaxX + ValueReachPt)
                    .OrderBy(t => t.Cx)
                    .Select(t => t.Text));
                string candidate = (inline.Substring(cut) + " " + tail).Trim();
                if (candidate.Length == 0) continue;
                // The value must FOLLOW the label directly — a candidate that opens with words
                // ("AS NOTED …") is not a scale even if a ratio-shaped token from a neighbouring
                // title-block field got spliced onto the same baseline further right.
                if (!char.IsDigit(candidate[0])) continue;

                // Validate — a note is only a scale if it converts. DPI here is arbitrary; only
                // parseability and the relative factor matter.
                double? mpp = PlanGeometry.MetresPerPixel(candidate, 96);
                if (mpp is not double v || v <= 0) continue;
                values.Add(v);
                note ??= candidate;
            }

            if (values.Count == 0) return null;
            // Conflicting stated scales (two parseable SCALE fields disagreeing) → ambiguous, no guess.
            if (values.Any(v => Math.Abs(v - values[0]) / values[0] > 0.01)) return null;
            return note;
        }

        public static IReadOnlyList<ScaleNote> ScaleNotesAnywhere(VectorPageReader.PageContent? page)
        {
            if (page is null || page.WidthPts <= 0 || page.HeightPts <= 0 || page.Words.Count == 0)
                return Array.Empty<ScaleNote>();

            var notes = new List<ScaleNote>();
            foreach (var label in page.Words)
            {
                if (!label.Text.TrimStart().StartsWith("SCALE", StringComparison.OrdinalIgnoreCase)) continue;

                string inline = label.Text.TrimStart();
                int cut = "SCALE".Length;
                while (cut < inline.Length && (inline[cut] == ':' || char.IsWhiteSpace(inline[cut]))) cut++;
                string tail = string.Join(" ", page.Words
                    .Where(t => Math.Abs(t.Cy - label.Cy) <= BaselineTolPt
                                && t.Cx > label.Cx + 1
                                && t.MinX <= label.MaxX + ValueReachPt)
                    .OrderBy(t => t.Cx)
                    .Select(t => t.Text));
                string candidate = (inline.Substring(cut) + " " + tail).Trim();
                if (candidate.Length == 0) continue;
                if (!char.IsDigit(candidate[0])) continue;

                double? mpp = PlanGeometry.MetresPerPixel(candidate, 96);
                if (mpp is not double v || v <= 0) continue;
                if (notes.Any(n => Math.Abs(n.MetresPerPixel - v) / n.MetresPerPixel <= 0.01))
                    continue;

                notes.Add(new ScaleNote(
                    candidate,
                    v,
                    label.Cx / page.WidthPts,
                    label.Cy / page.HeightPts));
            }

            return notes;
        }
    }
}
