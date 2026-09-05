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
        string? Ties,
        MarkRowScheduleReader.MarkRoute Route = MarkRowScheduleReader.MarkRoute.ScheduleColumn);

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

            var options = MarkRowScheduleReader.ColumnDefaults() with
            {
                RowWidthPts = rowWidth,
                HeadingBandFraction = HeadingBandFraction,
                MinDimensionMm = MinDimMm,
                MaxDimensionMm = MaxDimMm,
            };

            foreach (var row in MarkRowScheduleReader.ReadSchedule(page, options))
            {
                var size = row.DimensionsMm;
                if (size.Count < 2) continue;

                double w1 = Math.Min(size[0], size[1]);
                double d1 = Math.Max(size[0], size[1]);

                // One row per mark: a schedule states a mark once, but the mark is also printed
                // against every column on the plan, and those carry no size.
                if (!seen.Add(row.Mark)) continue;

                rows.Add(new ColumnScheduleRow(
                    row.Mark, w1, d1, row.StrengthMPa,
                    Reinforcing: VertsRe.Match(row.RowText) is { Success: true } v ? v.Value.Trim() : null,
                    Ties: TiesRe.Match(row.RowText) is { Success: true } t ? t.Value.Trim() : null,
                    Route: row.Route));
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

            return MarkRowScheduleReader.SchedulesOn(page, MarkRowScheduleReader.ColumnDefaults())
                .Select(h => new ScheduleHeading(h.X, h.Y, h.IsTarget, h.Title))
                .ToList();
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

            var owner = MarkRowScheduleReader.OwnerOf(
                x,
                y,
                headings.Select(h => new MarkRowScheduleReader.ScheduleHeading(h.X, h.Y, h.IsColumn, h.Title)).ToList(),
                band);

            return owner is { } h ? new ScheduleHeading(h.X, h.Y, h.IsTarget, h.Title) : null;
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
