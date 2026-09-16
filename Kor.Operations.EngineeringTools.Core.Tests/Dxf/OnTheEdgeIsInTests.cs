#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// ON THE EDGE IS IN (step 82, 2026-09-15). <see cref="LoopGeometry.PointInPolygon"/> answers for a point on the
/// boundary with rounding noise, so a ring sharing an edge with the floor it lies in was "inside" in one frame of
/// the page and not in another, and the step-56 differential found 31170-arch gaining a plate on L5 and L2 when
/// the page was shifted 5,000 x 3,001 mm. <see cref="LoopGeometry.InsideOrOn"/> decides it by distance.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a point a hair outside a horizontal edge, and one exactly on a vertex, at four origins - in by
/// distance, and the ray test alone disagreeing with itself across those origins or calling the outside point out;
/// a point clearly outside stays out. WHAT IT DOES NOT: the two callers (the reader's MostlyInside, the composer's
/// SettleFloorsAcrossSheets) on real sheets - TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests is the
/// check that found this and holds them.
/// </remarks>
public sealed class OnTheEdgeIsInTests
{
    private static IReadOnlyList<DxfPoint> Square(double dx, double dy) =>
        [new(8730 + dx, 19438 + dy), new(85149 + dx, 19438 + dy), new(85149 + dx, 58590 + dy), new(8730 + dx, 58590 + dy)];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5000, 3001)]
    [InlineData(0.37, 0.61)]
    [InlineData(-1234.5, 777.25)]
    public void APointAHairOutsideTheEdgeOrOnAVertexIsInByDistanceWhateverTheOrigin(double dx, double dy)
    {
        var square = Square(dx, dy);
        var hairBelow = new DxfPoint(47021 + dx, 19438 + dy - 1e-7);    // a computed intersection, a tenth of a micron outside
        var onVertex = new DxfPoint(8730 + dx, 19438 + dy);
        var wellOutside = new DxfPoint(47021 + dx, 19438 + dy - 40);   // 40 mm out: not on the edge at a 6 mm tolerance

        Assert.True(LoopGeometry.InsideOrOn(hairBelow, square, 6));
        Assert.True(LoopGeometry.InsideOrOn(onVertex, square, 6));
        Assert.False(LoopGeometry.InsideOrOn(wellOutside, square, 6));

        // the ray test alone calls the hair-outside point out - that is the noise the rule replaces
        Assert.False(LoopGeometry.PointInPolygon(hairBelow, square));
    }

    [Fact]
    public void ARingSharingTheFloorsEdgeIsWhollyInsideByDistanceAndNotByTheRayTest()
    {
        // a part plan's plate along the overall floor's south edge: its bottom vertices a tenth of a micron either
        // side of that edge, as an arrangement computes them
        var floor = Square(0, 0);
        var ring = new List<DxfPoint>();
        for (int i = 0; i < 30; i++)
        {
            double x = 47021 + i * 1138;
            ring.Add(new DxfPoint(x, 19438 + (i % 2 == 0 ? -1e-7 : 1e-7)));
        }
        for (int i = 29; i >= 0; i--) ring.Add(new DxfPoint(47021 + i * 1138, 37034));

        Assert.True(ring.All(p => LoopGeometry.InsideOrOn(p, floor, 6)));
        Assert.False(ring.All(p => LoopGeometry.PointInPolygon(p, floor)));
    }
}
