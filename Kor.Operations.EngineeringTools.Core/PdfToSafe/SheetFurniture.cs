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

        /// <summary>The line a plan too wide for one sheet was split on, labelled MATCH LINE; its two ends.</summary>
        public readonly record struct MatchLine(double X0, double Y0, double X1, double Y1);

        /// <summary>A match line spans at least this share of the page across the drawing.</summary>
        public const double MatchLineMinSpanShare = 0.4;
        /// <summary>The line runs within this many label heights of the words MATCH LINE.</summary>
        public const double MatchLineLabelReachHeights = 6.0;

        /// <summary>
        /// A PLAN TOO WIDE FOR ONE SHEET IS SPLIT ON A MATCH LINE (intake step 22), and the sheet says
        /// so: the words MATCH LINE beside a line that spans the drawing, drawn dash-dot — many
        /// collinear pieces. The DXF side already joins the sheets that carry the same one into one
        /// plan (<c>MatchLineSheetJoin</c>); no half closes a floor at its match line on its own.
        /// 31168's P1, P2 and P3 plans are north and south halves split on grid J, the label at
        /// the right margin, and read separately neither half's walls close a ring.
        /// </summary>
        public static IEnumerable<MatchLine> MatchLines(VectorPageReader.PageContent page)
        {
            // the label: MATCH with LINE beside or beneath it (the label stands in the margin,
            // often stacked), or one token MATCHLINE. The label is written ALONG the line — turned
            // with it: 31065's "MATCHLINE" stands 6 pt wide and 48 pt tall beside a vertical seam,
            // 31168's "MATCH"/"LINE" lie 30 pt wide and 6 pt tall beside a horizontal one — so its
            // shape says which way the line runs, and its text height (the short side) how near.
            var labels = new List<(double Cx, double Cy, double TextHeight, bool Vertical)>();
            foreach (var w in page.Words)
            {
                string t = w.Text.Trim().TrimEnd(':', '.', '-');
                double box = Math.Max(w.Width, w.Height), text = Math.Max(Math.Min(w.Width, w.Height), 1);
                if (t.Equals("MATCHLINE", StringComparison.OrdinalIgnoreCase)) { labels.Add((w.Cx, w.Cy, text, w.Height > w.Width)); continue; }
                if (!t.Equals("MATCH", StringComparison.OrdinalIgnoreCase)) continue;
                var line = page.Words.FirstOrDefault(o => o.Text.Trim().Equals("LINE", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(o.Cx - w.Cx) <= 2 * box && Math.Abs(o.Cy - w.Cy) <= 2 * box);
                if (line.Text is not null) labels.Add(((w.Cx + line.Cx) / 2, (w.Cy + line.Cy) / 2, text, w.Height > w.Width));
            }
            if (labels.Count == 0) yield break;

            // the line: every straight stroked piece, axis-aligned within a degree, bucketed by the
            // coordinate it runs along; the bucket nearest a label whose pieces span the drawing
            var horizontal = new Dictionary<int, (double Min, double Max)>();
            var vertical = new Dictionary<int, (double Min, double Max)>();
            foreach (var p in page.Paths)
            {
                if (!p.IsStroked || p.IsFilled || p.IsAnnotation || p.Points.Count != 2) continue;
                var a = p.Points[0]; var b = p.Points[1];
                double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                if (dx + dy < 0.5) continue;
                if (dy <= 0.0175 * dx)
                {
                    int key = (int)Math.Round((a.Y + b.Y) / 2);
                    var (lo, hi) = horizontal.TryGetValue(key, out var e) ? e : (double.MaxValue, double.MinValue);
                    horizontal[key] = (Math.Min(lo, Math.Min(a.X, b.X)), Math.Max(hi, Math.Max(a.X, b.X)));
                }
                else if (dx <= 0.0175 * dy)
                {
                    int key = (int)Math.Round((a.X + b.X) / 2);
                    var (lo, hi) = vertical.TryGetValue(key, out var e) ? e : (double.MaxValue, double.MinValue);
                    vertical[key] = (Math.Min(lo, Math.Min(a.Y, b.Y)), Math.Max(hi, Math.Max(a.Y, b.Y)));
                }
            }

            var found = new List<MatchLine>();
            foreach (var (cx, cy, h, isVertical) in labels)
            {
                // the line of the label's own orientation nearest the label, among those spanning the drawing
                double reach = MatchLineLabelReachHeights * h;
                MatchLine? best = null; double bestDistance = double.MaxValue;
                if (!isVertical)
                    foreach (var (y, (lo, hi)) in horizontal)
                        if (Math.Abs(y - cy) <= reach && hi - lo >= MatchLineMinSpanShare * page.WidthPts && Math.Abs(y - cy) < bestDistance)
                        { best = new MatchLine(lo, y, hi, y); bestDistance = Math.Abs(y - cy); }
                if (isVertical)
                    foreach (var (x, (lo, hi)) in vertical)
                        if (Math.Abs(x - cx) <= reach && hi - lo >= MatchLineMinSpanShare * page.HeightPts && Math.Abs(x - cx) < bestDistance)
                        { best = new MatchLine(x, lo, x, hi); bestDistance = Math.Abs(x - cx); }
                if (best is { } m && !found.Any(f => Math.Abs(f.X0 - m.X0) < 1 && Math.Abs(f.Y0 - m.Y0) < 1 && Math.Abs(f.X1 - m.X1) < 1 && Math.Abs(f.Y1 - m.Y1) < 1))
                    found.Add(m);
            }
            foreach (var m in found) yield return m;
        }

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
            /// <summary>The lines labelled MATCH LINE, each spanning the drawing (step 22); usually none or one.</summary>
            public IReadOnlyList<MatchLine> MatchLines { get; init; } = [];

            /// <summary>A two-point line lies on a match line when both ends sit on it within the axis tolerance and inside its extent.</summary>
            public bool IsOnMatchLine((double X, double Y) a, (double X, double Y) b)
            {
                foreach (var m in MatchLines)
                {
                    double dx = m.X1 - m.X0, dy = m.Y1 - m.Y0, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len <= 0) continue;
                    double ux = dx / len, uy = dy / len;
                    bool on = true;
                    foreach (var p in new[] { a, b })
                    {
                        double t = (p.X - m.X0) * ux + (p.Y - m.Y0) * uy, n = Math.Abs(-(p.X - m.X0) * uy + (p.Y - m.Y0) * ux);
                        if (n > AxisTolerance || t < -AxisTolerance || t > len + AxisTolerance) { on = false; break; }
                    }
                    if (on) return true;
                }
                return false;
            }

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
                MatchLines = MatchLines.Select(m => new MatchLine(m.X0 * factor, m.Y0 * factor, m.X1 * factor, m.Y1 * factor)).ToList(),
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

            // 1b. the north arrow: a compass — a stroked ring beside the word NORTH — and whatever is
            //     drawn inside it. Its filled shaft measured 48" x 6" at 1:96 and read as a wall on
            //     3 of 5 sets' plans (2026-09-08). The ring is the region; the word alone is not,
            //     because NORTH is also a word in notes on the plan.
            foreach (var region in NorthArrows(page)) regions.Add(region);

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
                MatchLines = MatchLines(page).ToList(),
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
        /// <summary>A compass ring is this many points across at least, and at most.</summary>
        public const double NorthArrowMinPts = 30, NorthArrowMaxPts = 200;

        /// <summary>
        /// The north arrows on the sheet: each stroked path of at least twelve points, 30 to
        /// 200 points across and no more than 2.5 times as long as wide (31168's is a ring, 31202's
        /// an arrow outline 1/2" x 1"), whose centre lies within two lengths of a word NORTH. The
        /// region is the path's box and the word's, together.
        /// </summary>
        public static IEnumerable<Region> NorthArrows(VectorPageReader.PageContent page)
        {
            var norths = page.Words.Where(w => w.Text.Equals("NORTH", StringComparison.OrdinalIgnoreCase)).ToList();
            if (norths.Count == 0) yield break;
            foreach (var p in page.Paths)
            {
                // closed or open: 31202's arrow outline is an open 17-point polyline
                if (!p.IsStroked || p.IsAnnotation || p.Points.Count < 12) continue;
                double w = p.Width, h = p.Height, d = Math.Max(w, h);
                if (d < NorthArrowMinPts || d > NorthArrowMaxPts || d > 2.5 * Math.Min(w, h)) continue;
                double cx = (p.MinX + p.MaxX) / 2, cy = (p.MinY + p.MaxY) / 2;
                var near = norths.Where(n => Math.Sqrt((n.Cx - cx) * (n.Cx - cx) + (n.Cy - cy) * (n.Cy - cy)) <= 2 * d).ToList();
                if (near.Count == 0) continue;
                var word = near[0];
                yield return new Region("north arrow",
                    Math.Min(p.MinX, word.MinX) - 2, Math.Min(p.MinY, word.MinY) - 2,
                    Math.Max(p.MaxX, word.MaxX) + 2, Math.Max(p.MaxY, word.MaxY) + 2);
            }
        }

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
