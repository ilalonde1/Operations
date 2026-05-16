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

        /// <summary>
        /// Returns true if the quantized RGB color looks like black, near-black, or gray
        /// (i.e., architectural background line work rather than structural markup).
        /// Quantized values use 0xF0 masking, so (0,0,0)=black, (128,128,128)=gray, etc.
        /// </summary>
        private static bool IsBlackOrGray((byte R, byte G, byte B) c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            return (max - min) <= 0x20 && max <= 0xC0;
        }

        public static void Classify(
            IReadOnlyList<RawSubpath> rawSubpaths,
            ExtractedGeometry result,
            double slabMinDiagonalMm,
            double lineMinLengthMm,
            bool   excludeGridLines,
            double pageWidthMm,
            double pageHeightMm,
            bool   annotationsOnly = true,
            double columnMaxSizeMm = 1500.0,
            double columnMinDimMm = 200.0)
        {
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;

            foreach (var sub in rawSubpaths)
            {
                var pts = sub.Points;
                var color = sub.Color;
                bool isClosed = sub.IsClosed;

                // ── Annotations-only mode ────────────────────────────────────
                // Bluebeam / PDF markup annotations ARE the structural model.
                // Page content is the architect's base drawing — skip it entirely.
                if (annotationsOnly && !sub.IsAnnotation)
                    continue;

                // ── Classification ───────────────────────────────────────────

                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    double bboxW = maxX - minX;
                    double bboxH = maxY - minY;

                    bool looksLikeColumn = bboxW <= columnMaxSizeMm && bboxH <= columnMaxSizeMm;

                    if (!looksLikeColumn && diag >= slabMinDiagonalMm)
                    {
                        result.Slabs.Add(pts);
                        result.SlabColors.Add(color);
                        if (diag >= 300 && diag <= 2000)
                            result.DropPanelCandidates.Add(pts);
                    }
                    else if (looksLikeColumn)
                    {
                        // Column candidates must pass structural plausibility checks:
                        // 1. Both dimensions above minimum (filters annotation boxes, symbols)
                        // 2. Aspect ratio <= 2.5 (columns are roughly square, not elongated)
                        double minDim = Math.Min(bboxW, bboxH);
                        double maxDim = Math.Max(bboxW, bboxH);
                        if (!sub.IsAnnotation && minDim < columnMinDimMm) continue;

                        // Additional filter: non-annotation, non-filled small closed shapes
                        // in black/gray are likely annotation boxes or symbols, not columns
                        if (!sub.IsAnnotation && !sub.IsFilled && IsBlackOrGray(color))
                            continue;

                        // For annotations, all small filled shapes are structural
                        // elements — classify as columns regardless of aspect ratio.
                        // User can right-click to reclassify elongated ones as Beam.
                        // For page content, keep the 2.5 aspect ratio filter.
                        if (!sub.IsAnnotation && maxDim > 2.5 * minDim)
                            continue;

                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnSizes.Add((bboxW, bboxH));
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
