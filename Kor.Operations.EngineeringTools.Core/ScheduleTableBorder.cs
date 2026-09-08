#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The border a schedule draws around itself, read off the page's own linework, and the row
    /// cells inside it. This is how <see cref="MarkRowScheduleReader"/> knows where a table ENDS.
    /// </summary>
    /// <remarks>
    /// ⭐ A TABLE STATES ITS OWN EXTENT. Every earlier bound on a schedule's rows was a number that
    /// fitted one sheet and failed the next: a 260pt scope, the nearest heading, a band of ±18% of
    /// the page width around the heading. Each let a token from a neighbouring table, or the second
    /// line of a wrapped cell, become the leftmost "mark" of a row — 31130's footings read 48 cy
    /// under the marks 15M and 20M out of the shear wall schedule beside them, and 31065 p14 read
    /// 8-35M and BOT. as column marks. Measured 2026-09-04 across the five KOR stick files (294
    /// pages, 311 schedule titles): every FOUNDATION and COLUMN table the readers target is ruled,
    /// 12 of 12 and 55 of 55 real ones, so the rules ARE the extent.
    ///
    /// HOW THE BORDER IS FOUND. Every straight segment of every path becomes a horizontal or a
    /// vertical rule; collinear pieces that abut are one rule, because a CAD export draws one table
    /// line as a piece per cell. The topmost horizontal rule just under the title that spans the
    /// title's left edge is the top; its span is the sides; the bottom is CHAINED — any vertical
    /// inside the span that touches the box extends it — because a table's side can be drawn per
    /// row, and taking only the verticals that meet the top rule cut 31130's FOUNDATION table off
    /// above its last row (SF1) when it was first measured.
    ///
    /// ROWS ARE CELLS. A row is the band between two horizontal rules that cross the mark column;
    /// a wrapped cell has two lines and one rule above and below, so both lines belong to one row.
    /// A rule that stops short of the border is still a separator when it crosses the mark column's
    /// centre: 31138 p9 draws the rule between PC4 and PC5 43pt short of its left edge.
    ///
    /// WHAT THIS DOES NOT COVER. A title with a rule under it and no table returns null — a box
    /// under <see cref="MinHeightPts"/> tall is an underline, not a table — and the caller falls
    /// back to its band (31202's column schedules are titled, underlined, and drawn cell by cell
    /// with no mark column). Two tables stacked in one border chain into one box (31065's STEEL
    /// BEAM &amp; COLUMN schedule is two tables in one border by design); the rows still read, each
    /// under its own header. A separator missing across the WHOLE width merges two rows, and the
    /// reader splits them again only where the mark cell then holds two marks on two lines. And a
    /// table with no drawn rules at all is invisible here by construction — that is what the band
    /// fallback is for, and <see cref="MarkRowScheduleReader.MarkRoute"/> says which one read a row.
    /// </remarks>
    public static class ScheduleTableBorder
    {
        /// <summary>A rule: its span along its own direction, and its position across it.</summary>
        public readonly record struct Rule(double Lo, double Hi, double At)
        {
            public double Length => Hi - Lo;
        }

        /// <summary>Horizontal rules are (x-span, y); vertical rules are (y-span, x). PDF points, y-up.</summary>
        public sealed record Rules(IReadOnlyList<Rule> Horizontal, IReadOnlyList<Rule> Vertical);

        /// <summary>One table's drawn extent and the rules inside it that make its rows and cells.</summary>
        public sealed record Border(
            double MinX,
            double MinY,
            double MaxX,
            double MaxY,
            IReadOnlyList<double> RowRuleYs,
            IReadOnlyList<Rule> ColumnRules)
        {
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;

            /// <summary>The x of every column rule, left to right.</summary>
            public IReadOnlyList<double> ColumnRuleXs { get; } = ColumnRules.Select(r => r.At).OrderBy(x => x).ToList();

            /// <summary>
            /// Whether a band is a ruled row — at least one column rule runs through it — as opposed
            /// to a merged full-width cell: the title row, or the NOTES the drafter boxes under the
            /// last row. A table with no column rules at all has only ruled rows.
            /// </summary>
            public bool IsRuledRow(double top, double bottom)
            {
                if (ColumnRules.Count == 0) return true;
                double height = top - bottom;
                return ColumnRules.Any(v => Math.Min(v.Hi, top) - Math.Max(v.Lo, bottom) >= 0.5 * height);
            }

            /// <summary>Row bands, top of the table first. PDF points are y-up, so Top &gt; Bottom.</summary>
            public IReadOnlyList<(double Top, double Bottom)> RowBands()
            {
                var edges = new List<double> { MaxY };
                edges.AddRange(RowRuleYs.OrderByDescending(y => y));
                edges.Add(MinY);

                var bands = new List<(double Top, double Bottom)>();
                for (int i = 1; i < edges.Count; i++)
                {
                    // a thick rule drawn as a filled polygon leaves two rules a point apart; no text
                    // fits between them, so that is not a row
                    if (edges[i - 1] - edges[i] >= MinRowHeightPts)
                        bands.Add((edges[i - 1], edges[i]));
                }
                return bands;
            }

            /// <summary>Which cell column a point at x falls in: the number of column rules left of it.</summary>
            public int CellOf(double x) => ColumnRuleXs.Count(r => r < x);

            public bool Contains(double x, double y)
                => x >= MinX - 1 && x <= MaxX + 1 && y >= MinY - 1 && y <= MaxY + 1;

            /// <summary>
            /// A row's tokens in reading order: cell by cell left to right, each cell's lines top to
            /// bottom, each line left to right. A wrapped cell reads as one cell, and 16" x 48" stays
            /// "16\" x 48\"" whatever the sub-point drift between its glyphs' centres.
            /// </summary>
            /// <remarks>
            /// ⚠ Lines are clustered, not bucketed. Rounding Cy into 4pt buckets put the x of
            /// "16\" x 48\"" on a different line from its numbers on 31130 p11, the size never parsed,
            /// PC4 was lost, and the pattern route won the count on three of four sheets.
            /// </remarks>
            public IReadOnlyList<VectorPageReader.TextToken> InReadingOrder(IEnumerable<VectorPageReader.TextToken> tokens)
            {
                var ordered = new List<VectorPageReader.TextToken>();
                foreach (var cell in tokens.GroupBy(t => CellOf(t.Cx)).OrderBy(g => g.Key))
                {
                    var line = new List<VectorPageReader.TextToken>();
                    foreach (var t in cell.OrderByDescending(t => t.Cy))
                    {
                        if (line.Count > 0 && Math.Abs(line[0].Cy - t.Cy) > LineTolerancePts)
                        {
                            ordered.AddRange(line.OrderBy(l => l.Cx));
                            line.Clear();
                        }
                        line.Add(t);
                    }
                    ordered.AddRange(line.OrderBy(l => l.Cx));
                }
                return ordered;
            }
        }

        /// <summary>Tokens whose centres are within this of a line's first token are on that line.</summary>
        public const double LineTolerancePts = 4.0;

        /// <summary>A segment is a rule when it deviates less than this across its length.</summary>
        public const double StraightPts = 0.6;

        /// <summary>Pieces shorter than this are glyph strokes and hatch, not rules.</summary>
        public const double MinPiecePts = 4.0;

        /// <summary>Collinear pieces closer than this are one rule.</summary>
        public const double AbutPts = 1.0;

        /// <summary>How far outside the top rule's span a side vertical may sit.</summary>
        public const double SideSlackPts = 3.0;

        /// <summary>A vertical touches the box when its top is within this of the current bottom.</summary>
        public const double TouchPts = 3.0;

        /// <summary>A box shorter than this is a title underline, not a table.</summary>
        public const double MinHeightPts = 8.0;

        /// <summary>A band thinner than this holds no text and is not a row.</summary>
        public const double MinRowHeightPts = 2.0;

        /// <summary>The top rule must reach this far right of the title's left edge to be under it.</summary>
        public const double TopRuleMinReachPts = 60.0;

        /// <summary>And be at least this long: a table, not a leader or an underline of one word.</summary>
        public const double TopRuleMinLengthPts = 100.0;

        /// <summary>A vertical spanning at least this share of the table's height is a column rule.</summary>
        public const double ColumnRuleMinShare = 0.3;

        /// <summary>Every horizontal and vertical rule on the page, pieces merged.</summary>
        public static Rules RulesOn(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var horizontal = new List<Rule>();
            var vertical = new List<Rule>();
            foreach (var path in page.Paths)
            {
                if (path.IsAnnotation) continue;
                var pts = path.Points;
                int n = pts.Count;
                if (n < 2) continue;

                int last = path.IsClosed ? n : n - 1;
                for (int i = 0; i < last; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % n];
                    double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                    if (dy < StraightPts && dx >= MinPiecePts)
                        horizontal.Add(new Rule(Math.Min(a.X, b.X), Math.Max(a.X, b.X), (a.Y + b.Y) / 2));
                    else if (dx < StraightPts && dy >= MinPiecePts)
                        vertical.Add(new Rule(Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), (a.X + b.X) / 2));
                }
            }

            return new Rules(Merge(horizontal), Merge(vertical));
        }

        /// <summary>
        /// Cluster pieces by their position across the rule (within <see cref="StraightPts"/>), then
        /// within a cluster merge spans that overlap or abut within <see cref="AbutPts"/>.
        /// </summary>
        /// <remarks>
        /// ⚠ Comparing each piece only with the previous merged output is not enough: a piece sorted
        /// after a rule at nearly the same y whose span it never touched was swallowed by it, and the
        /// row separator between PC6 and PC7 on 31130 p12 vanished in the first measurement. The
        /// cluster must be formed first and the spans merged within it.
        /// </remarks>
        public static IReadOnlyList<Rule> Merge(IEnumerable<Rule> pieces)
        {
            ArgumentNullException.ThrowIfNull(pieces);

            var sorted = pieces.OrderBy(r => r.At).ToList();
            var merged = new List<Rule>();
            int i = 0;
            while (i < sorted.Count)
            {
                double at = sorted[i].At;
                int j = i;
                while (j < sorted.Count && Math.Abs(sorted[j].At - at) < StraightPts) j++;

                Rule? current = null;
                foreach (var piece in sorted.Skip(i).Take(j - i).OrderBy(r => r.Lo))
                {
                    if (current is { } c && piece.Lo <= c.Hi + AbutPts)
                    {
                        current = c with { Hi = Math.Max(c.Hi, piece.Hi) };
                    }
                    else
                    {
                        if (current is { } done) merged.Add(done);
                        current = piece;
                    }
                }
                if (current is { } tail) merged.Add(tail);
                i = j;
            }

            return merged;
        }

        /// <summary>
        /// The border under a schedule title, or null when the title has no table drawn under it.
        /// </summary>
        /// <param name="page">The page.</param>
        /// <param name="titleMinX">The title's left edge.</param>
        /// <param name="titleBottomY">The title's bottom edge (y-up: its smallest y).</param>
        /// <param name="reachBelowTitlePts">How far below the title's bottom the top rule may sit.</param>
        /// <param name="titleRowPts">How far above the top rule a side vertical may start (a boxed title row).</param>
        /// <param name="rules">The page's rules, if already extracted.</param>
        public static Border? Under(
            VectorPageReader.PageContent page,
            double titleMinX,
            double titleBottomY,
            double reachBelowTitlePts,
            double titleRowPts,
            Rules? rules = null)
        {
            ArgumentNullException.ThrowIfNull(page);
            rules ??= RulesOn(page);

            // the top: the highest horizontal rule just under the title that spans its left edge
            Rule? top = null;
            foreach (var h in rules.Horizontal)
            {
                if (h.At > titleBottomY + 4 || h.At < titleBottomY - reachBelowTitlePts) continue;
                if (h.Lo > titleMinX + 5 || h.Hi < titleMinX + TopRuleMinReachPts) continue;
                if (h.Length < TopRuleMinLengthPts) continue;
                if (top is null || h.At > top.Value.At) top = h;
            }
            if (top is not { } t) return null;

            double minX = t.Lo, maxX = t.Hi, maxY = t.At, minY = t.At;

            // the bottom: chained down through every vertical in the span that touches the box
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var v in rules.Vertical)
                {
                    if (v.At < minX - SideSlackPts || v.At > maxX + SideSlackPts) continue;
                    if (v.Hi > maxY + titleRowPts) continue;   // starts far above the table: a frame, not a side
                    if (v.Hi < minY - TouchPts) continue;      // does not touch the box
                    if (v.Lo < minY - 0.5)
                    {
                        minY = v.Lo;
                        grew = true;
                    }
                }
            }
            if (maxY - minY < MinHeightPts) return null;

            double height = maxY - minY;
            var columns = rules.Vertical
                .Where(v => v.At > minX + 5 && v.At < maxX - 5
                            && Math.Min(v.Hi, maxY) - Math.Max(v.Lo, minY) >= ColumnRuleMinShare * height)
                .OrderBy(v => v.At)
                .ToList();

            // a separator is a rule that crosses the mark column's centre, not one that reaches the edge
            double probeX = columns.Count > 0 ? (minX + columns[0].At) / 2 : minX + 10;
            var rowYs = rules.Horizontal
                .Where(h => h.At > minY + 1 && h.At < maxY - 1 && h.Lo <= probeX && h.Hi >= probeX)
                .Select(h => h.At)
                .OrderByDescending(y => y)
                .ToList();

            // a box with nothing ruled inside it is a frame around a title, not a table; the caller
            // falls back rather than reading nothing out of a border that was never one
            if (rowYs.Count == 0 && columns.Count == 0) return null;

            return new Border(minX, minY, maxX, maxY, rowYs, columns);
        }
    }
}
