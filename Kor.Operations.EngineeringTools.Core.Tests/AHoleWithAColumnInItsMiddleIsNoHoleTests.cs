using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A HOLE WITH A COLUMN IN ITS MIDDLE IS NO HOLE (intake step 107, 2026-09-16). 31032 draws 5 x 5 m X-boxes centred on its
/// columns on P1, L1 and L6 - footings or drop panels marked with an X - on a sheet that draws no columns of its own, so
/// the slab pass's same-sheet exclusion (step 104) could not see them and the yardstick counted fourteen openings of ours
/// with none of hers. The composer knows every column the set placed on the storey: an opening of a shaft's size (under
/// 40 sq m) whose centroid stands within 300 mm of a column's centre is a mark on the column, not cut, and flagged.
/// WHAT THIS COVERS: an opening centred on a column from ANOTHER sheet of the same storey is not cut; one a bay away is;
/// the flag names the sheet and the storey. WHAT IT DOES NOT: a void over 40 sq m holding a column (an atrium's stays);
/// the slab pass's own same-sheet exclusion (AnXAcrossARegionIsAnOpeningTests); the real sets (31032 in run 30).
/// </summary>
public sealed class AHoleWithAColumnInItsMiddleIsNoHoleTests
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

    private static PlanLoop Box(string layer, double x0, double y0, double x1, double y1)
        => new(layer, new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) }, true);

    [Fact]
    public void AnOpeningCentredOnAColumnFromAnotherSheetIsNotCut_OneABayAwayIs()
    {
        var doc = E2kDocument.Parse(Reference);
        var story = doc.ReadStories().Single(s => s.Name == "LEVEL 3");

        // the column sheet: one column at (240, 240) in, and the plate's frame to stand on
        var columns = new PlanGeometrySet();
        columns.Columns.Add(new ColumnFootprint(new DxfPoint(240, 240), 24, 24, "JBP_V_COL"));
        columns.Walls.Add(new WallAxis(new DxfPoint(0, 0), new DxfPoint(480, 0), 12, "JBP_V-WALL"));
        // the plan sheet: a 40 x 40 ft plate, an X-box 10 ft square centred on the column (a footing's mark) and one a bay
        // away with nothing in it (a shaft); the sheet draws no column of its own
        var plan = new PlanGeometrySet();
        plan.Slabs.Add(Box("JBP_C_SLABEDG", 0, 0, 480, 480));
        plan.Openings.Add(Box("JBP_C_SLABEDG", 180, 180, 300, 300));   // centred on (240, 240): the column
        plan.Openings.Add(Box("JBP_C_SLABEDG", 380, 380, 440, 440));   // centred on (410, 410): open field

        var summary = E2kGeometryComposer.Compose(doc, new[]
        {
            new StoryPlacement(story, columns, "columns.dxf"),
            new StoryPlacement(story, plan, "plan.dxf"),
        });

        int cut = doc.LinesOf("AREA ASSIGNS").Count(l => l.Contains("OPENING \"Yes\"", StringComparison.Ordinal));
        Assert.Equal(1, cut);
        string flag = Assert.Single(summary.Flags, f => f.Contains("a column stands in its middle", StringComparison.Ordinal));
        Assert.Contains("plan.dxf", flag);
        Assert.Contains("LEVEL 3", flag);
    }
}
