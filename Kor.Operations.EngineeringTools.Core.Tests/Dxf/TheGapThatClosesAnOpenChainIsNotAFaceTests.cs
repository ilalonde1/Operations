#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// THE EDGE THAT CLOSES AN OPEN CHAIN IS NOT DRAWN, SO IT IS NOT A FACE (step 83, 2026-09-15). 31168's LEVEL 1
/// basement chain, as the loop builder handed it to the decomposer: nine drawn points from (2289,2440) round the
/// east wall to (2414,5021), a gap of 2,584 in between its ends. The gap paired with the 255 in drawn face at x 2312
/// and the material run walked the whole gap - a 2,424 in wall along a line nothing draws, which rose to LEVEL 1
/// MEZZ and stood on nothing. Reproduced here at the chain's own points.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: an open chain whose closing gap runs within a wall's thickness of a drawn face makes no wall
/// along the gap; the same points closed exactly still decompose (the closing edge is drawn then). WHAT IT DOES
/// NOT: the loop builder that produced the chain; whether the drawn faces of this chain pair among themselves
/// (they do not here - none faces another within a wall's thickness); the coverage gate on the real set.
/// </remarks>
public sealed class TheGapThatClosesAnOpenChainIsNotAFaceTests
{
    private static readonly DxfPoint[] Chain =
    [
        new(2289, 2440), new(2324, 2440), new(2312, 2440), new(2312, 2695), new(2604, 2695), new(2605, 5232),
        new(2414, 5232), new(2422, 5232), new(2422, 5013), new(2414, 5013), new(2414, 5021),
    ];

    [Fact]
    public void AnOpenChainsClosingGapMakesNoWall()
    {
        var walls = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_B_WALL", Chain, closedExactly: false), new PlanClassificationOptions(), out _);

        // no wall may run along the gap (2414,5021)-(2289,2440): every wall's midpoint lies on a drawn edge
        foreach (var w in walls)
        {
            var mid = new DxfPoint((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2);
            double toGap = LoopGeometry.DistanceToSegment(mid, Chain[^1], Chain[0]);
            double toDrawn = Enumerable.Range(0, Chain.Length - 1).Min(i => LoopGeometry.DistanceToSegment(mid, Chain[i], Chain[i + 1]));
            Assert.True(toDrawn <= w.Thickness, $"{w.Start}-{w.End} t {w.Thickness}: its midpoint is {toDrawn:0} from every drawn edge and {toGap:0} from the gap");
        }
        Assert.DoesNotContain(walls, w => w.Start.DistanceTo(w.End) > 1000);
    }

    /// <summary>
    /// NO WALL STANDS INSIDE A WALL (step 83). 31138's LEVEL 1 AT 55'-0: the west wall's outer face and the stair's
    /// inner face, 57 in apart in one open chain through a short return, read as one 57 in wall - and the 12 in west
    /// wall, already resolved from its own closed outline, stood inside that band. With the standing wall given, the
    /// pair is not made; without it (nothing else read yet), the same chain still pairs, so the rule is the standing
    /// wall and not a thickness cap - 31202's real 30 in tower walls are open chains too, and a cap lost them.
    /// </summary>
    [Fact]
    public void APairWhoseBandHoldsAWallAlreadyReadIsTheVoidBetweenTwoWalls()
    {
        // faces at x 5.5 and 62.5, 500 in long, joined by one return at the bottom: a U, open at the top
        var u = new DxfPoint[] { new(5.5, 0), new(5.5, 500), new(62.5, 500), new(62.5, 0) };
        var westWall = new WallAxis(new DxfPoint(-0.5, 0), new DxfPoint(-0.5, 500), 12, "JBP_V-WALL");   // its faces at -6.5 and 5.5: outside the band

        var alone = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", u, closedExactly: false), new PlanClassificationOptions(), out _);
        Assert.Contains(alone, w => Math.Abs(w.Thickness - 57) < 0.5);

        var inside = new WallAxis(new DxfPoint(12, 0), new DxfPoint(12, 500), 12, "JBP_V-WALL");           // a wall standing inside the band
        var withAWallInside = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", u, closedExactly: false), new PlanClassificationOptions(), [inside], out _);
        Assert.DoesNotContain(withAWallInside, w => Math.Abs(w.Thickness - 57) < 0.5);

        // touching the band's face from outside is not inside; a re-reading of the same wall (an axis a hair off,
        // the same thickness) is not inside either - that is the duplicate rule's business
        Assert.False(WallOutlineDecomposer.AWallStandsInside(new DxfPoint(34, 0), new DxfPoint(34, 500), 57, [westWall]));
        Assert.True(WallOutlineDecomposer.AWallStandsInside(new DxfPoint(34, 0), new DxfPoint(34, 500), 57, [inside]));
        Assert.False(WallOutlineDecomposer.AWallStandsInside(new DxfPoint(34, 0), new DxfPoint(34, 500), 12, [new WallAxis(new DxfPoint(35, 0), new DxfPoint(35, 500), 12, "x")]));
    }

    /// <summary>
    /// A FACE'S PARTNER IS THE NEAREST FACE THAT FACES IT, WHEREVER IT LIES (step 83). 31138's LEVEL 1 AT 55'-0 as
    /// the chains came: the west wall's OUTER face at x 5.6 in one chain with the stair walls' faces at x 63 and 71
    /// (a U through the top return at y -584); its INNER face at x 17.6 in another chain. The per-chain pass paired
    /// 5.6 with 63 (57 in) and consumed the outer face; the pooled pass never saw the 12 in wall. With the other
    /// chain's faces given, the 57 in pair is refused and the pooled pass makes the 12 in wall. Without them, the
    /// 57 in pair is made - the rule is the nearer face, not a cap.
    /// </summary>
    [Fact]
    public void AFaceWithANearerPartnerInAnotherChainIsNotPairedAcrossIt()
    {
        var outerChain = new DxfPoint[] { new(5.6, -1323), new(5.6, -584), new(71, -584), new(71, -761), new(63, -761), new(63, -596) };
        var innerChain = new DxfPoint[] { new(18, -1311), new(18, -596), new(18, -494), new(164, -412) };
        var otherFaces = new List<(DxfPoint A, DxfPoint B)>();
        for (int k = 0; k + 1 < innerChain.Length; k++) otherFaces.Add((innerChain[k], innerChain[k + 1]));

        var alone = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", outerChain, closedExactly: false), new PlanClassificationOptions(), out _);
        Assert.Contains(alone, w => w.Thickness > 50);

        var seeingTheInnerFace = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", outerChain, closedExactly: false), new PlanClassificationOptions(), null, otherFaces, out _);
        Assert.DoesNotContain(seeingTheInnerFace, w => w.Thickness > 50);

        // a centreline drawn between the faces (31170-arch's 229 mm wall with its centreline on the wall layer) is
        // nearer than a wall's thickness and does not count as a nearer partner
        var faces = new DxfPoint[] { new(0, 0), new(0, 500), new(9, 500), new(9, 0) };        // a 9 in wall, open at the top
        var centreline = new List<(DxfPoint A, DxfPoint B)> { (new DxfPoint(4.5, 0), new DxfPoint(4.5, 500)) };
        var wall = Assert.Single(WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", faces, closedExactly: false), new PlanClassificationOptions(), null, centreline, out _));
        Assert.Equal(9, wall.Thickness, 1);
    }

    [Fact]
    public void TheSamePointsClosedExactlyMayUseTheirLastEdge()
    {
        // closed exactly, the last-to-first edge is drawn and may pair like any other: the decomposer is not asked
        // to refuse it, only to know that an open chain's is not there
        var closed = new DxfPoint[] { new(0, 0), new(0, 300), new(17, 300), new(17, 0) };
        var walls = WallOutlineDecomposer.Decompose(new PlanLoop("JBP_V-WALL", closed, closedExactly: true), new PlanClassificationOptions(), out _);
        var w = Assert.Single(walls);
        Assert.Equal(17, w.Thickness, 1);
    }
}
