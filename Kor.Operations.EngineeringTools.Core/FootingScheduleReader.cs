#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;

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
        public sealed record FootingType(
            string Mark,
            double LengthMm,
            double WidthMm,
            double DepthMm,
            MarkRowScheduleReader.MarkRoute Route = MarkRowScheduleReader.MarkRoute.ScheduleColumn)
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
                    ? new FootingType(row.Mark, a, b, depth, row.Route)
                    : new FootingType(row.Mark, 0, a, b, row.Route));
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
        /// Why a page that mentions a foundation schedule yielded no footing rows, in the sheet's own
        /// words: which foundation-type schedules it titles, whether each is drawn as a ruled table,
        /// and how many ruled rows that table holds.
        /// </summary>
        /// <remarks>
        /// "Schedule text present but no parseable rows" said the same thing about 31168, whose
        /// FOUNDATION SCHEDULE is a bordered placeholder with blank rows on the 2026-04-21 stick file,
        /// and about 31202, whose foundation is a RAFT SLAB with a reinforcing schedule of its own.
        /// Neither is a parse failure, and a reader that reports them as one sends somebody to look
        /// for a bug in the reader. The sheet says what it has; this repeats it.
        /// </remarks>
        public static string WhyNoRows(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var any = MarkRowScheduleReader.ColumnDefaults() with { HeadingWords = Array.Empty<string>() };
            var foundation = MarkRowScheduleReader.SchedulesOn(page, any)
                .Where(h => h.Title.Contains("FOUNDATION", StringComparison.OrdinalIgnoreCase)
                            || h.Title.Contains("FOOTING", StringComparison.OrdinalIgnoreCase)
                            || h.Title.Contains("RAFT", StringComparison.OrdinalIgnoreCase)
                            || h.Title.Contains("MAT", StringComparison.OrdinalIgnoreCase)
                            || h.Title.Contains("PILE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (foundation.Count == 0)
                return "foundation schedule mentioned in the text, but no schedule title on the sheet says FOUNDATION, FOOTING, RAFT, MAT or PILE";

            var rules = ScheduleTableBorder.RulesOn(page);
            var parts = new List<string>();
            foreach (var h in foundation)
            {
                var border = h.TitleMaxX > h.TitleMinX
                    ? ScheduleTableBorder.Under(page, h.TitleMinX, h.TitleMaxX, h.TitleMinY, h.TitleHeight, rules)
                    : null;
                if (border is null)
                {
                    parts.Add($"\"{h.Title}\" is titled but no ruled table is drawn under it");
                    continue;
                }

                int ruled = border.RowBands().Count(b => border.IsRuledRow(b.Top, b.Bottom));
                bool targeted = MarkRowScheduleReader.FootingDefaults().HeadingWords.Any(w =>
                    MarkRowScheduleReader.IsHeadedBy(h.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToList(), w));
                parts.Add(targeted
                    ? $"\"{h.Title}\" is drawn as a ruled table with {ruled} ruled row(s) and none states a size: a placeholder table, not a reader fault"
                    : $"\"{h.Title}\" is the foundation this sheet schedules ({ruled} ruled row(s)); it is not a spread-footing schedule, so 0 footings is what the sheet says");
            }
            return string.Join("; ", parts);
        }

        /// <summary>
        /// Count each declared mark's PLACEMENTS on the plan: standalone mark words outside the
        /// schedule's own table box — and, when <paramref name="furniture"/> is given, outside every
        /// note, legend and schedule box on the sheet. Each footing is labelled once by convention.
        /// </summary>
        /// <remarks>
        /// A mark in a note is a mention, not a placement. 31065 p14's note "ADD BOND BREAKER BETWEEN
        /// F4 &amp; CORE FOOTING" appears twice and counted two F4s (13 where the plan places 11) until
        /// the furniture was passed — 74 cu.yd of footing that is not there. Callers without a
        /// furniture set keep the old count; the intake passes it (brief 21).
        /// </remarks>
        public static Dictionary<string, int> CountPlacements(
            VectorPageReader.PageContent page,
            IReadOnlyList<FootingType> types,
            (double MinX, double MinY, double MaxX, double MaxY) tableBox,
            SheetFurniture.Set? furniture = null)
            => PlacementPositions(page, types, tableBox, furniture)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every plan placement of each mark WITH its position (PDF points, y-up) — the same
        /// outside-the-table (and, given <paramref name="furniture"/>, outside-the-furniture) filter as
        /// <see cref="CountPlacements"/>. Positions let a strip footing's contour run be assigned to its
        /// nearest mark (the length the schedule itself cannot state), and a spread footing's outline
        /// be tied to the label that names it.
        /// </summary>
        public static Dictionary<string, List<(double X, double Y)>> PlacementPositions(
            VectorPageReader.PageContent page,
            IReadOnlyList<FootingType> types,
            (double MinX, double MinY, double MaxX, double MaxY) tableBox,
            SheetFurniture.Set? furniture = null)
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
                if (furniture is not null && furniture.IsFurniture(w.Cx, w.Cy)) continue;
                if (!found.TryGetValue(txt, out var list)) found[txt] = list = new List<(double X, double Y)>();
                list.Add((w.Cx, w.Cy));
            }
            return found;
        }
    }
}
