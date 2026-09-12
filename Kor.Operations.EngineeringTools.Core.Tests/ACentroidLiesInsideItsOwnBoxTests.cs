#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A loop's centroid lies inside the loop's bounding box (2026-09-12). The area formula divides
/// by the signed area; a bow-tie — two lobes that cancel — has an area near zero but not zero,
/// and the quotient landed 31202's 18x18 column at (-3.2 km, 3.3 km). Eight such points sat in
/// every model of that set since its first bank; the render fitted every storey to a frame that
/// held them and showed the building as a dot.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a rectangle's centroid is its centre (the formula still governs); a bow-tie
/// with lobes of equal area gets the vertex mean, inside the box; a bow-tie with lobes of nearly
/// equal area — the case that blew up — likewise; a thin sliver. WHAT IT DOES NOT: a loop that is
/// wrong for other reasons (a column drawn as a bow-tie is still a shape the classifier judges by
/// its box; the model invariant `member-outside-the-building` is the second gate).
/// </remarks>
public sealed class ACentroidLiesInsideItsOwnBoxTests
{
    private static PlanLoop Loop(params (double X, double Y)[] pts)
        => new("JBP_V_COL", pts.Select(p => new DxfPoint(p.X, p.Y)).ToList(), closedExactly: true);

    [Fact]
    public void ARectanglesCentroidIsItsCentre()
    {
        var c = Loop((0, 0), (457, 0), (457, 457), (0, 457)).Centroid();
        Assert.Equal(228.5, c.X, 1e-6);
        Assert.Equal(228.5, c.Y, 1e-6);
    }

    [Fact]
    public void ABowTiesCentroidIsInsideItsBoxNotKilometresAway()
    {
        // two lobes that cancel exactly, and two that nearly cancel (the second lobe 0.01 mm taller)
        var exact = Loop((0, 0), (457, 457), (457, 0), (0, 457)).Centroid();
        var nearly = Loop((0, 0), (457, 457.01), (457, 0), (0, 457)).Centroid();
        foreach (var c in new[] { exact, nearly })
        {
            Assert.InRange(c.X, 0, 457);
            Assert.InRange(c.Y, 0, 457.01);
        }
    }

    [Fact]
    public void ASliversCentroidIsOnTheSliver()
    {
        var c = Loop((0, 0), (10000, 0), (10000, 0.5), (0, 0.5)).Centroid();
        Assert.Equal(5000, c.X, 1e-6);
        Assert.InRange(c.Y, 0, 0.5);
    }
}
