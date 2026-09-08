#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// What a sheet draws that is not the structure — schedules, notes, tables, legends, details,
    /// the title block, the grid lines and the underlines — and what the sheet DECLARES its columns
    /// to be. The classifier reads structure with this in hand and nothing inside it becomes a
    /// column, a slab or a beam.
    /// </summary>
    /// <remarks>
    /// A structural sheet carries its plan and, beside it, tables and a title block drawn with the
    /// same pen. The classifier saw a tie-arrangement sketch as a column, a schedule's cell box as a
    /// slab, its rules as beams, the title block's boxes as more of each, every grid line as a beam
    /// and every heading's underline as one more, and every DXF this pipeline wrote carried them.
    /// This says where the sheet's furniture is, from what the sheet itself draws:
    ///
    ///   - a schedule is the ruled border under a title ending in SCHEDULE, whatever it schedules
    ///     (<see cref="ScheduleTableBorder"/>), plus the title row above its top rule;
    ///   - a notes box, a table, a legend or a sheet of details is the same border under a title
    ///     ending in one of <see cref="FurnitureHeadings"/> — measured on 705 titled ruled boxes
    ///     across the five KOR jobs' 294 pages, and chosen to leave out "... PLAN" and "OUTLINE",
    ///     which are the plan's own viewport;
    ///   - the title block is the strip along the sheet's edge that holds the sheet number, cut off
    ///     from the drawing by the long edge nearest to that number on the drawing's side;
    ///   - a grid line is the line through a grid bubble (<see cref="GridBubbles"/>);
    ///   - an underline is a rule directly under a line of text, matching its extent.
    ///
    /// And the schedule the sheet carries states its columns' sizes; a filled shape of a declared
    /// size IS a column, whatever a plausibility window fitted to other sheets would say. 31138
    /// declares PC7 at 18" x 60" and PC8 at 18" x 96", aspects 3.3 and 5.3, and a 3.0 limit
    /// refused both on the sheet that declares them.
    ///
    /// WHAT THIS DOES NOT COVER: a legend or note that is not ruled; a sheet with no readable
    /// sheet number, which keeps its title block; a grid drawn without bubbles. Those read as
    /// they did before.
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

        /// <summary>Everything the classifier needs to know about a sheet before it reads it.</summary>
        /// <param name="Regions">Where the furniture is.</param>
        /// <param name="VerticalAxesX">The x of every vertical grid axis.</param>
        /// <param name="HorizontalAxesY">The y of every horizontal grid axis.</param>
        /// <param name="AxisTolerance">How close to an axis a line must run to be the grid line.</param>
        /// <param name="DeclaredColumnSizesMm">The sizes the sheet's column schedule declares, in mm.</param>
        /// <param name="SizeToleranceMm">How close a drawn size must be to a declared one.</param>
        /// <summary>A heading's underline: a horizontal rule under a line of text, matching its extent.</summary>
        public readonly record struct Underline(double MinX, double MaxX, double Y);

        public sealed record Set(
            IReadOnlyList<Region> Regions,
            IReadOnlyList<double> VerticalAxesX,
            IReadOnlyList<double> HorizontalAxesY,
            double AxisTolerance,
            IReadOnlyList<(double W, double D)> DeclaredColumnSizesMm,
            double SizeToleranceMm)
        {
            public static Set Empty { get; } = new([], [], [], 0, [], 0);

            /// <summary>The underlines on the sheet. A LINE matching one is an underline; a shape centred on one is not.</summary>
            public IReadOnlyList<Underline> Underlines { get; init; } = [];

            public bool IsFurniture(double x, double y) => Regions.Any(r => r.Contains(x, y));

            public bool OnVerticalAxis(double x) => VerticalAxesX.Any(a => Math.Abs(a - x) <= AxisTolerance);
            public bool OnHorizontalAxis(double y) => HorizontalAxesY.Any(a => Math.Abs(a - y) <= AxisTolerance);

            /// <summary>Whether a horizontal line from x0 to x1 at y is one of the sheet's underlines.</summary>
            public bool IsUnderline(double x0, double x1, double y)
            {
                double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
                return Underlines.Any(u => Math.Abs(u.Y - y) <= AxisTolerance
                                           && Math.Abs(u.MinX - lo) <= AxisTolerance
                                           && Math.Abs(u.MaxX - hi) <= AxisTolerance);
            }

            /// <summary>Whether a drawn size, either way round, is one the schedule declares.</summary>
            public bool IsDeclaredColumnSize(double a, double b)
            {
                double small = Math.Min(a, b), large = Math.Max(a, b);
                return DeclaredColumnSizesMm.Any(s =>
                    Math.Abs(small - Math.Min(s.W, s.D)) <= SizeToleranceMm && Math.Abs(large - Math.Max(s.W, s.D)) <= SizeToleranceMm);
            }

            /// <summary>Points to another unit: regions, axes and underlines scale; declared sizes are millimetres already.</summary>
            public Set Scaled(double factor) => new Set(
                Regions.Select(r => r.Scaled(factor)).ToList(),
                VerticalAxesX.Select(x => x * factor).ToList(),
                HorizontalAxesY.Select(y => y * factor).ToList(),
                AxisTolerance * factor,
                DeclaredColumnSizesMm,
                SizeToleranceMm)
            {
                Underlines = Underlines.Select(u => new Underline(u.MinX * factor, u.MaxX * factor, u.Y * factor)).ToList(),
            };
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

        /// <summary>
        /// A word in a title that makes the ruled box with it furniture, never the plan. From the
        /// population of 705 titled boxes: NOTES (14), DETAILS (12), TRANSITIONS (9), ORDER (8,
        /// "BAR PLACING ORDER U.N.O. ON PLAN"), ZONES and HEADERS (8 each, 31130's wall tables),
        /// TABLE (7). A list that can only widen. PLAN and OUTLINE are the plan's viewport and are
        /// never here; and whatever the title, furniture is never half the sheet
        /// (<see cref="FurnitureMaxShare"/>).
        /// </summary>
        public static readonly IReadOnlyList<string> FurnitureHeadings =
            ["NOTES", "NOTE", "TABLE", "LEGEND", "ORDER", "DETAILS", "DETAIL", "TRANSITIONS", "ZONES", "HEADERS", "REVISIONS", "ISSUES"];

        /// <summary>
        /// A titled box larger than this share of the page in either direction is not furniture, it
        /// is the drawing: a NOTES heading placed inside the plan's viewport has the viewport's own
        /// frame around it, and nothing here may swallow the plan.
        /// </summary>
        public const double FurnitureMaxShare = 0.5;

        /// <summary>
        /// A title is a short line. The longest in the population is seven words ("NORTH TOWER CORE
        /// FOOTING BAR PLACING ORDER"); a sentence of notes that mentions DETAILS runs to eleven.
        /// </summary>
        public const int TitleMaxWords = 8;

        public static Set On(VectorPageReader.PageContent page, double sizeToleranceMm = PlanAgreesWithItsSchedule.DefaultToleranceMm)
        {
            ArgumentNullException.ThrowIfNull(page);

            var regions = new List<Region>();
            var rules = ScheduleTableBorder.RulesOn(page);

            // 1. every schedule on the sheet, whatever it schedules, and every titled box of furniture
            var anySchedule = MarkRowScheduleReader.ColumnDefaults() with { HeadingWords = Array.Empty<string>() };
            foreach (var heading in MarkRowScheduleReader.SchedulesOn(page, anySchedule))
                AddTitledBox(regions, page, rules, "schedule: " + heading.Title, heading.TitleMinX, heading.TitleMaxX, heading.TitleMinY, heading.TitleHeight);
            foreach (var (title, minX, maxX, minY, height) in TitledBoxes(page))
                AddTitledBox(regions, page, rules, "furniture: " + title, minX, maxX, minY, height, enclosingToo: true);

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

            // 3. the grid, and what the sheet declares its columns to be
            var grid = GridBubbles.On(page);
            var declared = ColumnScheduleReader.ReadSchedule(page)
                .Where(r => !r.SizeVaries && r.WidthMm > 0 && r.DepthMm > 0)
                .Select(r => (r.WidthMm, r.DepthMm))
                .Distinct()
                .ToList();

            // 4. underlines: a rule directly under a line of text, matching its extent — kept apart
            //    from the regions, because a region swallows any shape centred inside it and an
            //    underline may only ever claim the one line that IS it
            return new Set(regions, grid.VerticalAxesX, grid.HorizontalAxesY, GridBubbles.AxisTolerancePts, declared, sizeToleranceMm)
            {
                Underlines = Underlines(page, rules).ToList(),
            };
        }

        private static void AddTitledBox(
            List<Region> regions,
            VectorPageReader.PageContent page,
            ScheduleTableBorder.Rules rules,
            string kind,
            double titleMinX,
            double titleMaxX,
            double titleMinY,
            double titleHeight,
            bool enclosingToo = false)
        {
            if (titleMaxX <= titleMinX) return;
            var border = ScheduleTableBorder.Under(page, titleMinX, titleMaxX, titleMinY, titleHeight, rules, requireRules: false);
            if (border is null && enclosingToo)
                border = ScheduleTableBorder.Enclosing(page, titleMinX, titleMinY + titleHeight, titleHeight, rules);
            if (border is null) return;

            // furniture is never half the sheet
            if (border.Width > FurnitureMaxShare * page.WidthPts || border.Height > FurnitureMaxShare * page.HeightPts) return;

            regions.Add(new Region(
                kind,
                Math.Min(border.MinX, titleMinX),
                border.MinY,
                Math.Max(border.MaxX, titleMaxX),
                Math.Max(border.MaxY, titleMinY + titleHeight)));
        }

        /// <summary>
        /// Every line of text carrying one of <see cref="FurnitureHeadings"/> as a whole word: the
        /// title, its extent and its height, found the way schedule titles are.
        /// </summary>
        private static IEnumerable<(string Title, double MinX, double MaxX, double MinY, double Height)> TitledBoxes(VectorPageReader.PageContent page)
        {
            foreach (var w in page.Words)
            {
                string word = w.Text.Trim().TrimEnd(':', '.', '-', ',');
                if (!FurnitureHeadings.Any(h => h.Equals(word, StringComparison.OrdinalIgnoreCase))) continue;

                // the title is the run of tokens on this baseline, contiguous within two text heights
                var line = page.Words
                    .Where(t => Math.Abs(t.Cy - w.Cy) <= Math.Max(w.Height, 2) / 2)
                    .OrderBy(t => t.MinX)
                    .ToList();
                var run = new List<VectorPageReader.TextToken> { w };
                double left = w.MinX, right = w.MaxX;
                foreach (var t in line.Where(t => t.MaxX <= w.MinX).OrderByDescending(t => t.MaxX))
                {
                    if (left - t.MaxX > 2 * Math.Max(t.Height, w.Height)) break;
                    run.Add(t);
                    left = t.MinX;
                }
                foreach (var t in line.Where(t => t.MinX >= w.MaxX).OrderBy(t => t.MinX))
                {
                    if (t.MinX - right > 2 * Math.Max(t.Height, w.Height)) break;
                    run.Add(t);
                    right = t.MaxX;
                }

                // a title is a short line; a sentence of notes that happens to say DETAILS is not one
                if (run.Count > TitleMaxWords) continue;

                yield return (
                    string.Join(" ", run.OrderBy(t => t.MinX).Select(t => t.Text)),
                    run.Min(t => t.MinX),
                    run.Max(t => t.MaxX),
                    run.Min(t => t.MinY),
                    run.Max(t => t.Height));
            }
        }

        /// <summary>
        /// A rule directly beneath a line of text, no wider than the line and no narrower than half
        /// of it, within a text height below its baseline: the heading's underline, not a beam.
        /// </summary>
        private static IEnumerable<Underline> Underlines(VectorPageReader.PageContent page, ScheduleTableBorder.Rules rules)
        {
            // text lines: tokens on one baseline, contiguous within two heights
            var lines = new List<(double MinX, double MaxX, double MinY, double Height)>();
            foreach (var group in page.Words.OrderByDescending(t => t.Cy).GroupBy(t => Math.Round(t.Cy)))
            {
                double? left = null, right = null, minY = null, height = null;
                foreach (var t in group.OrderBy(t => t.MinX))
                {
                    if (left is not null && right is not null && t.MinX - right.Value > 2 * Math.Max(t.Height, height ?? 0))
                    {
                        lines.Add((left.Value, right.Value, minY!.Value, height!.Value));
                        left = right = minY = height = null;
                    }
                    left ??= t.MinX;
                    right = Math.Max(right ?? t.MaxX, t.MaxX);
                    minY = Math.Min(minY ?? t.MinY, t.MinY);
                    height = Math.Max(height ?? t.Height, t.Height);
                }
                if (left is not null) lines.Add((left.Value, right!.Value, minY!.Value, height!.Value));
            }

            foreach (var h in rules.Horizontal)
            {
                foreach (var line in lines)
                {
                    if (h.At > line.MinY + 0.2 * line.Height || h.At < line.MinY - line.Height) continue;
                    if (h.Lo < line.MinX - line.Height || h.Hi > line.MaxX + line.Height) continue;
                    if (h.Length < 0.5 * (line.MaxX - line.MinX)) continue;
                    yield return new Underline(h.Lo, h.Hi, h.At);
                    break;
                }
            }
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
