using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// TWO PLATES OF ONE STOREY COVERING EACH OTHER ARE ONE FLOOR (intake step 113b, 2026-09-17). The composer held one plate
/// per PLACE per storey - one centre - so two readings of one floor from two sheets whose centres sit a foot and more
/// apart were both written: 31202's L1, the foundation plan's ring (34,590 sq ft) and the L1 plan's (34,145), "every
/// floor we have two slabs on top of each other". Now the smaller's ground is sampled on a grid, and a plate nine
/// tenths of whose ground lies inside a plate already written on the storey is that plate read again; the first stands.
/// WHAT THIS COVERS: a second reading of a floor, its outline a foot off along one edge and its centre two feet away,
/// not written and flagged; a genuine second plate beside the first (a wing sharing an edge) written; two plates on
/// different storeys untouched. WHAT IT DOES NOT: the real sets (31202 L1 on the six); a plate half over another (the
/// classifier's own clash rule, per sheet).
/// </summary>
public sealed class AFloorReadTwiceIsOneFloorTests
{
    private static readonly string[] Reference =
    {
        "$ PROGRAM INFORMATION",
        "  PROGRAM  \"ETABS\"  VERSION \"21.2.0\"",
        "",
        "$ CONTROLS",
        "  UNITS  \"KIP\"  \"IN\"  \"F\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 3\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 144",
        "  STORY \"Base\"  HEIGHT 0",
        "",
        "$ MATERIAL PROPERTIES",
        "  MATERIAL  \"65 MPa Walls\"    TYPE \"Concrete\"    GRADE \"x\"",
        "",
        "$ POINT COORDINATES",
        "  POINT \"1\"  0 0 0",
        "",
        "$ AREA CONNECTIVITIES",
        "",
    };

    private static PlanLoop Box(double x0, double y0, double x1, double y1)
        => new("JBP_C_SLABEDG", new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) }, true);

    private static PlanGeometrySet Sheet(PlanLoop plate, params (double X, double Y)[] columns)
    {
        var set = new PlanGeometrySet();
        set.Slabs.Add(plate);
        foreach (var (x, y) in columns) set.Columns.Add(new ColumnFootprint(new DxfPoint(x, y), 24, 24, "JBP_V_COL"));
        set.Walls.Add(new WallAxis(new DxfPoint(plate.Points[0].X, plate.Points[0].Y), new DxfPoint(plate.Points[1].X, plate.Points[1].Y), 12, "JBP_V-WALL"));
        return set;
    }

    private static (int Plates, List<string> Flags) Compose(params StoryPlacement[] placements)
    {
        var doc = E2kDocument.Parse(Reference);
        var summary = E2kGeometryComposer.Compose(doc, placements);
        int plates = doc.LinesOf("AREA CONNECTIVITIES").Count(l => l.Contains(" FLOOR ", StringComparison.Ordinal));
        return (plates, summary.Flags.Where(f => f.Contains("one floor, not two", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public void ASecondReadingOfTheFloorIsNotWritten_AWingBesideItIs()
    {
        var doc = E2kDocument.Parse(Reference);
        var l3 = doc.ReadStories().Single(s => s.Name == "LEVEL 3");
        var l2 = doc.ReadStories().Single(s => s.Name == "LEVEL 2");
        var cols = new[] { (100.0, 100.0), (400.0, 100.0), (100.0, 400.0), (400.0, 400.0) };

        // the same 40 x 40 ft floor read from two sheets, the second a foot wider along its east edge and shifted a foot north
        var (twice, flags) = Compose(
            new StoryPlacement(l3, Sheet(Box(0, 0, 480, 480), cols), "foundation.dxf"),
            new StoryPlacement(l3, Sheet(Box(0, 12, 492, 492), cols), "level-3.dxf"));
        Assert.Equal(1, twice);
        var flag = Assert.Single(flags);
        Assert.Contains("level-3.dxf", flag);

        // a wing sharing the east edge: two floors
        var (wing, none) = Compose(
            new StoryPlacement(l3, Sheet(Box(0, 0, 480, 480), cols), "west.dxf"),
            new StoryPlacement(l3, Sheet(Box(480, 0, 960, 480), (600.0, 100.0), (900.0, 400.0)), "east.dxf"));
        Assert.Equal(2, wing);
        Assert.Empty(none);

        // the same ground on two storeys: two floors
        var (stacked, still) = Compose(
            new StoryPlacement(l3, Sheet(Box(0, 0, 480, 480), cols), "level-3.dxf"),
            new StoryPlacement(l2, Sheet(Box(0, 12, 492, 492), cols), "level-2.dxf"));
        Assert.Equal(2, stacked);
        Assert.Empty(still);
    }
}
