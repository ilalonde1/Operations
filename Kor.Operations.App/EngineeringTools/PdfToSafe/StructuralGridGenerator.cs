#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// A single structural grid line with a label and ordinate in model mm coordinates.
    /// IsAlongX = true   line runs parallel to X-axis (constant Y ordinate, labelled A, B, C)
    /// IsAlongX = false  line runs parallel to Y-axis (constant X ordinate, labelled 1, 2, 3)
    /// </summary>
    internal sealed record StructuralGridLine(
        string Label,
        bool IsAlongX,
        double OrdMm);

    /// <summary>
    /// Generates a structural grid from a set of column positions by clustering
    /// near-coincident X and Y coordinates within a tolerance.
    /// </summary>
    internal static class StructuralGridGenerator
    {
        /// <summary>
        /// Generates grid lines from column centroid positions.
        /// Coordinates must already be centroid-shifted (model mm).
        /// </summary>
        /// <param name="columnPositions">Centroid-shifted column positions (mm).</param>
        /// <param name="clusterToleranceMm">
        /// Columns within this distance on a single axis are merged into one grid line.
        /// Typically 300500 mm  tight enough to merge offset duplicates, loose enough
        /// not to merge genuinely separate grid lines.
        /// </param>
        public static List<StructuralGridLine> Generate(
            IEnumerable<(double X, double Y)> columnPositions,
            double clusterToleranceMm = 400)
        {
            var cols = columnPositions.ToList();
            if (cols.Count == 0) return new List<StructuralGridLine>();

            var xOrds = ClusterOrdinates(cols.Select(c => c.X), clusterToleranceMm);
            var yOrds = ClusterOrdinates(cols.Select(c => c.Y), clusterToleranceMm);

            var result = new List<StructuralGridLine>();

            for (int i = 0; i < xOrds.Count; i++)
                result.Add(new StructuralGridLine(
                    Label: (i + 1).ToString(),
                    IsAlongX: false,
                    OrdMm: xOrds[i]));

            for (int i = 0; i < yOrds.Count; i++)
                result.Add(new StructuralGridLine(
                    Label: GridLabel(i),
                    IsAlongX: true,
                    OrdMm: yOrds[i]));

            return result;
        }

        /// <summary>
        /// Clusters a sequence of 1-D ordinates: sorts, then merges any pair within
        /// <paramref name="tol"/> into a single ordinate at their mean.
        /// Returns the cluster means in ascending order.
        /// </summary>
        private static List<double> ClusterOrdinates(IEnumerable<double> values, double tol)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return new List<double>();

            var clusters = new List<List<double>> { new List<double> { sorted[0] } };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - clusters[^1][^1] <= tol)
                    clusters[^1].Add(sorted[i]);
                else
                    clusters.Add(new List<double> { sorted[i] });
            }

            return clusters.Select(c => c.Average()).OrderBy(v => v).ToList();
        }

        /// <summary>Converts a zero-based index to a spreadsheet-column-style letter label (A, B  Z, AA, AB ).</summary>
        private static string GridLabel(int index)
        {
            var sb = new System.Text.StringBuilder();
            do
            {
                sb.Insert(0, (char)('A' + index % 26));
                index = index / 26 - 1;
            } while (index >= 0);
            return sb.ToString();
        }
    }
}
