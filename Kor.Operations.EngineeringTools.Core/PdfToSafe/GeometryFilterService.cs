#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.Dxf;

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
        /// Half an inch of slack on the wall limits, the DXF side's own allowance: a wall drawn at
        /// exactly 4" or exactly 48" measures a hair under after the export's arithmetic, and a
        /// limit is a statement about walls, not about floating point.
        /// </summary>
        private const double WallLimitSlackMm = 12.7;

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

        /// <summary>
        /// A closed shape spanning at least this share of the page in both directions is the sheet's
        /// frame, not a slab: no floor plate is drawn the size of the paper.
        /// </summary>
        public const double SheetFrameMinShare = 0.6;

        /// <summary>
        /// A fill this light, on every channel, is the paper. A shape filled with it and drawn with
        /// no stroke is invisible ink: it knocks out hatching under a column or a footing, and it
        /// is not structure. Colours are quantized to 0xF0, so white is (240, 240, 240).
        /// </summary>
        /// <remarks>
        /// Measured on the five KOR jobs' schedule pages: every closed shape whose size matched a
        /// declared column mark was grey-filled (208) with no stroke, and the paper-filled shapes
        /// matched none — 192 of 31168 p11's 266 "columns" were 36" and 46" white squares around
        /// each real one, 31 of 31202 p17's 72, 7 of 31065 p14's 46.
        /// </remarks>
        public const byte PaperMinChannel = 0xF0;

        private static bool IsPaper((byte R, byte G, byte B) c)
            => c.R >= PaperMinChannel && c.G >= PaperMinChannel && c.B >= PaperMinChannel;

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
            double maxColumnAspect = DefaultMaxColumnAspect,
            SheetFurniture.Set? furniture = null,
            IList<PathFate>? fates = null,
            double minWallThicknessMm = PdfIntakeOptions.DefaultMinWallThicknessMm,
            double maxWallThicknessMm = PdfIntakeOptions.DefaultMaxWallThicknessMm,
            double minWallLengthMm = PdfIntakeOptions.DefaultMinWallLengthMm,
            double minWallAspect = PdfIntakeOptions.DefaultMinWallAspect)
        {
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;
            furniture ??= SheetFurniture.Set.Empty;

            for (int pathIndex = 0; pathIndex < rawSubpaths.Count; pathIndex++)
            {
                var sub = rawSubpaths[pathIndex];
                void Fate(PathReason reason, int? objectIndex = null)
                    => fates?.Add(new PathFate(pathIndex, PathFate.DispositionOf(reason), reason, objectIndex));
                var pts = sub.Points;
                var color = sub.Color;
                bool isClosed = sub.IsClosed;

                // ── Annotations-only mode ────────────────────────────────────
                // Bluebeam / PDF markup annotations ARE the structural model.
                // Page content is the architect's base drawing — skip it entirely.
                if (annotationsOnly && !sub.IsAnnotation)
                { Fate(PathReason.MarkupOnlyMode); continue; }

                // A PATH THAT DRAWS NOTHING IS NOT GEOMETRY. A closed path with neither fill nor stroke is a
                // clipping boundary or a construction artefact; it puts no ink on the page. On 31130 it was
                // 518 of the 1,557 "slabs" the DXF carried (2026-09-08).
                if (!sub.IsAnnotation && !sub.IsFilled && !sub.IsStroked) { Fate(PathReason.NoInk); continue; }

                // ── Sheet furniture ──────────────────────────────────────────
                // Whatever sits inside a schedule's border, a notes box or the title block is a
                // table cell, a tie sketch, a rule or a logo box, and it is not read as structure;
                // a line along a grid axis is the grid — see SheetFurniture.
                if (!sub.IsAnnotation && pts.Count > 0)
                {
                    double cx = (pts.Min(p => p.X) + pts.Max(p => p.X)) / 2;
                    double cy = (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2;
                    if (furniture.IsFurniture(cx, cy))
                    { Fate(PathReason.FurnitureRegion); continue; }

                    if (!isClosed && pts.Count == 2)
                    {
                        double dx = Math.Abs(pts[1].X - pts[0].X), dy = Math.Abs(pts[1].Y - pts[0].Y);
                        if (dx <= furniture.AxisTolerance && furniture.OnVerticalAxis(cx)) { Fate(PathReason.GridAxis); continue; }
                        if (dy <= furniture.AxisTolerance && furniture.OnHorizontalAxis(cy)) { Fate(PathReason.GridAxis); continue; }
                        if (dy <= furniture.AxisTolerance && furniture.IsUnderline(pts[0].X, pts[1].X, cy)) { Fate(PathReason.Underline); continue; }
                    }
                }

                // Invisible ink: a shape with no stroke filled the colour of the paper draws nothing.
                if (!sub.IsAnnotation && sub.IsFilled && !sub.IsStroked && IsPaper(color))
                { Fate(PathReason.PaperFill); continue; }

                // ── Classification ───────────────────────────────────────────

                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    double bboxW = maxX - minX;
                    double bboxH = maxY - minY;

                    // the sheet's own frame, drawn around everything: not a slab
                    if (!sub.IsAnnotation && bboxW >= SheetFrameMinShare * pageWidthMm && bboxH >= SheetFrameMinShare * pageHeightMm)
                    { Fate(PathReason.SheetFrame); continue; }

                    // ⭐ THE SHEET SAYS WHAT ITS COLUMNS ARE. A filled shape the size the column
                    // schedule declares is a column, whatever a size window or an aspect limit
                    // fitted to other sheets would make of it: 31138 declares PC7 at 18" x 60" and
                    // PC8 at 18" x 96", and the 3.0 aspect limit refused both on their own sheet.
                    if (!sub.IsAnnotation && sub.IsFilled && furniture.IsDeclaredColumnSize(bboxW, bboxH))
                    {
                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnIsAnnotation.Add(sub.IsAnnotation);
                        result.ColumnSizes.Add((bboxW, bboxH));
                        Fate(PathReason.BecameColumnByDeclaredSize, result.Columns.Count - 1);
                        continue;
                    }

                    // A WALL IS A FILLED RECTANGLE OF WALL PROPORTIONS. Declared columns win above.
                    // The banked defaults are 4"-60" thick, at least 48" long, and aspect at least 2.
                    // Measured 2026-09-08 on five sets: the candidates were four-vertex rectangles;
                    // on 31168 their counts were within four of Revit's (intake convergence brief 15).
                    if (!sub.IsAnnotation && sub.IsFilled)
                    {
                        var box = LoopGeometry.MinAreaBox(pts.Select(p => new DxfPoint(p.X, p.Y)).ToList());
                        // Half an inch of slack on the limits, as the DXF side carries (its LengthSlack):
                        // a wall drawn at exactly 4" or exactly 48" measures a hair under after the
                        // export's arithmetic, and a limit is a statement about walls, not about
                        // floating point.
                        if (box.Thickness >= minWallThicknessMm - WallLimitSlackMm && box.Thickness <= maxWallThicknessMm + WallLimitSlackMm
                            && box.Length >= minWallLengthMm - WallLimitSlackMm && box.Aspect >= minWallAspect)
                        {
                            if (pts.Count == 4)
                            {
                                result.Walls.Add(new WallPanel(pts,
                                    (box.AxisStart.X, box.AxisStart.Y), (box.AxisEnd.X, box.AxisEnd.Y), box.Thickness));
                                result.WallColors.Add(color);
                                result.WallIsAnnotation.Add(false);
                                Fate(PathReason.BecameWall, result.Walls.Count - 1);
                                continue;
                            }
                            if (pts.Count > 4) result.WallRibbonsNotSplit++;
                        }
                    }

                    bool looksLikeColumn = bboxW <= columnMaxSizeMm && bboxH <= columnMaxSizeMm;

                    if (!looksLikeColumn && diag >= slabMinDiagonalMm)
                    {
                        result.Slabs.Add(pts);
                        result.SlabColors.Add(color);
                        result.SlabIsAnnotation.Add(sub.IsAnnotation);
                        if (diag >= 300 && diag <= 2000)
                            result.DropPanelCandidates.Add(pts);
                        Fate(PathReason.BecameSlab, result.Slabs.Count - 1);
                    }
                    else if (looksLikeColumn)
                    {
                        // Column candidates must pass structural plausibility checks:
                        // 1. Both dimensions above minimum (filters annotation boxes, symbols)
                        // 2. Aspect ratio within maxColumnAspect (columns are roughly square, not elongated)
                        double minDim = Math.Min(bboxW, bboxH);
                        double maxDim = Math.Max(bboxW, bboxH);
                        if (!sub.IsAnnotation && minDim < columnMinDimMm) { Fate(PathReason.ColumnTooSmall); continue; }

                        // Additional filter: non-annotation, non-filled small closed shapes
                        // in black/gray are likely annotation boxes or symbols, not columns
                        if (!sub.IsAnnotation && !sub.IsFilled && IsBlackOrGray(color))
                        { Fate(PathReason.UnfilledSmallShape); continue; }

                        // For annotations, all small filled shapes are structural
                        // elements — classify as columns regardless of aspect ratio.
                        // User can right-click to reclassify elongated ones as Beam.
                        // For page content, keep the aspect filter.
                        if (!sub.IsAnnotation && maxDim > maxColumnAspect * minDim)
                        { Fate(PathReason.ColumnAspect); continue; }

                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnIsAnnotation.Add(sub.IsAnnotation);
                        result.ColumnSizes.Add((bboxW, bboxH));
                        Fate(PathReason.BecameColumnByShape, result.Columns.Count - 1);
                    }
                    else Fate(pts.Count < 2 ? PathReason.TooFewPoints : PathReason.TooShort);
                }
                else
                {
                    double len = PolygonProcessor.PathLength(pts);
                    if (len >= lineMinLengthMm)
                    {
                        if (excludeGridLines && pts.Count == 2 && len > gridThreshMm)
                        { Fate(PathReason.GridLineExcluded); continue; }

                        // the sheet's frame, drawn as four separate strokes rather than one closed
                        // rectangle, is a line the length of the paper: not a beam
                        if (!sub.IsAnnotation && pts.Count == 2)
                        {
                            double dx = Math.Abs(pts[1].X - pts[0].X), dy = Math.Abs(pts[1].Y - pts[0].Y);
                            if (dx >= SheetFrameMinShare * pageWidthMm && dy < dx * 0.01) { Fate(PathReason.FrameEdgeLine); continue; }
                            if (dy >= SheetFrameMinShare * pageHeightMm && dx < dy * 0.01) { Fate(PathReason.FrameEdgeLine); continue; }
                        }
                        result.Lines.Add(pts); result.LineColors.Add(color);
                        result.LineIsAnnotation.Add(sub.IsAnnotation);
                        Fate(PathReason.EmittedAsLine, result.Lines.Count - 1);
                    }
                    else Fate(pts.Count < 2 ? PathReason.TooFewPoints : PathReason.TooShort);
                }
            }
        }
    }
}
