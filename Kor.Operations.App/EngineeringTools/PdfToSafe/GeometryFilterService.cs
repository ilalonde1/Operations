#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class GeometryFilterService
    {
        internal static double BoundingBoxDiagonal(List<(double X, double Y)> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double dx = maxX - minX, dy = maxY - minY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static void Classify(
            IReadOnlyList<(List<(double X, double Y)> Points, bool IsClosed, (byte R, byte G, byte B) Color)> rawSubpaths,
            ExtractedGeometry result,
            double slabMinDiagonalMm,
            double lineMinLengthMm,
            bool   excludeGridLines,
            double pageWidthMm,
            double pageHeightMm)
        {
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;

            foreach (var (pts, isClosed, color) in rawSubpaths)
            {
                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    if (diag >= slabMinDiagonalMm) { result.Slabs.Add(pts);                           result.SlabColors.Add(color);   }
                    else
                    {
                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                        result.ColumnSizes.Add((maxX - minX, maxY - minY));
                    }
                }
                else
                {
                    double len = PolygonProcessor.PathLength(pts);
                    if (len >= lineMinLengthMm)
                    {
                        if (excludeGridLines && pts.Count == 2 && len > gridThreshMm)
                            continue;
                        result.Lines.Add(pts); result.LineColors.Add(color);
                    }
                }
            }
        }
    }
}
