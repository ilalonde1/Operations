using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// A gap in a wall run that holds a column is not a doorway: no header is spanned across the column (2026-10-28, board 52's
/// narrow form, VD's decision; the full wall-through join is parked on board 76 - it disturbed T joints, chaining and the
/// perimeter-wall floor). 84 of 3,177 headers on the 57 gate sets stood over a column (tools/CorpusGateBisect/header_over_column.py).
/// WHAT THIS COVERS: a 200 mm wall with a 700 mm gap holding a 600 x 600 column, in millimetres, keeps both pieces and the column
/// and gets no opening (a gap under the finder's 18 in minimum never gets a header, column or not); a real 900 mm doorway with no column keeps its header; a column well off the line does not stop the header; a gap
/// wider than the column plus 6 in (the ASSUMED slack) with a column in it is still a doorway.
/// WHAT IT DOES NOT: inch or feet drawings; a column only another sheet draws (the finder sees this sheet's columns).
/// </summary>
public sealed class ADoorwayHoldingAColumnGetsNoHeaderTests
{
    private static readonly PlanClassificationOptions Options = new PlanClassificationOptions().InUnitOf(1.0 / 25.4) with
    {
        FloorFromPerimeterWall = false,
    };

    [Fact]
    public void AGapThatHoldsAColumnGetsNoHeader()
    {
        // 700 mm: a doorway's width (over the 18 in minimum), and within a 600 mm column plus 152 mm of slack (752)
        var made = StructuralPlanClassifier.Classify(Run(gap: 700, column: (3050, -300, 600)), Options);

        Assert.Equal(2, made.Walls.Count);
        Assert.Empty(made.WallOpenings);
        Assert.Single(made.Columns);
    }

    [Fact]
    public void ADoorwayWithNoColumnKeepsItsHeader()
    {
        var made = StructuralPlanClassifier.Classify(Run(gap: 900, column: null), Options);

        Assert.Equal(2, made.Walls.Count);
        Assert.Single(made.WallOpenings);
    }

    [Fact]
    public void AColumnOffTheWallsLineDoesNotStopTheHeader()
    {
        var made = StructuralPlanClassifier.Classify(Run(gap: 900, column: (3250, 1300, 400)), Options);

        Assert.Single(made.WallOpenings);
    }

    [Fact]
    public void AGapWiderThanTheColumnAndItsSlackIsStillADoorway()
    {
        // 1,200 mm against a 600 mm column: 600 + 152 mm of slack is 752 - a doorway beside a column, not a wall broken for it
        var made = StructuralPlanClassifier.Classify(Run(gap: 1200, column: (3300, -300, 600)), Options);

        Assert.Single(made.WallOpenings);
    }

    /// <summary>Two 3,000 mm wall pieces on y = 0 with a gap, and optionally a square column (x, y, side) at its corner point.</summary>
    private static List<DxfSegment> Run(double gap, (double X, double Y, double Side)? column)
    {
        var segments = Rectangle("JBP_WALL", 0, -100, 3000, 200).Concat(Rectangle("JBP_WALL", 3000 + gap, -100, 3000, 200)).ToList();
        if (column is { } c)
            segments.AddRange(Rectangle("JBP_V_COL", c.X, c.Y, c.Side, c.Side));
        return segments;
    }

    private static IEnumerable<DxfSegment> Rectangle(string layer, double x, double y, double length, double thickness)
    {
        DxfPoint[] points = [new(x, y), new(x + length, y), new(x + length, y + thickness), new(x, y + thickness)];
        for (int i = 0; i < points.Length; i++)
            yield return new DxfSegment(layer, points[i], points[(i + 1) % points.Length]) { OfClosedOutline = true };
    }
}
