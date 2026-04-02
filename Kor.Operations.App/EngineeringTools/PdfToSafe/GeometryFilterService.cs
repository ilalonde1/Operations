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
            // After quantization to 16-level steps (0x00, 0x10, … 0xF0),
            // architectural line work is typically black (0,0,0) or dark gray.
            // Structural markup uses saturated colors (red, magenta, blue, cyan).
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            // If all channels are within one quantization step of each other,
            // it's a shade of gray. Also catch pure black.
            return (max - min) <= 0x20 && max <= 0xC0;
        }

        /// <summary>
        /// Returns true if the point falls inside the title block exclusion zone
        /// (rightmost 18% of the page — where title blocks, legends, and revision
        /// tables typically live on structural drawings).
        /// </summary>
        private static bool InTitleBlockZone(
            double x, double y,
            double pageWidthMm, double pageHeightMm)
        {
            return x > pageWidthMm * 0.82;
        }

        public static void Classify(
            IReadOnlyList<RawSubpath> rawSubpaths,
            ExtractedGeometry result,
            double slabMinDiagonalMm,
            double lineMinLengthMm,
            bool   excludeGridLines,
            double pageWidthMm,
            double pageHeightMm,
            double columnMaxSizeMm = 1500.0,
            double columnMinDimMm = 200.0)
        {
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;

            foreach (var sub in rawSubpaths)
            {
                var pts = sub.Points;
                var color = sub.Color;
                bool isClosed = sub.IsClosed;

                // ── Pre-filter: skip architectural background noise ──────────

                // Non-annotation, stroke-only (not filled), black/gray = architectural line work
                // (parking stall lines, dimension strings, hatching, grid lines)
                if (!sub.IsAnnotation && !sub.IsFilled && IsBlackOrGray(color))
                    continue;

                // Non-annotation, stroke-only, very thin lines = dimension leaders, hatching
                if (!sub.IsAnnotation && !sub.IsFilled && sub.LineWidth < 0.5 && !isClosed)
                    continue;

                // Title block exclusion: skip non-annotation geometry whose centroid
                // falls in the title block zone (right edge of the page)
                if (!sub.IsAnnotation && pts.Count >= 2)
                {
                    double cx = pts.Average(p => p.X);
                    double cy = pts.Average(p => p.Y);
                    if (InTitleBlockZone(cx, cy, pageWidthMm, pageHeightMm))
                        continue;
                }

                // ── Classification (same logic as before, now with cleaner input) ──

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
                        if (minDim < columnMinDimMm) continue;
                        if (maxDim > 2.5 * minDim) continue;

                        // Additional filter: non-annotation, non-filled small closed shapes
                        // in black/gray are likely annotation boxes or symbols, not columns
                        if (!sub.IsAnnotation && !sub.IsFilled && IsBlackOrGray(color))
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
