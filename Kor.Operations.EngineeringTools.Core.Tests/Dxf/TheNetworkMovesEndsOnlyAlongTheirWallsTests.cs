using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// The wall network moves a wall's end along the wall, never across it; a header lies on its jambs' axis; a wall drawn twice is
/// not joined to itself (2026-10-27, oversight.Mistake 64-65, board 54). 0f3e4ee4 made every metric drawing's snap the 2 in it
/// was written for and broke all three across 27 of 56 gate sets; these are the three shapes it broke, in millimetres, each
/// checked by WallNetworkInvariant and by what the engineer would see.
/// WHAT THIS COVERS: a column outline drawn twice 21 mm apart (31087's), a short wall ending 20 mm from a long one whose
/// centreline is 30 mm off its own (30864's tilt), two jambs 30 mm out of line either side of a 1 m door (31076's headers).
/// WHAT IT DOES NOT: the composer's joint merge or storeys; inch or feet drawings (the corpus-wide run is
/// `takeoff network-check`, and Halifax in feet is AModelIsTheSameInInchesAndMillimetresTests). A tilt under 0.5 deg
/// (WallNetworkInvariant.ToleranceDegrees) would pass here, and so would a near-copy that also touches a third wall.
/// </summary>
public sealed class TheNetworkMovesEndsOnlyAlongTheirWallsTests
{
    private static readonly PlanClassificationOptions Options = new PlanClassificationOptions().InUnitOf(1.0 / 25.4) with
    {
        FloorFromPerimeterWall = false,
    };

    private static IReadOnlyList<WallNetworkInvariant.Violation> Broken(PlanGeometrySet made)
        => WallNetworkInvariant.Check(made, Options.MinWallLength, 1.0 / Options.UnitInInches);

    [Fact]
    public void AColumnDrawnTwiceIsAColumnNotAWallJoinedToItself()
    {
        var segments = Rectangle("JBP_WALL", 80000, 57180, 305, 760).Concat(Rectangle("JBP_WALL", 80021, 57193, 305, 764)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        Assert.Empty(Broken(made));
        Assert.DoesNotContain(made.Walls, w => w.Length < Options.MinWallLength);
        Assert.NotEmpty(made.Columns);
    }

    [Fact]
    public void AShortWallEndingNearALongOneOffItsLineIsNotTurnedToReachIt()
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200).Concat(Rectangle("JBP_WALL", 3020, -70, 1400, 200)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        Assert.Empty(Broken(made));
        Assert.All(made.Walls, w => Assert.True(Math.Abs(w.Start.Y - w.End.Y) < 1e-6, $"wall ({w.Start.X:0},{w.Start.Y:0})-({w.End.X:0},{w.End.Y:0}) is not level"));
    }

    [Fact]
    public void JambsOutOfLineGetNoCrookedHeader()
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200).Concat(Rectangle("JBP_WALL", 4000, -70, 3000, 200)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        Assert.Empty(Broken(made));
        Assert.All(made.WallOpenings, o => Assert.True(Math.Abs(o.Start.Y - o.End.Y) < 1e-6, $"header ({o.Start.X:0},{o.Start.Y:0})-({o.End.X:0},{o.End.Y:0}) is not level"));
    }

    private static IEnumerable<DxfSegment> Rectangle(string layer, double x, double y, double length, double thickness)
    {
        DxfPoint[] points = [new(x, y), new(x + length, y), new(x + length, y + thickness), new(x, y + thickness)];
        for (int i = 0; i < points.Length; i++)
            yield return new DxfSegment(layer, points[i], points[(i + 1) % points.Length]) { OfClosedOutline = true };
    }
}
