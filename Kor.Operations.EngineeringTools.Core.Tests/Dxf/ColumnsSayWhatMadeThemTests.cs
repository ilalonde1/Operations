using System.Text.Json;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// WHAT THIS COVERS: hand-made millimetre loops reaching all four column-add sites, their source
/// layer/index/box, the requested 203 x 398 fragment inside a 2845 x 203 wall, a 400 x 400 column,
/// and a standalone 300 x 900 stub; segment-clamped containment and metadata-neutral equality,
/// copying and JSON. The fixture sets a 500 mm panel-overlap floor to isolate the unmatched-loop
/// branch and disables wall connection/floor recovery; the short-wall case also caps wall thickness.
/// WHAT IT DOES NOT: actual PDF/DXF extraction, production-default routing of the measured clip,
/// CLI formatting/path discovery, final model bytes, or every possible loop shape; a clipped fill
/// lost before classification or a provenance error on a recovered open chain would escape it.
/// </summary>
public sealed class ColumnsSayWhatMadeThemTests
{
    [Theory]
    [InlineData("CLIP_WALL", 398, 203, true, "nothing-paired-up")]
    [InlineData("JBP_V_COL", 400, 400, false, "column-layer-loop")]
    [InlineData("JBP_WALL", 900, 300, false, "standalone-stub")]
    [InlineData("PIER_WALL", 400, 400, false, "short-wall-layer-loop")]
    public void EveryColumnNamesItsSourceAndWhetherItsCenterIsInsideAWall(
        string layer, double length, double thickness, bool inside, string branch)
    {
        var options = new PlanClassificationOptions().InUnitOf(1.0 / 25.4) with
        {
            ConnectWalls = false,
            FloorFromPerimeterWall = false,
            MinPanelOverlap = 500,
        };
        if (branch == "short-wall-layer-loop") options = options with { MaxWallThickness = 200 };
        var segments = Rectangle(layer, 1000, 0, length, thickness).ToList();
        if (inside) segments.AddRange(Rectangle("LONG_WALL", 0, 0, 2845, 203));

        var made = StructuralPlanClassifier.Classify(segments, options);
        var column = Assert.Single(made.Columns);

        Assert.Equal(branch, column.Origin.Branch);
        Assert.Equal(layer, column.Origin.Layer);
        Assert.Equal(length, column.Origin.LoopLength, 6);
        Assert.Equal(thickness, column.Origin.LoopThickness, 6);
        Assert.Equal(thickness, column.Width, 6);
        Assert.Equal(length, column.Depth, 6);
        Assert.Equal(new DxfPoint(1000 + length / 2, thickness / 2), column.Center);
        if (branch == "standalone-stub") Assert.Equal(-1, column.Origin.LoopIndex);
        else Assert.True(column.Origin.LoopIndex >= 0);

        var containing = made.Walls.Where(w => LoopGeometry.WallContainsPoint(w, column.Center)).ToList();
        Assert.Equal(inside, containing.Count > 0);
        if (inside)
        {
            var wall = Assert.Single(containing);
            Assert.Equal(2845, wall.Length, 6);
            Assert.Equal(203, wall.Thickness, 6);
        }
    }

    [Theory]
    [InlineData(1000, 101.5, true)]
    [InlineData(1000, 101.6, false)]
    [InlineData(2946.5, 0, true)]
    [InlineData(2947, 0, false)]
    [InlineData(-102, 0, false)]
    public void ContainmentUsesHalfTheThicknessAndStopsAtTheSegmentEnds(double x, double y, bool inside)
    {
        var wall = new WallAxis(new DxfPoint(0, 0), new DxfPoint(2845, 0), 203, "WALL");
        Assert.Equal(inside, LoopGeometry.WallContainsPoint(wall, new DxfPoint(x, y)));
    }

    [Fact]
    public void OriginIsDiagnosticMetadataAndSurvivesRecordCopies()
    {
        var original = new ColumnFootprint(new DxfPoint(100, 200), 300, 400, "COL", 30)
        {
            IsRound = true,
            DrawnAsAPolygonCircle = true,
        };
        var traced = original with { Origin = new ColumnOrigin("COL", 7, 400, 300, "column-layer-loop") };

        Assert.Equal("unknown", original.Origin.Branch);
        Assert.Equal(original, traced);
        Assert.Equal(original.GetHashCode(), traced.GetHashCode());
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(traced));
        Assert.Equal(traced.Origin, (traced with { FromBelow = true }).Origin);
        Assert.NotEqual(original, traced with { FromBelow = true });
        Assert.NotEqual(original, traced with { IsRound = false });
        Assert.NotEqual(original, traced with { DrawnAsAPolygonCircle = false });
    }

    private static IEnumerable<DxfSegment> Rectangle(string layer, double x, double y, double length, double thickness)
    {
        DxfPoint[] points = [new(x, y), new(x + length, y), new(x + length, y + thickness), new(x, y + thickness)];
        for (int i = 0; i < points.Length; i++)
            yield return new DxfSegment(layer, points[i], points[(i + 1) % points.Length]) { OfClosedOutline = true };
    }
}
