#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    public static class GeometryFilterService
    {
        public static double BoundingBoxDiagonal(List<(double X, double Y)> pts)
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

        /// <summary>
        /// How much longer than it is wide a closed shape may be and still be a column.
        /// </summary>
        /// <remarks>
        /// This was the literal 2.5 in the aspect test below, and it silently discarded a declared
        /// column type. 31168's COLUMN SCHEDULE - PARKADE states `PC01 14" x 36"` — aspect 2.571 —
        /// and measured on that job's own sheets, 54 shapes at exactly 14x36 were rejected on p11
        /// and 32 on p12. Its commonest parkade column never reached a model, and the sheet still
        /// reported 220 "columns", which were isolation-joint squares.
        ///
        /// 3.0 is the value KorStandards already banks as `dxf.max-column-aspect`, replay-verified on the
        /// authority of ETABS-e2k. It admits every column these five KOR jobs declare, the most slender
        /// being TC04 at 12"x36". An earlier 3.2 here was invented rather than looked up.
        /// It is not a licence: the limit still exists to keep linework out, and
        /// <see cref="Classify"/> takes it as a parameter so a job can state its own.
        ///
        /// ⚠ Raising it admits more, and the differential across all five jobs is in the commit that
        /// changed it. If this moves again, run that comparison again — slabs and lines shift too.
        /// </remarks>
        public const double DefaultMaxColumnAspect = 3.0;

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
            double columnMinDimMm = 200.0,
            double maxColumnAspect = DefaultMaxColumnAspect)
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
                        result.SlabIsAnnotation.Add(sub.IsAnnotation);
                        if (diag >= 300 && diag <= 2000)
                            result.DropPanelCandidates.Add(pts);
                    }
                    else if (looksLikeColumn)
                    {
                        // Column candidates must pass structural plausibility checks:
                        // 1. Both dimensions above minimum (filters annotation boxes, symbols)
                        // 2. Aspect ratio within maxColumnAspect (columns are roughly square, not elongated)
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
                        // For page content, keep the aspect filter.
                        if (!sub.IsAnnotation && maxDim > maxColumnAspect * minDim)
                            continue;

                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnIsAnnotation.Add(sub.IsAnnotation);
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
                        result.LineIsAnnotation.Add(sub.IsAnnotation);
                    }
                }
            }
        }
    }
}
