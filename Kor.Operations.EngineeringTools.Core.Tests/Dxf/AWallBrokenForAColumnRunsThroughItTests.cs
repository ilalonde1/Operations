using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// A wall the drawing stops at each face of a column runs through the column; it is not a doorway with a header over the column
/// (2026-10-27, board 52; VD's decision, as the engineer models 30972 P3 and Jim reads Halifax's south-west wall).
/// WHAT THIS COVERS: a 200 mm wall broken for a 400 x 400 column in millimetres becomes one wall end to end, with no opening and
/// the column kept; a real 900 mm doorway with no column in it keeps its header; a column well off the wall's line does not join it.
/// WHAT IT DOES NOT: inch or feet drawings; a column on the composer's side (a column only another sheet draws); a gap wider than
/// the column plus 6 in, which stays a doorway even with a column in it (the ASSUMED slack). The corpus-wide count is
/// tools/CorpusGateBisect/header_over_column.py.
/// </summary>
public sealed class AWallBrokenForAColumnRunsThroughItTests
{
    private static readonly PlanClassificationOptions Options = new PlanClassificationOptions().InUnitOf(1.0 / 25.4) with
    {
        FloorFromPerimeterWall = false,
    };

    [Fact]
    public void AWallStoppedAtAColumnsFacesIsOneWallThroughIt()
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200)
            .Concat(Rectangle("JBP_V_COL", 3000, -200, 400, 400))
            .Concat(Rectangle("JBP_WALL", 3400, -100, 3000, 200)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        var wall = Assert.Single(made.Walls);
        Assert.Equal(6400, wall.Length, 0);
        Assert.Empty(made.WallOpenings);
        Assert.Single(made.Columns);
        Assert.Empty(WallNetworkInvariant.Check(made, Options.MinWallLength, 1.0 / Options.UnitInInches));
    }

    [Fact]
    public void ADoorwayWithNoColumnKeepsItsHeader()
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200).Concat(Rectangle("JBP_WALL", 3900, -100, 3000, 200)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        Assert.Equal(2, made.Walls.Count);
        Assert.Single(made.WallOpenings);
    }

    [Fact]
    public void AColumnOffTheWallsLineDoesNotJoinIt()
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200)
            .Concat(Rectangle("JBP_V_COL", 3000, 1300, 400, 400))
            .Concat(Rectangle("JBP_WALL", 3400, -100, 3000, 200)).ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        Assert.Equal(2, made.Walls.Count);
    }

    private static IEnumerable<DxfSegment> Rectangle(string layer, double x, double y, double length, double thickness)
    {
        DxfPoint[] points = [new(x, y), new(x + length, y), new(x + length, y + thickness), new(x, y + thickness)];
        for (int i = 0; i < points.Length; i++)
            yield return new DxfSegment(layer, points[i], points[(i + 1) % points.Length]) { OfClosedOutline = true };
    }
}
