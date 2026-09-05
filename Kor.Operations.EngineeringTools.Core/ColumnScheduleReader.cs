#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>One column mark as the drawing's COLUMN SCHEDULE declares it.</summary>
    /// <param name="Mark">PC1, TC02, C4 — the mark placed against each column on the plan.</param>
    /// <param name="WidthMm">The smaller printed plan dimension.</param>
    /// <param name="DepthMm">The larger printed plan dimension.</param>
    /// <param name="StrengthMPa">Concrete strength, when the schedule states one.</param>
    /// <param name="Reinforcing">Vertical bars as printed, when stated.</param>
    /// <param name="Ties">Tie arrangement as printed, when stated.</param>
    public sealed record ColumnScheduleRow(
        string Mark,
        double WidthMm,
        double DepthMm,
        double? StrengthMPa,
        string? Reinforcing,
        string? Ties);

    /// <summary>
    /// Reads a drawing's COLUMN SCHEDULE — mark, size, strength and reinforcing — from the native
    /// vector text. Deterministic: no raster, no vision, no API call.
    /// </summary>
    /// <remarks>
    /// This is the semantic layer a DXF cannot carry. A DXF says a rectangle is here; the schedule
    /// says that rectangle is PC1, 12x24 inches, 45 MPa, with 20-30M verticals and 15M ties at 8in.
    /// ETABS wants the section, S-Concrete wants the strength and the bars, and a rebar takeoff
    /// wants all of it — and every one of them is on the sheet already.
    ///
    /// There WAS a column-schedule reader: `takeoff sched-read column` calls
    /// PlanVisionClient.ReadColumnScheduleJsonAsync on a downscaled PNG. That is an AI read of a
    /// picture of text this project can read exactly, for free, and the same way twice.
    ///
    /// ⭐ CELLS ARE IDENTIFIED BY WHAT THEY ARE, NOT BY WHERE THEY SIT. Two KOR jobs print the same
    /// schedule in different column orders — 31130 is MARK | STRENGTH | SIZE, 31168 is MARK | SIZE |
    /// STRENGTH — so a reader keyed on position is right for one practice and silently wrong for the
    /// next. That is the mistake this file exists to avoid: FootingScheduleReader baked "millimetres"
    /// into its size pattern and returned 0 cy on 4 of 5 KOR jobs for months, silently, because a
    /// deterministic tool reporting a total does not look broken.
    ///
    /// So: whatever in the row reads as "a x b" is the size, whatever reads as a number before MPa
    /// is the strength, and the rest is reinforcing text. Nothing depends on the order.
    ///
    /// WHAT THIS READS: the mark, both plan dimensions in millimetres, the strength when stated, and
    /// the reinforcing and tie text as printed.
    ///
    /// WHAT IT DOES NOT: it does not parse the bar callout into a count and a size — "20-30M VERTS"
    /// stays a string, because what a rebar takeoff needs from it is a separate question with its own
    /// conventions. It does not know which storeys a mark applies to; a schedule banded by level
    /// needs the level ladder ScheduleGridReader recovers, and this returns one row per mark. And it
    /// cannot tell a COLUMN schedule from any other MARK | SIZE table on the same sheet by itself —
    /// the caller supplies the heading region, or accepts every mark-shaped row on the page.
    /// </remarks>
    public static class ColumnScheduleReader
    {
        // PC1, TC02, C4, CC12 — 1-3 letters then 1-2 digits, the same shape a footing mark takes.
        private static readonly Regex MarkRe = new(
            @"^[A-Z]{1,3}\d{1,2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // "45 MPa", "45MPa", "45 mpa"
        private static readonly Regex StrengthRe = new(
            @"(?<v>\d{2,3}(?:\.\d+)?)\s*MPa",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>How far right of the mark a row's cells are taken from, in PDF points.</summary>
        public const double DefaultRowWidth = 340.0;

        /// <summary>Plausible printed column dimensions, in millimetres.</summary>
        public const double MinDimMm = 150.0;    // 6 in
        public const double MaxDimMm = 3000.0;   // 10 ft

        /// <summary>
        /// How far either side of a heading its own table may sit, as a fraction of sheet width.
        /// </summary>
        /// <remarks>
        /// A structural sheet carries several MARK | SIZE tables side by side. 31130 page 12 has a
        /// FOUNDATION SCHEDULE and a PARKADE COLUMN SCHEDULE in the same bottom band, and unscoped
        /// the reader returned F1..F4 and SF1 — the footings, correctly parsed, out of the wrong
        /// table. Being right about the wrong table is the failure mode that looks most like success.
        ///
        /// ⚠ AND THE SCOPE CANNOT BE A DISTANCE IN POINTS. The first attempt used 260pt and measured
        /// as: 31130's rows sit 56-183pt below their heading, 31168's sit 287-512pt below theirs on a
        /// larger sheet. A constant read one job and silently returned nothing for the other — the
        /// same class of mistake as assuming millimetres, one level up.
        ///
        /// Nor is "the nearest heading" enough: on 31168 the column rows are physically CLOSER to the
        /// FOUNDATION SCHEDULE heading than to their own. What actually identifies a table is that
        /// its rows lie BELOW its heading and share its horizontal band, so that is what is used, and
        /// the band scales with the sheet.
        /// </remarks>
        public const double HeadingBandFraction = 0.18;

        /// <summary>
        /// Every mark-shaped row that reads as a column, scoped to a COLUMN SCHEDULE heading when the
        /// page has one.
        /// </summary>
        /// <param name="page">The page's native vector text.</param>
        /// <param name="rowWidth">How far right of the mark to gather the row from.</param>
        public static IReadOnlyList<ColumnScheduleRow> ReadSchedule(
            VectorPageReader.PageContent page,
            double rowWidth = DefaultRowWidth)
        {
            ArgumentNullException.ThrowIfNull(page);

            var rows = new List<ColumnScheduleRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Every schedule heading on the sheet, not just the column ones: a row is kept when the
            // table it belongs to is a column schedule, which cannot be decided without knowing where
            // the OTHER tables are.
            var headings = SchedulesOn(page);
            double band = page.WidthPts * HeadingBandFraction;

            // A mark with a readable SIZE beside it — the shape of a schedule row. This filter comes
            // first because the same marks are printed all over the PLAN as well: 31130 page 12
            // carries 52 PC* tokens and 45 of them label a column on the drawing, carrying no size.
            // Grouping before filtering let those plan labels join the schedule's own mark column and
            // drag its anchor up the sheet, which cost 31130 and 31138 every row they had.
            var candidates = new List<(VectorPageReader.TextToken Token, string Mark, string RowText, IReadOnlyList<double> Size)>();
            foreach (var token in page.Words)
            {
                string mark = token.Text.Trim();
                if (!MarkRe.IsMatch(mark)) continue;

                var cells = page.Words
                    .Where(w => Math.Abs(w.Cy - token.Cy) <= 6
                                && w.Cx > token.Cx
                                && w.Cx - token.Cx <= rowWidth)
                    .OrderBy(w => w.Cx)
                    .Select(w => w.Text)
                    .ToList();
                if (cells.Count == 0) continue;

                string rowText = string.Join(" ", cells).Replace(",", "");

                // The size is whatever in the row reads as "a x b" — wherever it sits.
                var size = PrintedLength.TryFindSizeMm(rowText);
                if (size is null || size.Count < 2) continue;
                if (Math.Min(size[0], size[1]) < MinDimMm || Math.Max(size[0], size[1]) > MaxDimMm) continue;

                candidates.Add((token, mark, rowText, size));
            }

            // ⭐ OWNERSHIP IS DECIDED PER MARK COLUMN, NOT PER ROW. Every row of a table shares one
            // mark column, so they belong to one heading by construction. Scoring each row on its own
            // let a heading that is sideways-but-near beat the table's real one further above, and
            // 31168 lost TC02..TC04 that way while keeping TC01 — half a table, which is worse than
            // none, because the total still looks like an answer.
            var allowed = new HashSet<double>();
            foreach (var column in candidates.GroupBy(c => Math.Round(c.Token.Cx / 15.0)))
            {
                var top = column.OrderByDescending(c => c.Token.Cy).First();   // y-up: the first row
                if (headings.Count == 0 ||
                    OwnerOf(top.Token.Cx, top.Token.Cy, headings, band) is { IsColumn: true })
                    allowed.Add(column.Key);
            }

            foreach (var (token, mark, rowText, size) in candidates)
            {
                if (!allowed.Contains(Math.Round(token.Cx / 15.0))) continue;

                double w1 = Math.Min(size[0], size[1]);
                double d1 = Math.Max(size[0], size[1]);

                // One row per mark: a schedule states a mark once, but the mark is also printed
                // against every column on the plan, and those carry no size.
                if (!seen.Add(mark)) continue;

                var s = StrengthRe.Match(rowText);
                double? strength = s.Success
                    ? double.Parse(s.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : null;

                rows.Add(new ColumnScheduleRow(
                    mark, w1, d1, strength,
                    Reinforcing: VertsRe.Match(rowText) is { Success: true } v ? v.Value.Trim() : null,
                    Ties: TiesRe.Match(rowText) is { Success: true } t ? t.Value.Trim() : null));
            }

            return rows;
        }

        /// <summary>One schedule heading on the sheet, and whether it is a COLUMN one.</summary>
        public readonly record struct ScheduleHeading(double X, double Y, bool IsColumn, string Title);

        /// <summary>
        /// Every "... SCHEDULE" heading on the page, with the words that qualify it.
        /// </summary>
        /// <remarks>
        /// The whole set is needed, not only the column ones. A row is kept because the table it sits
        /// under is a column schedule — and on 31168 the column rows are nearer the FOUNDATION
        /// heading than their own, so a reader that only knows where COLUMN SCHEDULE is cannot tell.
        /// </remarks>
        public static IReadOnlyList<ScheduleHeading> SchedulesOn(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);

            var found = new List<ScheduleHeading>();
            foreach (var w in page.Words)
            {
                if (!w.Text.StartsWith("SCHEDULE", StringComparison.OrdinalIgnoreCase)) continue;

                // the qualifying words on the same baseline, to its left: "PARKADE COLUMN SCHEDULE"
                var before = page.Words
                    .Where(s => Math.Abs(s.Cy - w.Cy) <= 6 && s.Cx < w.Cx && w.Cx - s.Cx <= 260)
                    .OrderBy(s => s.Cx)
                    .Select(s => s.Text)
                    .ToList();

                string title = string.Join(" ", before.Append("SCHEDULE"));
                bool isColumn = before.Any(t => t.StartsWith("COLUMN", StringComparison.OrdinalIgnoreCase));

                // anchor on the leftmost word of the heading, which is where its table starts
                double x = before.Count > 0
                    ? page.Words.Where(s => Math.Abs(s.Cy - w.Cy) <= 6 && s.Cx < w.Cx && w.Cx - s.Cx <= 260)
                                .Min(s => s.Cx)
                    : w.Cx;

                found.Add(new ScheduleHeading(x, w.Cy, isColumn, title));
            }
            return found;
        }

        /// <summary>
        /// Which table a row at (x, y) belongs to: the nearest heading ABOVE it whose horizontal band
        /// it falls in, scored so a heading far to the side loses to one directly overhead.
        /// </summary>
        /// <remarks>
        /// PDF points are y-up, so "above" is a GREATER Cy.
        /// </remarks>
        public static ScheduleHeading? OwnerOf(
            double x, double y, IReadOnlyList<ScheduleHeading> headings, double band)
        {
            ArgumentNullException.ThrowIfNull(headings);

            ScheduleHeading? best = null;
            double bestScore = double.MaxValue;

            foreach (var h in headings)
            {
                if (h.Y <= y) continue;                       // not above this row
                double dx = Math.Abs(x - h.X);
                if (dx > band) continue;                      // a different column of the sheet

                double score = (h.Y - y) + dx;
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        // "20-30M VERTS.", "14-25M @ 5\" VERTS" — a bar callout ending in VERT/VERTS.
        private static readonly Regex VertsRe = new(
            @"\d{1,2}\s*-\s*\d{1,2}M[^|]{0,24}?VERTS?\.?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "15M @ 8\" TIES", "10M @ 6\" TIES"
        private static readonly Regex TiesRe = new(
            @"\d{1,2}M\s*@\s*[^|]{0,14}?TIES?\.?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
