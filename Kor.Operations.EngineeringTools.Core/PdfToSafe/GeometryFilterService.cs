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

        /// <summary>A corner is square within about 3° (cos 87°).</summary>
        private const double RectangleCornerCos = 0.05;
        /// <summary>A rectangle fills its own oriented box; a taper or a self-crossing outline does not.</summary>
        private const double RectangleFillShare = 0.95;

        /// <summary>
        /// Four points are a rectangle when every corner is square and the polygon fills its oriented
        /// box. The wall rule tested the box and the vertex count only, so a filled trapezoid
        /// (0,0) (6000,0) (5800,300) (200,300) became a wall (audit F1, 2026-09-08).
        /// </summary>
        public static bool IsRectangle(List<(double X, double Y)> pts, double boxLength, double boxThickness)
        {
            ArgumentNullException.ThrowIfNull(pts);
            if (pts.Count != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % 4]; var c = pts[(i + 2) % 4];
                double e1x = b.X - a.X, e1y = b.Y - a.Y, e2x = c.X - b.X, e2y = c.Y - b.Y;
                double n = Math.Sqrt(e1x * e1x + e1y * e1y) * Math.Sqrt(e2x * e2x + e2y * e2y);
                if (n <= 0) return false;
                if (Math.Abs(e1x * e2x + e1y * e2y) > RectangleCornerCos * n) return false;
            }
            double area = Math.Abs(PolygonProcessor.PolygonAreaMm2(pts));
            return boxLength > 0 && boxThickness > 0 && area >= RectangleFillShare * boxLength * boxThickness;
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
            double minWallAspect = PdfIntakeOptions.DefaultMinWallAspect,
            IReadOnlyDictionary<int, int>? footingPieces = null)
        {
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;
            furniture ??= SheetFurniture.Set.Empty;

            // A PLAN TOO WIDE FOR ONE SHEET IS SPLIT ON A MATCH LINE (intake step 22): the sheet's
            // match lines, read by the furniture from the words MATCH LINE and the line spanning the
            // drawing beside them, go to the geometry so the DXF carries them on a MATCH layer and
            // the ETABS side joins the sheets that share one. The pieces of the line are read below.
            foreach (var m in furniture.MatchLines)
                result.MatchLines.Add(new PlanMatchLine((m.X0, m.Y0), (m.X1, m.Y1)));

            // A DOORWAY IS A PAPER-COLOURED FILL PAINTED OVER A WALL (intake step 14). The drafter
            // draws the wall its full length and knocks each opening out with a white rectangle
            // across it, so the paper fills are gathered before the walls are read and a wall is
            // split at those that lie on it: painted after it, covering its thickness, at least a
            // door wide. What remains on either side are the piers — the members the model carries
            // (31168 p22: the core's west face read as one 328" wall where Revit has 104", 41" and
            // 54" piers). A paper fill's fate is decided once the walls are known.
            var paperFills = new List<(int Index, List<(double X, double Y)> Pts)>();
            // A WALL IS WHAT ITS CLIP LETS THROUGH. Revit's export draws a core face as one fill the
            // length of the face, clipped (W n) to its piers by the path drawn just before it; the
            // fill shows only through the clip. The clip's pieces are gathered here and a wall drawn
            // right after its clip, with every piece on it, is emitted as those pieces (31168 p22:
            // 328" faces that are 74", 118" and 41" of pier). A clip is a no-ink path until then.
            var clipPieces = new List<(int Index, int PathOrdinal, List<(double X, double Y)> Pts)>();
            for (int i = 0; i < rawSubpaths.Count; i++)
            {
                var s = rawSubpaths[i];
                if (s.IsAnnotation || s.Points.Count < 3) continue;
                if (s.IsFilled && !s.IsStroked && IsPaper(s.Color)) paperFills.Add((i, s.Points));
                else if (!s.IsFilled && !s.IsStroked && s.IsClipping && s.PathOrdinal >= 0) clipPieces.Add((i, s.PathOrdinal, s.Points));
            }
            var deferredPaper = new List<int>();
            var deferredNoInk = new List<int>();
            var doorwayOf = new Dictionary<int, int>();
            var clipOf = new Dictionary<int, int>();
            int firstFate = fates?.Count ?? 0;
            void FateAt(int index, PathReason reason, int? objectIndex = null)
                => fates?.Add(new PathFate(index, PathFate.DispositionOf(reason), reason, objectIndex));

            for (int pathIndex = 0; pathIndex < rawSubpaths.Count; pathIndex++)
            {
                var sub = rawSubpaths[pathIndex];
                void Fate(PathReason reason, int? objectIndex = null)
                    => fates?.Add(new PathFate(pathIndex, PathFate.DispositionOf(reason), reason, objectIndex));

                // A DASH OF A FOOTING OUTLINE IS THE FOOTING'S. FootingOutlines read the page's dashed
                // rectangles against the foundation schedule before this loop; a piece it claimed is
                // accounted for here and goes nowhere else. Before 2026-09-08 these pieces were BEAM
                // lines or TooShort, and no footing was ever placed.
                if (footingPieces is not null && footingPieces.TryGetValue(pathIndex, out int footingIndex))
                {
                    // A box no label on the plan names is a box the size of a footing, and nothing on the
                    // sheet says it is one: its pieces are unaccounted, not read (audit F2, 2026-09-08).
                    bool labelled = footingIndex < result.Footings.Count && result.Footings[footingIndex].LabelledOnThePlan;
                    Fate(labelled ? PathReason.BecameFooting : PathReason.FootingBoxNoLabel, footingIndex);
                    continue;
                }
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
                // — unless it is the clip a wall is drawn through, which is known once the walls are read.
                if (!sub.IsAnnotation && !sub.IsFilled && !sub.IsStroked) { deferredNoInk.Add(pathIndex); continue; }

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
                        if (furniture.IsOnMatchLine(pts[0], pts[1])) { Fate(PathReason.MatchLine); continue; }
                    }
                }

                // Invisible ink: a shape with no stroke filled the colour of the paper draws nothing —
                // unless it is a doorway knocked out of a wall, which is known once the walls are read.
                if (!sub.IsAnnotation && sub.IsFilled && !sub.IsStroked && IsPaper(color))
                { deferredPaper.Add(pathIndex); continue; }

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
                    // The oriented box of a filled shape, once: the declared-size rule and the wall rule
                    // both read it, so a rotated column is judged by its own sides and not by the sheet's
                    // axes (audit F11, 2026-09-08: a declared 18 x 60 turned 30° matched no schedule
                    // size on the world axes and fell through to the wall rule).
                    var obox = !sub.IsAnnotation && sub.IsFilled && pts.Count >= 3
                        ? LoopGeometry.MinAreaBox(pts.Select(p => new DxfPoint(p.X, p.Y)).ToList()) : null;
                    bool declaredOnAxes = !sub.IsAnnotation && sub.IsFilled && furniture.IsDeclaredColumnSize(bboxW, bboxH);
                    bool declaredTurned = !declaredOnAxes && obox is { } ob && furniture.IsDeclaredColumnSize(ob.Length, ob.Thickness);
                    if (declaredOnAxes || declaredTurned)
                    {
                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnIsAnnotation.Add(sub.IsAnnotation);
                        result.ColumnSizes.Add(declaredOnAxes ? (bboxW, bboxH) : (obox!.Length, obox.Thickness));
                        Fate(PathReason.BecameColumnByDeclaredSize, result.Columns.Count - 1);
                        continue;
                    }

                    // A WALL IS A FILLED RECTANGLE OF WALL PROPORTIONS. Declared columns win above.
                    // The banked defaults are 4"-60" thick, at least 48" long, and aspect at least 2.
                    // Measured 2026-09-08 on five sets: the candidates were four-vertex rectangles;
                    // on 31168 their counts were within four of Revit's (intake convergence brief 15).
                    // Non-paper: a white fill is not poché whether or not it is stroked (audit F11).
                    if (obox is { } box && !IsPaper(color))
                    {
                        // Half an inch of slack on the limits, as the DXF side carries (its LengthSlack):
                        // a wall drawn at exactly 4" or exactly 48" measures a hair under after the
                        // export's arithmetic, and a limit is a statement about walls, not about
                        // floating point.
                        if (box.Thickness >= minWallThicknessMm - WallLimitSlackMm && box.Thickness <= maxWallThicknessMm + WallLimitSlackMm
                            && box.Length >= minWallLengthMm - WallLimitSlackMm && box.Aspect >= minWallAspect)
                        {
                            // four points are a rectangle only when they are one: a trapezoid's box passed
                            // these limits and became a wall (audit F1, 2026-09-08)
                            if (pts.Count == 4 && IsRectangle(pts, box.Length, box.Thickness))
                            {
                                int first = result.Walls.Count;
                                var clip = ClipPiecesOn(box, sub.PathOrdinal, clipPieces);
                                var doorways = DoorwaysOn(box, pathIndex, paperFills);
                                var piers = clip.Count == 0 && doorways.Count == 0
                                    ? []
                                    : Piers(clip.Count == 0 ? [(0.0, box.Length)] : clip.Select(c => (c.T0, c.T1)).ToList(), doorways);
                                if (piers.Count == 0)
                                {
                                    // No clip and no doorway, or nothing a panel wide was left: the wall as drawn.
                                    result.Walls.Add(new WallPanel(pts,
                                        (box.AxisStart.X, box.AxisStart.Y), (box.AxisEnd.X, box.AxisEnd.Y), box.Thickness));
                                    result.WallColors.Add(color);
                                    result.WallIsAnnotation.Add(false);
                                }
                                else
                                {
                                    foreach (var (t0, t1) in piers)
                                    {
                                        result.Walls.Add(Pier(box, t0, t1));
                                        result.WallColors.Add(color);
                                        result.WallIsAnnotation.Add(false);
                                    }
                                    foreach (var c in clip) clipOf[c.PathIndex] = first;
                                    foreach (var d in doorways)
                                    {
                                        doorwayOf[d.PathIndex] = result.Doorways.Count;
                                        result.Doorways.Add(new Doorway(Along(box, d.T0), Along(box, d.T1), box.Thickness, first));
                                    }
                                }
                                Fate(PathReason.BecameWall, first);
                                continue;
                            }
                            if (pts.Count > 4) result.WallRibbonsNotSplit++;
                        }
                    }

                    // A FILLED BAND THICKER THAN THE THICKEST WALL IS NOT A SLAB (intake step 21).
                    // 31168's parkade plans carry a 63"-66" grey band 127 ft long along the property
                    // line, the real 12"-15" wall drawn as two lines inside it; read as a slab it became
                    // the storey's only floor plate, a strip 5.5 ft wide, and stood in the way of the
                    // plate the perimeter walls enclose. It is not a wall (thicker than any) and not a
                    // floor (narrower than a bay, many times longer than wide); it stays unaccounted.
                    if (!sub.IsAnnotation && obox is { } bandBox && !IsPaper(color)
                        && bandBox.Thickness > maxWallThicknessMm + WallLimitSlackMm
                        && bandBox.Thickness <= BandMaxThicknessShare * maxWallThicknessMm
                        && bandBox.Aspect >= BandMinAspect)
                    { Fate(PathReason.Band); continue; }

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
                        result.Lines.Add(pts); result.LineColors.Add(color); result.LineWidths.Add(sub.LineWidth);
                        result.LineIsAnnotation.Add(sub.IsAnnotation);
                        Fate(PathReason.EmittedAsLine, result.Lines.Count - 1);
                    }
                    else Fate(pts.Count < 2 ? PathReason.TooFewPoints : PathReason.TooShort);
                }
            }

            // The paper fills, last: one that opened a wall is a doorway, the rest are invisible ink.
            // Then this call's fates back into path order, which is how every reader of them is written.
            foreach (int index in deferredPaper)
            {
                if (doorwayOf.TryGetValue(index, out int d)) FateAt(index, PathReason.Doorway, d);
                else FateAt(index, PathReason.PaperFill);
            }
            // And the no-ink paths: a clip a wall was drawn through is read as that wall's shape.
            foreach (int index in deferredNoInk)
            {
                if (clipOf.TryGetValue(index, out int w)) FateAt(index, PathReason.ClipOfWall, w);
                else FateAt(index, PathReason.NoInk);
            }
            // TWO FACE LINES A WALL'S THICKNESS APART ARE A WALL (intake step 20). A retaining wall
            // on a foundation plan is drawn as its two faces, unfilled — 31168 p11's west and south
            // perimeter, 31130 p11's west. Measured 2026-09-09 with pdf-overlay --face-pairs: the
            // pairs were those walls and, on 31130, 139 pairs inside the elevator pit's hatch at its
            // regular spacing. A hatch has a third line at the same spacing beyond either face; a
            // wall does not.
            if (FaceTrace is not null && fates is not null)
                for (int k = firstFate; k < fates.Count; k++)
                {
                    var f = fates[k];
                    if (f.PathIndex < 0 || f.PathIndex >= rawSubpaths.Count) continue;
                    var s = rawSubpaths[f.PathIndex];
                    if (s.Points.Count != 2 || !s.IsStroked || s.IsFilled) continue;
                    double ln = Math.Sqrt(Math.Pow(s.Points[1].X - s.Points[0].X, 2) + Math.Pow(s.Points[1].Y - s.Points[0].Y, 2));
                    if (ln >= FaceTraceMinOverlapMm)
                        FaceTrace($"line {ln / 25.4:0}\" w{s.LineWidth:0.00} at ({(s.Points[0].X + s.Points[1].X) / 2:0},{(s.Points[0].Y + s.Points[1].Y) / 2:0}) mm: {f.Reason}");
                }
            WallsFromFaceLines(result, fates, firstFate, minWallThicknessMm, maxWallThicknessMm, minWallLengthMm, minWallAspect);
            SlabEdgesFromLoops(result, fates, firstFate);

            if (fates is not null && (deferredPaper.Count > 0 || deferredNoInk.Count > 0))
            {
                var ordered = fates.Skip(firstFate).OrderBy(f => f.PathIndex).ToList();
                for (int i = 0; i < ordered.Count; i++) fates[firstFate + i] = ordered[i];
            }
        }

        /// <summary>Two faces are parallel within this cosine (about a degree).</summary>
        private const double FaceParallelCos = 0.9998;
        /// <summary>The two faces' separations at either end may differ by this much and still be one wall.</summary>
        private const double FaceTaperMm = 25.0;
        /// <summary>
        /// Or by a third of the mean gap: a wall's faces may converge (step 21). 31168 p11's
        /// property-line wall runs 127 ft at 15" narrowing to 12" — a fifth — its faces 0.13° apart.
        /// </summary>
        private const double FaceTaperShare = 1.0 / 3.0;
        /// <summary>A filled band this many times longer than it is thick, thicker than the thickest wall, is a band, not a slab (step 21).</summary>
        private const double BandMinAspect = 10.0;
        /// <summary>And no thicker than this many times the thickest wall; wider than that it is a floor.</summary>
        private const double BandMaxThicknessShare = 2.0;
        /// <summary>A third cut-pen line at the pair's spacing, within this share of it, beyond either face, makes the pair a pattern.</summary>
        private const double PatternSpacingShare = 0.15;
        /// <summary>A line is drawn with the cut pen when its width is within this share of the sheet's.</summary>
        private const double PenMatchShare = 0.10;
        /// <summary>A line across the pair is perpendicular within this cosine (about 10°): a riser, not a hatch.</summary>
        private const double AcrossCos = 0.17;
        /// <summary>A riser spans at least this share of the gap between the faces; stringers inset it from them.</summary>
        private const double RiserGapShare = 0.7;
        /// <summary>Risers closer than this along the pair are a run: a tread is at most 14".</summary>
        private const double TreadMaxMm = 14 * 25.4;
        /// <summary>This many risers in a run and the pair encloses a stair, not a wall.</summary>
        private const int RiserRunMin = 3;
        /// <summary>This many lighter lines along the pair between its faces and it encloses something drawn, not a wall's one batter line.</summary>
        private const int LighterBetweenMax = 2;
        /// <summary>Points along a pair's axis asked whether they lie in a wall or column already read; more than half inside and the pair is under it.</summary>
        private const int CoverSamples = 5;

        /// <summary>
        /// The lines drawn along the filled walls' faces: each with the side of the line its wall
        /// lies on (the sign of the wall's centre along the line's normal, the normal being the
        /// line's direction turned a quarter left) and the pen it was drawn with. A face line is
        /// one wall's; the face rule may pair it again only for a wall on the same side (piers
        /// along one outer face), never for one on its other side.
        /// </summary>
        internal static List<(int Line, int Side, double Width)> FilledWallFaceLines(ExtractedGeometry result)
        {
            var faces = new List<(int Line, int Side, double Width)>();
            foreach (var w in result.Walls)
            {
                // the wall's faces: the outline's edges along its axis
                double ax = w.End.X - w.Start.X, ay = w.End.Y - w.Start.Y, al = Math.Sqrt(ax * ax + ay * ay);
                if (al <= 0) continue;
                ax /= al; ay /= al;
                var o = w.Outline;
                double cx = o.Average(p => p.X), cy = o.Average(p => p.Y);
                for (int e = 0; e < o.Count; e++)
                {
                    var p0 = o[e]; var p1 = o[(e + 1) % o.Count];
                    double ex = p1.X - p0.X, ey = p1.Y - p0.Y, el = Math.Sqrt(ex * ex + ey * ey);
                    if (el <= 0) continue;
                    ex /= el; ey /= el;
                    if (Math.Abs(ex * ax + ey * ay) < FaceParallelCos) continue;
                    for (int i = 0; i < result.Lines.Count && i < result.LineWidths.Count; i++)
                    {
                        var l = result.Lines[i];
                        if (l.Count != 2) continue;
                        bool on = true;
                        foreach (var q in l)
                        {
                            double t = (q.X - p0.X) * ex + (q.Y - p0.Y) * ey, n = Math.Abs(-(q.X - p0.X) * ey + (q.Y - p0.Y) * ex);
                            if (n >= 2 || t <= -2 || t >= el + 2) { on = false; break; }
                        }
                        if (!on) continue;
                        double ll = Math.Sqrt(Math.Pow(l[1].X - l[0].X, 2) + Math.Pow(l[1].Y - l[0].Y, 2));
                        if (ll <= el / 2) continue;
                        // the side of THIS line (its own direction) the wall's centre lies on
                        double lx = (l[1].X - l[0].X) / ll, ly = (l[1].Y - l[0].Y) / ll;
                        int side = Math.Sign((cx - l[0].X) * -ly + (cy - l[0].Y) * lx);
                        faces.Add((i, side, result.LineWidths[i]));
                    }
                }
            }
            return faces;
        }

        /// <summary>
        /// The sheet's cut pen: the commonest stroke width of the lines drawn along the filled
        /// walls' faces (the fills carry no stroke of their own on 5 of 5 stick-file plans; their
        /// edges are separate lines, one pen on 259 of 264 measured). Zero when the sheet has no
        /// filled wall with a face line, and then no face wall is read.
        /// </summary>
        internal static double CutPen(ExtractedGeometry result)
        {
            var pens = FilledWallFaceLines(result).Select(f => f.Width).ToList();
            if (pens.Count == 0) return 0;
            return pens.GroupBy(p => p).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        }

        /// <summary>
        /// The walls drawn as two face lines (intake step 20). A wall is what the cut pen encloses:
        /// two lines in the sheet's cut pen, parallel within a degree, a wall's thickness apart,
        /// overlapping a wall's length, with no third cut-pen line between them or at the same
        /// spacing beyond either (a lighter line between is the wall's own batter or step; a hatch
        /// repeats its spacing), nothing drawn across them between their ends (a stair's risers),
        /// and not under a wall or column already read (a filled wall's edges and a column's
        /// outline are pairs too). One wall panel over the overlap; both lines re-fated as faces.
        /// </summary>
        /// <summary>
        /// An instrument, not a setting: when set, every candidate pair of cut-pen lines at least
        /// <see cref="FaceTraceMinOverlapMm"/> long reports why it was or was not a wall. The CLI's
        /// <c>pdf-overlay --walls</c> sets it; nothing in production does.
        /// </summary>
        internal static Action<string>? FaceTrace;
        internal const double FaceTraceMinOverlapMm = 2400;

        internal static void WallsFromFaceLines(ExtractedGeometry result, IList<PathFate>? fates, int firstFate,
            double minWallThicknessMm, double maxWallThicknessMm, double minWallLengthMm, double minWallAspect = PdfIntakeOptions.DefaultMinWallAspect)
        {
            result.FirstFaceWall = result.Walls.Count;
            double cutPen = CutPen(result);
            FaceTrace?.Invoke($"cut pen w{cutPen:0.00}");
            if (FaceTrace is not null)
                for (int i = 0; i < result.Lines.Count; i++)
                {
                    var l = result.Lines[i];
                    if (l.Count != 2) continue;
                    double ln = Math.Sqrt(Math.Pow(l[1].X - l[0].X, 2) + Math.Pow(l[1].Y - l[0].Y, 2));
                    if (ln >= FaceTraceMinOverlapMm)
                        FaceTrace($"emitted line {ln / 25.4:0}\" w{(i < result.LineWidths.Count ? result.LineWidths[i] : -1):0.00} at ({(l[0].X + l[1].X) / 2:0},{(l[0].Y + l[1].Y) / 2:0}) mm{(result.LineIsAnnotation[i] ? " (annotation)" : "")}");
                }
            if (cutPen <= 0) return;
            bool CutPenLine(int i) => i < result.LineWidths.Count && Math.Abs(result.LineWidths[i] - cutPen) <= PenMatchShare * cutPen;

            // every straight line long enough to be a face, and which are in the cut pen
            var segs = new List<(int Line, (double X, double Y) A, (double X, double Y) B, double Len, double Ux, double Uy, bool Cut)>();
            for (int i = 0; i < result.Lines.Count; i++)
            {
                var line = result.Lines[i];
                if (line.Count != 2 || result.LineIsAnnotation[i]) continue;
                double dx = line[1].X - line[0].X, dy = line[1].Y - line[0].Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < minWallLengthMm - WallLimitSlackMm) continue;
                segs.Add((i, line[0], line[1], len, dx / len, dy / len, CutPenLine(i)));
            }
            if (segs.Count(s => s.Cut) < 2) return;

            // the perpendicular offset of segment j's ends from segment i's line, and their overlap along it
            static (double D0, double D1, double T0, double T1) Relative(
                (int Line, (double X, double Y) A, (double X, double Y) B, double Len, double Ux, double Uy, bool Cut) a,
                (int Line, (double X, double Y) A, (double X, double Y) B, double Len, double Ux, double Uy, bool Cut) b)
            {
                double nx = -a.Uy, ny = a.Ux;
                double d0 = (b.A.X - a.A.X) * nx + (b.A.Y - a.A.Y) * ny, d1 = (b.B.X - a.A.X) * nx + (b.B.Y - a.A.Y) * ny;
                double t0 = (b.A.X - a.A.X) * a.Ux + (b.A.Y - a.A.Y) * a.Uy, t1 = (b.B.X - a.A.X) * a.Ux + (b.B.Y - a.A.Y) * a.Uy;
                if (t0 > t1) (t0, t1) = (t1, t0);
                return (d0, d1, Math.Max(0, t0), Math.Min(a.Len, t1));
            }

            // what is already read where the pair would sit: a filled wall's outline, a column's box
            var columnBoxes = result.Columns.Select((c, i) =>
            {
                var (w, d) = i < result.ColumnSizes.Count ? result.ColumnSizes[i] : (0.0, 0.0);
                return (X0: c.X - w / 2 - WallLimitSlackMm, X1: c.X + w / 2 + WallLimitSlackMm, Y0: c.Y - d / 2 - WallLimitSlackMm, Y1: c.Y + d / 2 + WallLimitSlackMm);
            }).ToList();
            // the walls read so far, the face walls made in this pass included: a pair that lies in
            // a wall already made is that wall's own edges, or a stub against its face. Judged along
            // the pair, not at one point — at its midpoint alone a wall crossing another read as
            // under it, or not, by which was made first (Codex 31, F6): a wall is under another
            // when most of its length is
            bool Covered(double x, double y)
                => result.Walls.Any(w => LoopGeometry.PointInPolygon(new DxfPoint(x, y), w.Outline.Select(p => new DxfPoint(p.X, p.Y)).ToList()))
                   || columnBoxes.Any(b => x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1);
            bool CoveredAlong((double X, double Y) from, (double X, double Y) to)
            {
                int inside = 0;
                for (int k = 1; k <= CoverSamples; k++)
                    if (Covered(from.X + (to.X - from.X) * k / (CoverSamples + 1), from.Y + (to.Y - from.Y) * k / (CoverSamples + 1))) inside++;
                return inside * 2 > CoverSamples;
            }

            var lineToPath = new Dictionary<int, int>();
            if (fates is not null)
                for (int k = firstFate; k < fates.Count; k++)
                    if (fates[k].Reason == PathReason.EmittedAsLine && fates[k].ObjectIndex is int li) lineToPath[li] = fates[k].PathIndex;

            // which side of each face line its wall lies on, for the filled walls' face lines now
            // and for every face wall as it is made
            var faceSide = new Dictionary<int, int>();
            foreach (var f in FilledWallFaceLines(result))
                if (f.Side != 0) faceSide[f.Line] = f.Side;

            // every qualifying pair first, then the longest overlap first: a face line may serve more
            // than one wall (a retaining wall's outer face against the piers on its inner side), and
            // a 96" stub must not take the face a 1,527" wall needs (31168 p11's property-line wall,
            // whose outer face a short line at its foot paired with first while the walk went by index)
            var pairs = new List<(int I, int J, double Gap, (double D0, double D1, double T0, double T1) Rel)>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (!segs[i].Cut) continue;
                for (int j = i + 1; j < segs.Count; j++)
                {
                    if (!segs[j].Cut) continue;
                    var a = segs[i]; var b = segs[j];
                    if (Math.Abs(a.Ux * b.Ux + a.Uy * b.Uy) < FaceParallelCos) continue;
                    var rel = Relative(a, b);
                    double gap = Math.Abs((rel.D0 + rel.D1) / 2);
                    // the trace hears why a long pair never qualified too, so "every pair" is every
                    // pair (Codex 31, F13): both lines long enough to trace, within twice a wall
                    bool traced = FaceTrace is not null && a.Len >= FaceTraceMinOverlapMm && b.Len >= FaceTraceMinOverlapMm && gap <= 2 * maxWallThicknessMm;
                    void Never(string reason)
                    {
                        if (traced) FaceTrace!($"lines {a.Len / 25.4:0}\"/{b.Len / 25.4:0}\" {gap / 25.4:0.0}\" apart at ({(a.A.X + a.B.X) / 2:0},{(a.A.Y + a.B.Y) / 2:0}) mm: not a pair, {reason}");
                    }
                    // the faces may converge: by an inch, or by a third of the gap, with a wall's
                    // thickness at both ends (step 21)
                    if (Math.Abs(rel.D0 - rel.D1) > Math.Max(FaceTaperMm, FaceTaperShare * gap)) { Never($"they converge by {Math.Abs(rel.D0 - rel.D1) / 25.4:0.0}\""); continue; }
                    if (Math.Min(Math.Abs(rel.D0), Math.Abs(rel.D1)) < minWallThicknessMm - WallLimitSlackMm
                        || Math.Max(Math.Abs(rel.D0), Math.Abs(rel.D1)) > maxWallThicknessMm + WallLimitSlackMm) { Never("the gap is outside wall thicknesses"); continue; }
                    if (rel.T1 - rel.T0 < minWallLengthMm - WallLimitSlackMm) { Never($"they overlap by only {(rel.T1 - rel.T0) / 25.4:0}\""); continue; }
                    // and a wall's proportions, as the filled rule asks: a box of lines 49" x 38"
                    // around an unfilled column (31168's tower plans, sixteen a sheet) is not a wall
                    if (rel.T1 - rel.T0 < minWallAspect * gap) { Never($"the overlap is under {minWallAspect:0.#} times the gap"); continue; }
                    pairs.Add((i, j, gap, rel));
                }
            }
            if (FaceTrace is not null)
                for (int i = 0; i < segs.Count; i++)
                    if (segs[i].Cut && segs[i].Len >= FaceTraceMinOverlapMm && !pairs.Any(p => p.I == i || p.J == i))
                        FaceTrace($"cut line {segs[i].Len / 25.4:0}\" at ({(segs[i].A.X + segs[i].B.X) / 2:0},{(segs[i].A.Y + segs[i].B.Y) / 2:0}) mm: no partner a wall's thickness away");

            // and a line is one wall's face, the longest wall's: letting a face serve several walls
            // (piers along one outer face) read the balcony bands along 31168's tower slab edges as
            // walls, 32 a sheet where Revit has none — a spandrel is two lines too. Measured
            // 2026-09-09: 750 walls to the model's 666 with faces shared, 681 with each face used once.
            //
            // A LINE IS TAKEN WHOLE, AND THAT COSTS THE SECOND PIER. Codex 31 (F7) is right that a
            // retaining wall's outer face drawn as one line, with its inner face broken by pilasters,
            // gives the longest pier only; the rest of the outer face is spent. Letting a wall take
            // just the STRETCH it lies along was built and measured on 2026-09-09, and it is worse:
            // on 31168's BLDG A tower plan (p23) the walls went 28 to 52, every new one a balcony
            // band beside a balcony's own box, and the same on B and C. The balconies pair with the
            // slab-edge line one after another exactly as pilasters do, so no test on the pair alone
            // separates them. Kept whole, with the cost recorded rather than traded for that one.
            var used = new HashSet<int>();
            foreach (var pr in pairs.OrderByDescending(p => p.Rel.T1 - p.Rel.T0).ThenBy(p => p.Gap))
            {
                int i = pr.I, bestJ = pr.J;
                if (used.Contains(segs[i].Line) || used.Contains(segs[bestJ].Line)) continue;
                {
                    var a = segs[i]; var b = segs[bestJ];
                    var (d0, d1, t0, t1) = pr.Rel;
                    double gap = pr.Gap;
                    double side = Math.Sign((d0 + d1) / 2);
                    double nx = -a.Uy, ny = a.Ux;

                    double mx = a.A.X + a.Ux * (t0 + t1) / 2 + nx * side * gap / 2, my = a.A.Y + a.Uy * (t0 + t1) / 2 + ny * side * gap / 2;
                    string tag = FaceTrace is not null && t1 - t0 >= FaceTraceMinOverlapMm
                        ? $"pair {(t1 - t0) / 25.4:0}\" x {gap / 25.4:0.0}\" (ends {Math.Abs(d0) / 25.4:0.0}\"/{Math.Abs(d1) / 25.4:0.0}\") at ({mx:0},{my:0}) mm" : "";
                    void Why(string reason) { if (tag.Length > 0) FaceTrace!(tag + ": " + reason); }

                    var axisFrom = (a.A.X + a.Ux * t0 + nx * side * gap / 2, a.A.Y + a.Uy * t0 + ny * side * gap / 2);
                    var axisTo = (a.A.X + a.Ux * t1 + nx * side * gap / 2, a.A.Y + a.Uy * t1 + ny * side * gap / 2);
                    if (CoveredAlong(axisFrom, axisTo)) { Why("under a wall or column already read"); continue; }

                    // a face line is one wall's: a line already the face of a filled wall, or of a
                    // face wall made in this pass, may serve again only for a wall on the SAME side
                    // (piers along one outer face), never on its other side — the 6" walls flanking a
                    // stair flight (31168 p11, 31138 p9) and the 8" ramp wall beside a 58" gap (31202)
                    int sideOfA = (int)side;
                    double amx = a.A.X + a.Ux * (t0 + t1) / 2, amy = a.A.Y + a.Uy * (t0 + t1) / 2;
                    int sideOfB = Math.Sign((amx - b.A.X) * -b.Uy + (amy - b.A.Y) * b.Ux);
                    if (faceSide.TryGetValue(a.Line, out int usedA) && usedA != sideOfA) { Why("its first face is another wall's, on its other side"); continue; }
                    if (faceSide.TryGetValue(b.Line, out int usedB) && usedB != sideOfB) { Why("its second face is another wall's, on its other side"); continue; }

                    // a pattern: another cut-pen line at the same spacing beyond either face, or any
                    // cut-pen line between them (a wall's two faces are adjacent; a hatch's are not)
                    bool pattern = false;
                    for (int k = 0; k < segs.Count && !pattern; k++)
                    {
                        if (k == i || k == bestJ) continue;
                        var c = segs[k];
                        if (!c.Cut || Math.Abs(a.Ux * c.Ux + a.Uy * c.Uy) < FaceParallelCos) continue;
                        var (e0, e1, s0, s1) = Relative(a, c);
                        double off = (e0 + e1) / 2 * side;   // along the normal towards b: b is at +gap
                        if (Math.Min(s1, t1) - Math.Max(s0, t0) < (t1 - t0) / 2) continue;
                        bool beyondB = Math.Abs(off - 2 * gap) <= PatternSpacingShare * gap;
                        bool beyondA = Math.Abs(off + gap) <= PatternSpacingShare * gap;
                        bool between = off > WallLimitSlackMm && off < gap - WallLimitSlackMm;
                        if (beyondA || beyondB || between) { pattern = true; Why($"a cut-pen line {(between ? "between the faces" : "beyond a face at the same spacing")}, {c.Len / 25.4:0}\" long at {off / 25.4:0.0}\""); }
                    }
                    if (pattern) continue;

                    // between a wall's faces there is nothing, or one line of its own in a lighter pen
                    // (a batter, a step, the property line a retaining wall stands on — dashed, so
                    // its dashes are one line, counted by where it lies across the wall); several
                    // lighter lines along the pair are something drawn there (31138 p9's flights: a
                    // dozen 15" segments between cut lines 45" apart)
                    var lighterOffsets = new List<double>();
                    for (int li = 0; li < result.Lines.Count; li++)
                    {
                        var l = result.Lines[li];
                        if (l.Count != 2 || li == a.Line || li == b.Line || CutPenLine(li)) continue;
                        double lx = l[1].X - l[0].X, ly = l[1].Y - l[0].Y, ll = Math.Sqrt(lx * lx + ly * ly);
                        if (ll <= 0 || Math.Abs((lx * a.Ux + ly * a.Uy) / ll) < FaceParallelCos) continue;
                        double off = ((l[0].X - a.A.X) * nx + (l[0].Y - a.A.Y) * ny) * side;
                        if (off <= WallLimitSlackMm || off >= gap - WallLimitSlackMm) continue;
                        double u0 = (l[0].X - a.A.X) * a.Ux + (l[0].Y - a.A.Y) * a.Uy, u1 = (l[1].X - a.A.X) * a.Ux + (l[1].Y - a.A.Y) * a.Uy;
                        if (Math.Max(u0, u1) < t0 || Math.Min(u0, u1) > t1) continue;
                        if (!lighterOffsets.Any(o => Math.Abs(o - off) <= WallLimitSlackMm)) lighterOffsets.Add(off);
                        if (lighterOffsets.Count >= LighterBetweenMax) break;
                    }
                    if (lighterOffsets.Count >= LighterBetweenMax) { Why($"lighter lines between the faces at {string.Join(", ", lighterOffsets.Select(o => $"{o / 25.4:0.0}\""))}"); continue; }

                    // something drawn across the pair between its ends, in any pen. A stair is a run
                    // of risers: lines square to the faces, spanning the gap (inset from the stringers,
                    // or past them to the walls), a tread apart — three or more closer than a tread
                    // (31138's, 31065's and 31168's flights are 44"–45" wide, risers 10"–12" apart).
                    // A shaft's X runs corner to corner (31202's, 45" x 60"). A dimension's extension
                    // lines cross a wall too, but a bay apart; a hatch line is diagonal and a gap long.
                    var risers = new List<double>();
                    bool shaft = false;
                    for (int li = 0; li < result.Lines.Count && !shaft; li++)
                    {
                        var l = result.Lines[li];
                        if (l.Count != 2 || li == a.Line || li == b.Line) continue;
                        double lx = l[1].X - l[0].X, ly = l[1].Y - l[0].Y, ll = Math.Sqrt(lx * lx + ly * ly);
                        if (ll <= 0) continue;
                        double ca = ((l[0].X - a.A.X) * nx + (l[0].Y - a.A.Y) * ny) * side, cb = ((l[1].X - a.A.X) * nx + (l[1].Y - a.A.Y) * ny) * side;
                        double lo = Math.Min(ca, cb), hi = Math.Max(ca, cb);
                        if (hi < RiserGapShare * gap || lo > (1 - RiserGapShare) * gap) continue;             // does not span the gap
                        double u0 = (l[0].X - a.A.X) * a.Ux + (l[0].Y - a.A.Y) * a.Uy, u1 = (l[1].X - a.A.X) * a.Ux + (l[1].Y - a.A.Y) * a.Uy;
                        if (Math.Max(u0, u1) < t0 + 2 * WallLimitSlackMm || Math.Min(u0, u1) > t1 - 2 * WallLimitSlackMm) continue;   // at an end: a cap or a jamb
                        bool square = Math.Abs((lx * a.Ux + ly * a.Uy) / ll) <= AcrossCos;
                        if (square && hi - lo >= RiserGapShare * gap) risers.Add((u0 + u1) / 2);
                        // an X's diagonal ends ON the faces; a line that crosses the pair and runs on
                        // past both (31168 p11: the band's edge line crossing the slanted wall at 1.9°)
                        // is somebody else's line
                        else if (lo >= -WallLimitSlackMm && lo <= WallLimitSlackMm && hi >= gap - WallLimitSlackMm && hi <= gap + WallLimitSlackMm
                                 && Math.Abs(u1 - u0) >= (t1 - t0) / 2) shaft = true;
                    }
                    if (shaft) { Why("a line corner to corner: a shaft's X"); continue; }
                    risers.Sort();
                    int run = 1, longestRun = 1;
                    for (int r = 1; r < risers.Count; r++)
                    {
                        run = risers[r] - risers[r - 1] <= TreadMaxMm ? run + 1 : 1;
                        longestRun = Math.Max(longestRun, run);
                    }
                    if (longestRun >= RiserRunMin) { Why($"a run of {longestRun} risers a tread apart ({risers.Count} lines across it)"); continue; }
                    Why($"a wall (faces w{(a.Line < result.LineWidths.Count ? result.LineWidths[a.Line] : 0):0.00}/w{(b.Line < result.LineWidths.Count ? result.LineWidths[b.Line] : 0):0.00}, {risers.Count} across, lighter between at {string.Join(", ", lighterOffsets.Select(o => $"{o / 25.4:0.0}\""))})");

                    var s = (a.A.X + a.Ux * t0, a.A.Y + a.Uy * t0);
                    var e = (a.A.X + a.Ux * t1, a.A.Y + a.Uy * t1);
                    double ox = nx * side * gap, oy = ny * side * gap;
                    var outline = new List<(double X, double Y)> { s, e, (e.Item1 + ox, e.Item2 + oy), (s.Item1 + ox, s.Item2 + oy) };
                    var axisS = (s.Item1 + ox / 2, s.Item2 + oy / 2);
                    var axisE = (e.Item1 + ox / 2, e.Item2 + oy / 2);
                    int wallIndex = result.Walls.Count;
                    result.Walls.Add(new WallPanel(outline, axisS, axisE, gap));
                    result.WallColors.Add(a.Line < result.LineColors.Count ? result.LineColors[a.Line] : ((byte)0, (byte)0, (byte)0));
                    result.WallIsAnnotation.Add(false);
                    result.WallFaceLines[a.Line] = wallIndex;
                    result.WallFaceLines[b.Line] = wallIndex;
                    faceSide[a.Line] = sideOfA;
                    faceSide[b.Line] = sideOfB;
                    used.Add(a.Line); used.Add(b.Line);
                    if (fates is not null)
                        foreach (int line in new[] { a.Line, b.Line })
                            if (lineToPath.TryGetValue(line, out int pathIndex))
                                for (int k = firstFate; k < fates.Count; k++)
                                    if (fates[k].PathIndex == pathIndex)
                                        fates[k] = new PathFate(pathIndex, Disposition.Read, PathReason.BecameWallFace, wallIndex);
                }
            }
        }

        /// <summary>
        /// A FLOOR'S EDGE IS THE OUTERMOST CLOSED LOOP THE PLAN DRAWS (intake step 24).
        ///
        /// A parkade's perimeter is a wall, and step 22 takes its plate from the walls' outer face.
        /// Every other storey's perimeter is a slab edge: the tower plans, the ground floors, the
        /// mezzanines. The drafter draws that edge as ordinary lines — 31168's "CONCRETE OUTLINE"
        /// sheets are named after it — so the intake emitted them as beams and the storey reached
        /// ETABS with no diaphragm at all.
        ///
        /// The lines are chained into rings by the same builder the DXF side uses on a Revit export
        /// (<see cref="PlanLoopBuilder"/>: endpoints within a tolerance are one node), and a ring
        /// big enough to be a floor, with structure standing inside it, and inside no other such
        /// ring, is this plan's slab edge. Measured 2026-09-09 on 31168's BLDG A tower plan: the
        /// outermost ring closes on its own and encloses 9,866 sq ft, against the 9,743 sq ft
        /// Revit's own export gives that storey — 1.3%, the difference being the balcony steps the
        /// drawing rounds off.
        ///
        /// WHAT IT IS NOT: a fill, a hatch or a flood. Where the drawing's edge does not close, this
        /// finds nothing and the storey keeps having no plate, which the DXF side already reports.
        /// </summary>
        internal static void SlabEdgesFromLoops(ExtractedGeometry result, IList<PathFate>? fates, int firstFate)
        {
            result.FirstEdgeSlab = result.Slabs.Count;
            if (result.Lines.Count < 4) return;

            // every line that is not already something: a wall's face is the wall, a match line is
            // the seam, and both are read
            var candidates = new List<int>();
            for (int i = 0; i < result.Lines.Count; i++)
            {
                if (result.Lines[i].Count != 2 || result.LineIsAnnotation[i] || result.WallFaceLines.ContainsKey(i)) continue;
                candidates.Add(i);
            }
            if (candidates.Count < 4) return;

            var segments = candidates
                .Select(i => new DxfSegment("SLABEDGE",
                    new DxfPoint(result.Lines[i][0].X, result.Lines[i][0].Y),
                    new DxfPoint(result.Lines[i][1].X, result.Lines[i][1].Y)))
                .ToList();
            var built = new PlanLoopBuilder(SlabEdgeJoinMm, SlabEdgeJoinMm, SlabEdgeJoinMm).Build(segments);
            var loops = built.Loops.ToList();

            // AN EDGE INTERRUPTED IS STILL ONE EDGE (intake step 27). A slab edge is broken where a
            // bubble's leader, a note or a dimension crosses it, and stops short of its corner where
            // the other edge's line took the corner: exact joins leave every tower plan's ring open
            // (31168's L4–L14 and L15–L32 views, measured 2026-09-09). The DXF side closes a Revit
            // export's slab edge the same way, with the same two bounds: chain ends within a hand's
            // width (6 in) are one edge, and an edge running toward a corner it stops short of by up
            // to 4 ft is carried to it. Only chains long enough to be a piece of an edge are tried —
            // the bridge search is every pair of chains, and a hatched plan has ten thousand dashes.
            var pieces = built.OpenChains
                .Where(c => c.Count >= 2 && ChainLength(c) >= SlabEdgeChainMinMm)
                .ToList();
            if (pieces.Count >= 2)
            {
                var chainSegments = new List<DxfSegment>();
                foreach (var c in pieces)
                    for (int i = 0; i + 1 < c.Count; i++)
                        chainSegments.Add(new DxfSegment("SLABEDGE", c[i], c[i + 1]));
                var bridged = new PlanLoopBuilder(SlabEdgeJoinMm, SlabEdgeBridgeMm, SlabEdgeExtendMm).Build(chainSegments);
                loops.AddRange(bridged.Loops);
            }
            // big enough to be a floor, and with something standing in it
            var floors = loops
                .Where(l => l.Area >= MinSlabAreaMm2 && StandsIn(l))
                .OrderByDescending(l => l.Area)
                .ToList();

            // ⛔ MEASURED AND REJECTED (step 27, 2026-09-09). A typical tower floor's edge is not one
            // line: it steps out round every balcony, and each balcony is a closed box drawn against
            // the outline with its own diagonals — 31168's L4–L14 views, where the west edge is drawn
            // in two five-metre pieces eighteen metres apart. No chain closes that, so the DXF side's
            // own flood-fill recovery was tried here as a fallback where no ring closed: every line
            // 500 mm or longer painted, gaps bridged, the outside flooded, the boundary taken, gated
            // the same way on area and on structure standing in it. On 31168 it did not find a tower
            // plate. It found the CORES — 858 sq ft on A-L27..A-L32, 1,218 on B's, 869 beside them on
            // L15..L26, against Revit's 9,743 — and it cost the parkades their step-22 plates, P1
            // 77,182 -> 5,536 and P2 77,144 -> 893. A recovery that answers with the core when it was
            // asked for the floor is not a weaker version of the ring rule, it is a different and
            // wrong one. The tower edge stays open until the rule that closes it is known: a storey
            // with no plate is honest, a storey carrying its core's area as its floor is not.
            if (floors.Count == 0) return;

            // and outermost: a core's ring inside a floor is a hole in it, not a second floor
            var outermost = new List<PlanLoop>();
            foreach (var l in floors)
                if (!outermost.Any(o => LoopGeometry.PointInPolygon(l.Points[0], o.Points)))
                    outermost.Add(l);

            foreach (var loop in outermost)
            {
                int slabIndex = result.Slabs.Count;
                result.Slabs.Add(loop.Points.Select(p => (p.X, p.Y)).ToList());
                result.SlabColors.Add(((byte)0, (byte)0, (byte)0));
                result.SlabIsAnnotation.Add(false);

                // the lines that lie on it are its edge, not beams
                foreach (int i in candidates)
                {
                    if (result.SlabEdgeLines.ContainsKey(i)) continue;
                    var a = new DxfPoint(result.Lines[i][0].X, result.Lines[i][0].Y);
                    var b = new DxfPoint(result.Lines[i][1].X, result.Lines[i][1].Y);
                    if (!OnRing(a, loop) || !OnRing(b, loop)) continue;
                    result.SlabEdgeLines[i] = slabIndex;
                }
            }

            if (fates is not null)
            {
                var lineToPath = new Dictionary<int, int>();
                for (int k = firstFate; k < fates.Count; k++)
                    if (fates[k].Reason == PathReason.EmittedAsLine && fates[k].ObjectIndex is int li) lineToPath[li] = fates[k].PathIndex;
                foreach (var (line, slab) in result.SlabEdgeLines)
                    if (lineToPath.TryGetValue(line, out int pathIndex))
                        for (int k = firstFate; k < fates.Count; k++)
                            if (fates[k].PathIndex == pathIndex)
                                fates[k] = new PathFate(pathIndex, Disposition.Read, PathReason.BecameSlabEdge, slab);
            }

            // A FLOOR IS WHAT THE STRUCTURE AROUND IT STANDS ON. Something stands in the ring, and
            // of the columns and walls within a tenth of the ring's size of it, most stand in it:
            // a 28.6 m x 6.3 m strip on 31168's BLDG C plan closed on its own with a column in it
            // and would have been the storey's plate at 1,922 sq ft where the floor is 14,988 —
            // the columns beside it, outside it, say it is a strip of the floor and not the floor.
            bool StandsIn(PlanLoop loop)
            {
                double x0 = loop.Points.Min(p => p.X), x1 = loop.Points.Max(p => p.X);
                double y0 = loop.Points.Min(p => p.Y), y1 = loop.Points.Max(p => p.Y);
                double reach = Math.Max(x1 - x0, y1 - y0) * SlabEdgeNeighbourhoodShare;
                int inside = 0, near = 0;
                void Count(double x, double y)
                {
                    if (x < x0 - reach || x > x1 + reach || y < y0 - reach || y > y1 + reach) return;
                    near++;
                    if (LoopGeometry.PointInPolygon(new DxfPoint(x, y), loop.Points)) inside++;
                }
                foreach (var c in result.Columns) Count(c.X, c.Y);
                foreach (var w in result.Walls) Count((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2);
                return inside > 0 && inside * 2 > near;
            }

            static bool OnRing(DxfPoint p, PlanLoop loop)
            {
                for (int i = 0; i < loop.Points.Count; i++)
                    if (LoopGeometry.DistanceToSegment(p, loop.Points[i], loop.Points[(i + 1) % loop.Points.Count]) <= SlabEdgeJoinMm) return true;
                return false;
            }

            static double ChainLength(IReadOnlyList<DxfPoint> c)
            {
                double len = 0;
                for (int i = 0; i + 1 < c.Count; i++) len += c[i].DistanceTo(c[i + 1]);
                return len;
            }
        }

        /// <summary>Endpoints this close are one corner of a ring, in millimetres: the intake reads a
        /// PDF's own coordinates, which meet exactly where the drafter closed a polyline.</summary>
        private const double SlabEdgeJoinMm = 1.0;

        /// <summary>Two chain ends this close are one edge broken by what crossed it: a hand's width, the DXF side's own bridge (6 in).</summary>
        private const double SlabEdgeBridgeMm = 6 * 25.4;

        /// <summary>An edge stopping this short of the corner the other edge's line makes is carried to it: the DXF side's own limit (48 in).</summary>
        private const double SlabEdgeExtendMm = 48 * 25.4;

        /// <summary>A chain shorter than this is a tick, a dash or a letter, not a piece of a floor's edge, and is not bridged.</summary>
        private const double SlabEdgeChainMinMm = 2000;

        /// <summary>The structure a ring is judged against stands within this share of the ring's own size of it.</summary>
        private const double SlabEdgeNeighbourhoodShare = 0.10;

        /// <summary>
        /// Smaller than this and a closed ring is a stair, a shaft or a box of notes, not a floor.
        /// 400 sq ft, the DXF side's own <c>MinPlateArea</c>, in millimetres.
        /// </summary>
        private const double MinSlabAreaMm2 = 400 * 144 * 25.4 * 25.4;

        /// <summary>
        /// Narrower than this along the wall and a paper fill is a slot or a text mask, not an
        /// opening a person walks through. Revit's own doorways on 31168 measured 36"–48" on 142 of
        /// 160 and none below 18" (reference_kor_dxf_drafting_conventions).
        /// </summary>
        public const double DoorwayMinLengthMm = 18 * 25.4;

        /// <summary>
        /// A pier shorter than this between two openings is not carried as a wall. The DXF side's
        /// panel floor (PlanClassificationOptions.MinPanelOverlap, 12"): 31138's model carries piers
        /// at 9"–27" with pier labels, so the wall's own 48" minimum must not apply to a pier.
        /// </summary>
        public const double PierMinLengthMm = 12 * 25.4;

        /// <summary>
        /// The openings knocked out of the wall whose oriented box this is: paper fills painted after
        /// the wall (paint order — one painted before it is covered by it), crossing its thickness
        /// and no wider across than twice it (a mask under a note is wider), overlapping its length
        /// by at least a door. Extents are along the wall's axis from its start.
        /// </summary>
        internal static List<(int PathIndex, double T0, double T1)> DoorwaysOn(OrientedBox box, int wallPathIndex,
            IReadOnlyList<(int Index, List<(double X, double Y)> Pts)> paperFills)
        {
            var found = new List<(int PathIndex, double T0, double T1)>();
            if (paperFills.Count == 0) return found;
            double ux = box.AxisEnd.X - box.AxisStart.X, uy = box.AxisEnd.Y - box.AxisStart.Y;
            double len = Math.Sqrt(ux * ux + uy * uy);
            if (len < 1e-9) return found;
            ux /= len; uy /= len;
            double nx = -uy, ny = ux, half = box.Thickness / 2;
            foreach (var (index, fill) in paperFills)
            {
                if (index < wallPathIndex) continue;
                double u0 = double.MaxValue, u1 = double.MinValue, n0 = double.MaxValue, n1 = double.MinValue;
                foreach (var p in fill)
                {
                    double dx = p.X - box.AxisStart.X, dy = p.Y - box.AxisStart.Y;
                    double u = dx * ux + dy * uy, n = dx * nx + dy * ny;
                    u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); n0 = Math.Min(n0, n); n1 = Math.Max(n1, n);
                }
                bool crosses = n0 <= -half + WallLimitSlackMm && n1 >= half - WallLimitSlackMm
                               && n1 - n0 <= 2 * box.Thickness + 2 * WallLimitSlackMm;
                if (!crosses) continue;
                double t0 = Math.Max(0, u0), t1 = Math.Min(box.Length, u1);
                if (t1 - t0 < DoorwayMinLengthMm) continue;
                found.Add((index, t0, t1));
            }
            return found;
        }

        /// <summary>
        /// The pieces of the clip the wall was drawn through, along its axis: the clip path drawn
        /// immediately before the wall's own path (nothing painted between), every piece of which
        /// lies on the wall — across its thickness and within its length. A clip with a piece off
        /// the wall is somebody else's, left over from a graphics state this reader cannot see the
        /// end of, and the wall is taken whole.
        /// </summary>
        internal static List<(int PathIndex, double T0, double T1)> ClipPiecesOn(OrientedBox box, int wallPathOrdinal,
            IReadOnlyList<(int Index, int PathOrdinal, List<(double X, double Y)> Pts)> clipPieces)
        {
            var found = new List<(int PathIndex, double T0, double T1)>();
            if (wallPathOrdinal < 1 || clipPieces.Count == 0) return found;
            double ux = box.AxisEnd.X - box.AxisStart.X, uy = box.AxisEnd.Y - box.AxisStart.Y;
            double len = Math.Sqrt(ux * ux + uy * uy);
            if (len < 1e-9) return found;
            ux /= len; uy /= len;
            double nx = -uy, ny = ux, half = box.Thickness / 2;
            foreach (var (index, ordinal, piece) in clipPieces)
            {
                if (ordinal != wallPathOrdinal - 1) continue;
                double u0 = double.MaxValue, u1 = double.MinValue, n0 = double.MaxValue, n1 = double.MinValue;
                foreach (var p in piece)
                {
                    double dx = p.X - box.AxisStart.X, dy = p.Y - box.AxisStart.Y;
                    double u = dx * ux + dy * uy, n = dx * nx + dy * ny;
                    u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); n0 = Math.Min(n0, n); n1 = Math.Max(n1, n);
                }
                bool onTheWall = n0 <= -half + WallLimitSlackMm && n1 >= half - WallLimitSlackMm
                                 && n1 - n0 <= box.Thickness + 2 * WallLimitSlackMm
                                 && u0 >= -WallLimitSlackMm && u1 <= box.Length + WallLimitSlackMm;
                if (!onTheWall) { found.Clear(); return found; }
                found.Add((index, Math.Max(0, u0), Math.Min(box.Length, u1)));
            }
            return found;
        }

        /// <summary>The runs of wall a panel wide or wider left between the doorways, along the axis.</summary>
        internal static List<(double T0, double T1)> PiersBetween(IReadOnlyList<(int PathIndex, double T0, double T1)> doorways, double length)
            => Piers([(0.0, length)], doorways);

        /// <summary>
        /// The runs a panel wide or wider that the wall shows: each allowed interval (the whole wall,
        /// or the clip's pieces) less the doorways cut through it.
        /// </summary>
        internal static List<(double T0, double T1)> Piers(IReadOnlyList<(double T0, double T1)> allowed,
            IReadOnlyList<(int PathIndex, double T0, double T1)> doorways)
        {
            var piers = new List<(double T0, double T1)>();
            var cuts = doorways.OrderBy(d => d.T0).ToList();
            foreach (var (a0, a1) in allowed.OrderBy(a => a.T0))
            {
                double at = a0;
                foreach (var d in cuts)
                {
                    if (d.T1 <= at || d.T0 >= a1) continue;
                    if (d.T0 - at >= PierMinLengthMm) piers.Add((at, d.T0));
                    at = Math.Max(at, d.T1);
                }
                if (a1 - at >= PierMinLengthMm) piers.Add((at, a1));
            }
            return piers;
        }

        private static (double X, double Y) Along(OrientedBox box, double t)
        {
            double ux = box.AxisEnd.X - box.AxisStart.X, uy = box.AxisEnd.Y - box.AxisStart.Y;
            double len = Math.Sqrt(ux * ux + uy * uy);
            return (box.AxisStart.X + ux / len * t, box.AxisStart.Y + uy / len * t);
        }

        /// <summary>The wall between two stations on its axis, as its own four-cornered panel.</summary>
        private static WallPanel Pier(OrientedBox box, double t0, double t1)
        {
            double ux = box.AxisEnd.X - box.AxisStart.X, uy = box.AxisEnd.Y - box.AxisStart.Y;
            double len = Math.Sqrt(ux * ux + uy * uy);
            ux /= len; uy /= len;
            double nx = -uy * box.Thickness / 2, ny = ux * box.Thickness / 2;
            var s = Along(box, t0);
            var e = Along(box, t1);
            var outline = new List<(double X, double Y)>
            {
                (s.X + nx, s.Y + ny), (e.X + nx, e.Y + ny), (e.X - nx, e.Y - ny), (s.X - nx, s.Y - ny),
            };
            return new WallPanel(outline, s, e, box.Thickness);
        }
    }
}
