#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;   // ExtractedGeometry: the geometry half

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>One column found in the drawing's linework, and what the schedule says it should be.</summary>
    public sealed record ColumnAgreement(
        double XMm,
        double YMm,
        double WidthMm,
        double DepthMm,
        string? NearestMark,
        double? NearestMarkDistanceMm,
        bool SizeIsDeclaredSomewhere,
        bool SizeMatchesItsOwnMark);

    /// <summary>How far a sheet's geometry and its own schedule agree.</summary>
    public sealed record PlanScheduleAgreement(
        int ColumnsFound,
        int SizesDeclaredSomewhere,
        int MatchedToTheirOwnMark,
        int AttributedToAMark,
        IReadOnlyList<string> MarksDeclaredButNeverFound,
        IReadOnlyList<ColumnAgreement> Columns)
    {
        /// <summary>Of the columns found, the share whose size the schedule declares. 0 when none found.</summary>
        public double Score => ColumnsFound == 0 ? 0.0 : (double)SizesDeclaredSomewhere / ColumnsFound;
    }

    /// <summary>
    /// Check a drawing against itself: every column in the linework should be a size the sheet's own
    /// COLUMN SCHEDULE declares, and every declared mark should appear on the plan.
    /// </summary>
    /// <remarks>
    /// ⭐ THE POINT IS THAT IT NEEDS NOTHING ELSE. Every other gate in this repo compares a generated
    /// model against a reference model, an engineer's ruling, or a previous run — so none of them can
    /// say anything about the first sheet of a new job, which is exactly when a reader is most likely
    /// to be silently wrong about a practice it has not seen.
    ///
    /// A structural drawing carries the same facts twice: once as geometry, once as a schedule. They
    /// are produced by different parts of the drafting process and read here by completely separate
    /// code — GeometryFilterService off the vector paths, ColumnScheduleReader off the vector text.
    /// So agreement between them is real evidence, and disagreement is a number an engineer can be
    /// handed: "41 of 52 columns on this sheet are a size the schedule declares".
    ///
    /// That measurement is what this was built from. On 31130's S2.01.2 at 1:96, by hand: 52 columns
    /// found, and 41 of them 12x24, 24x24 or 24x30 — PC1, PC2 and PC5 on the same sheet.
    ///
    /// WHAT THIS CHECKS: that a found column's size is declared somewhere in the schedule; where a
    /// mark label sits near the column, that the size matches THAT mark; and which declared marks
    /// never appear in the geometry at all.
    ///
    /// WHAT IT DOES NOT CHECK: it says nothing about walls, slabs or beams. It cannot tell a column
    /// the reader MISSED from one the drawing does not have, because it only ever looks at what was
    /// found — a sheet where half the columns went unread scores well on the half that did. It does
    /// not know storeys, so a mark declared for level 20 and absent from a parkade plan reads as
    /// "declared but never found", which is correct for the sheet and misleading for the job. And a
    /// low score is a reason to LOOK, not proof of a defect: the sheet border and title block are in
    /// the geometry too, and a schedule may legitimately declare marks used on other sheets.
    /// </remarks>
    public static class PlanAgreesWithItsSchedule
    {
        /// <summary>How close two printed dimensions must be to be the same size, in mm.</summary>
        /// <remarks>
        /// A schedule states exact inches; the geometry is measured off drawn outlines, which carry
        /// line width and joinery. 25 mm is one inch — tight enough that 12x24 does not match 12x30,
        /// loose enough that a drawn 12x24 does not miss its own row.
        /// </remarks>
        public const double DefaultToleranceMm = 25.0;

        /// <summary>How near a mark label must be to a column to be taken as ITS label, in mm.</summary>
        /// <remarks>
        /// Marks are printed beside the column they name, not on it. 1500 mm is about five feet at
        /// full size — beyond that the nearest label is more likely a neighbour's.
        /// </remarks>
        public const double DefaultLabelReachMm = 1500.0;

        public static PlanScheduleAgreement Check(
            ExtractedGeometry geometry,
            IReadOnlyList<ColumnScheduleRow> declared,
            VectorPageReader.PageContent? page = null,
            double toleranceMm = DefaultToleranceMm,
            double labelReachMm = DefaultLabelReachMm)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            ArgumentNullException.ThrowIfNull(declared);

            // Mark labels on the plan, in the geometry's own millimetre space. ExtractedGeometry is
            // PDF points multiplied by the scale, and nothing else moves, so the same multiply puts
            // the words where the shapes are.
            double scale = geometry.ScaleDenominator * PdfToSafeConstants.PointsToMm;
            var marks = new HashSet<string>(declared.Select(d => d.Mark), StringComparer.OrdinalIgnoreCase);
            var labels = page is null || scale <= 0
                ? new List<(string Mark, double X, double Y)>()
                : page.Words
                    .Where(w => marks.Contains(w.Text.Trim()))
                    .Select(w => (Mark: w.Text.Trim(), X: w.Cx * scale, Y: w.Cy * scale))
                    .ToList();

            var results = new List<ColumnAgreement>(geometry.Columns.Count);
            var seenMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                var (cx, cy) = geometry.Columns[i];
                (double w, double d) = i < geometry.ColumnSizes.Count
                    ? geometry.ColumnSizes[i]
                    : (0.0, 0.0);

                double small = Math.Min(w, d), large = Math.Max(w, d);

                bool declaredSomewhere = declared.Any(r => Same(small, large, r, toleranceMm));

                string? nearest = null;
                double? nearestDistance = null;
                foreach (var label in labels)
                {
                    double dist = Math.Sqrt((label.X - cx) * (label.X - cx) + (label.Y - cy) * (label.Y - cy));
                    if (dist > labelReachMm) continue;
                    if (nearestDistance is null || dist < nearestDistance)
                    {
                        nearestDistance = dist;
                        nearest = label.Mark;
                    }
                }

                bool matchesOwn = false;
                if (nearest is not null)
                {
                    seenMarks.Add(nearest);
                    var row = declared.FirstOrDefault(r =>
                        string.Equals(r.Mark, nearest, StringComparison.OrdinalIgnoreCase));
                    matchesOwn = row is not null && Same(small, large, row, toleranceMm);
                }

                results.Add(new ColumnAgreement(
                    cx, cy, small, large, nearest, nearestDistance, declaredSomewhere, matchesOwn));
            }

            var never = declared
                .Select(d => d.Mark)
                .Where(m => !seenMarks.Contains(m))
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PlanScheduleAgreement(
                ColumnsFound: results.Count,
                SizesDeclaredSomewhere: results.Count(r => r.SizeIsDeclaredSomewhere),
                MatchedToTheirOwnMark: results.Count(r => r.SizeMatchesItsOwnMark),
                AttributedToAMark: results.Count(r => r.NearestMark is not null),
                MarksDeclaredButNeverFound: never,
                Columns: results);
        }

        private static bool Same(double small, double large, ColumnScheduleRow row, double toleranceMm)
        {
            double rs = Math.Min(row.WidthMm, row.DepthMm);
            double rl = Math.Max(row.WidthMm, row.DepthMm);
            return Math.Abs(small - rs) <= toleranceMm && Math.Abs(large - rl) <= toleranceMm;
        }
    }
}
