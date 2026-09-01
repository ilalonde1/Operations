using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Cutting a floor plate at the match line it was recovered across.
/// </summary>
/// <remarks>
/// A plan too wide for one sheet is split on a match line and drawn twice, and neither half closes
/// a slab edge alone — measured on 31168's level 1, building C's half recovers no plate at all,
/// because the edge runs off the page at the seam and a flood fill escapes through it. The halves
/// are read together, and the ring that comes back covers both buildings.
///
/// Every case here is one this could meet on a real sheet. The degenerate ones matter most: a clip
/// that silently returns the whole ring hands a one-building model the whole site, which is the
/// fault it exists to fix, and it would look exactly like success.
/// </remarks>
public class ClipToSideOfTests
{
    private static List<DxfPoint> Square(double x0, double y0, double x1, double y1) => new()
    {
        new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1),
    };

    private static double Area(IReadOnlyList<DxfPoint> ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum / 2);
    }

    [Fact]
    public void ARingCrossingTheSeamIsCutAtIt()
    {
        var ring = Square(0, 0, 100, 100);

        // Vertical seam at x = 40; keep the side the anchor is on.
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(90, 50));

        Assert.Equal(6_000, Area(kept), 3);          // 60 x 100
        Assert.All(kept, p => Assert.True(p.X >= 40 - 1e-6, $"x={p.X}"));
    }

    [Fact]
    public void TheOtherSideIsKeptWhenTheAnchorIsThere()
    {
        var ring = Square(0, 0, 100, 100);
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(10, 50));

        Assert.Equal(4_000, Area(kept), 3);          // 40 x 100
        Assert.All(kept, p => Assert.True(p.X <= 40 + 1e-6, $"x={p.X}"));
    }

    [Fact]
    public void ARingEntirelyOnTheKeptSideIsUntouched()
    {
        var ring = Square(60, 0, 100, 100);
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(90, 50));

        Assert.Equal(Area(ring), Area(kept), 3);
        Assert.Equal(ring.Count, kept.Count);
    }

    /// <summary>
    /// The whole ring is on the FAR side. Clipping leaves nothing, and nothing is not a plate, so
    /// the ring comes back whole rather than as a scrap — the caller drops it on area instead.
    /// </summary>
    [Fact]
    public void ARingEntirelyOnTheFarSideComesBackWholeRatherThanAsAScrap()
    {
        var ring = Square(0, 0, 30, 100);
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(90, 50));

        Assert.Equal(Area(ring), Area(kept), 3);
    }

    [Fact]
    public void ASeamThroughAVertexKeepsTheSideItShould()
    {
        var ring = new List<DxfPoint>
        {
            new(0, 0), new(40, 0), new(80, 50), new(40, 100), new(0, 100),
        };

        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(70, 50));

        Assert.True(Area(kept) > 0);
        Assert.All(kept, p => Assert.True(p.X >= 40 - 1e-6, $"x={p.X}"));
    }

    [Fact]
    public void ARingLYINGAlongTheSeamIsNotDestroyed()
    {
        var ring = Square(40, 0, 100, 100);          // its left edge IS the seam
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(90, 50));

        Assert.Equal(Area(ring), Area(kept), 3);
    }

    /// <summary>
    /// A seam of zero length says nothing about sides, and an anchor ON the line says nothing about
    /// which side to keep. Both return the ring whole: refusing to cut is safe, cutting on a guess
    /// is not.
    /// </summary>
    [Fact]
    public void ADegenerateSeamOrAnAnchorOnTheLineCutsNothing()
    {
        var ring = Square(0, 0, 100, 100);

        var noSeam = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, 50), new DxfPoint(40, 50), new DxfPoint(90, 50));
        Assert.Equal(Area(ring), Area(noSeam), 3);

        var onTheLine = LoopGeometry.ClipToSideOf(ring, new DxfPoint(40, -50), new DxfPoint(40, 150), new DxfPoint(40, 50));
        Assert.Equal(Area(ring), Area(onTheLine), 3);
    }

    [Fact]
    public void ADiagonalSeamCutsOnTheDiagonal()
    {
        var ring = Square(0, 0, 100, 100);

        // y = x, keeping the lower right.
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(-50, -50), new DxfPoint(150, 150), new DxfPoint(90, 10));

        Assert.Equal(5_000, Area(kept), 3);
        Assert.All(kept, p => Assert.True(p.X >= p.Y - 1e-6, $"({p.X},{p.Y})"));
    }

    [Fact]
    public void ATooSmallRingIsRefusedRatherThanReturnedAsTwoPoints()
    {
        var ring = new List<DxfPoint> { new(0, 0), new(10, 0) };
        var kept = LoopGeometry.ClipToSideOf(ring, new DxfPoint(5, -50), new DxfPoint(5, 50), new DxfPoint(9, 0));

        Assert.Equal(ring.Count, kept.Count);
    }
}
