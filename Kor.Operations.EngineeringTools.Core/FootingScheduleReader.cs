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
    /// Imperial or metric: the size cell is read by <see cref="PrintedLength"/>, so "4' - 0\" x 4' - 0\"
    /// x 26\" DEEP" and "2500 x 2500 x 900 DEEP" both parse. Until 2026-09-02 this required
    /// \d{3,4} and so read millimetres only, which was ONE of five KOR jobs — the other four
    /// reported 0 cy, silently, and a deterministic tool reporting a total does not look broken.
    /// Dimensions outside 200–6000 mm are still refused as implausible.
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

            var rows = MarkRowScheduleReader.ReadSchedule(page, MarkRowScheduleReader.FootingDefaults());
            foreach (var row in rows)
            {
                var dims = row.DimensionsMm;
                if (dims.Count < 2) continue;
                double a = dims[0], b = dims[1];
                double? c = dims.Count >= 3 ? dims[2] : null;

                // Three dims = spread (L x W x DEEP); two dims = strip (width x depth, length on plan).
                types.Add(c is double depth
                    ? new FootingType(row.Mark, a, b, depth)
                    : new FootingType(row.Mark, 0, a, b));
                minX = Math.Min(minX, row.MarkToken.MinX); minY = Math.Min(minY, row.MarkToken.MinY);
                maxX = Math.Max(maxX, row.MarkToken.MaxX); maxY = Math.Max(maxY, row.MarkToken.MaxY);
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
            => PlacementPositions(page, types, tableBox)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every plan placement of each mark WITH its position (PDF points, y-up) — the same
        /// outside-the-table filter as <see cref="CountPlacements"/>. Positions let a strip footing's
        /// contour run be assigned to its nearest mark (the length the schedule itself cannot state).
        /// </summary>
        public static Dictionary<string, List<(double X, double Y)>> PlacementPositions(
            VectorPageReader.PageContent page,
            IReadOnlyList<FootingType> types,
            (double MinX, double MinY, double MaxX, double MaxY) tableBox)
        {
            ArgumentNullException.ThrowIfNull(page);
            var marks = new HashSet<string>(types.Select(t => t.Mark), StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, List<(double X, double Y)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in page.Words)
            {
                string txt = w.Text.Trim();
                if (!marks.Contains(txt)) continue;
                bool inTable = w.Cx >= tableBox.MinX && w.Cx <= tableBox.MaxX
                            && w.Cy >= tableBox.MinY && w.Cy <= tableBox.MaxY;
                if (inTable) continue;
                if (!found.TryGetValue(txt, out var list)) found[txt] = list = new List<(double X, double Y)>();
                list.Add((w.Cx, w.Cy));
            }
            return found;
        }
    }
}
