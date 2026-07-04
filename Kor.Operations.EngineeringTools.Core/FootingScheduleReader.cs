#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Deterministic spread-footing takeoff from the drawing's own FOUNDATION SCHEDULE — the universal
    /// convention: a table of TYPE | SIZE | REINFORCING rows ("F1 | 2500 x 2500 x 900 DEEP | ...") with
    /// the marks then placed on the foundation plan beside each footing. Volume per mark = plan
    /// placements × L×W×D. Everything is positioned vector text; no vision, no guessing:
    ///   • a SPREAD footing row carries three dimensions (L x W x D) and is priced;
    ///   • a STRIP footing row carries two (width x depth) — its length lives on the plan geometry, so
    ///     it is reported as an honest residual, never fabricated;
    ///   • marks are counted OUTSIDE the schedule's own table region, so the schedule row itself is
    ///     never counted as a placement.
    /// Metric-mm sets only (dimensions 200–6000 mm); an imperial schedule reads as no rows and the
    /// foundation stays flagged unmeasured rather than misread.
    /// </summary>
    public static class FootingScheduleReader
    {
        /// <summary>One schedule row. Strip footings have <see cref="LengthMm"/> = 0.</summary>
        public sealed record FootingType(string Mark, double LengthMm, double WidthMm, double DepthMm)
        {
            public bool IsSpread => LengthMm > 0;

            /// <summary>Volume of ONE placement (cu.yd). 0 for a strip footing (length unknown).</summary>
            public double VolumeCuYdEach =>
                IsSpread ? LengthMm * WidthMm * DepthMm / 1e9 * 1.30795 : 0;
        }

        private const double DimMinMm = 200, DimMaxMm = 6000;

        // The size cell, read from the row text right of the mark: "2500 x 2500 x 900 DEEP" (spread)
        // or "550 x 300 DEEP" (strip). DEEP is required — it is what distinguishes a footing size row
        // from any other "a x b" dimension string that shares a baseline with a short token.
        private static readonly Regex SizeRe = new(
            @"^(\d{3,4})\s*[xX×]\s*(\d{3,4})(?:\s*[xX×]\s*(\d{3,4}))?\s*(?:DEEP|DP)\b",
            RegexOptions.Compiled);

        // A footing mark: 1–3 letters + 1–2 digits ("F1", "SF2", "PF10"). The schedule anchors which
        // marks exist; the plan count only ever counts marks the schedule declared.
        private static readonly Regex MarkRe = new(@"^[A-Z]{1,3}\d{1,2}$", RegexOptions.Compiled);

        /// <summary>
        /// Parse the FOUNDATION SCHEDULE rows on a page: for each mark-shaped token, the words on its
        /// baseline to its right are joined and must read as a footing SIZE cell. Returns the types and
        /// the table's bounding box (so placement counting can exclude it).
        /// </summary>
        public static (IReadOnlyList<FootingType> Types, (double MinX, double MinY, double MaxX, double MaxY) TableBox)
            ReadSchedule(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);
            var types = new List<FootingType>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            foreach (var t in page.Words)
            {
                string mark = t.Text.Trim();
                if (!MarkRe.IsMatch(mark)) continue;

                // The size cell: words on the same baseline, right of the mark, within the table width.
                var row = page.Words
                    .Where(w => Math.Abs(w.Cy - t.Cy) <= 6 && w.Cx > t.Cx && w.Cx - t.Cx <= 320)
                    .OrderBy(w => w.Cx).Select(w => w.Text).ToList();
                if (row.Count == 0) continue;
                // CAD tables format 4-digit mm with a thousands comma ("1,300 DEEP") — normalize first.
                var m = SizeRe.Match(string.Join(" ", row).Replace(",", ""));
                if (!m.Success) continue;

                double a = double.Parse(m.Groups[1].Value), b = double.Parse(m.Groups[2].Value);
                double? c = m.Groups[3].Success ? double.Parse(m.Groups[3].Value) : null;
                if (a < DimMinMm || a > DimMaxMm || b < DimMinMm || b > DimMaxMm) continue;
                if (c is double cd && (cd < DimMinMm || cd > DimMaxMm)) continue;

                // Three dims = spread (L x W x DEEP); two dims = strip (width x depth, length on plan).
                types.Add(c is double depth
                    ? new FootingType(mark, a, b, depth)
                    : new FootingType(mark, 0, a, b));
                minX = Math.Min(minX, t.MinX); minY = Math.Min(minY, t.MinY);
                maxX = Math.Max(maxX, t.MaxX); maxY = Math.Max(maxY, t.MaxY);
            }

            // Same mark twice (a mirrored/duplicated table) → keep one.
            var distinct = types.GroupBy(f => f.Mark, StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.First()).ToList();
            // Pad the box so headers/size cells fall inside the exclusion zone.
            return (distinct, types.Count == 0 ? (0, 0, 0, 0) : (minX - 20, minY - 20, maxX + 340, maxY + 30));
        }

        /// <summary>
        /// Count each declared mark's PLACEMENTS on the plan: standalone mark words outside the
        /// schedule's own table box. Each footing is labelled once by convention.
        /// </summary>
        public static Dictionary<string, int> CountPlacements(
            VectorPageReader.PageContent page,
            IReadOnlyList<FootingType> types,
            (double MinX, double MinY, double MaxX, double MaxY) tableBox)
        {
            ArgumentNullException.ThrowIfNull(page);
            var marks = new HashSet<string>(types.Select(t => t.Mark), StringComparer.OrdinalIgnoreCase);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in page.Words)
            {
                string txt = w.Text.Trim();
                if (!marks.Contains(txt)) continue;
                bool inTable = w.Cx >= tableBox.MinX && w.Cx <= tableBox.MaxX
                            && w.Cy >= tableBox.MinY && w.Cy <= tableBox.MaxY;
                if (inTable) continue;
                counts[txt] = counts.GetValueOrDefault(txt) + 1;
            }
            return counts;
        }
    }
}
