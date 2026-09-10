#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A floor's edge is the outermost closed loop the plan draws (intake step 24).
///
/// A parkade's perimeter is a wall and its plate comes from the walls' outer face (step 22).
/// Every other storey's perimeter is a slab edge, drawn as ordinary lines — 31168's tower sheets
/// are titled "CONCRETE OUTLINE" after it — so the lines are chained into rings, and a ring big
/// enough to be a floor, with structure standing in it, and inside no other such ring, is the
/// storey's plate.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: four lines closing a floor-sized ring becoming one slab, with its lines read
/// as its edge and kept out of the DXF's beams; a ring too small to be a floor; a ring with nothing
/// standing in it; a core's ring inside a floor's, which is not a second floor; a ring left open,
/// which is nothing; a wall's own face lines, which are the wall and not an edge; (step 27) an
/// edge broken by a hand's width closing, an edge stopping short of its corner carried to it, a
/// gap wider than the bridge staying open, a hatch of short dashes not being searched, and an edge
/// stepping round a balcony closing through it and keeping the floor's area; and (step 28) a wall
/// standing on PART of the edge leaving the rest of it the floor's edge, against a wall that used
/// the whole line keeping it. WHAT IT DOES NOT: the
/// areas on a real sheet (31168's tower plans against Revit's plates, measured in the build, not
/// banked here); which storey the plate lands on (the DXF side's business); a ring whose inside is
/// another sheet's view, and a sheet carrying two plans' rings — both give one slab each, and only
/// the sheet-to-view split decides where they belong; a gap bridged to the wrong neighbour where
/// two edges end near one another; and the storeys that still have no plate at all, which only the
/// build's own count against Revit shows.
///
/// AND IT DOES NOT CATCH A RING THAT IS A PIECE OF THE FLOOR RATHER THAN THE FLOOR. The gates ask
/// that structure stand in the ring and that most of the structure NEAR it be inside, both measured
/// against the ring's own size, so a small ring in the corner of a big floor passes: 31168's LEVEL 2
/// takes a 4,222 sq ft rectangle where Revit's storey is 48,501 sq ft in three plates, and tower C's
/// L5-L8 take a 1,922 sq ft strip of a 14,988 sq ft floor. Nothing here fails on either. That is the
/// open item this rule hands on, and it is measured in the build, not banked.
/// </remarks>
public sealed class AFloorsEdgeIsTheOutermostClosedLoopTests
{
    private const double W = 8000, H = 6000;      // 48 sq m: over the 400 sq ft a floor must have

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);

    private static RawSubpath[] Ring(double x, double y, double w, double h) =>
    [
        Line(x, y, x + w, y), Line(x + w, y, x + w, y + h), Line(x + w, y + h, x, y + h), Line(x, y + h, x, y),
    ];

    /// <summary>A column standing inside the ring, so the ring is a floor and not a box of notes.</summary>
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);

    private static (ExtractedGeometry Geometry, List<PathFate> Fates) Read(params RawSubpath[] paths)
    {
        var fates = new List<PathFate>();
        var geometry = FateFixture.Classify(paths.ToList(), fates);
        return (geometry, fates);
    }

    [Fact]
    public void FourLinesClosingAFloorSizedRingAreItsEdge()
    {
        var (g, fates) = Read([.. Ring(40000, 20000, W, H), Column(43000, 22000)]);
        var slab = Assert.Single(g.Slabs);
        Assert.Equal(4, slab.Count);
        Assert.Equal(W * H, Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)), 1);
        Assert.Equal(0, g.FirstEdgeSlab);
        Assert.Equal(4, fates.Count(f => f.Reason == PathReason.BecameSlabEdge));
        Assert.All(fates.Where(f => f.Reason == PathReason.BecameSlabEdge), f => Assert.Equal(0, f.ObjectIndex));
        // and its lines are the floor, not beams
        Assert.Equal(4, g.SlabEdgeLines.Count);
        Assert.All(g.SlabEdgeLines.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void ARingTooSmallForAFloorIsNotOne()
    {
        // 3 m x 3 m is 97 sq ft: a stair, a shaft, a box of notes
        var (g, fates) = Read([.. Ring(40000, 20000, 3000, 3000), Column(41500, 21500)]);
        Assert.Empty(g.Slabs);
        Assert.DoesNotContain(fates, f => f.Reason == PathReason.BecameSlabEdge);
    }

    [Fact]
    public void ARingWithNothingStandingInItIsNotAFloor()
    {
        var (g, _) = Read(Ring(40000, 20000, W, H));
        Assert.Empty(g.Slabs);
    }

    [Fact]
    public void ACoresRingInsideAFloorIsNotASecondFloor()
    {
        var inner = Ring(42000, 21000, 4000, 3500);           // 150 sq ft short of a floor on its own
        var (g, _) = Read([.. Ring(40000, 20000, W, H), .. inner, Column(43000, 22000)]);
        var slab = Assert.Single(g.Slabs);
        Assert.Equal(W * H, Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)), 1);
    }

    [Fact]
    public void ATwoStoreySheetGivesEachPlansRing()
    {
        // one sheet, two views (31168's tower sheets draw two storeys side by side): each closed
        // ring is a floor, and neither is inside the other
        var (g, _) = Read([.. Ring(40000, 20000, W, H), Column(43000, 22000),
                           .. Ring(60000, 20000, W, H), Column(63000, 22000)]);
        Assert.Equal(2, g.Slabs.Count);
        Assert.All(g.Slabs, s => Assert.Equal(W * H, Math.Abs(PolygonProcessor.PolygonAreaMm2(s)), 1));
    }

    [Fact]
    public void AStripWithTheStructureBesideItIsNotTheFloor()
    {
        // a closed strip along one side of a plan, one column in it and eight beside it (31168's
        // BLDG C plan: a 28.6 m x 6.3 m ring closed on its own where the 14,988 sq ft floor did not)
        var strip = Ring(40000, 20000, 28000, 6300);
        var beside = Enumerable.Range(0, 8).Select(i => Column(41000 + i * 3400, 27500)).ToArray();
        var (g, _) = Read([.. strip, Column(43000, 22000), .. beside]);
        Assert.Empty(g.Slabs);
        // and the same strip with the structure in it is a floor
        var (whole, _) = Read([.. strip, Column(43000, 22000), Column(50000, 23000), Column(60000, 24000)]);
        Assert.Single(whole.Slabs);
    }

    [Fact]
    public void ARingLeftOpenIsNothing()
    {
        var ring = Ring(40000, 20000, W, H);
        var (g, fates) = Read([ring[0], ring[1], ring[2], Column(43000, 22000)]);   // three sides
        Assert.Empty(g.Slabs);
        Assert.DoesNotContain(fates, f => f.Reason == PathReason.BecameSlabEdge);
    }

    // ---------------------------------------------------------------------------------------
    // An edge interrupted is still one edge (intake step 27)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AnEdgeBrokenByAHandsWidthIsStillTheEdge()
    {
        // the south edge drawn in two pieces 100 mm apart where a leader crosses it: the DXF
        // side's own bridge (6 in) closes it, and the ring is the floor
        var (g, _) = Read(
            Line(40000, 20000, 43000, 20000), Line(43100, 20000, 48000, 20000),
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000));
        var slab = Assert.Single(g.Slabs);
        Assert.InRange(Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)), W * H * 0.999, W * H * 1.001);
    }

    [Fact]
    public void AnEdgeStoppingShortOfItsCornerIsCarriedToIt()
    {
        // the east edge stops a metre short of the north-east corner (a bubble's leader took the
        // rest): carried on to where the north edge's line meets it, within the 4 ft the DXF side
        // allows, and the ring is the floor at its true area
        var (g, _) = Read(
            Line(40000, 20000, 48000, 20000), Line(48000, 20000, 48000, 25000),
            Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000));
        var slab = Assert.Single(g.Slabs);
        Assert.InRange(Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)), W * H * 0.999, W * H * 1.001);
    }

    [Fact]
    public void AGapWiderThanTheBridgeStaysOpen()
    {
        // three metres missing from the south edge is a ramp, a stair or a drawing not finished:
        // nothing bridges it and the storey keeps having no plate
        var (g, _) = Read(
            Line(40000, 20000, 42000, 20000), Line(45000, 20000, 48000, 20000),
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000));
        Assert.Empty(g.Slabs);
    }

    [Fact]
    public void AnEdgeSteppingRoundABalconyClosesThroughItAndKeepsTheFloorsArea()
    {
        // the south edge stops either side of a balcony: a closed box standing out from the
        // outline, its diagonals drawn, with the outline's line ending at its sides two metres
        // apart. The outline itself never closes, so the chain pass carries on through the
        // balcony's own linework — here its diagonal — and the ring that comes back is the floor
        // plus part of the balcony. That is the answer this rule is allowed to give: somewhere
        // between the outline and the outline with the balcony, never less and never more.
        double bx0 = 43000, bx1 = 45000, by = 18500;                        // the box hangs 1.5 m below the south edge
        var (g, _) = Read(
            Line(40000, 20000, bx0, 20000), Line(bx1, 20000, 48000, 20000),
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Line(bx0, 20000, bx0, by), Line(bx0, by, bx1, by), Line(bx1, by, bx1, 20000),
            Line(bx0, 20000, bx1, by), Line(bx0, by, bx1, 20000),
            Column(43000, 22000), Column(46000, 24000), Column(41000, 24000));
        var slab = Assert.Single(g.Slabs);
        Assert.InRange(Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)),
                       W * H * 0.999, W * H + (bx1 - bx0) * (20000 - by));
    }

    [Fact]
    public void OnlyLongChainsAreBridgedSoAHatchIsNotSearched()
    {
        // a hatch of a thousand short dashes beside the ring: none of them is long enough to be a
        // piece of an edge, and the ring still closes across its one gap
        var paths = new List<RawSubpath>
        {
            Line(40000, 20000, 43000, 20000), Line(43100, 20000, 48000, 20000),
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000),
        };
        for (int i = 0; i < 1000; i++) paths.Add(Line(50000 + (i % 40) * 120, 20000 + (i / 40) * 120, 50000 + (i % 40) * 120 + 60, 20000 + (i / 40) * 120 + 60));
        var (g, _) = Read(paths.ToArray());
        Assert.Single(g.Slabs);
    }

    [Fact]
    public void AWallsFaceLinesAreTheWallNotAnEdge()
    {
        // two face lines a wall's thickness apart make a wall (step 20) and cannot also be a floor's
        // edge: the rule takes only lines nothing else claimed. The filled wall and its own two edge
        // lines are there because the cut pen is the pen those edges are drawn with — no filled
        // wall, no face walls.
        var (g, fates) = Read(FateFixture.Rect(2000, 1000), Line(0, 0, 2000, 0), Line(0, 1000, 2000, 1000),
                              Line(10000, 8000, 13000, 8000), Line(10000, 8300, 13000, 8300));
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.BecameWallFace));
        Assert.Empty(g.Slabs);
    }

    /// <summary>
    /// The floor's south edge runs the full 8 m; a balcony band is drawn 711 mm inside it for 3 m,
    /// and the two pair as a wall's faces. The wall stands on a quarter of the edge, so three
    /// quarters of it are still the plan's line and the ring closes — 31168's tower plans, where a
    /// 3,633 mm overlap was spending a 21,320 mm edge and every tower storey lost its floor.
    /// </summary>
    [Fact]
    public void AWallOnPartOfTheEdgeLeavesTheRestOfItTheFloorsEdge()
    {
        var (g, fates) = Read(
            FateFixture.Rect(2000, 1000), Line(0, 0, 2000, 0), Line(0, 1000, 2000, 1000),   // a filled wall: the sheet's cut pen
            Line(40000, 20000, 48000, 20000),                                               // the south edge, whole
            Line(44000, 20711, 47000, 20711),                                               // a balcony band 711 mm inside it
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000), Column(41000, 24000));

        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.BecameWallFace));            // the pair was read as a wall
        var slab = Assert.Single(g.Slabs);                                                   // and the floor still closed
        Assert.InRange(Math.Abs(PolygonProcessor.PolygonAreaMm2(slab)), W * H * 0.999, W * H * 1.001);
    }

    /// <summary>
    /// And the test is whether the wall used the whole line. A band running 7 m of the 8 m edge
    /// leaves 1 m over — shorter than the 2 m a piece of an edge has to be — so the line stays the
    /// wall's and no floor is read. Without this, slanted walls' own faces chained into a
    /// 12,391 sq ft chevron on 31168's LEVEL 2 sheet that the rendered storey showed was no floor.
    /// </summary>
    [Fact]
    public void AWallThatUsedTheWholeLineKeepsIt()
    {
        var (g, fates) = Read(
            FateFixture.Rect(2000, 1000), Line(0, 0, 2000, 0), Line(0, 1000, 2000, 1000),
            Line(40000, 20000, 48000, 20000),
            Line(40500, 20711, 47500, 20711),                                                // 7 m of the 8 m edge
            Line(48000, 20000, 48000, 26000), Line(48000, 26000, 40000, 26000), Line(40000, 26000, 40000, 20000),
            Column(43000, 22000), Column(46000, 24000), Column(41000, 24000));

        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.BecameWallFace));
        Assert.Empty(g.Slabs);
    }
}
