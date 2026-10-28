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

    [Fact]
    public void ACrossWallThatMetAPiecesEndStillMeetsTheJoinedRun()
    {
        // board 76 consumer 1 (31053 L2, KW86 280 mm short): the cross wall met the east piece's end at the column's face, a
        // corner; joined, the run goes on past it, the crossing is mid-span, and the network carries no stem into a T
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200)
            .Concat(Rectangle("JBP_V_COL", 3000, -300, 600, 600))
            .Concat(Rectangle("JBP_WALL", 3600, -100, 3000, 200))
            .Concat(Rectangle("JBP_WALL", 3600, -3000, 200, 2900))          // the cross wall: x 3,600-3,800, up to the run's face
            .ToList();

        var made = StructuralPlanClassifier.Classify(segments, Options);

        var stem = Assert.Single(made.Walls, w => Math.Abs(w.Start.X - w.End.X) < 1);
        double top = Math.Max(stem.Start.Y, stem.End.Y);
        Assert.Equal(0, top, 0);                                              // on the run's axis, not 100 short at its face
        Assert.Contains(made.Walls, w => Math.Abs(w.Start.Y) < 1 && Math.Abs(w.End.Y) < 1
                                         && (Math.Abs(w.Start.X - 3700) < 1 || Math.Abs(w.End.X - 3700) < 1));   // split there: a T joint
        Assert.Empty(WallNetworkInvariant.Check(made, Options.MinWallLength, 1.0 / Options.UnitInInches));
    }

    private static IEnumerable<DxfSegment> Rectangle(string layer, double x, double y, double length, double thickness)
    {
        DxfPoint[] points = [new(x, y), new(x + length, y), new(x + length, y + thickness), new(x, y + thickness)];
        for (int i = 0; i < points.Length; i++)
            yield return new DxfSegment(layer, points[i], points[(i + 1) % points.Length]) { OfClosedOutline = true };
    }
}
