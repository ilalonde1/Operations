#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// The parts of a sheet that are not the drawing: every schedule's border and the title block.
    /// Linework inside them is furniture, whatever shape it has, and never structure.
    /// </summary>
    /// <remarks>
    /// A structural sheet carries its plan and, beside it, tables and a title block drawn with the
    /// same pen. The classifier saw a tie-arrangement sketch in a column schedule as a column, a
    /// schedule's cell box as a slab, its rules as beams, and the title block's boxes as more of
    /// each, and every DXF this pipeline wrote carried them. This says where the sheet's furniture
    /// is, from what the sheet itself draws:
    ///
    ///   - a schedule is the ruled border under a title ending in SCHEDULE, whatever it schedules
    ///     (<see cref="ScheduleTableBorder"/>), plus the title row above its top rule;
    ///   - the title block is the strip along the sheet's edge that holds the sheet number, cut off
    ///     from the drawing by the long rule nearest to that number on the drawing's side.
    ///
    /// WHAT THIS DOES NOT COVER: notes, legends and key plans that are not ruled tables; a schedule
    /// with no title; a sheet with no readable sheet number, which then has no title block here
    /// and keeps whatever is in it. Those read as they did before.
    /// </remarks>
    public static class SheetFurniture
    {
        /// <summary>One region of the sheet that is not the drawing, in the page's own points.</summary>
        public readonly record struct Region(string Kind, double MinX, double MinY, double MaxX, double MaxY)
        {
            public bool Contains(double x, double y)
                => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

            /// <summary>The same region in another unit, for geometry that has already been scaled.</summary>
            public Region Scaled(double factor)
                => new(Kind, MinX * factor, MinY * factor, MaxX * factor, MaxY * factor);
        }

        /// <summary>
        /// A rule must span at least this share of the page to cut the title block off from the
        /// drawing: a title block runs the sheet's height (or width), a grid line runs the plan's.
        /// </summary>
        public const double TitleBlockRuleMinShare = 0.6;

        /// <summary>
        /// A title block is a strip: at most this share of the page's width (or height). A cut that
        /// leaves more than that on the sheet-number's side has cut through the drawing.
        /// </summary>
        public const double TitleBlockMaxShare = 0.3;

        public static IReadOnlyList<Region> On(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var regions = new List<Region>();
            var rules = ScheduleTableBorder.RulesOn(page);

            // 1. every schedule on the sheet, whatever it schedules
            var anySchedule = MarkRowScheduleReader.ColumnDefaults() with { HeadingWords = Array.Empty<string>() };
            foreach (var heading in MarkRowScheduleReader.SchedulesOn(page, anySchedule))
            {
                if (heading.TitleMaxX <= heading.TitleMinX) continue;
                var border = ScheduleTableBorder.Under(
                    page, heading.TitleMinX, heading.TitleMaxX, heading.TitleMinY, heading.TitleHeight, rules);
                if (border is null) continue;

                double titleTop = heading.TitleMinY + heading.TitleHeight;
                regions.Add(new Region(
                    "schedule: " + heading.Title,
                    Math.Min(border.MinX, heading.TitleMinX),
                    border.MinY,
                    Math.Max(border.MaxX, heading.TitleMaxX),
                    Math.Max(border.MaxY, titleTop)));
            }

            // 2. the title block: the strip beyond the long edge nearest the sheet number. An edge
            //    is every rule at one position taken together — a title block's side is drawn box
            //    by box, and no one box's side is the length of the sheet, but the side is.
            if (SheetTitleReader.SheetNumber(page) is { } number)
            {
                double w = page.WidthPts, h = page.HeightPts;

                var cutX = Edges(rules.Vertical, TitleBlockRuleMinShare * h)
                    .Where(x => Between(x, w / 2, number.Cx))
                    .OrderBy(x => Math.Abs(x - number.Cx))
                    .Select(x => (double?)x)
                    .FirstOrDefault();
                if (cutX is double x && StripShare(x, number.Cx, w) <= TitleBlockMaxShare)
                {
                    regions.Add(number.Cx > w / 2
                        ? new Region("title block", x, 0, w, h)
                        : new Region("title block", 0, 0, x, h));
                }
                else
                {
                    var cutY = Edges(rules.Horizontal, TitleBlockRuleMinShare * w)
                        .Where(y => Between(y, h / 2, number.Cy))
                        .OrderBy(y => Math.Abs(y - number.Cy))
                        .Select(y => (double?)y)
                        .FirstOrDefault();
                    if (cutY is double y && StripShare(y, number.Cy, h) <= TitleBlockMaxShare)
                    {
                        regions.Add(number.Cy > h / 2
                            ? new Region("title block", 0, y, w, h)
                            : new Region("title block", 0, 0, w, y));
                    }
                }
            }

            return regions;
        }

        /// <summary>
        /// The positions at which the rules, taken together, cover at least <paramref name="minCover"/>
        /// of their direction: a side of the sheet drawn as one stroke or as twenty.
        /// </summary>
        public static IReadOnlyList<double> Edges(IReadOnlyList<ScheduleTableBorder.Rule> rules, double minCover)
        {
            ArgumentNullException.ThrowIfNull(rules);
            var edges = new List<double>();
            foreach (var group in rules.OrderBy(r => r.At).GroupBy(r => Math.Round(r.At / ScheduleTableBorder.StraightPts)))
            {
                double covered = 0, lo = double.NaN, hi = double.NaN;
                foreach (var r in group.OrderBy(r => r.Lo))
                {
                    if (double.IsNaN(lo) || r.Lo > hi + ScheduleTableBorder.AbutPts * 20)
                    {
                        if (!double.IsNaN(lo)) covered += hi - lo;
                        lo = r.Lo;
                        hi = r.Hi;
                    }
                    else
                    {
                        hi = Math.Max(hi, r.Hi);
                    }
                }
                if (!double.IsNaN(lo)) covered += hi - lo;
                if (covered >= minCover) edges.Add(group.Average(r => r.At));
            }
            return edges;
        }

        private static bool Between(double value, double a, double b)
            => value > Math.Min(a, b) && value < Math.Max(a, b);

        /// <summary>The share of the page on the sheet-number's side of a cut.</summary>
        private static double StripShare(double cut, double numberAt, double extent)
            => numberAt > extent / 2 ? (extent - cut) / extent : cut / extent;
    }
}
