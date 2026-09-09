using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Compares the disabled wall rule to the frozen step-14 classifier at b73665a3, then enables it
/// and verifies that only specified rectangles change fate. Compares ordered slab/line vertices,
/// column centroids/sizes, colours, annotation flags, drop panels, qualifying wall outlines,
/// ribbon count, metadata and path fates. Inputs are independently cloned.
/// Does not validate wall axes/thicknesses (the separate shape tests do), real PDFs, DXF bytes,
/// overlay pixels or ETABS placements. A parsing error or
/// erroneous ink flag shared by both runs would pass; the synthetic shape and export gates are
/// separate, and the verifier banks the corpus. The frozen implementation must not track edits
/// to the production classifier.
/// </summary>
public sealed class TheWallRuleChangesNothingElseTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisabledRuleMatchesThePreWallClassifierExactly(bool markupOnly, bool excludeGrid)
    {
        var (paths, _, _) = Fixture();
        var old = new ExtractedGeometry();
        var oldFates = new List<PathFate>();
        PreWallClassifier.Classify(Clone(paths), old, 1000, 200, excludeGrid, 100000, 70000,
            markupOnly, furniture: Furniture(), fates: oldFates);
        var (disabled, disabledFates) = WallFixture.Read(Clone(paths),
            WallFixture.Disabled, Furniture(), markupOnly, excludeGrid);
        TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(old, disabled);
        Assert.Equal(oldFates.ToArray(), disabledFates.ToArray());
    }

    [Fact]
    public void EnabledRuleOnlyReclassifiesTheQualifyingRectangles()
    {
        var (paths, walls, ribbonIndex) = Fixture();
        var options = PdfIntakeOptions.Default;
        var (before, beforeFates) = WallFixture.Read(Clone(paths),
            options with { MinWallLengthMm = double.PositiveInfinity }, Furniture());
        var (after, afterFates) = WallFixture.Read(Clone(paths), options, Furniture());
        Assert.Equal(walls.Count, after.Walls.Count);
        Assert.Equal(1, after.WallRibbonsNotSplit);
        Assert.Equal(beforeFates[ribbonIndex].Reason, afterFates[ribbonIndex].Reason);
        Assert.Equal(Enumerable.Range(0, paths.Count), afterFates.Select(f => f.PathIndex));
        Assert.Equal(walls.OrderBy(i => i), afterFates.Where(f => f.Reason == PathReason.BecameWall).Select(f => f.PathIndex));
        for (int i = 0; i < paths.Count; i++)
        {
            if (walls.Contains(i))
            {
                var wall = after.Walls[afterFates[i].ObjectIndex!.Value];
                Assert.Equal(paths[i].Points.ToArray(), wall.Outline.ToArray());
                Assert.Equal(paths[i].Color, after.WallColors[afterFates[i].ObjectIndex!.Value]);
                Assert.False(after.WallIsAnnotation[afterFates[i].ObjectIndex!.Value]);
            }
            else
            {
                // Object indices may compact when a former slab is removed; its reason cannot change.
                Assert.Equal(beforeFates[i].Reason, afterFates[i].Reason);
                Assert.Equal(beforeFates[i].Disposition, afterFates[i].Disposition);
            }
        }

        // Remove exactly the newly recognised paths from the old input, and classify what remains
        // with the old rule. This compares every surviving object's full geometry, not just counts.
        var survivingPaths = paths.Where((_, i) => !walls.Contains(i)).ToList();
        var expected = new ExtractedGeometry();
        PreWallClassifier.Classify(Clone(survivingPaths), expected, 1000, 200, false, 100000, 70000,
            false, furniture: Furniture());
        // Wall geometry was checked above; the remainder must match the old output verbatim.
        after.Walls.Clear();
        after.WallColors.Clear();
        after.WallIsAnnotation.Clear();
        after.WallRibbonsNotSplit = 0;
        TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(expected, after);
        Assert.True(before.Slabs.Count > after.Slabs.Count);
    }

    private static (List<RawSubpath> Paths, HashSet<int> Walls, int RibbonIndex) Fixture()
    {
        // Without the doorway case (step 14): this differential is about the wall RULE against the
        // pre-wall classifier, and it asserts each wall keeps the path's own outline, which a wall
        // split into piers does not. AWallIsThePiersBesideItsDoorwaysTests covers the doorway.
        // the other wall-shaping steps (14: doorways and clips; 20: face lines) have differentials of their own
        var cases = FateFixture.Cases().Where(c => c.Reason is not (PathReason.Doorway or PathReason.ClipOfWall or PathReason.BecameWallFace or PathReason.Band)).ToList();
        var paths = cases.Select(c => c.Path).ToList();
        var walls = cases.Select((c, i) => (c, i)).Where(x => x.c.Reason == PathReason.BecameWall).Select(x => x.i).ToHashSet();
        foreach (var rectangle in new[] { WallFixture.Rect(12, 240), WallFixture.Rect(12, 50), WallFixture.Rect(4, 48) })
        {
            walls.Add(paths.Count);
            paths.Add(rectangle);
        }
        int ribbonIndex = paths.Count;
        paths.Add(WallFixture.Ribbon());
        paths.Add(FateFixture.Line(0, 0, 70000, 10000)); // the optional grid exclusion
        paths.Add(FateFixture.Rect(1600, 300) with { IsAnnotation = true }); // a drop panel
        paths.Add(WallFixture.Rect(12, 240) with { IsAnnotation = true });
        return (paths, walls, ribbonIndex);
    }

    private static SheetFurniture.Set Furniture() => new(
        [new("schedule: COLUMN SCHEDULE", 20000, 20000, 22000, 22000)],
        [30000], [30000], 1.5, [(1800, 400)], 1)
    {
        Underlines = [new(1000, 3000, 2000)],
    };

    private static List<RawSubpath> Clone(IEnumerable<RawSubpath> paths)
        => paths.Select(p => p with { Points = p.Points.ToList() }).ToList();
}

// Frozen from b73665a35316fca09bb3f11c99811caf52a5c368, before the wall rule.
// Only the class name/accessibility and namespace differ. Never use this for production reads.
internal static class PreWallClassifier
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
        IList<PathFate>? fates = null)
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
