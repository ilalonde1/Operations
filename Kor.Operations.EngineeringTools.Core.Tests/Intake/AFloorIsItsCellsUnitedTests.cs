#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A FLOOR IS THE CELLS ITS STRUCTURE STANDS IN, UNITED (intake step 78, 2026-09-15), and the two readings that
/// feed it: a slab edge drawn along a grid line is still the slab edge, and an edge interrupted in line is one
/// edge. 31087's tower plans (LEVEL 4 to 53 of a 53-storey tower) read no ring: the corridor's edge ran along
/// grid 5 at 9 pt and was kept for the tendon reader, the north edge was eleven pieces with a 36-inch gap at
/// every column, and the balconies were boxes against the outline whose shared edge the chain walk spent first.
/// WHAT THIS COVERS: the heavy stroke along the axis closing the ring where the grid-pen stroke does not; the
/// in-line gap under the corner-carry limit closing, one over it staying open, a jog staying open; a floor cut
/// by a line across it coming out as one plate; a balcony box sharing the outline's edge, drawn first, leaving
/// the floor whole and staying out of it. WHAT IT DOES NOT: the real sheets (the six-set gate and the corpus
/// plates instrument); a refused arrangement, which falls back to the walk and is reported on the sheet.
/// A same-class fault it would not catch: a dimension strip whose cell holds a column and joins the floor.
/// </summary>
public sealed class AFloorIsItsCellsUnitedTests
{
    private const double W = 8000, H = 6000;      // 48 sq m: over the 400 sq ft a floor must have

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static RawSubpath Heavy(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1) with { LineWidth = 2.0 };
    private static RawSubpath Thin(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1) with { LineWidth = 0.25 };

    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());

    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    [Fact]
    public void AHeavyStrokeAlongAGridLineIsTheEdgeAndAGridPenStrokeIsTheGrid()
    {
        // the fixture's vertical axis is at x 30000; the ring's west edge runs along it
        RawSubpath[] rest = [Line(30000, 20000, 38000, 20000), Line(38000, 20000, 38000, 26000), Line(38000, 26000, 30000, 26000), Column(33000, 22000), Column(36000, 24000)];
        var heavy = Read([Heavy(30000, 26000, 30000, 20000), .. rest]);
        Assert.Equal(W * H, Area(Assert.Single(heavy.Slabs)), 1);

        var gridPen = Read([Line(30000, 26000, 30000, 20000), .. rest]);
        Assert.Empty(gridPen.Slabs);
    }

    /// <summary>
    /// A STROKE ON THE GRID IS A PIECE OF THE EDGE (intake step 131, 2026-09-19): the heavy stroke along the axis is
    /// bridged in line across a column's box and carried through a column like any piece of the drawn lines. 30990's
    /// tower draws its rim ON its grid lines, the west edge in four pieces with a column's box between each, and the
    /// floor was open at every column on a grid line. WHAT THIS COVERS: the axis edge in three pieces with a 900 mm
    /// gap at each column closing at the floor's full area. WHAT IT DOES NOT: a gap over the corner-carry limit (the
    /// same bound as the drawn lines', AnEdgeInterruptedInLineIsOneEdgeUpToTheCornerCarryLimit).
    /// </summary>
    [Fact]
    public void AHeavyStrokeAlongAGridLineInterruptedAtItsColumnsIsOneEdge()
    {
        // the columns on the axis stand centred on it, as 30990 draws them (Column(x, y) puts a box's corner at x, y)
        RawSubpath[] rest = [Line(30000, 20000, 38000, 20000), Line(38000, 20000, 38000, 26000), Line(38000, 26000, 30000, 26000),
                             Column(33000, 22000), Column(36000, 24000), FateFixture.Rect(600, 800, 29700, 21600), FateFixture.Rect(600, 800, 29700, 23600)];
        var g = Read([Heavy(30000, 26000, 30000, 24450), Heavy(30000, 23550, 30000, 22450), Heavy(30000, 21550, 30000, 20000), .. rest]);
        Assert.Equal(W * H, Area(Assert.Single(g.Slabs)), 1);

        // a run of dashes along the axis (31202's LEVEL 13: 1,380 of 4 mm at 1.5x the grid's pen, crossing at every
        // intersection) stays as drawn under StrokeOnGridPieceMinMm - the first cut bridged them dash to dash into an
        // arrangement the embedding refused and the storey's 28,200 sq ft outline was lost. NOT WITNESSED HERE: 300 dashes
        // on two crossing axes of this fixture arrange fine either way; the witness is 31202's page in its set's context
        // (the trace probe, KOR_STEP131_OFF, 2026-09-20 02:35), and the six-set gate.
    }

    /// <summary>
    /// A FLOOR'S WORTH OF COLUMNS OUTSIDE THE WALK'S FLOOR IS ANOTHER FLOOR (intake step 135, 2026-09-20): a page of two
    /// views draws two floors; the walk closes the one drawn exactly and the other, closed only by the arrangement's
    /// bridges, was unread whenever the first held half the page's columns (31065's LEVEL 6 / LEVEL 7 NT page, six
    /// storeys). WHAT THIS COVERS: two outlines side by side with four columns each, the right one interrupted at a
    /// column - two floors; the same page with two columns on the right - one floor (a canopy's or a stair's, not a
    /// floor's worth). WHAT IT DOES NOT: the real page (the six-set gate).
    /// </summary>
    [Fact]
    public void AFloorsWorthOfColumnsOutsideTheWalksFloorIsAnotherFloor()
    {
        RawSubpath[] left = [Line(20000, 20000, 28000, 20000), Line(28000, 20000, 28000, 26000), Line(28000, 26000, 20000, 26000), Line(20000, 26000, 20000, 20000),
                             Column(22000, 22000), Column(24000, 22000), Column(26000, 22000), Column(22000, 24500), Column(24000, 24500), Column(26000, 24500)];   // six: the walk's floor holds over half the page's
        // the right outline's south edge in two pieces with a 900 mm gap at a column: closed by the arrangement, not the walk
        RawSubpath[] rightEdges = [Line(40000, 20000, 43000, 20000), Line(43900, 20000, 48000, 20000), Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000)];
        var two = Read([.. left, .. rightEdges, Column(43000, 20000 - 400), Column(42000, 22000), Column(46000, 22000), Column(42000, 24500), Column(46000, 24500)]);
        Assert.Equal(2, two.Slabs.Count);
        Assert.All(two.Slabs, s => Assert.Equal(W * H, Area(s), 1));

        var few = Read([.. left, .. rightEdges, Column(43000, 20000 - 400), Column(42000, 22000), Column(46000, 24500)]);   // three outside: not a floor's worth
        Assert.Equal(W * H, Area(Assert.Single(few.Slabs)), 1);

        // AND A WALK FLOOR STANDS DOWN ONLY TO AN ARRANGEMENT FLOOR OVER IT: the exact outline under four columns beside the
        // interrupted one under six - the arrangement's floor holds more of the page's columns and lies nowhere near the
        // walk's, which stays (31202's LEVEL 13 outline stood down to cells over the column schedule beside it)
        RawSubpath[] leftFour = [.. left.Take(4), Column(22000, 22000), Column(26000, 22000), Column(22000, 24500), Column(26000, 24500)];
        var walkKept = Read([.. leftFour, .. rightEdges, Column(43000, 20000 - 400), Column(42000, 22000), Column(44000, 22000), Column(46000, 22000), Column(42000, 24500), Column(46000, 24500)]);
        Assert.Equal(2, walkKept.Slabs.Count);
        Assert.All(walkKept.Slabs, s => Assert.Equal(W * H, Area(s), 1));
    }

    /// <summary>
    /// A WALK FLOOR STANDS DOWN ONLY TO A RING THAT LIES ON IT (intake step 135's second half, mended 2026-09-23 after
    /// Codex's audit). The test was whether the AVERAGE of one ring's vertices fell inside the other, and the average of
    /// a concave floor's vertices is not inside the floor: Codex's C-shaped storey - (0,0) (60,0) (60,20) (20,20)
    /// (20,40) (60,40) (60,60) (0,60) in metres, 2,800 sq m - averages to (35,30), in the notch. A schedule's border
    /// drawn in that notch then "contained" the floor's mean point, and the storey stood down to a 120 sq m table with
    /// no overlap between them at all. Step 136 has just made concave floors ordinary, so this was about to matter.
    /// WHAT THIS COVERS: the C and the rectangle in its notch, at the drawing's own scale - the floor stays, the table
    /// does not replace it. WHAT IT DOES NOT: two rings that cross with no corner inside either (a plus sign), which no
    /// two readings of one floor make.
    /// </summary>
    [Fact]
    public void AConcaveFloorDoesNotStandDownToARingDrawnInItsNotch()
    {
        // the C, in mm: the notch is the empty right-middle, and the vertex average (35,30) m lands in it
        (double X, double Y)[] c = [(0, 0), (60000, 0), (60000, 20000), (20000, 20000), (20000, 40000), (60000, 40000), (60000, 60000), (0, 60000)];
        var walk = new PlanLoop("SLABEDGE", c.Select(p => new DxfPoint(p.X, p.Y)).ToList(), closedExactly: true);
        var table = new PlanLoop("SLABEDGE", new List<DxfPoint> { new(30000, 24000), new(40000, 24000), new(40000, 36000), new(30000, 36000) }, closedExactly: true);

        Assert.True(LoopGeometry.PointInPolygon(new DxfPoint(walk.Points.Average(p => p.X), walk.Points.Average(p => p.Y)), table.Points),
            "the fixture only bites if the C's vertex average really does fall inside the table");
        Assert.False(LoopGeometry.PointInPolygon(new DxfPoint(walk.Points.Average(p => p.X), walk.Points.Average(p => p.Y)), walk.Points),
            "and outside the C itself");

        // no corner of either ring lies inside the other: they do not lie on each other, whatever their averages do
        Assert.DoesNotContain(table.Points, p => LoopGeometry.PointInPolygon(p, walk.Points));
        Assert.DoesNotContain(walk.Points, p => LoopGeometry.PointInPolygon(p, table.Points));
    }

    [Fact]
    public void AnEdgeInterruptedInLineIsOneEdgeUpToTheCornerCarryLimit()
    {
        RawSubpath[] rest = [Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000), Column(43000, 22000), Column(46000, 24000)];
        // the south edge in three pieces with a 900 mm gap at each column: one edge, the floor at its full area
        var atColumns = Read([Line(40000, 20000, 42500, 20000), Line(43400, 20000, 45500, 20000), Line(46400, 20000, 48000, 20000), .. rest]);
        Assert.Equal(W * H, Area(Assert.Single(atColumns.Slabs)), 1);
        // a gap over the limit (48 in) is a ramp, a stair or a drawing not finished
        var wide = Read([Line(40000, 20000, 42500, 20000), Line(43800, 20000, 48000, 20000), .. rest]);
        Assert.Empty(wide.Slabs);
        // a jog is not in line: the pieces 900 mm apart and 50 mm off each other's line stay open
        var jog = Read([Line(40000, 20000, 42500, 20000), Line(43400, 20050, 48000, 20050), Line(48000, 20050, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000), Column(43000, 22000), Column(46000, 24000)]);
        Assert.Empty(jog.Slabs);
    }

    [Fact]
    public void AFloorCutByALineAcrossItIsOnePlate()
    {
        // a slab step drawn across the floor divides it into two cells with a column in each: one plate, the whole floor
        var g = Read(Line(40000, 20000, 48000, 20000), Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
                     Line(44000, 20000, 44000, 26000), Column(42000, 22000), Column(46000, 24000));
        Assert.Equal(W * H, Area(Assert.Single(g.Slabs)), 1);
    }

    [Fact]
    public void ABalconyBoxAgainstTheOutlineLeavesTheFloorWholeAndStaysOut()
    {
        // the balcony is a closed box below the south edge sharing that edge's piece, drawn FIRST so a walk that
        // spends a segment on the first ring it closes would give the edge to the box; its cell holds nothing. Its own
        // three sides are drawn with a lighter pen than the outline's: a box facing the page with the outline's pen is
        // slab by the drawing's own word (step 115, ARimCellFacingThePageThroughTheOutlinesPenIsInsideTheOutlineTests)
        double bx0 = 43000, bx1 = 45000, by = 18500;
        var g = Read(Thin(bx0, 20000, bx0, by), Thin(bx0, by, bx1, by), Thin(bx1, by, bx1, 20000), Line(bx1, 20000, bx0, 20000),
                     Line(40000, 20000, bx0, 20000), Line(bx1, 20000, 48000, 20000),
                     Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
                     Column(43000, 22000), Column(46000, 24000), Column(41000, 24000));
        Assert.Equal(W * H, Area(Assert.Single(g.Slabs)), 1);
    }

    [Fact]
    public void ACellUnionInsideADrawnFloorIsThatFloorsInteriorNotASecondFloor()
    {
        // the architect closes the outline as one drawn path and draws the rooms inside it: the rooms' cells
        // hold columns and unite, but they stand inside the drawn floor and are not floors of their own
        var outline = new RawSubpath([(40000, 20000), (48000, 20000), (48000, 26000), (40000, 26000)], true, (0, 0, 0), false, true, 0.5, false);
        var g = Read(outline,
                     Line(40000, 23000, 48000, 23000), Line(44000, 20000, 44000, 26000),   // two room walls, four rooms
                     Column(42000, 21500), Column(46000, 21500), Column(42000, 24500), Column(46000, 24500));
        var slab = Assert.Single(g.Slabs);
        Assert.Equal(W * H, Area(slab), 1);
        Assert.Equal(1, g.FirstEdgeSlab);                                   // the drawn one, and no edge slab after it
    }

    /// <summary>
    /// A SECTION CUT LINE IS NOT A SLAB EDGE (intake step 79, 2026-09-15). 31130's tower plan draws its section marks
    /// as long diagonals across the floor ending in filled arrowheads; united with the outline's cells they gave the
    /// storey a slanted hexagon. A line ending at an arrowhead never bounds a floor. WHAT THIS COVERS: the outline
    /// open on one side, a section line with an arrowhead lying where the edge would be - no floor; the same line
    /// without the arrowhead closes the ring. WHAT IT DOES NOT: a section line
    /// whose arrowhead the column reader read as something else (four points, or a curve).
    /// </summary>
    [Fact]
    public void ALineEndingAtAnArrowheadIsASectionCutNotAnEdge()
    {
        // the north edge is not drawn; a section line runs along where it would be, so with it the cells would
        // close a floor of the outline's area
        RawSubpath[] outline = [Line(40000, 20000, 48000, 20000), Line(48000, 20000, 48000, 26000), Line(40000, 26000, 40000, 20000)];
        RawSubpath[] columns = [Column(43000, 22000), Column(46000, 24000), Column(41000, 24500)];
        var sectionLine = Line(40000, 26000, 48000, 26000);
        // the arrowhead: a filled triangle 300 mm long at the line's east end, pointing east
        var arrowhead = new RawSubpath([(47700, 25900), (48000, 26000), (47700, 26100)], true, (0, 0, 0), true, false, 0.5, false);

        var withArrow = Read([.. outline, sectionLine, arrowhead, .. columns]);
        Assert.Empty(withArrow.Slabs);
        Assert.Single(withArrow.Arrowheads);

        var without = Read([.. outline, sectionLine, .. columns]);
        Assert.Equal(W * H, Area(Assert.Single(without.Slabs)), 1);
    }

    [Fact]
    public void BridgesInLinePairEachEndOnceNearestFirstAndOnlyFacingEnds()
    {
        DxfSegment[] lines =
        [
            new("E", new(0, 0), new(1000, 0)),
            new("E", new(1500, 0), new(2500, 0)),      // 500 from the first: in line, facing
            new("E", new(3300, 0), new(4000, 0)),      // 800 from the second: in line, facing
            new("E", new(1200, 300), new(2000, 300)),  // parallel, off the line
            new("E", new(5000, 0), new(4400, 0)),      // 400 from the third, drawn the other way round: a line has no direction
        ];
        var pieces = GeometryFilterService.BridgesInLine(lines, 1219, 1);
        Assert.Equal(3, pieces.Count);
        Assert.Contains(pieces, p => p.Start == new DxfPoint(1000, 0) && p.End == new DxfPoint(1500, 0));
        Assert.Contains(pieces, p => p.Start == new DxfPoint(2500, 0) && p.End == new DxfPoint(3300, 0));
        Assert.Contains(pieces, p => p.Start == new DxfPoint(4000, 0) && p.End == new DxfPoint(4400, 0));
        // two pieces that overlap on one line have no gap between them: their inner ends do not face each other
        Assert.Empty(GeometryFilterService.BridgesInLine([new("E", new(0, 0), new(1000, 0)), new("E", new(500, 0), new(1500, 0))], 1219, 1));
    }
}
