#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        // THE FLOOR IS HELD TO HALF AN INCH, LIKE THE OTHER LIMITS (intake step 63, 2026-09-14). An eighth was tried
        // first - the gap between a 2x6 stud wall (5.5 in) and the six inches every engineer's wall measures - and it
        // refused 60 walls on 31065's concrete tower: its 6 in walls are DRAWN at 142-150 mm (5.6-5.9 in), which is
        // where a 2x6 stud wall lands too. Thickness alone cannot tell a thinly drawn six-inch wall from a 2x6; it can
        // tell a 2x4 (89-115 mm), which is what the floor refuses on a wood-frame set (31066-01: 112 and 115 mm bands).
        private const double WallFloorSlackMm = 12.7;

        /// <summary>A corner is square within about 3° (cos 87°).</summary>
        private const double RectangleCornerCos = 0.05;
        /// <summary>A rectangle fills its own oriented box; a taper or a self-crossing outline does not.</summary>
        private const double RectangleFillShare = 0.95;

        /// <summary>
        /// A WALL'S END MAY BE MITRED (intake step 38). Four points are a wall's shape when the two long
        /// edges are opposite and parallel (a taper is not a wall), each end edge runs no further along
        /// the wall than the thickest wall is thick (a mitre against a wall of any thickness, a square
        /// end at the least), and the polygon fills at least half its box (a bow-tie does not). The
        /// rectangle test this replaces refused 31170's P1 perimeter: a 54 m filled band 10" thick with
        /// one end mitred against a 12" return — corners (26586,58142) (26586,57888) (80751,57888)
        /// (81056,58142), a 305 mm skew at the east end — which is a wall on any plan. Audit F1's
        /// (0,0) (6000,0) (5800,300) (200,300) passes this too: parallel faces 300 apart, ends 200
        /// along — a wall with chamfered ends, not a taper; the taper the audit meant is faces that
        /// are not parallel, and that is what the test now says.
        /// </summary>
        public static bool IsWallShape(List<(double X, double Y)> pts, double boxLength, double boxThickness, double maxWallThicknessMm)
        {
            ArgumentNullException.ThrowIfNull(pts);
            if (pts.Count != 4 || boxLength <= 0 || boxThickness <= 0) return false;
            var edges = Enumerable.Range(0, 4).Select(i => (A: pts[i], B: pts[(i + 1) % 4])).Select(e =>
            {
                double dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
                return (dx, dy, len, ux: len > 0 ? dx / len : 0, uy: len > 0 ? dy / len : 0);
            }).ToList();
            int longest = Enumerable.Range(0, 4).OrderByDescending(i => edges[i].len).First();
            int opposite = (longest + 2) % 4;
            var a = edges[longest]; var b = edges[opposite];
            if (a.len <= 0 || b.len <= 0) return false;
            if (Math.Abs(a.ux * b.ux + a.uy * b.uy) < FaceParallelCos) return false;              // a taper
            // and the faces may converge by an inch, or by a third of the gap, as the face reader allows a
            // retaining wall's (step 21) - a 6 m shape 300 thick at one end and 400 at the other passed
            // the angle alone (Codex audit 2026-09-11, F17)
            double nx = -a.uy, ny = a.ux;
            double d0 = Math.Abs((pts[opposite].X - pts[longest].X) * nx + (pts[opposite].Y - pts[longest].Y) * ny);
            double d1 = Math.Abs((pts[(opposite + 1) % 4].X - pts[longest].X) * nx + (pts[(opposite + 1) % 4].Y - pts[longest].Y) * ny);
            if (Math.Abs(d0 - d1) > Math.Max(FaceTaperMm, WallShapeTaperShare * Math.Min(d0, d1))) return false;
            foreach (int e in new[] { (longest + 1) % 4, (longest + 3) % 4 })
            {
                double along = Math.Abs(edges[e].dx * a.ux + edges[e].dy * a.uy);                   // the end's run along the wall
                if (along > maxWallThicknessMm + WallLimitSlackMm) return false;
            }
            double area = Math.Abs(PolygonProcessor.PolygonAreaMm2(pts));
            return area >= 0.5 * boxLength * boxThickness;
        }

        /// <summary>
        /// Four points are a rectangle when every corner is square and the polygon fills its oriented
        /// box. The wall rule tested the box and the vertex count only, so a filled trapezoid
        /// (0,0) (6000,0) (5800,300) (200,300) became a wall (audit F1, 2026-09-08). Since step 38 the
        /// wall rule asks <see cref="IsWallShape"/> instead, which admits a mitred end; this stays for
        /// any reader that wants a rectangle and nothing else.
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
            IReadOnlyDictionary<int, int>? footingPieces = null,
            double slabEdgeBridgeMm = DefaultSlabEdgeBridgeMm,
            double minSlabAreaMm2 = DefaultMinSlabAreaMm2,
            IReadOnlyList<string>? voidWords = null,
            IReadOnlyList<string>? slabWords = null)
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
            // A FILLED SHAPE MAY ARRIVE AS TWO TRIANGLES (intake step 61, 2026-09-14): a PDF driver tessellates a
            // filled rectangle into two triangles sharing its diagonal, and run 9 - step 58's "a filled shape of
            // three points is a symbol's triangle" over the whole corpus - lost 47,963 columns on 144 sets (31048-01
            // 2,640 -> 57 against 449 of hers; 31017-01 2,175 -> 596) that the six harness sets, drawn with four
            // corners, never showed. Two filled triangles of one colour that share an edge and whose union is a
            // convex quadrilateral are ONE shape: the first carries the four corners, the second is its other half.
            var twins = TriangleTwins.Pair(rawSubpaths);
            var deferredPaper = new List<int>();
            var columnByShape = new List<bool>();          // parallel to result.Columns: read by shape (a cell candidate) or by declared size
            var deferredNoInk = new List<int>();
            var doorwayOf = new Dictionary<int, int>();
            var clipOf = new Dictionary<int, int>();
            int firstFate = fates?.Count ?? 0;
            void FateAt(int index, PathReason reason, int? objectIndex = null)
                => fates?.Add(new PathFate(index, PathFate.DispositionOf(reason), reason, objectIndex));

            // A CURVE DRAWN AS SHORT STROKES IS ONE LINE (intake step 98, 2026-09-16). 31138's tower outline turns
            // its corners as arcs, and the PDF holds each arc as a run of 9 pt strokes under 200 mm long, end to end;
            // every one was TooShort, the ring was open at every rounded corner, and fourteen storeys carried no
            // plate. The strokes that meet end to end exactly, one pen, are one path; the path is what the length
            // gate judges, and it reaches the readers as the polyline the drafter drew. Only strokes under the length
            // gate are chained: a long line stays the two-point line the wall reader pairs.
            var curves = CurvesOfShortStrokes(rawSubpaths, lineMinLengthMm, footingPieces);   // a footing's dashes are the footing's (step 44), whatever they touch
            var curveMembers = new HashSet<int>(curves.SelectMany(c => c.Members));
            // A SHORT STROKE BETWEEN TWO LONG LINES OF ONE PEN IS A JOG OF THE EDGE (intake step 110, 2026-09-16 22:00).
            // 30838's concrete-outline plans step their slab edge sideways by 150 mm at grid 2: an 8 pt stroke 150 mm long
            // between two 8 pt verticals of 3.9 m and 2.1 m, end to end. Step 98 chains short strokes with each other and
            // leaves a long line alone, so the jog alone was TooShort, the ring stood open by 150 mm on every tower storey,
            // and the plate those storeys carried came from the slab-reinforcing plan drawn beside it - gone the moment the
            // page's two views were told apart (step 109). A stroke under the gate whose BOTH ends meet an end of a long
            // line drawn with the same pen and colour is that line's jog, and is kept as the two-point line it is.
            var jogs = JogsBetweenLongLines(rawSubpaths, lineMinLengthMm, curveMembers, footingPieces);
            for (int pathIndex = 0; pathIndex < rawSubpaths.Count + curves.Count; pathIndex++)
            {
                bool isCurve = pathIndex >= rawSubpaths.Count;
                var sub = isCurve ? curves[pathIndex - rawSubpaths.Count].Path : rawSubpaths[pathIndex];
                void Fate(PathReason reason, int? objectIndex = null)
                {
                    if (fates is null) return;
                    if (isCurve) foreach (int m in curves[pathIndex - rawSubpaths.Count].Members) fates.Add(new PathFate(m, PathFate.DispositionOf(reason), reason, objectIndex));
                    else fates.Add(new PathFate(pathIndex, PathFate.DispositionOf(reason), reason, objectIndex));
                }
                if (!isCurve && curveMembers.Contains(pathIndex)) continue;   // its fate is its curve's

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
                if (twins.OtherHalfOf.TryGetValue(pathIndex, out int firstHalf))
                {
                    // the other half's fate IS its first half's - the same reason, the same object: the shape was judged whole
                    var whole = fates?.LastOrDefault(f => f.PathIndex == firstHalf);
                    if (whole is not null) fates!.Add(whole with { PathIndex = pathIndex });
                    continue;
                }
                var pts = twins.Union.TryGetValue(pathIndex, out var quad) ? quad : sub.Points;
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
                        // ...drawn with the grid's pen (step 53): a heavier stroke along the axis is a tendon or a beam,
                        // kept apart from the lines for the tendon reader - no wall reader sees it
                        if ((dx <= furniture.AxisTolerance && furniture.OnVerticalAxis(cx)) || (dy <= furniture.AxisTolerance && furniture.OnHorizontalAxis(cy)))
                        {
                            if (furniture.IsGridPen(sub.LineWidth)) { Fate(PathReason.GridAxis); continue; }
                            result.StrokesOnGrid.Add(pts);
                            Fate(PathReason.StrokeOnGrid, result.StrokesOnGrid.Count - 1);
                            continue;
                        }
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
                        columnByShape.Add(false);
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
                        if (box.Thickness >= minWallThicknessMm - WallFloorSlackMm && box.Thickness <= maxWallThicknessMm + WallLimitSlackMm
                            && box.Length >= minWallLengthMm - WallLimitSlackMm && box.Aspect >= minWallAspect)
                        {
                            // four points are a wall's shape when the faces are parallel and the ends are
                            // square or mitred: a taper's box passed these limits and became a wall (audit
                            // F1, 2026-09-08); a mitred end is a wall's (step 38, 31170's P1 perimeter)
                            if (pts.Count == 4 && IsWallShape(pts, box.Length, box.Thickness, maxWallThicknessMm))
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

                    // A FILLED BAND THINNER THAN THE THINNEST WALL IS NOT A WALL AND NOT A SLAB (intake step 63): a 2x6 stud
                    // wall on a wood-frame set, 140 mm, is a band of wall proportions under the six inches every engineer's
                    // wall measures; read as a slab candidate it stood in the plate reader's way for nothing
                    if (!sub.IsAnnotation && obox is { } thinBox && !IsPaper(color) && sub.IsFilled
                        && thinBox.Thickness < minWallThicknessMm - WallFloorSlackMm && thinBox.Length >= minWallLengthMm - WallLimitSlackMm && thinBox.Aspect >= minWallAspect)
                    { Fate(PathReason.ThinBand); continue; }

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
                        // A COLUMN IS DRAWN WITH FOUR CORNERS (intake step 58): a filled shape of three points is a
                        // symbol's triangle - the bearing-wall symbol on a small job's plan, an arrowhead, a hatch -
                        // and 01389's main floor plan read 139 "columns" from them (every one three points; the three
                        // harness plans measured read 180 columns, every one four). A curve is many points and stays.
                        if (!sub.IsAnnotation && pts.Count < 4)
                        {
                            result.Arrowheads.Add(((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2, Math.Max(bboxW, bboxH)));
                            Fate(PathReason.FilledTriangle);
                            continue;
                        }
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
                        columnByShape.Add(true);
                        Fate(PathReason.BecameColumnByShape, result.Columns.Count - 1);
                    }
                    else Fate(pts.Count < 2 ? PathReason.TooFewPoints : PathReason.TooShort);
                }
                else
                {
                    double len = PolygonProcessor.PathLength(pts);
                    if (len >= lineMinLengthMm || (!isCurve && jogs.Contains(pathIndex)))
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
                            // A LINE THAT CARRIES ITS BAR MARK IS A BAR (intake step 126, 2026-09-18). 31017's outline sheets
                            // draw the diaphragm bars over the slab edge - 140 bar-mark words on page 18 - and 30838's
                            // "CONCRETE OUTLINE AND DIAPHRAGM REINFORCING" views the same (128 on S2.28); the bars share
                            // their pens with the edge and the grid (five pens on one page), so no pen names them. The
                            // label does: a word in the bar grammar whose box sits on the line, over the line's length.
                            // Every bar that reached the rim cut the floor's arrangement into cells (31017: 1,768 of her
                            // 64,354 sq ft on L1). The bar is read for what it is and offered to no reader.
                            if (CarriesABarMark(result, pts)) { Fate(PathReason.BarRun); continue; }
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
            PatternCellsAreNotColumns(result, columnByShape, fates, firstFate);
            ATargetsQuadrantsAreNotColumns(result, columnByShape, fates, firstFate);
            PatternStripesAreNotWalls(result, fates, firstFate);
            AFaceInPiecesIsOneFace(result, fates, firstFate);
            WallsFromFaceLines(result, fates, firstFate, minWallThicknessMm, maxWallThicknessMm, minWallLengthMm, minWallAspect);
            // A STAIR IS A RUN OF TREADS (intake step 105): the paper fills that are not doorways, read as flights before the
            // slab pass, which cuts the well they stand in
            result.StairFlights.AddRange(StairFlights(deferredPaper.Where(i => !doorwayOf.ContainsKey(i)).Select(i => rawSubpaths[i].Points),
                                                     result.Lines.Where((l, i) => !result.LineIsAnnotation[i]), result.TreadLines));

            SlabEdgesFromLoops(result, fates, firstFate, slabEdgeBridgeMm, minSlabAreaMm2, minWallThicknessMm, maxWallThicknessMm, voidWords ?? PdfIntakeOptions.DefaultVoidWords, slabWords ?? PdfIntakeOptions.DefaultSlabWords);

            if (fates is not null && (deferredPaper.Count > 0 || deferredNoInk.Count > 0))
            {
                var ordered = fates.Skip(firstFate).OrderBy(f => f.PathIndex).ToList();
                for (int i = 0; i < ordered.Count; i++) fates[firstFate + i] = ordered[i];
            }
        }

        /// <summary>A filled wall's faces may converge by this share of the thinner end: a quarter (31168's retaining wall, 15" to 12", is a fifth; the audit's 300 to 400 mm shape, a third, is a taper).</summary>
        private const double WallShapeTaperShare = 0.25;

        /// <summary>Two cells abut when their facing edges are within this of each other: an inch of drafting.</summary>
        private const double CellAbutMm = 25.4;

        /// <summary>
        /// A PATTERN'S CELLS ABUT, THREE AND MORE OF A SIZE; A COLUMN STANDS ALONE (intake step 37).
        ///
        /// A stippled or hatched wall arrives from Vectorworks as its fill pattern's cells: closed,
        /// filled shapes one cell in size, shoulder to shoulder along the wall and cut short at its
        /// ends. Each is the size of a column, so 31170's LEVEL P1 PLAN read 311 columns, 248 of
        /// them 36" x 48" black cells edge to edge, and the walls they filled were not read at all.
        ///
        /// A pattern is many of one thing: three or more shapes of ONE size, each standing edge to
        /// edge with the next (facing edges an inch apart or less, overlapping by half the shorter
        /// side), are its cells; a shape of the same width abutting such a run is the run's last cell,
        /// cut short where the wall ends. Two shapes alone are not a pattern — 31138's GC15 (18" x 49")
        /// is drawn as two filled pieces, 18 x 41 and 11 x 18, where a bearing wall crosses it, and
        /// the first cut of this rule ("two shapes that abut are cells") took the column with the
        /// cells (2026-09-10, five columns on L1 and L2; seen in the crop, not argued). Cells leave
        /// the columns, are kept in <see cref="ExtractedGeometry.PatternCells"/> for the overlay, and
        /// their paths are fated <see cref="PathReason.PatternCell"/>, so the ledger says what they were.
        ///
        /// WHAT IT DOES NOT: a column declared by the schedule's own sizes (read by declared size,
        /// never a cell); a column drawn in two or three pieces of different sizes; two cells that only
        /// touch at a corner; a pattern whose cells are rotated (the boxes are the sheet's axes); one or
        /// two cells alone in a wall shorter than three, which still read as columns; the wall the cells
        /// filled, which this pass does not build.
        /// </summary>
        /// <summary>
        /// Open two-point strokes shorter than the length gate that meet end to end exactly, drawn with one pen and
        /// one colour, chained into one path each (step 98). A node where three or more such strokes meet is a
        /// hatch or a symbol, not a curve, and its group is left as it was; so is a chain of one.
        /// </summary>
        /// <summary>
        /// The open two-point strokes shorter than the length gate whose both ends meet, exactly, an end of a long open
        /// two-point stroke drawn with the same pen and colour (step 110): a jog in the drawn linework, kept as the line
        /// it is - a step in an edge, a notch round a column. A stroke a curve claimed (step 98) or a footing claimed
        /// (step 44) is not one.
        /// </summary>
        internal static HashSet<int> JogsBetweenLongLines(IReadOnlyList<RawSubpath> paths, double lineMinLengthMm, IReadOnlySet<int> curveMembers, IReadOnlyDictionary<int, int>? claimed = null)
        {
            // every long line's ends, with the direction the line leaves that end in
            var longEnds = new Dictionary<(long, long, double, (byte, byte, byte)), List<(double X, double Y)>>();
            (long, long, double, (byte, byte, byte)) Key((double X, double Y) p, RawSubpath s) => ((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10), s.LineWidth, s.Color);
            for (int i = 0; i < paths.Count; i++)
            {
                var s = paths[i];
                if (s.IsClosed || s.IsAnnotation || s.IsFilled || !s.IsStroked || s.Points.Count != 2) continue;
                double len = PolygonProcessor.PathLength(s.Points);
                if (len < lineMinLengthMm) continue;
                var a = s.Points[0]; var b = s.Points[1];
                (longEnds.TryGetValue(Key(a, s), out var la) ? la : longEnds[Key(a, s)] = new()).Add(((b.X - a.X) / len, (b.Y - a.Y) / len));
                (longEnds.TryGetValue(Key(b, s), out var lb) ? lb : longEnds[Key(b, s)] = new()).Add(((a.X - b.X) / len, (a.Y - b.Y) / len));
            }
            var jogs = new HashSet<int>();
            if (longEnds.Count == 0) return jogs;
            for (int i = 0; i < paths.Count; i++)
            {
                var s = paths[i];
                if (s.IsClosed || s.IsAnnotation || s.IsFilled || !s.IsStroked || s.Points.Count != 2 || curveMembers.Contains(i) || (claimed is not null && claimed.ContainsKey(i))) continue;
                if (PolygonProcessor.PathLength(s.Points) >= lineMinLengthMm) continue;
                if (!longEnds.TryGetValue(Key(s.Points[0], s), out var atA) || !longEnds.TryGetValue(Key(s.Points[1], s), out var atB)) continue;
                // FORM A, the one kept (22:30): both ends on long lines' ends, whichever way the long lines leave. Form B asked the
                // two long lines to leave the jog in OPPOSITE directions (a step, not a U) to spare 31065's stair wells, and lost
                // 31130 L17's plate at once - its outline notches round a column as a U (126 mm down, 354 mm along the column's
                // face, 914 mm down again). The U in the well is the well rule's to read (step 105d), not this rule's to refuse.
                jogs.Add(i); _ = atA; _ = atB;
            }
            return jogs;
        }

        internal static List<(RawSubpath Path, List<int> Members)> CurvesOfShortStrokes(IReadOnlyList<RawSubpath> paths, double lineMinLengthMm, IReadOnlyDictionary<int, int>? claimed = null)
        {
            var curves = new List<(RawSubpath, List<int>)>();
            var byEnd = new Dictionary<(long, long, double, (byte, byte, byte)), List<int>>();
            (long, long, double, (byte, byte, byte)) Key((double X, double Y) p, RawSubpath s) => ((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10), s.LineWidth, s.Color);
            var eligible = new List<int>();
            for (int i = 0; i < paths.Count; i++)
            {
                var s = paths[i];
                if (s.IsClosed || s.IsAnnotation || s.IsFilled || !s.IsStroked || s.Points.Count != 2 || (claimed is not null && claimed.ContainsKey(i))) continue;
                if (PolygonProcessor.PathLength(s.Points) >= lineMinLengthMm) continue;
                eligible.Add(i);
                foreach (var e in s.Points)
                    (byEnd.TryGetValue(Key(e, s), out var at) ? at : byEnd[Key(e, s)] = new List<int>()).Add(i);
            }
            if (eligible.Count < 2) return curves;
            var seen = new HashSet<int>();
            foreach (int start in eligible)
            {
                if (seen.Contains(start)) continue;
                // the connected group through shared ends
                var group = new List<int>(); var stack = new Stack<int>(); stack.Push(start); seen.Add(start);
                bool simple = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); group.Add(i);
                    foreach (var e in paths[i].Points)
                    {
                        var at = byEnd[Key(e, paths[i])];
                        if (at.Count > 2) simple = false;
                        foreach (int j in at) if (seen.Add(j)) stack.Push(j);
                    }
                }
                if (!simple || group.Count < 2) continue;
                // walk it from an end (a node with one stroke); a closed loop of short strokes has none and is a bubble, not a curve
                int first = group.FirstOrDefault(i => paths[i].Points.Any(e => byEnd[Key(e, paths[i])].Count == 1), -1);
                if (first < 0) continue;
                var pts = new List<(double X, double Y)>();
                var used = new HashSet<int>();
                int cur = first;
                var from = paths[first].Points.First(e => byEnd[Key(e, paths[first])].Count == 1);
                pts.Add(from);
                while (cur >= 0 && used.Add(cur))
                {
                    var next = paths[cur].Points[0] == from ? paths[cur].Points[1] : paths[cur].Points[0];
                    pts.Add(next);
                    var at = byEnd[Key(next, paths[cur])];
                    int following = at.FirstOrDefault(j => j != cur && !used.Contains(j), -1);
                    from = next; cur = following;
                }
                if (used.Count != group.Count) continue;
                var s0 = paths[first];
                curves.Add((new RawSubpath(pts, false, s0.Color, false, true, s0.LineWidth, false), group.OrderBy(i => i).ToList()));
            }
            return curves;
        }

        internal static void PatternCellsAreNotColumns(ExtractedGeometry result, IList<bool> columnByShape, IList<PathFate>? fates, int firstFate)
        {
            int n = result.Columns.Count;
            if (n < 3) return;
            bool[] byShape = Enumerable.Range(0, n).Select(i => i < columnByShape.Count && columnByShape[i]).ToArray();
            (double W, double H) SizeOf(int i) => i < result.ColumnSizes.Count ? result.ColumnSizes[i] : (0.0, 0.0);
            bool Abut(int i, int j)
            {
                var (wi, hi) = SizeOf(i); var (wj, hj) = SizeOf(j);
                if (wi <= 0 || hi <= 0 || wj <= 0 || hj <= 0) return false;
                double dx = Math.Abs(result.Columns[i].X - result.Columns[j].X), dy = Math.Abs(result.Columns[i].Y - result.Columns[j].Y);
                bool sideBySide = Math.Abs(dx - (wi + wj) / 2) <= CellAbutMm && dy <= (hi + hj) / 2 - Math.Min(hi, hj) / 2;
                bool endToEnd = Math.Abs(dy - (hi + hj) / 2) <= CellAbutMm && dx <= (wi + wj) / 2 - Math.Min(wi, wj) / 2;
                return sideBySide || endToEnd;
            }
            bool SameSize(int i, int j)
            {
                var (wi, hi) = SizeOf(i); var (wj, hj) = SizeOf(j);
                return Math.Abs(wi - wj) <= CellAbutMm && Math.Abs(hi - hj) <= CellAbutMm;
            }
            bool SharesASide(int i, int j)
            {
                var (wi, hi) = SizeOf(i); var (wj, hj) = SizeOf(j);
                return Math.Abs(wi - wj) <= CellAbutMm || Math.Abs(hi - hj) <= CellAbutMm;
            }

            // runs: connected groups of same-size shapes that abut; three or more make a pattern
            var group = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
            for (int i = 0; i < n; i++)
            {
                if (!byShape[i]) continue;
                for (int j = i + 1; j < n; j++)
                    if (byShape[j] && SameSize(i, j) && Abut(i, j)) group[Find(i)] = Find(j);
            }
            var size = new int[n];
            for (int i = 0; i < n; i++) if (byShape[i]) size[Find(i)]++;
            var cell = new bool[n];
            for (int i = 0; i < n; i++) cell[i] = byShape[i] && size[Find(i)] >= 3;

            // the cut-short cell at a run's end: the run's width, abutting a cell of it, and NO LARGER than
            // that cell either way - "cut short" means shorter (a 600 x 1000 column beside a run of 600 x 800
            // cells was taken as its end cell, Codex audit 2026-09-11, F16). Only cells of the run recruit;
            // an end cell recruits nothing.
            bool NoLargerThan(int i, int j)
            {
                var (wi, hi) = SizeOf(i); var (wj, hj) = SizeOf(j);
                return wi <= wj + CellAbutMm && hi <= hj + CellAbutMm;
            }
            var runCell = (bool[])cell.Clone();
            for (int i = 0; i < n; i++)
            {
                if (cell[i] || !byShape[i]) continue;
                for (int j = 0; j < n; j++)
                    if (runCell[j] && SharesASide(i, j) && NoLargerThan(i, j) && Abut(i, j)) { cell[i] = true; break; }
            }
            if (!cell.Any(c => c)) return;

            // the survivors keep their order; every fate that pointed at a column is re-pointed, and
            // a cell's path is fated as what it was
            var newIndex = new int[n];
            int kept = 0;
            for (int i = 0; i < n; i++) newIndex[i] = cell[i] ? -1 : kept++;
            for (int i = n - 1; i >= 0; i--)
            {
                if (!cell[i]) continue;
                result.PatternCells.Add((result.Columns[i], i < result.ColumnSizes.Count ? result.ColumnSizes[i].WidthMm : 0, i < result.ColumnSizes.Count ? result.ColumnSizes[i].DepthMm : 0));
                result.Columns.RemoveAt(i);
                if (i < result.ColumnColors.Count) result.ColumnColors.RemoveAt(i);
                if (i < result.ColumnIsAnnotation.Count) result.ColumnIsAnnotation.RemoveAt(i);
                if (i < result.ColumnSizes.Count) result.ColumnSizes.RemoveAt(i);
                // and the by-shape flags, which the quadrant pass reads next by the SURVIVORS' indexes: left
                // uncompacted, three removed cells ahead of two declared columns handed the pair the cells'
                // flags and the pair went as a target's quadrants (Codex audit 2026-09-13, F1)
                if (i < columnByShape.Count) columnByShape.RemoveAt(i);
            }
            if (fates is null) return;
            for (int k = firstFate; k < fates.Count; k++)
            {
                var f = fates[k];
                if (f.Reason is not (PathReason.BecameColumnByShape or PathReason.BecameColumnByDeclaredSize) || f.ObjectIndex is not int oi || oi < 0 || oi >= n) continue;
                fates[k] = newIndex[oi] < 0
                    ? new PathFate(f.PathIndex, PathFate.DispositionOf(PathReason.PatternCell), PathReason.PatternCell, null)
                    : f with { ObjectIndex = newIndex[oi] };
            }
        }

        /// <summary>
        /// A TARGET'S QUADRANTS ARE NOT COLUMNS (intake step 49).
        ///
        /// A spot-elevation target is a circle with two of its quadrants filled, diagonally opposite:
        /// two filled squares of one size that meet at the circle's centre and nowhere else. Each
        /// square is column-sized, so the reader took the pair as two 9" x 9" columns - 62 pairs on
        /// 31202's plans - and downstream the DXF loop builder walked both squares through the shared
        /// corner as one figure-of-eight, an "18 x 18" whose centroid the area formula put at
        /// (-3.2 km, 3.3 km). Every banked model of the set carried eight such points; the render
        /// drew each storey as a dot in a frame that held them (2026-09-12).
        ///
        /// No two columns share only a corner. A column drawn in pieces meets itself on an edge
        /// (31138's GC15, step 37); a pattern's cells stand edge to edge. Two same-size filled shapes
        /// whose centres are a width apart in x AND a depth apart in y touch at one point, and that
        /// is a symbol. The pair leaves the columns, is kept in <see cref="ExtractedGeometry.SymbolQuadrants"/>
        /// for the overlay, and its paths are fated <see cref="PathReason.SymbolQuadrant"/>.
        ///
        /// WHAT IT DOES NOT: a column declared by the schedule's sizes (never a quadrant); the circle
        /// and the two unfilled quadrants, which are lines; a target drawn with one filled quadrant
        /// or with all four; two shapes of different sizes at a corner; a diagonal of three or more
        /// sharing corners (a checkerboard, not a target: those stand); the spot elevation's text.
        /// </summary>
        internal static void ATargetsQuadrantsAreNotColumns(ExtractedGeometry result, IReadOnlyList<bool> columnByShape, IList<PathFate>? fates, int firstFate)
        {
            int n = result.Columns.Count;
            if (n < 2) return;
            bool[] byShape = Enumerable.Range(0, n).Select(i => i < columnByShape.Count && columnByShape[i]).ToArray();
            (double W, double H) SizeOf(int i) => i < result.ColumnSizes.Count ? result.ColumnSizes[i] : (0.0, 0.0);
            bool CornerToCorner(int i, int j)
            {
                var (wi, hi) = SizeOf(i); var (wj, hj) = SizeOf(j);
                if (wi <= 0 || hi <= 0 || wj <= 0 || hj <= 0) return false;
                if (Math.Abs(wi - wj) > CellAbutMm || Math.Abs(hi - hj) > CellAbutMm) return false;
                double dx = Math.Abs(result.Columns[i].X - result.Columns[j].X), dy = Math.Abs(result.Columns[i].Y - result.Columns[j].Y);
                return Math.Abs(dx - (wi + wj) / 2) <= CellAbutMm && Math.Abs(dy - (hi + hj) / 2) <= CellAbutMm;
            }
            // a PAIR: each is the other's only corner twin. A diagonal of three sharing corners is a
            // checkerboard's cells (step 37 leaves those standing), not a target, and stays.
            var twin = new int[n];
            var twins = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (!byShape[i]) continue;
                for (int j = i + 1; j < n; j++)
                    if (byShape[j] && CornerToCorner(i, j)) { twin[i] = j; twins[i]++; twin[j] = i; twins[j]++; }
            }
            var quadrant = new bool[n];
            for (int i = 0; i < n; i++)
                quadrant[i] = twins[i] == 1 && twins[twin[i]] == 1;
            if (!quadrant.Any(q => q)) return;

            var newIndex = new int[n];
            int kept = 0;
            for (int i = 0; i < n; i++) newIndex[i] = quadrant[i] ? -1 : kept++;
            for (int i = n - 1; i >= 0; i--)
            {
                if (!quadrant[i]) continue;
                result.SymbolQuadrants.Add((result.Columns[i], i < result.ColumnSizes.Count ? result.ColumnSizes[i].WidthMm : 0, i < result.ColumnSizes.Count ? result.ColumnSizes[i].DepthMm : 0));
                result.Columns.RemoveAt(i);
                if (i < result.ColumnColors.Count) result.ColumnColors.RemoveAt(i);
                if (i < result.ColumnIsAnnotation.Count) result.ColumnIsAnnotation.RemoveAt(i);
                if (i < result.ColumnSizes.Count) result.ColumnSizes.RemoveAt(i);
            }
            if (fates is null) return;
            for (int k = firstFate; k < fates.Count; k++)
            {
                var f = fates[k];
                if (f.Reason is not (PathReason.BecameColumnByShape or PathReason.BecameColumnByDeclaredSize) || f.ObjectIndex is not int oi || oi < 0 || oi >= n) continue;
                fates[k] = newIndex[oi] < 0
                    ? new PathFate(f.PathIndex, PathFate.DispositionOf(PathReason.SymbolQuadrant), PathReason.SymbolQuadrant, null)
                    : f with { ObjectIndex = newIndex[oi] };
            }
        }

        /// <summary>
        /// A PATTERN'S STRIPES ARE NOT WALLS (intake step 38, step 37's principle for the filled wall
        /// rule). Admitting a mitred end (<see cref="IsWallShape"/>) admits a hatch stripe too — a
        /// parallelogram of wall proportions — and 31170's P1 plan draws its accessible stalls with
        /// three grey 22" x 68" stripes each. A pattern is many of one thing: three or more filled
        /// walls of one thickness and length, parallel, spaced at one pitch across their width (a
        /// hatch's stripes stand apart by their own width; walls stand a room apart), are its stripes.
        /// They leave the walls, and their paths are fated <see cref="PathReason.PatternCell"/>.
        ///
        /// WHAT IT DOES NOT: two stripes; stripes of different lengths (a stripe clipped by the
        /// symbol's edge is shorter, and the first and last of a run may be); a wall read from face
        /// lines, which comes after this; a rotated pattern's box, which the oriented box handles.
        /// </summary>
        internal static void PatternStripesAreNotWalls(ExtractedGeometry result, IList<PathFate>? fates, int firstFate)
        {
            int n = result.Walls.Count;
            if (n < 3) return;
            var stripe = new bool[n];
            var info = result.Walls.Select(w =>
            {
                double dx = w.End.X - w.Start.X, dy = w.End.Y - w.Start.Y, len = Math.Sqrt(dx * dx + dy * dy);
                double ux = len > 0 ? dx / len : 1, uy = len > 0 ? dy / len : 0;
                if (ux < 0 || (ux == 0 && uy < 0)) { ux = -ux; uy = -uy; }
                double mx = (w.Start.X + w.End.X) / 2, my = (w.Start.Y + w.End.Y) / 2;
                return (Len: len, T: w.ThicknessMm, Ux: ux, Uy: uy, Off: -uy * mx + ux * my, Along: ux * mx + uy * my);
            }).ToList();
            for (int i = 0; i < n; i++)
            {
                if (stripe[i]) continue;
                // the family of i: same thickness and length, parallel, overlapping along the run
                var family = new List<int> { i };
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    if (Math.Abs(info[j].T - info[i].T) > CellAbutMm || Math.Abs(info[j].Len - info[i].Len) > CellAbutMm) continue;
                    if (info[i].Ux * info[j].Ux + info[i].Uy * info[j].Uy < FaceParallelCos) continue;
                    if (Math.Abs(info[j].Along - info[i].Along) > info[i].Len) continue;     // a diagonal hatch steps along as it steps across
                    family.Add(j);
                }
                if (family.Count < 3) continue;
                // sorted across the run: a run of three or more at one pitch, the pitch no more than a few widths
                var ordered = family.OrderBy(k => info[k].Off).ToList();
                for (int s = 0; s + 2 < ordered.Count; s++)
                {
                    double p0 = info[ordered[s + 1]].Off - info[ordered[s]].Off, p1 = info[ordered[s + 2]].Off - info[ordered[s + 1]].Off;
                    if (p0 <= 0 || Math.Abs(p1 - p0) > CellAbutMm || p0 > 4 * info[i].T) continue;
                    int e = s + 2;
                    while (e + 1 < ordered.Count && Math.Abs(info[ordered[e + 1]].Off - info[ordered[e]].Off - p0) <= CellAbutMm) e++;
                    for (int k = s; k <= e; k++) stripe[ordered[k]] = true;
                    s = e;
                }
            }
            if (!stripe.Any(x => x)) return;

            var newIndex = new int[n];
            int kept = 0;
            for (int i = 0; i < n; i++) newIndex[i] = stripe[i] ? -1 : kept++;
            for (int i = n - 1; i >= 0; i--)
            {
                if (!stripe[i]) continue;
                result.PatternCells.Add((((result.Walls[i].Start.X + result.Walls[i].End.X) / 2, (result.Walls[i].Start.Y + result.Walls[i].End.Y) / 2), result.Walls[i].ThicknessMm, info[i].Len));
                result.Walls.RemoveAt(i);
                if (i < result.WallColors.Count) result.WallColors.RemoveAt(i);
                if (i < result.WallIsAnnotation.Count) result.WallIsAnnotation.RemoveAt(i);
            }
            result.FirstFaceWall = result.Walls.Count;
            // a doorway whose first pier left with the stripes has no wall to be a doorway in: it leaves
            // too, and the fates that pointed at doorways are re-pointed or discarded (Codex audit
            // 2026-09-11, F14 - a doorway kept pointing at a wall index that no longer existed)
            var doorwayIndex = new int[result.Doorways.Count];
            var keptDoorways = new List<Doorway>();
            for (int d = 0; d < result.Doorways.Count; d++)
            {
                int pier = result.Doorways[d].FirstPier;
                bool survives = pier >= 0 && pier < n && newIndex[pier] >= 0;
                doorwayIndex[d] = survives ? keptDoorways.Count : -1;
                if (survives) keptDoorways.Add(result.Doorways[d] with { FirstPier = newIndex[pier] });
            }
            result.Doorways.Clear(); result.Doorways.AddRange(keptDoorways);
            var faceKeys = result.WallFaceLines.Keys.ToList();
            foreach (int k in faceKeys)
                if (result.WallFaceLines[k] < n) result.WallFaceLines[k] = newIndex[result.WallFaceLines[k]];
            if (fates is null) return;
            for (int k = firstFate; k < fates.Count; k++)
            {
                var f = fates[k];
                if (f.Reason == PathReason.Doorway && f.ObjectIndex is int di && di >= 0 && di < doorwayIndex.Length)
                {
                    fates[k] = doorwayIndex[di] < 0
                        ? new PathFate(f.PathIndex, PathFate.DispositionOf(PathReason.PaperFill), PathReason.PaperFill, null)
                        : f with { ObjectIndex = doorwayIndex[di] };
                    continue;
                }
                if (f.Reason is not (PathReason.BecameWall or PathReason.ClipOfWall) || f.ObjectIndex is not int oi || oi < 0 || oi >= n) continue;
                fates[k] = newIndex[oi] < 0
                    ? new PathFate(f.PathIndex, PathFate.DispositionOf(PathReason.PatternCell), PathReason.PatternCell, null)
                    : f with { ObjectIndex = newIndex[oi] };
            }
        }

        /// <summary>Two pieces of one line lie within this of the same line: drafting exact, with a pen's width of slack.</summary>
        private const double PieceLateralMm = 5.0;

        /// <summary>
        /// A FACE DRAWN IN PIECES THROUGH A FILL PATTERN IS ONE FACE (intake step 38; step 27's rule for
        /// a slab edge, for the lines a pattern runs along). A stippled wall on 31170's P1 plan is drawn
        /// as its two faces and a cross line at every cell of the fill, and each face arrives as the
        /// pieces between the cross lines — seven and nine pieces for a 9.4 m wall, each about a cell
        /// long. A piece is not a face: the face reader wants a line a wall's length.
        ///
        /// Two emitted lines of the same pen and colour, neither an annotation, BOTH LYING WITHIN THE
        /// CELLS OF A FILL PATTERN (step 37's), on one line (parallel within a degree, both ends within
        /// <see cref="PieceLateralMm"/> of it) and meeting end to end within an inch of drafting — or
        /// overlapping — are one line, and every fate that pointed at a piece points at the whole. A gap
        /// wider than an inch is a doorway or a break the drafter meant, and stays.
        ///
        /// ⛔ Joining EVERY collinear touching pair was the first cut, and the six-set run refused it
        /// (2026-09-10: 31065 walls 400 → 388, 31202 360 → 343): a Revit export draws two walls that
        /// meet end to end as two faces that touch — a 12" wall's face and the 10" wall's beyond it —
        /// and joined, the one face pairs with neither. Touching is not the same line; only the
        /// pattern that runs across both pieces says it is.
        ///
        /// WHAT IT DOES NOT: pieces outside any pattern cell; pieces in different pens; annotations; a
        /// face broken by a doorway; the closed shapes; the slab-edge chain, which still bridges its
        /// own gaps (step 27).
        /// </summary>
        internal static void AFaceInPiecesIsOneFace(ExtractedGeometry result, IList<PathFate>? fates, int firstFate)
        {
            int n = result.Lines.Count;
            if (n < 2 || result.PatternCells.Count == 0) return;
            var cellBoxes = result.PatternCells.Where(c => c.WidthMm > 0 && c.DepthMm > 0)
                .Select(c => (X0: c.Centre.X - c.WidthMm / 2 - CellAbutMm, X1: c.Centre.X + c.WidthMm / 2 + CellAbutMm, Y0: c.Centre.Y - c.DepthMm / 2 - CellAbutMm, Y1: c.Centre.Y + c.DepthMm / 2 + CellAbutMm)).ToList();
            bool InACell(double x, double y) => cellBoxes.Any(b => x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1);
            // the line each piece lies on: its direction (folded to a half-turn) and its signed offset
            // from the origin along the normal, binned coarsely; pieces of one line share a bin or a
            // neighbouring one, so each piece is tried against its own bin and the bins beside it
            var keyed = new List<(int Index, double Ux, double Uy, double Off, double T0, double T1)>();
            var bins = new Dictionary<(int A, int O), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var l = result.Lines[i];
                if (l.Count != 2 || (i < result.LineIsAnnotation.Count && result.LineIsAnnotation[i])) continue;
                if (!InACell(l[0].X, l[0].Y) || !InACell(l[1].X, l[1].Y)) continue;
                double dx = l[1].X - l[0].X, dy = l[1].Y - l[0].Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                double ux = dx / len, uy = dy / len;
                if (ux < 0 || (ux == 0 && uy < 0)) { ux = -ux; uy = -uy; }          // one direction per line
                double off = -uy * l[0].X + ux * l[0].Y;                              // along the normal (-uy, ux)
                double t0 = l[0].X * ux + l[0].Y * uy, t1 = l[1].X * ux + l[1].Y * uy;
                if (t0 > t1) (t0, t1) = (t1, t0);
                int a = (int)Math.Round(Math.Atan2(uy, ux) * 180 / Math.PI * 4);       // quarter-degree bins
                int o = (int)Math.Floor(off / (4 * PieceLateralMm));
                keyed.Add((i, ux, uy, off, t0, t1));
                if (!bins.TryGetValue((a, o), out var list)) bins[(a, o)] = list = new List<int>();
                list.Add(keyed.Count - 1);
            }
            var group = Enumerable.Range(0, keyed.Count).ToArray();
            int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
            bool SamePen(int i, int j)
                => (i >= result.LineWidths.Count || j >= result.LineWidths.Count || Math.Abs(result.LineWidths[i] - result.LineWidths[j]) <= 0.01)
                   && (i >= result.LineColors.Count || j >= result.LineColors.Count || result.LineColors[i] == result.LineColors[j]);
            for (int k = 0; k < keyed.Count; k++)
            {
                var a = keyed[k];
                int ab = (int)Math.Round(Math.Atan2(a.Uy, a.Ux) * 180 / Math.PI * 4), ob = (int)Math.Floor(a.Off / (4 * PieceLateralMm));
                for (int da = -1; da <= 1; da++)
                    for (int dO = -1; dO <= 1; dO++)
                    {
                        if (!bins.TryGetValue((ab + da, ob + dO), out var list)) continue;
                        foreach (int m in list)
                        {
                            if (m <= k) continue;
                            var b = keyed[m];
                            if (a.Ux * b.Ux + a.Uy * b.Uy < FaceParallelCos) continue;
                            if (Math.Abs(a.Off - b.Off) > PieceLateralMm) continue;
                            if (!SamePen(a.Index, b.Index)) continue;
                            // end to end within an inch, or overlapping
                            double gap = Math.Max(a.T0, b.T0) - Math.Min(a.T1, b.T1);
                            if (gap > CellAbutMm) continue;
                            group[Find(k)] = Find(m);
                        }
                    }
            }
            var members = new Dictionary<int, List<int>>();
            for (int k = 0; k < keyed.Count; k++)
            {
                int root = Find(k);
                if (!members.TryGetValue(root, out var list)) members[root] = list = new List<int>();
                list.Add(k);
            }
            if (!members.Values.Any(v => v.Count > 1)) return;

            // rebuild the lines: a piece alone stays as it is; a group becomes one line from its
            // first end to its last, in the first piece's pen, at the first piece's index
            var newIndex = new int[n];
            Array.Fill(newIndex, -1);
            var keepLines = new List<List<(double X, double Y)>>();
            var keepColors = new List<(byte R, byte G, byte B)>();
            var keepWidths = new List<double>();
            var keepAnnotation = new List<bool>();
            var joined = new Dictionary<int, (List<int> Pieces, double Ux, double Uy, double Off, double T0, double T1)>();
            foreach (var (root, list) in members)
            {
                if (list.Count < 2) continue;
                double t0 = list.Min(k => keyed[k].T0), t1 = list.Max(k => keyed[k].T1);
                var first = keyed[list.OrderBy(k => keyed[k].Index).First()];
                double off = list.Average(k => keyed[k].Off);
                joined[first.Index] = (list.Select(k => keyed[k].Index).ToList(), first.Ux, first.Uy, off, t0, t1);
            }
            var absorbed = new HashSet<int>(joined.Values.SelectMany(j => j.Pieces));
            for (int i = 0; i < n; i++)
            {
                if (joined.TryGetValue(i, out var j))
                {
                    double nx = -j.Uy, ny = j.Ux;
                    keepLines.Add([(j.Ux * j.T0 + nx * j.Off, j.Uy * j.T0 + ny * j.Off), (j.Ux * j.T1 + nx * j.Off, j.Uy * j.T1 + ny * j.Off)]);
                }
                else if (absorbed.Contains(i)) continue;
                else keepLines.Add(result.Lines[i]);
                keepColors.Add(i < result.LineColors.Count ? result.LineColors[i] : ((byte)0, (byte)0, (byte)0));
                keepWidths.Add(i < result.LineWidths.Count ? result.LineWidths[i] : 0);
                keepAnnotation.Add(i < result.LineIsAnnotation.Count && result.LineIsAnnotation[i]);
                newIndex[i] = keepLines.Count - 1;
            }
            foreach (var (head, j) in joined)
                foreach (int piece in j.Pieces) newIndex[piece] = newIndex[head];
            result.Lines.Clear(); result.Lines.AddRange(keepLines);
            result.LineColors.Clear(); result.LineColors.AddRange(keepColors);
            result.LineWidths.Clear(); result.LineWidths.AddRange(keepWidths);
            result.LineIsAnnotation.Clear(); result.LineIsAnnotation.AddRange(keepAnnotation);
            result.LinePiecesJoined += absorbed.Count - joined.Count;
            if (fates is null) return;
            for (int k = firstFate; k < fates.Count; k++)
            {
                var f = fates[k];
                if (f.Reason != PathReason.EmittedAsLine || f.ObjectIndex is not int oi || oi < 0 || oi >= n || newIndex[oi] < 0) continue;
                fates[k] = f with { ObjectIndex = newIndex[oi] };
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
        /// <summary>The leak finder's last break - the pinch on its route nearest a line's end (an instrument behind FaceTrace, 2026-09-19).</summary>
        [ThreadStatic] private static DxfPoint? LastBreak;
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
            // A WALL IS WHAT A FILL PATTERN FILLS (intake step 38, with step 37). A stippled wall's faces
            // are in a light pen — 31170's P1 plan draws them at w0.60 while its cut pen is w3.30 — and
            // the pen gate alone would never pair them. The fill says what they are: a line lying
            // within the cells of a fill pattern, both ends, is a cut line whatever its pen, because
            // the pattern is the cut material. The cells are the boxes step 37 took out of the columns.
            var cellBoxes = result.PatternCells
                .Where(c => c.WidthMm > 0 && c.DepthMm > 0)
                .Select(c => (X0: c.Centre.X - c.WidthMm / 2 - CellAbutMm, X1: c.Centre.X + c.WidthMm / 2 + CellAbutMm, Y0: c.Centre.Y - c.DepthMm / 2 - CellAbutMm, Y1: c.Centre.Y + c.DepthMm / 2 + CellAbutMm))
                .ToList();
            bool InACell(double x, double y) => cellBoxes.Any(b => x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1);
            bool InPattern(List<(double X, double Y)> l) => cellBoxes.Count > 0 && InACell(l[0].X, l[0].Y) && InACell(l[1].X, l[1].Y);
            // ⛔ A STIPPLE BESIDE A LINE IS NOT EVIDENCE OF CUT MATERIAL — tried and refused (2026-09-10). The
            // P1 perimeter of 31170 is a dot stipple between a heavy face and a light one, and "a line with a
            // stipple's dots beside it, on three rows, is a cut line whatever its pen" read it — and then read
            // 110 more walls on the LEVEL 1 key plan (195 → 305) and, on KOR's 31130 parkade, made the edges
            // of a cross-hatched slab-reinforcing zone ("17-35M19.8 @ 12" EXTRA BOT.") cut lines, 48"–114"
            // stubs all over L1 (191 → 264 walls). A hatch marks what a drafter chooses — a slab zone, a
            // footing, a stall — and only the architect's convention makes it concrete. The perimeter it was
            // built for is read anyway: it is a pattern-filled rectangle with a mitred end (IsWallShape).
            // It bought 3 walls on P1. Not universal; not kept.
            if (cutPen <= 0 && cellBoxes.Count == 0) return;
            bool CutPenLine(int i) => cutPen > 0 && i < result.LineWidths.Count && Math.Abs(result.LineWidths[i] - cutPen) <= PenMatchShare * cutPen;

            // every straight line long enough to be a face, and which are in the cut pen
            var segs = new List<(int Line, (double X, double Y) A, (double X, double Y) B, double Len, double Ux, double Uy, bool Cut)>();
            for (int i = 0; i < result.Lines.Count; i++)
            {
                var line = result.Lines[i];
                if (line.Count != 2 || result.LineIsAnnotation[i]) continue;
                double dx = line[1].X - line[0].X, dy = line[1].Y - line[0].Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < minWallLengthMm - WallLimitSlackMm) continue;
                segs.Add((i, line[0], line[1], len, dx / len, dy / len, CutPenLine(i) || InPattern(line)));
            }
            if (FaceTrace is not null)
                foreach (var sg in segs.Where(sg => sg.Cut && !CutPenLine(sg.Line)))
                    FaceTrace($"cut by fill: line {sg.Len / 25.4:0}\" w{(sg.Line < result.LineWidths.Count ? result.LineWidths[sg.Line] : -1):0.00} at ({(sg.A.X + sg.B.X) / 2:0},{(sg.A.Y + sg.B.Y) / 2:0}) mm is a cut line by the pattern cells around it");
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

            // every path that became this line: a face joined from pieces (step 38) is several paths, and
            // each of them became the wall's face (Codex audit 2026-09-11, F15 - one map entry per line
            // re-fated the last piece only)
            var lineToPaths = new Dictionary<int, List<int>>();
            if (fates is not null)
                for (int k = firstFate; k < fates.Count; k++)
                    if (fates[k].Reason == PathReason.EmittedAsLine && fates[k].ObjectIndex is int li)
                        (lineToPaths.TryGetValue(li, out var ps) ? ps : lineToPaths[li] = new List<int>()).Add(fates[k].PathIndex);

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
                    if (Math.Min(Math.Abs(rel.D0), Math.Abs(rel.D1)) < minWallThicknessMm - WallFloorSlackMm
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
                            if (lineToPaths.TryGetValue(line, out var pathIndices))
                                for (int k = firstFate; k < fates.Count; k++)
                                    if (pathIndices.Contains(fates[k].PathIndex))
                                        fates[k] = new PathFate(fates[k].PathIndex, Disposition.Read, PathReason.BecameWallFace, wallIndex);
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
        /// A wall's face line is offered to the chain too when the wall did not use the whole of it
        /// (step 28): a wall standing on part of the edge does not remove the edge, and taking a face
        /// whole was spending a tower floor's whole side on a few metres of balcony band. A face the
        /// wall runs the full length of stays the wall's, or slanted walls' own faces chain into
        /// floors that are not there.
        ///
        /// WHAT IT IS NOT: a fill, a hatch or a flood. Where the drawing's edge does not close, this
        /// finds nothing and the storey keeps having no plate, which the DXF side already reports.
        /// </summary>
        /// <summary>An X mark's centre, its reach (the longer arm) and its region (the four ends in order round the crossing).</summary>
        /// <summary>A tread is a paper-filled rectangle this wide across the flight (a stair is 1 m and more wide, 1.6 m and less on a plan drawn in concrete).</summary>
        internal const double TreadMinWidthMm = 900, TreadMaxWidthMm = 1700;
        /// <summary>A tread is this deep along the flight: 250-300 mm is a going; the paper fill is drawn to it (31202: 280 mm).</summary>
        internal const double TreadMinDepthMm = 220, TreadMaxDepthMm = 340;
        /// <summary>A flight is this many treads and more; fewer is a step or a symbol.</summary>
        internal const int FlightMinTreads = 5;

        /// <summary>
        /// The stair flights among the sheet's paper fills (step 105): runs of <see cref="FlightMinTreads"/> or more axis-aligned
        /// rectangles of one tread size (width across the flight, depth along it) stacked along the flight at their own depth
        /// (a gap under half a depth between neighbours). Each flight as its extent and its tread count.
        /// </summary>
        internal static List<(double X0, double Y0, double X1, double Y1, int Treads)> StairFlights(IEnumerable<List<(double X, double Y)>> paperFills, IEnumerable<List<(double X, double Y)>>? treadLines = null, List<((double X, double Y) A, (double X, double Y) B)>? flightLines = null)
        {
            treadLines ??= [];
            var treads = new List<(double X0, double Y0, double X1, double Y1, bool AlongY)>();
            foreach (var pts in paperFills)
            {
                if (pts.Count is < 4 or > 5) continue;
                double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
                double w = x1 - x0, h = y1 - y0;
                // axis-aligned: every vertex on the box's edges
                if (!pts.All(p => Math.Abs(p.X - x0) < 2 || Math.Abs(p.X - x1) < 2 || Math.Abs(p.Y - y0) < 2 || Math.Abs(p.Y - y1) < 2)) continue;
                if (w >= TreadMinWidthMm && w <= TreadMaxWidthMm && h >= TreadMinDepthMm && h <= TreadMaxDepthMm) treads.Add((x0, y0, x1, y1, true));    // stacked along Y
                else if (h >= TreadMinWidthMm && h <= TreadMaxWidthMm && w >= TreadMinDepthMm && w <= TreadMaxDepthMm) treads.Add((x0, y0, x1, y1, false));
            }
            // treads drawn as LINES (31138, 31130: a stroke across the flight per tread, twelve to a flight, no fill): an
            // axis-aligned two-point line of a tread's width is a tread of no depth; its depth is the pitch to the next.
            // A SHEET DRAWS ITS TREADS ONE WAY: where paper-filled treads were found, the lines are the fills' own edges
            // and the stair's outline, not treads (31202 draws both, and read as treads its lines broke every paper run -
            // eight of sixteen wells lost, 18:00-18:15; the riser-on-a-fill exclusion alone did not restore them).
            int paperTreads = treads.Count;
            foreach (var pts in paperTreads > 0 ? [] : treadLines)
            {
                if (pts.Count != 2) continue;
                double x0 = Math.Min(pts[0].X, pts[1].X), x1 = Math.Max(pts[0].X, pts[1].X), y0 = Math.Min(pts[0].Y, pts[1].Y), y1 = Math.Max(pts[0].Y, pts[1].Y);
                double w = x1 - x0, h = y1 - y0;
                bool alongY = h < 2 && w >= TreadMinWidthMm && w <= TreadMaxWidthMm, alongX = w < 2 && h >= TreadMinWidthMm && h <= TreadMaxWidthMm;
                if (!alongY && !alongX) continue;
                // A RISER LINE ON A FILLED TREAD IS THE TREAD'S OWN EDGE, not another tread (31202 draws both, the fill and a
                // stroke on its edge; taken as treads too they broke every run - characterised 18:10 on p29, a flight of 14)
                bool onAFill = treads.Any(t => t.AlongY == alongY && (alongY
                    ? Math.Abs(t.X0 - x0) <= 5 && Math.Abs(t.X1 - x1) <= 5 && (Math.Abs(t.Y0 - y0) <= 5 || Math.Abs(t.Y1 - y0) <= 5)
                    : Math.Abs(t.Y0 - y0) <= 5 && Math.Abs(t.Y1 - y1) <= 5 && (Math.Abs(t.X0 - x0) <= 5 || Math.Abs(t.X1 - x0) <= 5)));
                if (onAFill) continue;
                treads.Add((x0, y0, x1, y1, alongY));
            }
            var flights = new List<(double X0, double Y0, double X1, double Y1, int Treads)>();
            foreach (bool alongY in new[] { true, false })
            {
                // one column of treads: the same extent across the flight (within a tenth), sorted along it
                var groups = treads.Where(t => t.AlongY == alongY)
                    .GroupBy(t => alongY ? (Math.Round(t.X0 / 100), Math.Round(t.X1 / 100)) : (Math.Round(t.Y0 / 100), Math.Round(t.Y1 / 100)));
                foreach (var g in groups)
                {
                    var run = g.OrderBy(t => alongY ? t.Y0 : t.X0).ToList();
                    int start = 0;
                    for (int i = 1; i <= run.Count; i++)
                    {
                        bool breaks = true;
                        if (i < run.Count)
                        {
                            double depth = alongY ? run[i - 1].Y1 - run[i - 1].Y0 : run[i - 1].X1 - run[i - 1].X0;
                            double gap = alongY ? run[i].Y0 - run[i - 1].Y1 : run[i].X0 - run[i - 1].X1;
                            // a filled tread's neighbour follows within half a depth; a line tread's at a going's pitch
                            breaks = depth >= TreadMinDepthMm ? gap > 0.5 * depth : gap < TreadMinDepthMm || gap > TreadMaxDepthMm;
                        }
                        if (!breaks) continue;
                        if (i - start >= FlightMinTreads)
                        {
                            var f = run.Skip(start).Take(i - start).ToList();
                            flights.Add((f.Min(t => t.X0), f.Min(t => t.Y0), f.Max(t => t.X1), f.Max(t => t.Y1), f.Count));
                            // the treads that are lines, for the well's arrangement to leave out
                            if (flightLines is not null)
                                foreach (var t in f.Where(t => (alongY ? t.Y1 - t.Y0 : t.X1 - t.X0) < 2))
                                    flightLines.Add(alongY ? ((t.X0, t.Y0), (t.X1, t.Y0)) : ((t.X0, t.Y0), (t.X0, t.Y1)));
                        }
                        start = i;
                    }
                }
            }
            return flights;
        }

        internal sealed record XMark(DxfPoint Centre, double Reach, IReadOnlyList<DxfPoint> Region)
        {
            /// <summary>The two lines, by index into the geometry's lines.</summary>
            public (int A, int B) Arms { get; init; }
            /// <summary>The four arm ends are the corners of a drawn rectangle: the drafter's sleeve (step 111).</summary>
            public bool Boxed { get; init; }
        }

        /// <summary>The minimum arm of an X that marks an opening: a shaft's X is 11-12 ft; a symbol's is under a metre.</summary>
        internal const double XMarkMinArmMm = 2000;
        /// <summary>
        /// The minimum arm of a BOXED X (step 111): a sleeve drawn as a rectangle with its two diagonals - 31202's 1,118 x 382 mm
        /// chases (arms 1.18 m) on twelve storeys, 31065's 0.5 x 0.5 m sleeves (arms 0.7 m); 1,895 of her 4,967 exported
        /// openings are under a metre on the short side. A box under this is a symbol's (a column mark, a north arrow).
        /// </summary>
        internal const double XMarkBoxedMinArmMm = 600;
        /// <summary>The longest arm of an X that marks an opening: a stair's is under 30 ft; 31202's 109 ft X spans a region labelled 9" SLAB.</summary>
        internal const double XMarkMaxArmMm = 9144;

        /// <summary>
        /// The pieces of a wall's drawn face lines that run on past the wall's panel along its axis (step 124): for each face
        /// line the reader gave a wall, the parts of it before the panel's start and after its end, as two-point segments,
        /// pieces shorter than <see cref="FaceLeftoverMinMm"/> left out. Never the part over the panel - that lies on the
        /// panel's own outline.
        /// </summary>
        internal static IEnumerable<DxfSegment> FaceLeftovers(ExtractedGeometry result)
        {
            foreach (var (line, wall) in result.WallFaceLines)
            {
                if (line < 0 || line >= result.Lines.Count || wall < 0 || wall >= result.Walls.Count) continue;
                if (result.Lines[line].Count != 2 || result.LineIsAnnotation[line]) continue;
                var w = result.Walls[wall];
                double ax = w.End.X - w.Start.X, ay = w.End.Y - w.Start.Y, al = Math.Sqrt(ax * ax + ay * ay);
                if (al <= 0) continue;
                ax /= al; ay /= al;
                var l = result.Lines[line];
                double t0 = (l[0].X - w.Start.X) * ax + (l[0].Y - w.Start.Y) * ay, t1 = (l[1].X - w.Start.X) * ax + (l[1].Y - w.Start.Y) * ay;
                double lo = Math.Min(t0, t1), hi = Math.Max(t0, t1);
                // the face's own foot: its point at the wall's start, projected onto the face line's direction is the face itself
                (double X, double Y) At(double t) => (l[0].X + (l[1].X - l[0].X) * (t - t0) / (t1 - t0), l[0].Y + (l[1].Y - l[0].Y) * (t - t0) / (t1 - t0));
                if (Math.Abs(t1 - t0) < 1) continue;
                if (lo < -FaceLeftoverMinMm)
                {
                    var (x0, y0) = At(lo); var (x1, y1) = At(Math.Min(0, hi));
                    yield return new DxfSegment("FACE", new DxfPoint(x0, y0), new DxfPoint(x1, y1));
                }
                if (hi > al + FaceLeftoverMinMm)
                {
                    var (x0, y0) = At(Math.Max(al, lo)); var (x1, y1) = At(hi);
                    yield return new DxfSegment("FACE", new DxfPoint(x0, y0), new DxfPoint(x1, y1));
                }
            }
        }

        /// <summary>A face's piece beyond its panel shorter than this is the join tolerance's business, not the arrangement's (step 124).</summary>
        internal const double FaceLeftoverMinMm = 50;

        /// <summary>
        /// Whether a two-point line carries a bar mark (step 126): a word in the bar grammar whose box sits ON the line -
        /// its centre within <see cref="BarMarkOnLineHeights"/> text heights of the line and over the line's length. The
        /// text height is the box's short side, so a label along a vertical bar reads the same as one along a horizontal.
        /// Lines under <see cref="BarRunMinMm"/> are not bar runs - a mark beside a short stroke is a leader's or a tick's.
        /// </summary>
        internal static bool CarriesABarMark(ExtractedGeometry result, IReadOnlyList<(double X, double Y)> pts)
        {
            if (pts.Count != 2 || result.PageWordBoxes.Count == 0) return false;
            double ax = pts[0].X, ay = pts[0].Y, dx = pts[1].X - ax, dy = pts[1].Y - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < BarRunMinMm) return false;
            double ux = dx / len, uy = dy / len;
            foreach (var w in result.PageWordBoxes)
            {
                if (!BarMark.IsMatch(w.Text)) continue;
                double bw = w.MaxX - w.MinX, bh = w.MaxY - w.MinY;
                double h = Math.Min(bw, bh);                                  // the text height, whichever way the label runs
                if (h <= 0) continue;
                double cx = (w.MinX + w.MaxX) / 2, cy = (w.MinY + w.MaxY) / 2;
                double t = (cx - ax) * ux + (cy - ay) * uy;                   // along the line
                if (t < 0 || t > len) continue;
                double d = Math.Abs((cx - ax) * uy - (cy - ay) * ux);         // off the line
                if (d <= BarMarkOnLineHeights * h) return true;
            }
            return false;
        }

        /// <summary>
        /// The office's bar grammar: a Canadian mark - an optional count, an optional C, the size and M, an optional
        /// length and spacing («15M», «12-15M12.6», «C15M18.0», «6-C15M21.4», «15M@12») - or a US size WITH its spacing
        /// («#5@12»). A bare «#5» is not a mark: the architect's plans keynote with «#1»–«#4» (31170's, 82 of them),
        /// and one on a leader took a plate's edge with it.
        /// </summary>
        internal static readonly Regex BarMark = new(@"^(?:(?:\d{1,3}-)?C?\d{2}M(?:\d{1,2}(?:\.\d)?)?(?:@\d{1,3}""?)?|#\d{1,2}@\d{1,3}""?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>A bar's label sits on its bar: the label's centre within this many text heights of the line (step 126).</summary>
        internal const double BarMarkOnLineHeights = 0.8;
        /// <summary>A bar run is at least this long (step 126); a mark beside a shorter stroke labels a tick or a leader.</summary>
        internal const double BarRunMinMm = 600;

        /// <summary>
        /// Every X on the page (step 104): two two-point lines of at least <see cref="XMarkMinArmMm"/>, each over 10 degrees
        /// off both axes, of one length within a fifth, crossing within a tenth of the length of both midpoints.
        /// </summary>
        internal static List<XMark> XMarks(ExtractedGeometry result)
        {
            var marks = new List<XMark>();
            var longLines = new List<(int I, double Lx, double Ly, double Len)>();
            for (int i = 0; i < result.Lines.Count; i++)
            {
                var l = result.Lines[i];
                if (l.Count != 2 || result.LineIsAnnotation[i]) continue;
                double dx = l[1].X - l[0].X, dy = l[1].Y - l[0].Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < XMarkBoxedMinArmMm || Math.Min(Math.Abs(dx), Math.Abs(dy)) / len <= 0.17) continue;   // 10 degrees off both axes
                longLines.Add((i, dx, dy, len));
            }
            // A SLEEVE IS A BOX WITH ITS DIAGONALS (step 111, 2026-09-16 23:00): the drafter's mark for a small opening is the
            // shaft's mark at a sleeve's size - a rectangle with an X in it. Step 104 asked an X for arms of 2 m or more because a
            // symbol's are under a metre; her sleeves' are 0.7-1.2 m. So an X of a symbol's size is a mark only when its four
            // arm ends are the corners of a rectangle the page draws: a two-point line between each pair of neighbouring ends.
            var lineEnds = new List<(DxfPoint A, DxfPoint B)>();
            for (int i = 0; i < result.Lines.Count; i++)
                if (result.Lines[i].Count == 2 && !result.LineIsAnnotation[i]) lineEnds.Add((new DxfPoint(result.Lines[i][0].X, result.Lines[i][0].Y), new DxfPoint(result.Lines[i][1].X, result.Lines[i][1].Y)));
            static bool At(DxfPoint p, DxfPoint q) => Math.Abs(p.X - q.X) <= 50 && Math.Abs(p.Y - q.Y) <= 50;
            bool Boxed(IReadOnlyList<DxfPoint> corners)
            {
                for (int k = 0; k < corners.Count; k++)
                {
                    var a = corners[k]; var b = corners[(k + 1) % corners.Count];
                    if (!lineEnds.Any(l => (At(l.A, a) && At(l.B, b)) || (At(l.A, b) && At(l.B, a)))) return false;
                }
                return true;
            }
            for (int a = 0; a < longLines.Count; a++)
                for (int b = a + 1; b < longLines.Count; b++)
                {
                    var (ia, rx, ry, lp) = longLines[a]; var (ib, sx, sy, lq) = longLines[b];
                    if (Math.Abs(lp - lq) > 0.2 * Math.Max(lp, lq)) continue;
                    var p = result.Lines[ia]; var q = result.Lines[ib];
                    double cross = rx * sy - ry * sx;
                    if (Math.Abs(cross) < 1e-9) continue;
                    double qpx = q[0].X - p[0].X, qpy = q[0].Y - p[0].Y;
                    double t = (qpx * sy - qpy * sx) / cross, u = (qpx * ry - qpy * rx) / cross;
                    if (Math.Abs(t - 0.5) > 0.1 || Math.Abs(u - 0.5) > 0.1) continue;
                    var centre = new DxfPoint(p[0].X + t * rx, p[0].Y + t * ry);
                    var ends = new[] { p[0], p[1], q[0], q[1] }.Select(e => new DxfPoint(e.X, e.Y))
                        .OrderBy(e => Math.Atan2(e.Y - centre.Y, e.X - centre.X)).ToList();
                    bool boxed = Boxed(ends);
                    if (Math.Max(lp, lq) < XMarkMinArmMm && !boxed) continue;   // a symbol's X, and no box drawn round it
                    marks.Add(new XMark(centre, Math.Max(lp, lq), ends) { Arms = (ia, ib), Boxed = boxed });
                }
            // AN X STANDS ALONE: a cross-hatch is diagonals in two directions crossing each other at their middles by
            // the hundred (31130's L17 sheet: 100 "X marks" of 19 ft within a metre of one another), and none of them
            // is a shaft. An arm that crosses more than one partner is hatch; only a pair whose arms cross nothing else
            // this way is the mark.
            var partners = new Dictionary<int, int>();
            foreach (var m in marks) { partners[m.Arms.A] = partners.GetValueOrDefault(m.Arms.A) + 1; partners[m.Arms.B] = partners.GetValueOrDefault(m.Arms.B) + 1; }

            // A CROSSHATCH IS NOT A FIELD OF X MARKS (intake step 123, 2026-09-18). 30993's L3 hatches a region beside the
            // core with diagonals at a fifth of their length; each pair crossing at both midpoints was an X, and eight
            // 1.4 x 1.5 m "openings" stood in a stack 0.4 m apart - on nine sets, 100 of the 557 openings of ours she
            // has not stand inside another of ours. An X's arm has no companion: no other long line within 5 degrees
            // of it, closer than a quarter of its length across, running beside it for half its length or more. A hatch line
            // has one on each side. A box's sides are along the axes and never a companion to an arm 10 degrees off them.
            bool HasCompanion(int arm)
            {
                var (_, ax, ay, alen) = longLines.First(l => l.I == arm);
                var a0 = result.Lines[arm][0];
                double ux = ax / alen, uy = ay / alen;   // along the arm
                foreach (var (i, bx, by, blen) in longLines)
                {
                    if (i == arm) continue;
                    double cosine = Math.Abs(bx * ux + by * uy) / blen;
                    if (cosine < 0.9962) continue;   // more than 5 degrees off the arm's direction
                    var b0 = result.Lines[i][0]; var b1 = result.Lines[i][1];
                    double across = Math.Abs((b0.X - a0.X) * uy - (b0.Y - a0.Y) * ux);
                    if (across < 1 || across > alen / 4) continue;   // on the arm itself, or too far to be its hatch neighbour (two elevator cabs side by side, each with its X, sit a cab's width apart - near half the arm)
                    double s0 = (b0.X - a0.X) * ux + (b0.Y - a0.Y) * uy, s1 = (b1.X - a0.X) * ux + (b1.Y - a0.Y) * uy;
                    double overlap = Math.Min(Math.Max(s0, s1), alen) - Math.Max(Math.Min(s0, s1), 0);
                    if (overlap >= alen / 2) return true;
                }
                return false;
            }
            return marks.Where(m => partners[m.Arms.A] == 1 && partners[m.Arms.B] == 1 && !HasCompanion(m.Arms.A) && !HasCompanion(m.Arms.B)).ToList();
        }

        internal static void SlabEdgesFromLoops(ExtractedGeometry result, IList<PathFate>? fates, int firstFate,
            double slabEdgeBridgeMm = DefaultSlabEdgeBridgeMm, double minSlabAreaMm2 = DefaultMinSlabAreaMm2,
            double minWallThicknessMm = PdfIntakeOptions.DefaultMinWallThicknessMm, double maxWallThicknessMm = PdfIntakeOptions.DefaultMaxWallThicknessMm,
            IReadOnlyList<string>? voidWords = null, IReadOnlyList<string>? slabWords = null)
        {
            voidWords ??= PdfIntakeOptions.DefaultVoidWords; slabWords ??= PdfIntakeOptions.DefaultSlabWords;
            result.FirstEdgeSlab = result.Slabs.Count;
            if (result.Lines.Count + result.StrokesOnGrid.Count < 4) return;

            // A WALL STANDING ON THE SLAB EDGE DOES NOT REMOVE THE SLAB EDGE (intake step 28). A line
            // the drafter drew is still that line after a wall has been read from it, and a floor's
            // edge running along a wall's outer face is the normal case rather than the exception: it
            // is what the parkade plate already follows (step 22; Andrea, 25 Aug, "it should always
            // follow the outer edge of the walls").
            //
            // Until now a line was spent the moment ANY part of it paired as a face, because a face
            // is taken whole (see WallsFromFaceLines, where that is deliberate). On a tower plan each
            // side's slab edge is one long line and a balcony band is drawn a wall's thickness inside
            // it, so the two pair over a few metres and the whole edge became a "wall": on 31168's
            // L4-L14 view a 3,633 mm overlap consumed a 21,320 mm north edge and a 4,828 mm overlap
            // consumed an 18,649 mm west edge, all four sides of the floor disappearing before the
            // ring builder saw them. That is what left 53 of 62 storeys with no plate, and it is why
            // three closing heuristics in a row found nothing to close: the edge was already gone.
            //
            // SO THE TEST IS WHETHER THE WALL USED THE WHOLE LINE. A face line the wall runs the full
            // length of is the wall's and nothing else — that is the ordinary case, a wall drawn as
            // its two sides. A face line with a stretch left over is a longer line the wall stands on
            // part of, and that stretch is the plan's own. Offering EVERY face line back was measured
            // 2026-09-10 and invents floors: 31168's LEVEL 2 sheet chained slanted walls' own faces
            // into a 12,391 sq ft chevron with columns inside it, so the neighbourhood gate passed it,
            // and one look at the rendered storey showed it was not a floor. The leftover has to be
            // long enough to be a piece of an edge, which is the bridging pass's own bound
            // (SlabEdgeChainMinMm), not a new number.
            //
            // The line goes to the chain WHOLE, not as its leftover: it is one line the drafter drew,
            // and a floor's edge runs along the wall standing on it — cutting the wall's stretch out
            // would leave the ring a hole exactly where the balcony band pairs.
            // A SECTION CUT LINE IS NOT A SLAB EDGE (intake step 79, 2026-09-15). 31130's typical tower plan draws its
            // section marks as long diagonals across the floor from corner to corner, each ending in a filled
            // arrowhead; united with the outline's cells they gave the storey a slanted hexagon for a plate (step 78,
            // rendered). A line with an end at an arrowhead - within the arrowhead's own size of its centre - is a
            // section cut or a leader: the drawing's own convention says so, and it never bounds a floor.
            bool EndsAtAnArrowhead(IReadOnlyList<(double X, double Y)> line)
            {
                foreach (var (ax, ay, size) in result.Arrowheads)
                    foreach (var end in new[] { line[0], line[^1] })
                        if (Math.Abs(end.X - ax) <= size && Math.Abs(end.Y - ay) <= size) return true;
                return false;
            }
            var eligible = new List<int>();
            for (int i = 0; i < result.Lines.Count; i++)
            {
                // A CURVE IS ITS PIECES (PROTOTYPE step 98, 2026-09-16): 31138's tower outline turns its corners as arcs -
                // 33-point paths five feet long - and a pass that took two-point lines only left the ring open at every
                // rounded corner on fourteen storeys
                if (result.Lines[i].Count < 2 || result.LineIsAnnotation[i]) continue;
                if (result.WallFaceLines.TryGetValue(i, out int wall) && !WallLeftPartOfIt(i, wall)) continue;
                eligible.Add(i);
            }
            // AND THE CUT LINE DRAWN IN PIECES: a dash-dot section cut is many collinear pieces and the arrowhead ends
            // the last one only (31130's, 110 dashes under a metre, 26 of one to five, 17 longer). Every piece in line
            // with a piece that ends at an arrowhead, within the in-line reach, is the cut line - the run is dropped
            // whole, before any length gate can hide its last dash.
            var candidates = WithoutTheRunsEndingAtAnArrowhead(eligible);

            // whether the wall read from this face line leaves a piece of edge over
            bool WallLeftPartOfIt(int line, int wall)
            {
                if (wall < 0 || wall >= result.Walls.Count) return false;
                var l = result.Lines[line];
                double lineLen = Math.Sqrt(Math.Pow(l[1].X - l[0].X, 2) + Math.Pow(l[1].Y - l[0].Y, 2));
                var w = result.Walls[wall];
                double wallLen = Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
                return lineLen - wallLen >= SlabEdgeChainMinMm;
            }
            if (candidates.Count + result.StrokesOnGrid.Count(s => s.Count == 2) < 4) return;

            var segments = candidates.SelectMany(Pieces).ToList();
            // a two-point line is one piece; a path of more points is each consecutive pair
            IEnumerable<DxfSegment> Pieces(int i)
            {
                var l = result.Lines[i];
                for (int k = 1; k < l.Count; k++)
                    yield return new DxfSegment("SLABEDGE", new DxfPoint(l[k - 1].X, l[k - 1].Y), new DxfPoint(l[k].X, l[k].Y));
            }

            // A SLAB EDGE DRAWN ALONG A GRID LINE IS STILL THE SLAB EDGE (intake step 78, 2026-09-15). Step 53
            // keeps a stroke along a grid axis drawn heavier than the grid apart from the lines, for the tendon
            // reader; 31087's LEVEL 6-8 plan draws the 8.6 m of slab edge between its two blocks along grid 5
            // at 9 pt over a 2 pt grid, and the ring had a gap the width of the corridor. Those strokes are
            // the drawing's own lines and are offered to the ARRANGEMENT below (not to the walk, whose reading of the drawn
            // lines is step 24 and 27 verbatim: on the architect's set the strokes along its grids turned the walk into the
            // rooms and six storeys lost their 32,076 sq ft outline); a tendon along a grid cuts a floor into cells, and
            // the cells are united.
            var strokes = result.StrokesOnGrid.Where(s => s.Count == 2)
                .Select(s => new DxfSegment("SLABEDGE", new DxfPoint(s[0].X, s[0].Y), new DxfPoint(s[1].X, s[1].Y)))
                .ToList();

            // First the exact joins (step 24): what the drafter closed, closes here; the rest are open chains.
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
            // (Step 78 measured the same on the architect's A003: 42,501 lines arranged whole took 565 s
            // and bridged 25,504 hatch dashes in line with each other; the long chains alone are the edge.)
            var pieces = built.OpenChains
                .Where(c => c.Count >= 2 && ChainLength(c) >= SlabEdgeChainMinMm)
                .ToList();
            var pieceSegments = new List<DxfSegment>();
            foreach (var c in pieces)
                for (int i = 0; i + 1 < c.Count; i++)
                    pieceSegments.Add(new DxfSegment("SLABEDGE", c[i], c[i + 1]));

            // AN EDGE INTERRUPTED IN LINE IS ONE EDGE (intake step 78). The drafter draws the edge from column to
            // column and the column's own box over the gap: 31087's north edge is eleven pieces of 29.5 ft on
            // one line with a 36-inch gap at every 12 x 30 column, and the six-inch bridge of step 27 does not
            // reach. Two ends on one line, each running toward the other, closer than the corner-carry limit
            // (48 in, the same bound an edge is carried to its corner by) are one edge, whatever interrupted
            // them. A step in the edge is not in line and stays a step; a dash is too short to be a piece.
            // The pieces go to the arrangement, not to the walk: a bridge in line with a room wall's piece turns the walk into
            // the room (the architect's set lost its 32,076 sq ft outline to them on six storeys, measured 2026-09-15), where
            // in the arrangement an extra edge is one more cell boundary and the outline's own cells still unite.
            var inLine = BridgesInLine(pieceSegments, SlabEdgeExtendMm, SlabEdgeJoinMm);

            // ⛔ MEASURED AND REJECTED 2026-09-10: offering the small CLOSED loops to this pass too.
            // 31168's BLDG A L4-L14 view draws its tower corners as their own 5,133 x 5,186 mm
            // rectangles, one of which closes at 287 sq ft; BLDG B's sheet draws no such block and
            // closes, which is why A's L4-L14 and L15-26 carry no plate and B's do. It looked as
            // though the block was leaving OpenChains and taking the corner with it, so the small
            // loops' segments were fed back in here. It changed NOTHING on any of the five sets —
            // 31168 stayed at 37 floors, zero storeys moved — because the block re-closes on its own
            // exact joins in this pass exactly as it did in the first.
            //
            // The real shape of it, which this proved: A SEGMENT CAN ONLY BELONG TO ONE RING, and the
            // corner block SHARES TWO of its four edges with the perimeter — its top edge is a piece
            // of the north edge (both at y 15,700, meeting exactly at x -10,482) and its outer
            // vertical is a piece of the west edge; its other two edges are interior. Whichever ring
            // is built first spends them. So the fix is not about which chains this pass is given: it
            // is that a piece of linework must be allowed to serve a small loop AND the floor's edge,
            // or the outer boundary must be found by something other than chaining. Neither is a
            // tolerance, and neither should be guessed at.
            //
            // ⛔ AND MEASURED AND REJECTED THE SAME DAY: seeding PlanLoopBuilder's walk from the
            // LONGEST segment instead of in arrival order. The walk follows the straightest
            // continuation, so seeding on the floor's own long run should have carried it through the
            // shared corner and round the building. It did not close BLDG A, and it cost elsewhere:
            // 31168 37 floors -> 36, and 31065 9 floors -> 8 with columns 605 -> 601. Two attempts on
            // one symptom is where CLAUDE.md rule 10 says to stop, so the code stops here and the
            // finding is the deliverable. (Step 78 is the third way the paragraph above asked for: the
            // planar arrangement, where an edge serves both faces beside it.)
            if (pieceSegments.Count >= 2)
                loops.AddRange(new PlanLoopBuilder(SlabEdgeJoinMm, slabEdgeBridgeMm, SlabEdgeExtendMm).Build(pieceSegments).Loops);

            // A FLOOR IS THE CELLS ITS STRUCTURE STANDS IN, UNITED (intake step 78, 2026-09-15). The chain walk
            // above spends each segment on the first ring it closes, and a tower floor is drawn with its
            // balconies and corner blocks as boxes AGAINST the outline, sharing an edge with it: whichever box
            // closed first took the edge, and the floor never closed. In the planar arrangement of the same
            // lines every edge bounds the face on each side of it, so a balcony is one cell and the floor
            // beside it another, whole, and no order decides. The cells with structure standing in them are
            // the floor's cells - a slab step, a tendon or a construction joint drawn across the floor divides
            // it into cells, and they are united into one plate (PlanarRings.RecoverSurfaces); a balcony's cell
            // holds nothing and stays out, so does a dimension strip. The unions stand BESIDE the walk's rings,
            // not instead of them: where the outline closed as a chain (the architect's set, 32,076 sq ft with
            // every room drawn inside it) that ring contains the unions and outermost keeps it; the union only
            // adds what no chain could close. Gated as the walk's rings are: big enough, structure standing in
            // it, outermost. WHAT IT DOES NOT: a degenerate embedding PlanarRings will not resolve (coincident
            // rays, an overlap left unresolved) is refused, reported on the sheet, and the walk's rings stand.
            // AND ONLY WHERE THE WALK FOUND NO FLOOR: the arrangement is quadratic in the lines (the architect's
            // A003, 11,127 long pieces, 91 s), and a sheet whose outline the walk closed round its structure has
            // its floor already - the union could only add rings inside it, which outermost would drop.
            // TWO EDGES THAT MEET AT A COLUMN ARE JOINED THROUGH IT (intake step 97, 2026-09-16). The drafter draws
            // the slab edge from column to column and the column's box over the corner; the box was read as the
            // column and its lines left the set, so every edge now ends short of an empty corner (31130's west
            // tower plan p22: 17 of 58 chain ends within a foot of a column's footprint; the east p35: 2.0-2.5 ft)
            // and the ring is open at every corner column - fourteen storeys with no plate on 31130, and no
            // tendon to blame: PlanarRings bridges dangling ENDS one to one, and where a tendon's anchor arrives
            // at the same corner it takes one edge and the corner never closes. The column is the drawing's own
            // node. An edge's end that runs INTO a column - the column ahead of it within the corner-carry reach,
            // no further off its line than half the column and the bridge (an edge drawn flush with a column's
            // face passes its centre at exactly half a column) - is an edge that stops at that column, and two of
            // them meeting at one column are joined through its centre. A lone line running into a column (a
            // beam, a room wall) is left as drawn: a spur from it into the floor is a junction the drawing does
            // not have, and joined alone it closed a legend box against 31170-arch's L3 outline into a "floor".
            // ⛔ MEASURED AND REJECTED THE SAME DAY, each fixing one set and breaking another: the footprints as
            // box lines in the arrangement (an overlap PlanarRings refuses on 31202 L7-12; the floor keyholed
            // round each box and holding nothing on 31202 L6); every end within four feet of a column carried in
            // any direction (31065 L1 lost ten columns to notches, 31202 L6 its 19,670 sq ft to the neighbourhood
            // gate); a column on a cell's boundary holding no cell (31202 L6 fragmented to 13,868 + 1,868).
            var carried = new List<DxfSegment>();
            var endsAtColumn = new Dictionary<DxfPoint, List<DxfPoint>>();   // column centre -> the edge ends that run into it
            foreach (var c in pieces)
                foreach (var (end, prev) in new[] { (c[0], c[1]), (c[^1], c[^2]) })
                {
                    double dx = end.X - prev.X, dy = end.Y - prev.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    if (!(len > 0)) continue;
                    dx /= len; dy /= len;
                    double bestAlong = double.MaxValue; DxfPoint centre = default;
                    for (int k = 0; k < result.Columns.Count; k++)
                    {
                        var (cx, cy) = result.Columns[k];
                        double half = Math.Max(k < result.ColumnSizes.Count ? result.ColumnSizes[k].WidthMm : 400.0,
                                               k < result.ColumnSizes.Count ? result.ColumnSizes[k].DepthMm : 400.0) / 2;
                        // the column AHEAD of the end, on the edge's own line: within the corner-carry reach along it,
                        // and no further off the line than the column's own half-size and the bridge (an edge drawn
                        // flush with the column's face passes its centre at exactly half a column)
                        double vx = cx - end.X, vy = cy - end.Y;
                        double along = vx * dx + vy * dy, across = Math.Abs(vx * dy - vy * dx);
                        if (along < -half || along > SlabEdgeExtendMm + half || across > half + slabEdgeBridgeMm) continue;
                        if (along < bestAlong) { bestAlong = along; centre = new DxfPoint(cx, cy); }
                    }
                    if (bestAlong < double.MaxValue && end.DistanceTo(centre) > SlabEdgeJoinMm)
                        (endsAtColumn.TryGetValue(centre, out var at) ? at : endsAtColumn[centre] = new List<DxfPoint>()).Add(end);
                }
            // TWO edges meeting at a column are joined through it; a lone line running into a column (a room wall, a
            // beam) is left as drawn - a spur from it into the floor makes junctions the drawing does not have
            foreach (var (centre, ends) in endsAtColumn)
                if (ends.Count >= 2)
                    foreach (var end in ends) carried.Add(new DxfSegment("SLABEDGE", end, centre));
            // the trace says which columns took two ends and which were run into by one end alone (the edge-beside-a-column
            // class, 30838's 12/F and 31138's tower: the other edge starts at the column's far corner and runs away from it)
            FaceTrace?.Invoke($"slab pass: ends at columns (step 97; centre ft: ends): " + string.Join(" ", endsAtColumn.OrderByDescending(e => e.Value.Count)
                .Select(e => $"({e.Key.X / 304.8:0.0},{e.Key.Y / 304.8:0.0}):{e.Value.Count}{(e.Value.Count < 2 ? "-lone" : "")}")));
            FaceTrace?.Invoke($"slab pass: {carried.Count} edge end(s) joined through {endsAtColumn.Count(e => e.Value.Count >= 2)} column(s)");
            var arranged = pieceSegments.Concat(inLine).Concat(strokes).Concat(carried).ToList();
            foreach (var l in built.Loops)
                for (int i = 0; i < l.Points.Count; i++)
                    arranged.Add(new DxfSegment("SLABEDGE", l.Points[i], l.Points[(i + 1) % l.Points.Count]));
            // The drawing's own closed paths, not a mark-up's: an engineer's mark-up polygon over the whole sheet is a
            // slab on the mark-up route and says nothing about where the plan's floor is. WHAT THIS DOES NOT: a closed
            // viewport rectangle round the plan is a drawn "slab" today too, and a union inside it defers to it as it did.
            var drawn = result.Slabs.Take(result.FirstEdgeSlab)
                .Where((s, i) => s.Count >= 3 && !(i < result.SlabIsAnnotation.Count && result.SlabIsAnnotation[i])
                                 && Math.Abs(PolygonProcessor.PolygonAreaMm2(s)) >= minSlabAreaMm2)
                .Select(s => s.Select(p => new DxfPoint(p.X, p.Y)).ToList())
                .ToList();
            // AND ONLY WHERE NOTHING DREW ONE EITHER: the architect's set closes every storey's outline as a drawn path
            // (32,076 sq ft) and every room inside it; the union could only add cells inside that path.
            bool walkFoundAFloor = loops.Any(l => l.Area >= minSlabAreaMm2 && StandsIn(l))
                                   || drawn.Any(d => StandsIn(new PlanLoop("SLABEDGE", d, true)));
            var walkFloorsToReplace = new List<PlanLoop>();   // step 113: the walk's floors the arrangement's supersede when it finds one
            // A FLOOR THE WALK FOUND THAT HOLDS UNDER HALF THE PAGE'S COLUMNS IS NOT THE PAGE'S FLOOR (step 113, 2026-09-17 01:50).
            // 60061-03's typical plan (a hotel's, the outline under a dense reinforcing plan) walked a 53 x 37 ft rectangle at
            // the page's foot - the stud-rail schedule's border, three of its symbols read as columns - into a 1,953 sq ft
            // "floor" on every storey of a 10,000 sq ft floor, and the arrangement, which reads the real outline, was never
            // built ("only where the walk found no floor"). A ring that stands in structure is a floor; a ring holding
            // fewer than half the page's columns is not the whole of it, and the arrangement is built as well.
            if (walkFoundAFloor && result.Columns.Count >= 6)
            {
                var walkFloors = loops.Where(l => l.Area >= minSlabAreaMm2 && StandsIn(l)).Select(l => l.Points).Concat(drawn.Where(d => StandsIn(new PlanLoop("SLABEDGE", d, true)))).ToList();
                int held = result.Columns.Count(c => walkFloors.Any(f => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), f)));
                if (held * 2 < result.Columns.Count)
                {
                    FaceTrace?.Invoke($"slab pass: the walk's floor holds {held} of {result.Columns.Count} columns - under half: the arrangement is built as well (step 113)");
                    walkFoundAFloor = false;
                    walkFloorsToReplace = loops.Where(l => l.Area >= minSlabAreaMm2 && StandsIn(l)).ToList();
                }
            }
            // AN X ACROSS A SHAFT IS AN OPENING (intake step 104, 2026-09-16). The drafter's mark for a shaft or a stair is
            // two oblique lines of one length crossing at their midpoints - the X. 31202's elevator shafts carry 12 ft
            // ones on every plan, 31130's and 31138's 11 ft ones. The X's region is the quadrilateral of its four ends;
            // one inside a plate is written as an opening loop the DXF side cuts. The plate itself is left as the union
            // finds it: MEASURED AND REJECTED the same hour - keeping the X's cells out of the union took 31202's
            // 10,702 sq ft lower roof (its 109 ft X spans a region labelled 9" SLAB - a region mark, not a void), 31130's
            // L2 plate, and a rim off 31065 and 31168. An X longer than a stair is not an opening's mark (XMarkMaxArmMm).
            // WHAT IT DOES NOT: a box crossed by ONE diagonal (a section mark, step 79); axis-aligned crossings (a grid,
            // a tendon - both arms must be over 10 degrees off the axes); arms of unequal length (a leader over a line).
            // and never an X with structure in it: a box with an X is also how a column above or below is drawn (31168:
            // 561 "openings" on 37 storeys, one per column, before this)
            // A BIG X WITH THE WORDS OF A VOID INSIDE IT IS A VOID (step 106): 31202's L6-L13 draw a 31 x 8.7 m region open to
            // the deck below as an X of 105 ft arms with OPEN TO BELOW inside it - the shape XMarkMaxArmMm refused at 16:00
            // because the same set's ROOF draws a 109 ft X over a region labelled 9" SLAB. The words decide: a void word
            // inside the region and no slab word makes it an opening; a slab word makes it a slab whatever else it says;
            // neither, nothing. The X's of a shaft's size need no words.
            var allX = XMarks(result);
            // the words that name the region sit at the X's crossing, where the drafter puts them (31202 L6: OPEN TO BELOW at the
            // centre); a word near the region's edge names something beside it (its ROOF: OPEN BELOW ten metres off the centre,
            // a stair's, and the roof's 109 ft X is no void - her model roofs it; the first form cut 916 sq ft from it, 19:35)
            bool SaysAny(XMark x, IReadOnlyList<string> words) => result.PageWords.Any(w =>
                Math.Abs(w.X - x.Centre.X) <= 0.25 * x.Reach && Math.Abs(w.Y - x.Centre.Y) <= 0.25 * x.Reach
                && LoopGeometry.PointInPolygon(new DxfPoint(w.X, w.Y), x.Region)
                && words.Any(v => string.Equals(v, w.Text.Trim().TrimEnd(',', '.', ':'), StringComparison.OrdinalIgnoreCase)));
            var voids = allX.Where(x => x.Reach > XMarkMaxArmMm && SaysAny(x, voidWords) && !SaysAny(x, slabWords)).ToList();
            var xMarks = allX.Where(x => x.Reach <= XMarkMaxArmMm
                && !result.Columns.Any(c => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), x.Region))
                && !result.Walls.Any(w => LoopGeometry.PointInPolygon(new DxfPoint((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2), x.Region))).Concat(voids).ToList();
            FaceTrace?.Invoke($"slab pass: {voids.Count} void(s) by their words (step 106): " + string.Join(" ", voids.Select(x => $"{x.Reach / 304.8:0} ft at ({x.Centre.X / 304.8:0},{x.Centre.Y / 304.8:0}) [" +
                string.Join(" ", result.PageWords.Where(w => LoopGeometry.PointInPolygon(new DxfPoint(w.X, w.Y), x.Region) && voidWords.Concat(slabWords).Any(v => string.Equals(v, w.Text.Trim().TrimEnd(',', '.', ':'), StringComparison.OrdinalIgnoreCase)))
                    .Select(w => $"{w.Text}@({(w.X - x.Centre.X) / 304.8:0},{(w.Y - x.Centre.Y) / 304.8:0})ft")) + "]")));

            // the stair wells (step 105): the arrangement's cell holding all of a stair's flights, built here for the walk path
            // too - the walk finds the floor and builds no arrangement, and the wells are cells of it
            // THE WALLS ARE IN THE ARRANGEMENT AS THEIR OUTLINES (the wells since step 105; the floor since step 116, 2026-09-17
            // 20:50): a filled wall left no line for the slab pass - its faces are the fill's edges - so a floor bounded by its
            // walls (a parkade behind retaining walls: 31202's L2-L4 read 501 of her 33,572 sq ft; 31065's P1/P2, 31168's P1/P2
            // at 1-9% of hers) had no outline where the walls stand. The engineer's own rule: the plate runs to the
            // perimeter walls' OUTER face. A wall's outline makes a band cell that holds the wall (Holds), so the floor's
            // ring runs along the outer face.
            var wallEdges = result.Walls.Where(w => !result.WallIsAnnotation[result.Walls.IndexOf(w)]).SelectMany(w =>
                Enumerable.Range(0, w.Outline.Count).Select(k => new DxfSegment("WALL", new DxfPoint(w.Outline[k].X, w.Outline[k].Y), new DxfPoint(w.Outline[(k + 1) % w.Outline.Count].X, w.Outline[(k + 1) % w.Outline.Count].Y))))
                // AND A FACE'S LEFTOVER BEYOND ITS PANEL (intake step 124, 2026-09-18). A wall read from two face lines is
                // a panel over the faces' OVERLAP, and the reader keeps both faces out of the slab pass as the wall's. Where
                // the outer face runs on past the panel - 31202's L2-L4 rims: the 45-in wall's inner face stops 610 mm
                // short of the north rim, so the panel does, and the outer face's last 610 mm is drawn and given to no
                // one - the outside enters the plan there and every cell is "open to the page" (1,437 of her 38,105 sq ft
                // on L3). The leftover pieces of each face, beyond the panel's extent along the wall's axis, are the slab
                // pass's: lines that close what the drawing closes. NEVER THE FACE WHOLE: a face lies on the panel's own
                // outline over the overlap, and the arrangement refuses a segment on a segment (31130's P2 EAST lost its
                // 27,321 sq ft floor to that form of this rule).
                .Concat(FaceLeftovers(result));
            // a stair well has a door: the doorway knocked out of its wall (step 14) is closed again here, across the
            // opening on both faces, so the well does not leak into the corridor (31202: the cell holding the flights
            // was the whole floor, 14,000-33,000 sq ft, on nine of seventeen sheets before this)
            var doorEdges = result.Doorways.SelectMany(d =>
            {
                double dx = d.End.X - d.Start.X, dy = d.End.Y - d.Start.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) return Enumerable.Empty<DxfSegment>();
                double nx = -dy / len * d.ThicknessMm / 2, ny = dx / len * d.ThicknessMm / 2;
                return new[]
                {
                    new DxfSegment("DOOR", new DxfPoint(d.Start.X + nx, d.Start.Y + ny), new DxfPoint(d.End.X + nx, d.End.Y + ny)),
                    new DxfSegment("DOOR", new DxfPoint(d.Start.X - nx, d.Start.Y - ny), new DxfPoint(d.End.X - nx, d.End.Y - ny)),
                };
            });
            // A DOOR DRAWN AS A GAP BETWEEN TWO WALL PIECES (31065: the stair's walls stop either side of the door, no
            // paper fill over a wall for step 14 to read): two walls on one line, a door's width apart (500-1,300 mm),
            // are closed across the gap on both faces for the well's arrangement - the cell holding 31065's flights
            // was the whole plate, 6,700 sq ft, before this (19:05)
            var gapEdges = new List<DxfSegment>();
            var wallsForGaps = result.Walls.Where((w, i) => !result.WallIsAnnotation[i]).ToList();
            for (int a = 0; a < wallsForGaps.Count; a++)
                for (int b = a + 1; b < wallsForGaps.Count; b++)
                {
                    var wa = wallsForGaps[a]; var wb = wallsForGaps[b];
                    double ax = wa.End.X - wa.Start.X, ay = wa.End.Y - wa.Start.Y, la = Math.Sqrt(ax * ax + ay * ay);
                    if (la < 1) continue;
                    double ux = ax / la, uy = ay / la;
                    // parallel and on one line: b's ends lie within half a thickness of a's line
                    double Off((double X, double Y) p) => Math.Abs((p.X - wa.Start.X) * -uy + (p.Y - wa.Start.Y) * ux);
                    if (Off(wb.Start) > Math.Max(wa.ThicknessMm, wb.ThicknessMm) / 2 || Off(wb.End) > Math.Max(wa.ThicknessMm, wb.ThicknessMm) / 2) continue;
                    double Along((double X, double Y) p) => (p.X - wa.Start.X) * ux + (p.Y - wa.Start.Y) * uy;
                    double a0 = 0, a1 = la, b0 = Math.Min(Along(wb.Start), Along(wb.End)), b1 = Math.Max(Along(wb.Start), Along(wb.End));
                    double gap = Math.Max(b0 - a1, a0 - b1);
                    if (gap < 500 || gap > 1300) continue;
                    double from = b0 > a1 ? a1 : b1, to = b0 > a1 ? b0 : a0;
                    double t = Math.Min(wa.ThicknessMm, wb.ThicknessMm) / 2;
                    var p0 = new DxfPoint(wa.Start.X + ux * from, wa.Start.Y + uy * from); var p1 = new DxfPoint(wa.Start.X + ux * to, wa.Start.Y + uy * to);
                    gapEdges.Add(new DxfSegment("DOOR", new DxfPoint(p0.X - uy * t, p0.Y + ux * t), new DxfPoint(p1.X - uy * t, p1.Y + ux * t)));
                    gapEdges.Add(new DxfSegment("DOOR", new DxfPoint(p0.X + uy * t, p0.Y - ux * t), new DxfPoint(p1.X + uy * t, p1.Y - ux * t)));
                }
            var stairWells = new List<PlanLoop>();
            if (result.StairFlights.Count > 0 && arranged.Count >= 3)
            {
                try
                {
                    // a stair is its flights within 1.5 m of one another
                    var stairs = new List<List<(double X0, double Y0, double X1, double Y1, int Treads)>>();
                    foreach (var f in result.StairFlights)
                    {
                        var near = stairs.FirstOrDefault(s => s.Any(g => f.X0 <= g.X1 + 1500 && g.X0 <= f.X1 + 1500 && f.Y0 <= g.Y1 + 1500 && g.Y0 <= f.Y1 + 1500));
                        if (near is null) stairs.Add(new List<(double, double, double, double, int)> { f }); else near.Add(f);
                    }
                    // the stair's own symbol - the flights' outlines, the break line, the arrow - lies inside the flights' box and
                    // would cut the well into slivers; the well is bounded by what stands OUTSIDE the box: its walls
                    // the stair's own symbol - the break line, the arrow, anything strictly inside the flights' box by 150 mm -
                    // and the treads themselves where they are lines (31138: twelve strokes across the flight, ending on its
                    // edges, each of which would cut the well into a sliver) are left out of the well's arrangement; the well's
                    // own outline runs along and across the flights' edges (31202's ends where the fills end) and stays -
                    // "any line across the flight inside its box" took eight of 31202's sixteen wells (measured 17:55-18:00)
                    bool InAStair(DxfPoint p) => stairs.Any(s => p.X > s.Min(f => f.X0) + 150 && p.X < s.Max(f => f.X1) - 150 && p.Y > s.Min(f => f.Y0) + 150 && p.Y < s.Max(f => f.Y1) - 150);
                    bool ATreadOf(DxfSegment seg) => (InAStair(seg.Start) && InAStair(seg.End))
                        || result.TreadLines.Any(t => (Near(seg.Start, t.A) && Near(seg.End, t.B)) || (Near(seg.Start, t.B) && Near(seg.End, t.A)));
                    static bool Near(DxfPoint p, (double X, double Y) q) => Math.Abs(p.X - q.X) <= 5 && Math.Abs(p.Y - q.Y) <= 5;
                    var planarForWells = new PlanarRings(SlabEdgeJoinMm, slabEdgeBridgeMm, SlabEdgeExtendMm).Build(arranged.Concat(wallEdges).Concat(doorEdges).Concat(gapEdges).Where(s => !ATreadOf(s)));
                    foreach (var stair in stairs)
                    {
                        // the cells the flights' centres stand in, joined: two flights of one stair sit either side of a line
                        // (the stringer between them, a break line), in two cells that make one well
                        var centres = stair.Select(f => new DxfPoint((f.X0 + f.X1) / 2, (f.Y0 + f.Y1) / 2)).ToList();
                        var cells = new HashSet<int>(Enumerable.Range(0, planarForWells.Faces.Count).Where(i => centres.Any(p => LoopGeometry.PointInPolygon(p, planarForWells.Faces[i].Outer.Points))));
                        if (cells.Count == 0) continue;
                        double boxArea = (stair.Max(f => f.X1) - stair.Min(f => f.X0)) * (stair.Max(f => f.Y1) - stair.Min(f => f.Y0));
                        // THE LANDING BETWEEN THE FLIGHTS IS THE WELL'S (step 105d, 2026-09-16 22:35): a break line drawn across a
                        // stair as a zig-zag of short strokes closes once step 110 keeps its pieces, and the cell between two
                        // flights - the landing, no flight's centre in it - fell out of the well (31065's L7-L17 wells 2.4 x 7.1 m
                        // -> 2.4 x 3.3). A cell no bigger than the stair's box whose centroid lies inside that box is the well's too.
                        double sx0 = stair.Min(f => f.X0), sx1 = stair.Max(f => f.X1), sy0 = stair.Min(f => f.Y0), sy1 = stair.Max(f => f.Y1);
                        for (int i = 0; i < planarForWells.Faces.Count; i++)
                        {
                            if (cells.Contains(i)) continue;
                            var outer = planarForWells.Faces[i].Outer;
                            if (Math.Abs(outer.Area) > boxArea) continue;
                            var c = outer.Centroid();
                            if (c.X > sx0 && c.X < sx1 && c.Y > sy0 && c.Y < sy1) cells.Add(i);
                        }
                        var rings = planarForWells.RecoverSurfaces(_ => false, (i, _) => cells.Contains(i)).Slabs.Select(s => s.Outer).ToList();
                        FaceTrace?.Invoke($"slab pass: stair at ({centres.Average(p => p.X) / 304.8:0},{centres.Average(p => p.Y) / 304.8:0}) ft: {stair.Count} flight(s), box {boxArea / 92903.04:0} sq ft, {cells.Count} of {planarForWells.Faces.Count} cell(s) hold a flight (arranged {arranged.Count} + wall edges {wallEdges.Count()} + door edges {doorEdges.Count()}), ring(s) sq ft: {string.Join(" ", rings.Select(r => $"{Math.Abs(r.Area) / 92903.04:0}"))}");
                        foreach (var ring in rings)
                        {
                            double wellArea = Math.Abs(ring.Area);
                            if (wellArea > 4 * boxArea || wellArea > 60 * 1e6) continue;   // an open stair in a lobby: the lobby is not the well
                            stairWells.Add(ring);
                        }
                    }
                }
                catch (InvalidOperationException) { /* the arrangement refused: no wells this sheet, the trace says so through the slab pass */ }
            }
            FaceTrace?.Invoke($"slab pass: {stairWells.Count} stair well(s) (step 105; sq ft at centre ft): " +
                              string.Join(" ", stairWells.Select(w => $"{Math.Abs(w.Area) / 92903.04:0} ({w.Points.Average(p => p.X) / 304.8:0},{w.Points.Average(p => p.Y) / 304.8:0})")));

            double NearestColumnFootprintFt(DxfPoint p) => result.Columns.Count == 0 ? -1
                : result.Columns.Select((c, k) => (c, half: (k < result.ColumnSizes.Count ? Math.Max(result.ColumnSizes[k].WidthMm, result.ColumnSizes[k].DepthMm) : 400.0) / 2))
                    .Min(t => Math.Max(Math.Abs(t.c.X - p.X) - t.half, Math.Abs(t.c.Y - p.Y) - t.half)) / 304.8;
            // each X with the nearest column footprint and the nearest wall to its region's edge, in ft: a shaft is
            // bounded by walls; 31168's edge boxes flank a column
            FaceTrace?.Invoke($"slab pass: {xMarks.Count} X mark(s) (an opening's mark, step 104; reach ft at centre ft [column ft, wall ft]): " +
                      string.Join(" ", xMarks.OrderByDescending(x => x.Reach).Take(20).Select(x =>
                          $"{x.Reach / 304.8:0} ({x.Centre.X / 304.8:0},{x.Centre.Y / 304.8:0})[{x.Region.Min(NearestColumnFootprintFt):0.0},{(result.Walls.Count == 0 ? -1 : x.Region.Min(p => result.Walls.Min(w => LoopGeometry.DistanceToSegment(p, new DxfPoint(w.Start.X, w.Start.Y), new DxfPoint(w.End.X, w.End.Y)))) / 304.8):0.0}]")));
            FaceTrace?.Invoke($"slab pass: {result.StairFlights.Count} stair flight(s) (step 105; treads, extent ft, centre ft [column ft, wall ft]): " +
                              string.Join(" ", result.StairFlights.Select(f =>
                              {
                                  var c = new DxfPoint((f.X0 + f.X1) / 2, (f.Y0 + f.Y1) / 2);
                                  return $"{f.Treads} {(f.X1 - f.X0) / 304.8:0.0}x{(f.Y1 - f.Y0) / 304.8:0.0} ({c.X / 304.8:0},{c.Y / 304.8:0})[{NearestColumnFootprintFt(c):0.0},{(result.Walls.Count == 0 ? -1 : result.Walls.Min(w => LoopGeometry.DistanceToSegment(c, new DxfPoint(w.Start.X, w.Start.Y), new DxfPoint(w.End.X, w.End.Y))) / 304.8):0.0}]";
                              })));
            if (!walkFoundAFloor && arranged.Count >= 3)
            {
                try
                {
                    // A MATCH LINE CLOSES THE OUTLINE OF A PART PLAN (intake step 117, 2026-09-17 23:25). A plan too wide for one
                    // sheet is cut on a match line and drawn as a north and a south half; the floor's outline on each half is open
                    // where the seam runs, so no cell closed there and the half read no plate - 70061-01's P1 north (its west and
                    // north are walls, its east and south the match lines; her 59,027 sq ft, ours 5,578), 31065's P1 and P2 the
                    // same. The match line is the drafter's own statement that the floor continues: it bounds the half's cells,
                    // and the composer joins the halves across the seam (MatchLineSheetJoin) as it has since the DXF route.
                    var matchEdges = result.MatchLines.Select(m => new DxfSegment("MATCH", new DxfPoint(m.Start.X, m.Start.Y), new DxfPoint(m.End.X, m.End.Y))).ToList();
                    var planar = new PlanarRings(SlabEdgeJoinMm, slabEdgeBridgeMm, SlabEdgeExtendMm).Build(arranged.Concat(wallEdges).Concat(doorEdges).Concat(gapEdges).Concat(matchEdges));
                    if (FaceTrace is not null)
                    {
                        // WHAT THE ARRANGEMENT HOLDS ROUND A COLUMN ONE EDGE ALONE RUNS INTO (the edge-beside-a-column class,
                        // 23:40): its mesh points within 1.5 m of each such column, with their degree, offset in inches from
                        // the column's centre - so the next rule is written on what the arrangement sees, not on a picture
                        if (planar.Topology is { } mesh)
                            foreach (var (centre, ends) in endsAtColumn.Where(e => e.Value.Count == 1).Take(4))
                            {
                                var near = Enumerable.Range(0, mesh.Points.Count)
                                    .Where(i => mesh.Points[i].DistanceTo(centre) <= 1500)
                                    .OrderBy(i => mesh.Points[i].DistanceTo(centre))
                                    .Select(i => $"({(mesh.Points[i].X - centre.X) / 25.4:0},{(mesh.Points[i].Y - centre.Y) / 25.4:0})d{mesh.Adjacency[i].Count}");
                                FaceTrace($"slab pass: lone column at ({centre.X / 304.8:0.0},{centre.Y / 304.8:0.0}) ft: arrangement points within 1.5 m (offset in, degree): {string.Join(" ", near)}");
                            }
                        // AND THE ENDS LEFT DANGLING A HAND'S WIDTH FROM ANOTHER EDGE'S MIDDLE (23:45): the arrangement bridges an
                        // end to an end, and joins an end ON a span within the join tolerance; an end short of a span by up to
                        // the bridge (a T drawn short) is neither, and closes on the raster (150 mm) but not here
                        if (planar.Topology is { } m2)
                        {
                            var tees = new List<string>();
                            for (int i = 0; i < m2.Points.Count && tees.Count < 12; i++)
                            {
                                if (m2.Adjacency[i].Count != 1) continue;
                                var p = m2.Points[i]; int own = m2.Adjacency[i][0];
                                for (int e = 0; e < m2.Edges.Count; e++)
                                {
                                    if (e == own) continue;
                                    var a = m2.Points[m2.Edges[e].A]; var b = m2.Points[m2.Edges[e].B];
                                    double ex = b.X - a.X, ey = b.Y - a.Y, len2 = ex * ex + ey * ey;
                                    if (len2 <= 0) continue;
                                    double t = ((p.X - a.X) * ex + (p.Y - a.Y) * ey) / len2;
                                    if (t <= 0.02 || t >= 0.98) continue;
                                    double fx = a.X + t * ex - p.X, fy = a.Y + t * ey - p.Y, d = Math.Sqrt(fx * fx + fy * fy);
                                    if (d > SlabEdgeJoinMm && d <= slabEdgeBridgeMm) { tees.Add($"({p.X / 304.8:0.0},{p.Y / 304.8:0.0})ft {d / 25.4:0.0}in"); break; }
                                }
                            }
                            FaceTrace($"slab pass: {tees.Count}{(tees.Count >= 12 ? "+" : "")} end(s) short of another edge's middle by under the bridge (a T drawn short): {string.Join(" ", tees)}");
                        }
                        var cells = planar.Faces.OrderByDescending(f => Math.Abs(f.Outer.Area)).ToList();
                        int inACell = result.Columns.Count(c => cells.Any(f => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), f.Outer.Points)));
                        FaceTrace($"slab pass: arrangement {cells.Count} cell(s); columns in a cell {inACell} of {result.Columns.Count}; " +
                                  $"largest (sq ft, holds): {string.Join(" ", cells.Take(10).Select(f => $"{Math.Abs(f.Outer.Area) / 92903.04:0}{(Holds(f) ? "*" : "")}"))}");
                        var open = planar.OpenChains.Where(c => c.Count >= 2).OrderByDescending(ChainLength).ToList();
                        FaceTrace($"slab pass: {planar.OpenChains.Count} open chain(s) in the arrangement; longest (ft, ends in ft): " +
                                  string.Join(" | ", open.Select(c => $"{ChainLength(c) / 304.8:0} ({c[0].X / 304.8:0.0},{c[0].Y / 304.8:0.0})[{NearestColumnFt(c[0]):0.0}]-({c[^1].X / 304.8:0.0},{c[^1].Y / 304.8:0.0})[{NearestColumnFt(c[^1]):0.0}]")));
                        double NearestColumnFt(DxfPoint p) => result.Columns.Count == 0 ? -1
                            : result.Columns.Select((c, k) => (c, half: (k < result.ColumnSizes.Count ? Math.Max(result.ColumnSizes[k].WidthMm, result.ColumnSizes[k].DepthMm) : 400.0) / 2))
                                .Min(t => Math.Max(Math.Abs(t.c.X - p.X) - t.half, Math.Abs(t.c.Y - p.Y) - t.half)) / 304.8;
                        // THE DANGLING ENDS' NEIGHBOURS (instrument, 2026-09-19): for the longest open chains, each end's nearest
                        // arranged segment - the distance to its BODY and to its nearer END, and the segment itself. An end on a
                        // body at no distance is a contact the arrangement did not split; an end a hand from an end is a gap the
                        // bridge did not take; an end far from everything is the drawing's own opening. 31009's L4-L8 read no
                        // floor with every edge present in the DXF, and the raster finder named a pinch, not the entry.
                        {
                            var all = arranged.Concat(wallEdges).Concat(doorEdges).Concat(gapEdges).Concat(matchEdges).ToList();
                            string Neighbours(DxfPoint p, IReadOnlyList<DxfPoint> own)
                            {
                                double bestBody = double.MaxValue, bestEnd = double.MaxValue; DxfSegment? atBody = null, atEnd = null;
                                foreach (var s in all)
                                {
                                    // not the chain's own pieces
                                    if (own.Any(q => (Math.Abs(q.X - s.Start.X) < 0.5 && Math.Abs(q.Y - s.Start.Y) < 0.5)) && own.Any(q => Math.Abs(q.X - s.End.X) < 0.5 && Math.Abs(q.Y - s.End.Y) < 0.5)) continue;
                                    double ex = s.End.X - s.Start.X, ey = s.End.Y - s.Start.Y, len2 = ex * ex + ey * ey;
                                    double de = Math.Min(Math.Sqrt(Math.Pow(p.X - s.Start.X, 2) + Math.Pow(p.Y - s.Start.Y, 2)), Math.Sqrt(Math.Pow(p.X - s.End.X, 2) + Math.Pow(p.Y - s.End.Y, 2)));
                                    if (de < bestEnd) { bestEnd = de; atEnd = s; }
                                    if (len2 <= 0) continue;
                                    double t = ((p.X - s.Start.X) * ex + (p.Y - s.Start.Y) * ey) / len2;
                                    if (t <= 0 || t >= 1) continue;
                                    double fx = s.Start.X + t * ex - p.X, fy = s.Start.Y + t * ey - p.Y, db = Math.Sqrt(fx * fx + fy * fy);
                                    if (db < bestBody) { bestBody = db; atBody = s; }
                                }
                                string Seg(DxfSegment? s) => s is null ? "-" : $"{s.Layer} ({s.Start.X / 304.8:0.0},{s.Start.Y / 304.8:0.0})-({s.End.X / 304.8:0.0},{s.End.Y / 304.8:0.0})";
                                return $"body {(bestBody == double.MaxValue ? "-" : (bestBody / 25.4).ToString("0.0"))}in on {Seg(atBody)}; end {(bestEnd == double.MaxValue ? "-" : (bestEnd / 25.4).ToString("0.0"))}in of {Seg(atEnd)}";
                            }
                            foreach (var c in open.Take(3))
                                FaceTrace($"slab pass: chain {ChainLength(c) / 304.8:0} ft: start ({c[0].X / 304.8:0.0},{c[0].Y / 304.8:0.0}) {Neighbours(c[0], c)} | end ({c[^1].X / 304.8:0.0},{c[^1].Y / 304.8:0.0}) {Neighbours(c[^1], c)}");
                            // and everything arranged within a metre of the longest chain's two ends, so the opening is seen, not inferred
                            if (open.Count > 0)
                                foreach (var p in new[] { open[0][0], open[0][^1] })
                                {
                                    var near = all.Where(s => Math.Min(Math.Min(Math.Sqrt(Math.Pow(p.X - s.Start.X, 2) + Math.Pow(p.Y - s.Start.Y, 2)), Math.Sqrt(Math.Pow(p.X - s.End.X, 2) + Math.Pow(p.Y - s.End.Y, 2))), BodyDistance(p, s)) <= 1000)
                                        .Select(s => $"{s.Layer} ({s.Start.X:0},{s.Start.Y:0})-({s.End.X:0},{s.End.Y:0})").ToList();
                                    FaceTrace($"slab pass: arranged within 1 m of ({p.X:0},{p.Y:0}) mm: {near.Count}: {string.Join(" | ", near.Take(14))}");
                                }
                            static double BodyDistance(DxfPoint p, DxfSegment s)
                            {
                                double ex = s.End.X - s.Start.X, ey = s.End.Y - s.Start.Y, len2 = ex * ex + ey * ey;
                                if (len2 <= 0) return double.MaxValue;
                                double t = Math.Clamp(((p.X - s.Start.X) * ex + (p.Y - s.Start.Y) * ey) / len2, 0, 1);
                                return Math.Sqrt(Math.Pow(s.Start.X + t * ex - p.X, 2) + Math.Pow(s.Start.Y + t * ey - p.Y, 2));
                            }
                        }
                        // WHERE THE OUTSIDE GETS IN (instrument, 2026-09-16): a column in no cell stands in the unbounded face, so
                        // a path crosses no line from it to the page's edge. On a 150 mm raster of the arranged lines, the widest
                        // such path's narrowest place is the gap the ring leaks through; a ring open without a dangling end
                        // (both sides of the gap are junctions) shows nowhere else.
                        var big = cells.Where(f => Math.Abs(f.Outer.Area) >= 92903.04 * 500).ToList();
                        FaceTrace($"slab pass: {big.Count} cell(s) of 500 sq ft or more: " + string.Join(" | ", big.Select(f =>
                            $"{Math.Abs(f.Outer.Area) / 92903.04:0} sq ft x {f.Outer.Points.Min(p => p.X) / 304.8:0}..{f.Outer.Points.Max(p => p.X) / 304.8:0} y {f.Outer.Points.Min(p => p.Y) / 304.8:0}..{f.Outer.Points.Max(p => p.Y) / 304.8:0} ft, {f.Holes.Count} hole(s)")));
                        var outsideColumns = result.Columns.Where(c => !cells.Any(f => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), f.Outer.Points))).Take(3).ToList();
                        foreach (var col in outsideColumns)
                        {
                            // on EVERYTHING the arrangement had (2026-09-19): rasterising the slab edges alone found "gaps" between the
                            // outline and the walls that close it (31009 L4: an 8-in pinch beside a 12-in wall) - a pinch on a route the
                            // walls never allowed. The finder's raster is the arrangement's own edge set.
                            var everything = arranged.Concat(wallEdges).Concat(doorEdges).Concat(gapEdges).Concat(matchEdges).ToList();
                            var leak = Leak(everything, new DxfPoint(col.X, col.Y));
                            if (leak is { } lk)
                            {
                                // and what is arranged around the pinch, so the gap is seen: the two sides of it, or the one side and nothing
                                var around = everything.Where(s => Math.Min(Math.Min(Math.Sqrt(Math.Pow(lk.At.X - s.Start.X, 2) + Math.Pow(lk.At.Y - s.Start.Y, 2)), Math.Sqrt(Math.Pow(lk.At.X - s.End.X, 2) + Math.Pow(lk.At.Y - s.End.Y, 2))), BodyDistanceMm(lk.At, s)) <= 600)
                                    .Select(s => $"{s.Layer} ({s.Start.X:0},{s.Start.Y:0})-({s.End.X:0},{s.End.Y:0})").ToList();
                                FaceTrace($"slab pass: arranged within 600 mm of the pinch ({lk.At.X:0},{lk.At.Y:0}) mm: {around.Count}: {string.Join(" | ", around.Take(12))}");
                                if (LastBreak is { } br)
                                {
                                    var atBreak = everything.Where(s => Math.Min(Math.Min(Math.Sqrt(Math.Pow(br.X - s.Start.X, 2) + Math.Pow(br.Y - s.Start.Y, 2)), Math.Sqrt(Math.Pow(br.X - s.End.X, 2) + Math.Pow(br.Y - s.End.Y, 2))), BodyDistanceMm(br, s)) <= 600)
                                        .Select(s => $"{s.Layer} ({s.Start.X:0},{s.Start.Y:0})-({s.End.X:0},{s.End.Y:0})").ToList();
                                    FaceTrace($"slab pass: the route's break - its pinch nearest a line's end - at ({br.X / 304.8:0.0},{br.Y / 304.8:0.0}) ft = ({br.X:0},{br.Y:0}) mm; arranged within 600 mm: {atBreak.Count}: {string.Join(" | ", atBreak.Take(12))}");
                                }
                            }
                            static double BodyDistanceMm(DxfPoint p, DxfSegment s)
                            {
                                double ex = s.End.X - s.Start.X, ey = s.End.Y - s.Start.Y, len2 = ex * ex + ey * ey;
                                if (len2 <= 0) return double.MaxValue;
                                double t = Math.Clamp(((p.X - s.Start.X) * ex + (p.Y - s.Start.Y) * ey) / len2, 0, 1);
                                return Math.Sqrt(Math.Pow(s.Start.X + t * ex - p.X, 2) + Math.Pow(s.Start.Y + t * ey - p.Y, 2));
                            }
                            FaceTrace($"slab pass: column at ({col.X / 304.8:0.0},{col.Y / 304.8:0.0}) ft is in no cell; " +
                                      (leak is null ? "no path to the page's edge found on the raster" : $"the outside reaches it through a gap {leak.Value.Width / 25.4:0} in wide at ({leak.Value.At.X / 304.8:0.0},{leak.Value.At.Y / 304.8:0.0}) ft"));
                        }
                    }
                    // A CELL ENCLOSED BY THE FLOOR IS THE FLOOR (intake step 98, 2026-09-16). Step 78 took the cells that hold
                    // structure and united them; every line drawn across a floor - a tendon, a slab step, a curb, a curved
                    // wall's two faces once curves read as lines - makes cells that hold nothing, and each one breaks the
                    // union where it lies: 31202 L13 was three unions and 542 sq ft (section 110), L6 split at a curved
                    // wall's band, 31138's L1 fell to pieces. Where the floor's cell holds nothing it is still the floor if
                    // it is not open to the page: a cell that shares no edge with the unbounded outside is enclosed by the
                    // linework on every side, and enclosed by the floor is the floor. A balcony box, a dimension strip and
                    // a courtyard's open side all touch the outside and stay out as they did. WHAT IT DOES NOT: an opening
                    // drawn as a closed loop inside the floor (a stair, a shaft) is enclosed too and is now filled - the
                    // reading of a loop the plan labels an opening is owed, and named in the plan.
                    // AND A CELL WRAPPED ROUND THE FLOOR IS THE FLOOR (intake step 100, 2026-09-16): a lower roof drawn round a
                    // penthouse is a ring whose hole holds the penthouse's columns and whose own band holds nothing - three of
                    // 31202's five roof pieces went when Holds stopped looking through the hole, and 31130's east tower lost
                    // its L3-L13 half-plates to a cell whose columns all sat in its holes. A hole that holds structure makes
                    // its ring the floor round it; of two parallel edge lines the outer is the edge, where her plate runs.
                    bool WrapsTheFloor(PlanarRings.Face cell) => cell.Holes.Any(h => result.Columns.Any(c => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), h.Points))
                                                                                   || result.Walls.Any(w => LoopGeometry.PointInPolygon(new DxfPoint((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2), h.Points)));
                    // ⛔ MEASURED AND REJECTED (step 104, 16:10-16:30): "an X's four triangles are one region" - a triangle of an
                    // X touching the outside made floor when the X's other three are, so that an X-box on the plate's edge
                    // (31168's L15-26, whole by the walk) would not notch an arrangement-built plate. Two of six sets broke
                    // the same way: on a leaking plate the rule filled the open side of a stair or elevator core and lifted
                    // the core's box over the 400 sq ft floor gate - 31065's south tower L7-L17 gained a 408 sq ft "floor",
                    // 31202's L2-L4 a 443 sq ft one. An edge X-box in the arrangement path keeps its notch (no real set
                    // shows one yet; the fixture in AnXAcrossARegionIsAnOpeningTests takes the walk path, as 31168 does).
                    // A RIM CELL FACING THE PAGE THROUGH THE OUTLINE'S OWN PEN IS INSIDE THE OUTLINE (intake step 115, 2026-09-17).
                    // The rim rule above - a cell touching the outside is not the floor unless it holds structure - was written
                    // for the balcony box, the dimension strip and the courtyard's open side, all OUTSIDE the outline. A line
                    // drawn across the floor that reaches the slab edge closes a cell at the rim too, inside the outline, and
                    // that cell held nothing and was dropped: 31065's typical tower plan lost a 1.5 x 2.3 m box under column
                    // C10 against its west edge (her model has slab there) the moment step 112's carry made the box's lines
                    // reach the edge; 31130's L1 lost 3,257 sq ft the same way; and every interior line drawn exactly to the
                    // edge has done this all along (31065 L7: ours 6,690 sq ft, hers 7,766). The drawing itself says which side
                    // of the outline a rim cell is on: the outline is drawn with ONE pen, and a cell inside it faces the page
                    // through the outline's strokes, while a balcony box or a dimension strip faces the page through its own.
                    // The outline's pen is the pen the structure-holding cells OF A FLOOR'S SIZE face the page with (the mode over
                    // their outward edges - a core box holding a column is not the outline, and on a page whose outline never
                    // closed it would have named its own pen and stood as a 671 sq ft plate, 31065's south tower L7); a rim cell
                    // whose every outward edge lies on a line of that pen is inside the outline, and floor.
                    var pieces115 = candidates.Select(i => (Line: result.Lines[i], Width: i < result.LineWidths.Count ? result.LineWidths[i] : 0.0)).ToList();
                    double? PenAlong(DxfPoint a, DxfPoint b)
                    {
                        double minX = Math.Min(a.X, b.X) - SlabEdgeJoinMm, maxX = Math.Max(a.X, b.X) + SlabEdgeJoinMm;
                        double minY = Math.Min(a.Y, b.Y) - SlabEdgeJoinMm, maxY = Math.Max(a.Y, b.Y) + SlabEdgeJoinMm;
                        foreach (var (line, width) in pieces115)
                            for (int k = 1; k < line.Count; k++)
                            {
                                var p = new DxfPoint(line[k - 1].X, line[k - 1].Y); var q = new DxfPoint(line[k].X, line[k].Y);
                                if (Math.Max(p.X, q.X) < minX || Math.Min(p.X, q.X) > maxX || Math.Max(p.Y, q.Y) < minY || Math.Min(p.Y, q.Y) > maxY) continue;
                                if (LoopGeometry.DistanceToSegment(a, p, q) <= SlabEdgeJoinMm && LoopGeometry.DistanceToSegment(b, p, q) <= SlabEdgeJoinMm) return width;
                            }
                        return null;
                    }
                    var outlinePens = new List<double>();
                    for (int i = 0; i < planar.Faces.Count; i++)
                        if (Holds(planar.Faces[i]) && Math.Abs(planar.Faces[i].Outer.Area) >= minSlabAreaMm2)
                            foreach (var (a, b) in planar.OutwardEdges(i))
                                if (PenAlong(a, b) is double w) outlinePens.Add(Math.Round(w, 2));
                    double? outlinePen = outlinePens.Count == 0 ? null : outlinePens.GroupBy(w => w).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
                    bool FacesThePageThroughTheOutlinePen(int i)
                    {
                        if (outlinePen is null) return false;
                        var outward = planar.OutwardEdges(i);
                        if (outward.Count == 0) return false;
                        foreach (var (a, b) in outward)
                            if (PenAlong(a, b) is not double w || Math.Abs(Math.Round(w, 2) - outlinePen.Value) > 0.01) return false;
                        return true;
                    }
                    // AND BESIDE THE FLOOR: a rim cell is inside the outline only where it is reached from a floor-sized
                    // structure-holding cell across shared edges, through cells that are floor themselves (enclosed ones, or
                    // rim cells facing the page through the outline's pen). A core box on a view whose outline never closed
                    // (31065's south tower L7, drawn on the same page as L6 whose floor names the pen) faces the page through
                    // wall lines of that pen and stood as a 671 sq ft plate; it touches no floor and now stands out.
                    var insideTheOutline = new HashSet<int>();
                    if (outlinePen is not null)
                    {
                        var queue = new Queue<int>();
                        var reached = new HashSet<int>();
                        for (int i = 0; i < planar.Faces.Count; i++)
                            if (Holds(planar.Faces[i]) && Math.Abs(planar.Faces[i].Outer.Area) >= minSlabAreaMm2) { reached.Add(i); queue.Enqueue(i); }
                        while (queue.Count > 0)
                        {
                            int at = queue.Dequeue();
                            foreach (int n in planar.Neighbours(at))
                            {
                                if (reached.Contains(n)) continue;
                                bool floorAlready = Holds(planar.Faces[n]) || WrapsTheFloor(planar.Faces[n]) || !planar.TouchesTheOutside(n);
                                bool byPen = !floorAlready && FacesThePageThroughTheOutlinePen(n);
                                if (!floorAlready && !byPen) continue;
                                reached.Add(n); queue.Enqueue(n);
                                if (byPen) insideTheOutline.Add(n);
                            }
                        }
                    }
                    bool InsideTheOutline(int i) => insideTheOutline.Contains(i);
                    if (outlinePen is not null)
                    {
                        int insideCount = insideTheOutline.Count;
                        FaceTrace?.Invoke($"slab pass: the outline's pen is {outlinePen.Value:0.00} mm ({outlinePens.Count} outward edge(s) of the structure-holding cells); {insideCount} rim cell(s) face the page through it alone and are inside the outline (step 115)");
                    }
                    if (FaceTrace is not null)
                    {
                        int nHolds = 0, nEnclosed = 0, nOutside = 0, nWrap = 0; double aHolds = 0, aEnclosed = 0, aOutside = 0;
                        for (int i = 0; i < planar.Faces.Count; i++)
                        {
                            var c = planar.Faces[i]; double a = Math.Abs(c.Outer.Area) / 92903.04;
                            if (Holds(c)) { nHolds++; aHolds += a; }
                            else if (WrapsTheFloor(c)) { nWrap++; FaceTrace($"slab pass: wrapping cell {a:0} sq ft with {c.Holes.Count} hole(s) of {c.Holes.Sum(h => Math.Abs(h.Area)) / 92903.04:0} sq ft (step 116 study)"); }
                            else if (!planar.TouchesTheOutside(i)) { nEnclosed++; aEnclosed += a; }
                            else { nOutside++; aOutside += a; }
                        }
                        var biggest = planar.Faces.Select((f, i) => (i, a: Math.Abs(f.Outer.Area) / 92903.04)).OrderByDescending(x => x.a).Take(3).ToList();
                        FaceTrace("slab pass: biggest cells (step 116 study): " + string.Join(" | ", biggest.Select(x => $"{x.a:0} sq ft holds {Holds(planar.Faces[x.i])} wraps {WrapsTheFloor(planar.Faces[x.i])} outside {planar.TouchesTheOutside(x.i)} holes {planar.Faces[x.i].Holes.Count}")));
                        FaceTrace($"slab pass: cells by selection (step 116 study): holding structure {nHolds} ({aHolds:0} sq ft), wrapping {nWrap}, enclosed {nEnclosed} ({aEnclosed:0} sq ft), open to the page {nOutside} ({aOutside:0} sq ft)");
                    }
                    var fromArrangement = planar.RecoverSurfaces(_ => false, (i, cell) => Holds(cell) || WrapsTheFloor(cell) || !planar.TouchesTheOutside(i) || InsideTheOutline(i)).Slabs.Select(f => f.Outer).ToList();
                    // step 113: the arrangement was built although the walk had a floor, because that floor held under half the
                    // page's columns; where the arrangement finds a floor holding more, the walk's stands down (31202's L1 carried
                    // the same slab twice, 34,590 and 34,145 sq ft, when both stayed)
                    if (walkFloorsToReplace.Count > 0 && fromArrangement.Count > 0)
                    {
                        int Held(PlanLoop l) => result.Columns.Count(c => LoopGeometry.PointInPolygon(new DxfPoint(c.X, c.Y), l.Points));
                        int walkHeld = walkFloorsToReplace.Sum(Held), arrangementHeld = fromArrangement.Sum(Held);
                        if (arrangementHeld > walkHeld)
                        {
                            foreach (var w in walkFloorsToReplace) loops.Remove(w);
                            FaceTrace?.Invoke($"slab pass: the arrangement's floor(s) hold {arrangementHeld} columns to the walk's {walkHeld}: the walk's {walkFloorsToReplace.Count} stand down (step 113)");
                        }
                    }
                    loops.AddRange(fromArrangement);
                }
                catch (InvalidOperationException refused)
                {
                    result.SlabEdgeArrangementRefused = refused.Message;
                }
            }

            // big enough to be a floor, and with something standing in it - and not a shaft's own box: a ring that lies
            // wholly in an X's region is the opening the X marks, never a floor of its own (step 104: 31065's south
            // tower on a leaking plate, the two elevator shafts' box at 408 sq ft was the storey's only "floor")
            var floors = loops
                .Where(l => l.Area >= minSlabAreaMm2 && StandsIn(l))
                .Where(l => xMarks.Count == 0 || !l.Points.All(p => xMarks.Any(x => LoopGeometry.InsideOrOn(p, x.Region, SlabEdgeJoinMm))))   // two shafts side by side are one ring over two X's
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
            FaceTrace?.Invoke($"slab pass: {candidates.Count} of {eligible.Count} lines offered; walk {built.Loops.Count} loop(s), {pieces.Count} piece(s); " +
                              $"walk found a floor: {walkFoundAFloor}; arranged {arranged.Count}; refused: {result.SlabEdgeArrangementRefused ?? "-"}; " +
                              $"rings {loops.Count} (sq ft: {string.Join(",", loops.OrderByDescending(l => l.Area).Take(6).Select(l => (l.Area / 92903.04).ToString("0")))}); " +
                              $"floors (big enough, structure stands in) {floors.Count}; the three largest rings: " +
                              string.Join(" | ", loops.OrderByDescending(l => l.Area).Take(3).Select(l => $"{l.Area / 92903.04:0} sq ft {Neighbourhood(l)}, {l.Points.Count(p => xMarks.Any(x => LoopGeometry.InsideOrOn(p, x.Region, SlabEdgeJoinMm)))} of {l.Points.Count} vertices in an X, extent x {l.Points.Min(p => p.X) / 304.8:0.0}..{l.Points.Max(p => p.X) / 304.8:0.0} y {l.Points.Min(p => p.Y) / 304.8:0.0}..{l.Points.Max(p => p.Y) / 304.8:0.0} ft")));
            if (floors.Count == 0) return;

            // and outermost: a core's ring inside a floor is a hole in it, not a second floor. INSIDE A DRAWN FLOOR
            // TOO (step 78): the architect's set closes its outline as one drawn path (31170-arch, 32,076 sq ft) and
            // draws every room inside it; the rooms' cells united gave three more "floors" on L6 and six on L1, each
            // inside the drawn one. A cell union standing inside a floor-sized closed path is that floor's interior,
            // not a second floor. Judged at the centroid: a union shares its boundary with its neighbours, so a
            // vertex is on the line and answers with rounding noise.
            var outermost = new List<PlanLoop>();
            foreach (var l in floors)
            {
                // inside = more than half of its vertices inside: a centroid lies outside an L- or U-shaped floor
                // (31170's two wings round a court), and a vertex on a shared boundary answers with rounding noise
                if (drawn.Any(d => MostlyInside(l.Points, d))) continue;
                if (!outermost.Any(o => MostlyInside(l.Points, o.Points)))
                    outermost.Add(l);
            }

            foreach (var loop in outermost)
            {
                int slabIndex = result.Slabs.Count;
                result.Slabs.Add(loop.Points.Select(p => (p.X, p.Y)).ToList());
                result.SlabColors.Add(((byte)0, (byte)0, (byte)0));
                result.SlabIsAnnotation.Add(false);
                // the X regions inside this plate, written as loops of their own: the DXF side reads a ring inside a floor
                // as an opening (StructuralPlanClassifier), and the composer cuts it from the plate (step 104)
                foreach (var x in xMarks)
                {
                    if (!x.Region.All(p => LoopGeometry.InsideOrOn(p, loop.Points, SlabEdgeJoinMm))) continue;
                    double regionArea = Math.Abs(PolygonProcessor.PolygonAreaMm2(x.Region.Select(p => (p.X, p.Y)).ToList()));
                    if (regionArea >= 0.5 * loop.Area) continue;   // more than half the floor is not a hole in it
                    // ⛔ MEASURED AND REJECTED, TWICE, BY HER OWN MODELS (step 104, 16:00-16:55). 31168's L15-26 plans draw a
                    // 3.6 x 1.5 m (and 1.5 x 4.8 m) X-box either side of every perimeter column, 0-700 mm in from the slab
                    // edge - 72 of them, 561 "openings" on the towers, on storeys no model of hers covers. Two rules to refuse
                    // them, each judged by ModelYardstick.Openings on the five sets with her export (her storeys only):
                    //   none            ours judged 142, hers among them 111 (78%); 31168: 84 judged, 70 hers, 60 of her 64 ours
                    //   edge clear 300  ours judged  87, hers among them  60 (69%); 31168: 33 judged, 20 hers, 10 of her 64 ours
                    //   column clear    ours judged  ~90, ...              31168: 33 judged, 20 hers, 10 of her 64
                    // Her shafts on 31168's podium and building C stand within 300 mm of OUR plate's boundary (our podium
                    // plates are pieces) and have columns in their corner walls: both rules took fifty of hers to remove the
                    // boxes. The corpus judges; the boxes stay until a model of hers covers L15-26 or an engineer says what
                    // they are (QUESTIONS.md). What the boxes are, the sheet does not say; their arms sit on the drafter's
                    // layer JBP_G_EXISTING, not the slab layer.
                    result.Slabs.Add(x.Region.Select(p => (p.X, p.Y)).ToList());
                    result.SlabColors.Add(((byte)0, (byte)0, (byte)0));
                    result.SlabIsAnnotation.Add(false);
                }
                // A STAIR WELL IS THE CELL ITS FLIGHTS STAND IN (step 105): the drafter draws a stair as its treads (paper-filled
                // rectangles, eight to a flight on 31202) between the stair's walls, and no X; her model cuts the well - two
                // flights and the landing, 2 x 5.5 m, 44 of them on four sets that the X rule could not see. The well is the
                // arrangement's cell holding the flights' centres, when one cell holds them all and is no more than four times
                // the flights' own box (an open stair in a lobby would take the lobby).
                foreach (var well in stairWells)
                {
                    if (!well.Points.All(p => LoopGeometry.InsideOrOn(p, loop.Points, SlabEdgeJoinMm))) continue;
                    if (well.Area >= 0.5 * loop.Area) continue;
                    result.Slabs.Add(well.Points.Select(p => (p.X, p.Y)).ToList());
                    result.SlabColors.Add(((byte)0, (byte)0, (byte)0));
                    result.SlabIsAnnotation.Add(false);
                }

                // the lines that lie on it are its edge, not beams
                foreach (int i in candidates)
                {
                    if (result.SlabEdgeLines.ContainsKey(i)) continue;
                    if (!result.Lines[i].All(p => OnRing(new DxfPoint(p.X, p.Y), loop))) continue;
                    result.SlabEdgeLines[i] = slabIndex;
                }
            }

            if (fates is not null)
            {
                var lineToPaths = new Dictionary<int, List<int>>();
                for (int k = firstFate; k < fates.Count; k++)
                    if (fates[k].Reason == PathReason.EmittedAsLine && fates[k].ObjectIndex is int li)
                        (lineToPaths.TryGetValue(li, out var ps) ? ps : lineToPaths[li] = new List<int>()).Add(fates[k].PathIndex);
                foreach (var (line, slab) in result.SlabEdgeLines)
                    if (lineToPaths.TryGetValue(line, out var pathIndices))
                        for (int k = firstFate; k < fates.Count; k++)
                            if (pathIndices.Contains(fates[k].PathIndex))
                                fates[k] = new PathFate(fates[k].PathIndex, Disposition.Read, PathReason.BecameSlabEdge, slab);
            }

            // A FLOOR IS WHAT THE STRUCTURE AROUND IT STANDS ON. Something stands in the ring, and
            // of the columns and walls within a tenth of the ring's size of it, most stand in it:
            // a 28.6 m x 6.3 m strip on 31168's BLDG C plan closed on its own with a column in it
            // and would have been the storey's plate at 1,922 sq ft where the floor is 14,988 —
            // the columns beside it, outside it, say it is a strip of the floor and not the floor.
            // a cell is the floor's where anything stands in it, whatever its size (step 78): the halves a slab step
            // divides a floor into are each under the floor bound and each hold their columns
            bool Holds(PlanarRings.Face cell)
            {
                // strictly inside: cells meeting only at a column's centre (a carried edge's junction, step 97) are not
                // one floor, and uniting them pinches the union into a boundary PlanarRings refuses (measured 2026-09-16).
                // AND NOT IN A HOLE OF IT (step 98): a band round the floor - the outline drawn twice - is a cell whose
                // outer ring is the whole floor and whose hole is the floor; judged by its outer ring it "held" every
                // column, and the band was the plate
                bool In(DxfPoint p) => LoopGeometry.PointInPolygon(p, cell.Outer.Points) && !cell.Holes.Any(h => LoopGeometry.PointInPolygon(p, h.Points));
                foreach (var c in result.Columns)
                    if (In(new DxfPoint(c.X, c.Y))) return true;
                foreach (var w in result.Walls)
                    if (In(new DxfPoint((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2))) return true;
                return false;
            }

            bool StandsIn(PlanLoop loop) => Neighbourhood(loop).Stands;
            (bool Stands, int Inside, int Near) Neighbourhood(PlanLoop loop)
            {
                double x0 = loop.Points.Min(p => p.X), x1 = loop.Points.Max(p => p.X);
                double y0 = loop.Points.Min(p => p.Y), y1 = loop.Points.Max(p => p.Y);
                double reach = Math.Max(x1 - x0, y1 - y0) * SlabEdgeNeighbourhoodShare;
                int inside = 0, near = 0;
                // NEAR IS NEAR THE RING'S EDGE, NOT INSIDE ITS BOX (step 116, 2026-09-17 23:05): a union ring is seldom a
                // rectangle, and its bounding box took in the roofs beside it - 31202's ROOF, 9,677 sq ft holding 22 columns,
                // was refused with 49 "near", 27 of them under the upper roof and the penthouse a box-width away. What
                // stands within the reach of the ring's own edge is its neighbourhood.
                double NearestEdge(DxfPoint p)
                {
                    double best = double.MaxValue;
                    for (int i = 0; i < loop.Points.Count; i++)
                        best = Math.Min(best, LoopGeometry.DistanceToSegment(p, loop.Points[i], loop.Points[(i + 1) % loop.Points.Count]));
                    return best;
                }
                void Count(double x, double y)
                {
                    if (x < x0 - reach || x > x1 + reach || y < y0 - reach || y > y1 + reach) return;
                    var p = new DxfPoint(x, y);
                    // on the ring is in it (step 82): a column the edge runs through stands on this floor
                    bool inRing = LoopGeometry.InsideOrOn(p, loop.Points, SlabEdgeJoinMm);
                    if (!inRing && NearestEdge(p) > reach) return;
                    near++;
                    if (inRing) inside++;
                }
                foreach (var c in result.Columns) Count(c.X, c.Y);
                foreach (var w in result.Walls) Count((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2);
                return (inside > 0 && inside * 2 > near, inside, near);
            }


            // the lines in line with one another within the in-line reach form runs (the same pairing the edge is bridged
            // by); a run with a line ending at an arrowhead is a section cut or a leader, every piece of it
            List<int> WithoutTheRunsEndingAtAnArrowhead(List<int> lines)
            {
                if (result.Arrowheads.Count == 0 || lines.Count == 0) return lines;
                var segs = lines.Select(i => new DxfSegment("LINE", new DxfPoint(result.Lines[i][0].X, result.Lines[i][0].Y), new DxfPoint(result.Lines[i][^1].X, result.Lines[i][^1].Y))).ToList();
                var parent = Enumerable.Range(0, segs.Count).ToArray();
                int Root(int n) { while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; } return n; }
                var byEnd = new Dictionary<DxfPoint, int>();
                for (int i = 0; i < segs.Count; i++)
                    foreach (var end in new[] { segs[i].Start, segs[i].End })
                    {
                        if (byEnd.TryGetValue(end, out int other)) parent[Root(i)] = Root(other);
                        else byEnd[end] = i;
                    }
                foreach (var bridge in BridgesInLine(segs, SlabEdgeExtendMm, SlabEdgeJoinMm))
                    if (byEnd.TryGetValue(bridge.Start, out int a) && byEnd.TryGetValue(bridge.End, out int b)) parent[Root(a)] = Root(b);
                var cut = new HashSet<int>();
                for (int i = 0; i < segs.Count; i++)
                    if (EndsAtAnArrowhead(result.Lines[lines[i]])) cut.Add(Root(i));
                if (FaceTrace is not null && cut.Count > 0)
                {
                    var size = new Dictionary<int, int>();
                    for (int i = 0; i < segs.Count; i++) size[Root(i)] = size.GetValueOrDefault(Root(i)) + 1;
                    FaceTrace($"slab pass: {cut.Count} run(s) end at an arrowhead and take {Enumerable.Range(0, segs.Count).Count(i => cut.Contains(Root(i)))} of {lines.Count} lines with them; " +
                              $"run sizes {string.Join(",", cut.Select(r => size[r]).OrderByDescending(n => n).Take(8))}");
                }
                return cut.Count == 0 ? lines : lines.Where((_, i) => !cut.Contains(Root(i))).ToList();
            }

            // A VERTEX ON THE POLYGON'S EDGE IS INSIDE (step 82, 2026-09-15): a union along a drawn floor's south edge has
            // half its 60 vertices ON that edge, and PointInPolygon answers for each with rounding noise - the shifted
            // differential (step 56's) found 31170-arch's L5 and L2 each gaining a plate inside the drawn floor in one
            // frame and not the other. A place is decided by distance: a vertex within the join tolerance of an edge
            // is on it, and on it is in it.
            static bool MostlyInside(IReadOnlyList<DxfPoint> ring, IReadOnlyList<DxfPoint> polygon)
                => ring.Count > 0 && ring.Count(p => LoopGeometry.InsideOrOn(p, polygon, SlabEdgeJoinMm)) * 2 > ring.Count;

            static bool OnRing(DxfPoint p, PlanLoop loop)
            {
                for (int i = 0; i < loop.Points.Count; i++)
                    if (LoopGeometry.DistanceToSegment(p, loop.Points[i], loop.Points[(i + 1) % loop.Points.Count]) <= SlabEdgeJoinMm) return true;
                return false;
            }

            // the widest path from the point to the raster's border, and its narrowest place: a bottleneck (widest-path) search
            static (double Width, DxfPoint At)? Leak(IReadOnlyList<DxfSegment> lines, DxfPoint from)
            {
                if (lines.Count == 0) return null;
                const double LeakRasterMm = 60;   // the leak finder's raster: a gap two cells wide is found (an instrument, behind FaceTrace)
                double cell = LeakRasterMm;
                double x0 = lines.Min(l => Math.Min(l.Start.X, l.End.X)) - 2 * cell, x1 = lines.Max(l => Math.Max(l.Start.X, l.End.X)) + 2 * cell;
                double y0 = lines.Min(l => Math.Min(l.Start.Y, l.End.Y)) - 2 * cell, y1 = lines.Max(l => Math.Max(l.Start.Y, l.End.Y)) + 2 * cell;
                int nx = (int)Math.Ceiling((x1 - x0) / cell) + 1, ny = (int)Math.Ceiling((y1 - y0) / cell) + 1;
                if ((long)nx * ny > 12_000_000) return null;
                // distance from each raster cell's centre to the nearest line, capped: the passage width at that cell
                var dist = new double[nx * ny];
                Array.Fill(dist, 4 * cell);
                foreach (var l in lines)
                {
                    int ax = (int)((Math.Min(l.Start.X, l.End.X) - x0) / cell) - 4, bx = (int)((Math.Max(l.Start.X, l.End.X) - x0) / cell) + 4;
                    int ay = (int)((Math.Min(l.Start.Y, l.End.Y) - y0) / cell) - 4, by = (int)((Math.Max(l.Start.Y, l.End.Y) - y0) / cell) + 4;
                    for (int iy = Math.Max(0, ay); iy <= Math.Min(ny - 1, by); iy++)
                        for (int ix = Math.Max(0, ax); ix <= Math.Min(nx - 1, bx); ix++)
                        {
                            double d = LoopGeometry.DistanceToSegment(new DxfPoint(x0 + ix * cell, y0 + iy * cell), l.Start, l.End);
                            if (d < dist[iy * nx + ix]) dist[iy * nx + ix] = d;
                        }
                }
                int sx = (int)Math.Round((from.X - x0) / cell), sy = (int)Math.Round((from.Y - y0) / cell);
                if (sx < 0 || sy < 0 || sx >= nx || sy >= ny) return null;
                // widest path: a max-heap on the bottleneck width reached so far
                var best = new double[nx * ny];
                var via = new int[nx * ny];
                var queue = new PriorityQueue<int, double>();
                int start = sy * nx + sx;
                best[start] = Math.Max(dist[start], cell); via[start] = -1;
                queue.Enqueue(start, -best[start]);
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cx = cur % nx, cy = cur / nx;
                    if (cx == 0 || cy == 0 || cx == nx - 1 || cy == ny - 1)
                    {
                        // walk back to the narrowest place on the path - and THE BREAK: the pinch nearest a line's END, since a
                        // ring leaks where a line stops, not where two lines run close (2026-09-19: 31009's channel between its
                        // east edge and a wall face was the narrowest place on every route and no gap at all)
                        double narrow = double.MaxValue; int at = cur;
                        var path = new List<int>();
                        for (int k = cur; k >= 0; k = via[k]) { path.Add(k); if (dist[k] < narrow) { narrow = dist[k]; at = k; } }
                        int atBreak = -1; double breakNear = double.MaxValue;
                        foreach (int k in path)
                        {
                            if (dist[k] > 5 * cell) continue;                                   // wide open: not beside anything
                            var p = new DxfPoint(x0 + (k % nx) * cell, y0 + (k / nx) * cell);
                            foreach (var l in lines)
                                foreach (var e in new[] { l.Start, l.End })
                                {
                                    double d = p.DistanceTo(e);
                                    if (d <= 4 * cell && d < breakNear) { breakNear = d; atBreak = k; }
                                }
                        }
                        LastBreak = atBreak < 0 ? null : new DxfPoint(x0 + (atBreak % nx) * cell, y0 + (atBreak / nx) * cell);
                        return (2 * narrow, new DxfPoint(x0 + (at % nx) * cell, y0 + (at / nx) * cell));
                    }
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nxi = cx + dx, nyi = cy + dy;
                        if (nxi < 0 || nyi < 0 || nxi >= nx || nyi >= ny) continue;
                        int n = nyi * nx + nxi;
                        double w = Math.Min(best[cur], dist[n]);
                        if (dist[n] <= cell / 2 || w <= best[n]) continue;   // through a line, or no wider than the way already found
                        best[n] = w; via[n] = cur;
                        queue.Enqueue(n, -w);
                    }
                }
                return null;
            }

            static double ChainLength(IReadOnlyList<DxfPoint> c)
            {
                double len = 0;
                for (int i = 0; i + 1 < c.Count; i++) len += c[i].DistanceTo(c[i + 1]);
                return len;
            }
        }

        /// <summary>
        /// THE PIECES THAT CARRY AN EDGE ACROSS AN INTERRUPTION IN LINE (intake step 78): for every two ends of the
        /// given lines closer than <paramref name="maxGap"/>, on one line (each within <paramref name="collinear"/>
        /// of the other's line) and each running toward the other, the piece between them. Nearest pairs first,
        /// each end used once. WHAT THIS COVERS: an edge broken at a column's box, at a leader, at a note or at a
        /// dimension's text. WHAT IT DOES NOT: a step in the edge (not in line), a gap wider than the limit, two
        /// pieces running the same way (one behind the other, not toward each other).
        /// </summary>
        public static List<DxfSegment> BridgesInLine(IReadOnlyList<DxfSegment> lines, double maxGap, double collinear)
        {
            ArgumentNullException.ThrowIfNull(lines);
            var ends = new List<(int Line, DxfPoint P, DxfPoint Other)>();
            foreach (var (s, i) in lines.Select((s, i) => (s, i)))
            {
                if (s.Start == s.End) continue;
                ends.Add((i, s.Start, s.End));
                ends.Add((i, s.End, s.Start));
            }
            // pairs by x so that only ends within the gap of each other are compared
            var order = Enumerable.Range(0, ends.Count).OrderBy(k => ends[k].P.X).ToList();
            var pairs = new List<(double Gap, int A, int B)>();
            for (int oa = 0; oa < order.Count; oa++)
            {
                int a = order[oa];
                var (la, pa, qa) = ends[a];
                for (int ob = oa + 1; ob < order.Count; ob++)
                {
                    int b = order[ob];
                    var (lb, pb, qb) = ends[b];
                    if (pb.X - pa.X > maxGap) break;
                    if (la == lb || Math.Abs(pb.Y - pa.Y) > maxGap) continue;
                    double gap = pa.DistanceTo(pb);
                    if (gap > maxGap || gap <= 0) continue;
                    if (LoopGeometry.PerpendicularDistance(pb, qa, pa) > collinear || LoopGeometry.PerpendicularDistance(pa, qb, pb) > collinear) continue;
                    // each line runs toward the other's end: the ends face each other across the gap
                    if ((pa.X - qa.X) * (pb.X - pa.X) + (pa.Y - qa.Y) * (pb.Y - pa.Y) <= 0) continue;
                    if ((pb.X - qb.X) * (pa.X - pb.X) + (pb.Y - qb.Y) * (pa.Y - pb.Y) <= 0) continue;
                    pairs.Add((gap, a, b));
                }
            }
            var used = new HashSet<int>();
            var pieces = new List<DxfSegment>();
            foreach (var (_, a, b) in pairs.OrderBy(p => p.Gap))
            {
                if (used.Contains(a) || used.Contains(b)) continue;
                used.Add(a);
                used.Add(b);
                pieces.Add(new DxfSegment(lines[ends[a].Line].Layer, ends[a].P, ends[b].P));
            }
            return pieces;
        }

        /// <summary>Endpoints this close are one corner of a ring, in millimetres: the intake reads a
        /// PDF's own coordinates, which meet exactly where the drafter closed a polyline.</summary>
        public const double SlabEdgeJoinMm = 1.0;

        /// <summary>Two chain ends this close are one edge broken by what crossed it: a hand's width, the DXF side's own bridge (6 in). The compiled default of dxf.bridge-tolerance (PdfIntakeOptions.SlabEdgeBridgeMm).</summary>
        public const double DefaultSlabEdgeBridgeMm = 6 * 25.4;

        /// <summary>An edge stopping this short of the corner the other edge's line makes is carried to it: the DXF side's own limit (48 in).</summary>
        public const double SlabEdgeExtendMm = 48 * 25.4;

        /// <summary>A chain shorter than this is a tick, a dash or a letter, not a piece of a floor's edge, and is not bridged.</summary>
        public const double SlabEdgeChainMinMm = 2000;

        /// <summary>The structure a ring is judged against stands within this share of the ring's own size of it.</summary>
        private const double SlabEdgeNeighbourhoodShare = 0.10;

        /// <summary>
        /// Smaller than this and a closed ring is a stair, a shaft or a box of notes, not a floor.
        /// 400 sq ft, the DXF side's own <c>MinPlateArea</c>, in millimetres.
        /// </summary>
        public const double DefaultMinSlabAreaMm2 = 400 * 144 * 25.4 * 25.4;   // the compiled default of dxf.min-plate-area (PdfIntakeOptions.MinSlabAreaMm2)

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
