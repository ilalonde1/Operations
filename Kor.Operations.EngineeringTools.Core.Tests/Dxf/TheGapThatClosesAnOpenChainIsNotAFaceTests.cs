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
