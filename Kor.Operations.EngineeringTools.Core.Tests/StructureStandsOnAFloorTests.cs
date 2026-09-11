#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Across the sheets of one storey, a plate inside a larger plate is not a second floor — and that
/// is all the tool knows (intake step 35, composer side). Within one sheet the classifier already
/// knows a ring inside a floor; across the sheets of one storey it did not, so a stair box one
/// enlargement closed became a plate of its own inside the footprint the key plan closed.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a plate wholly inside a larger plate from ANOTHER sheet leaving the floors,
/// NOT becoming an opening, and being listed; a wall and a column standing beyond every plate by
/// more than a hand's width COUNTED "n of m" and left in place (the removal cost four KOR sets
/// their walls, 2026-09-10); a storey with no plate at all left untouched and unmentioned; a plate
/// the largest does not contain left and said, without the word "kept". WHAT IT DOES NOT: the real
/// sets (31170 floors 17 → 8, the five KOR sets measured 2026-09-10); a ring inside a floor on the
/// SAME sheet (the classifier's, not this pass's); whether the plate this pass leaves beyond the
/// floor is modelled later — the composer's own gates (a plate nothing stands under, one plate per
/// place) run after this and this test does not run them.
/// </remarks>
public sealed class StructureStandsOnAFloorTests
{
    private static WallAxis Wall(double x0, double y0, double x1, double y1) => new(new DxfPoint(x0, y0), new DxfPoint(x1, y1), 8, "KOR_V-WALL");
    private static ColumnFootprint Column(double x, double y) => new(new DxfPoint(x, y), 24, 24, "KOR_V_COL");

    private static PlanLoop Box(double x0, double y0, double x1, double y1) =>
        new("KOR_C_SLABEDG", [new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1)], true);

    private static (PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>) Sheet(string name, string storey, PlanGeometrySet g)
        => (new PlanSheetInfo(name, null, [], false, name), g, [storey]);      // no vocabulary read: the pass keys on nothing but the file name

    private const double Reach = 6;
    private const double SqFtPerUnit = 1.0 / 144.0;          // an inch model

    [Fact]
    public void ARingInsideAnotherSheetsFloorIsNotASecondFloorAndIsNotCutEither()
    {
        var keyPlan = new PlanGeometrySet(); keyPlan.Slabs.Add(Box(0, 0, 1200, 800));         // the footprint
        var enlargement = new PlanGeometrySet(); enlargement.Slabs.Add(Box(500, 300, 600, 400)); // the stair box, closed on its own
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", "L3", keyPlan), Sheet("A415_1_LEVEL 3 PLAN (SW).dxf", "L3", enlargement),
        };
        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets(parsed, Reach, SqFtPerUnit);

        Assert.Single(keyPlan.Slabs);
        Assert.Empty(enlargement.Slabs);
        Assert.Empty(enlargement.Openings);                                                    // the drawing did not say it is a hole
        Assert.Empty(keyPlan.Openings);                                                        // and not cut from the container either (audit F25)
        Assert.Contains(warnings, w => w.StartsWith("1 ring(s) inside another sheet's floor are not second floors", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("L3: a 69 sq ft ring on A415_1_LEVEL 3 PLAN (SW).dxf lies inside the 6,667 sq ft floor", StringComparison.Ordinal)
                                    && w.Contains("not cut as an opening", StringComparison.Ordinal));
    }

    [Fact]
    public void AMemberBeyondEveryPlateIsCountedAndLeftWhereItStands()
    {
        var g = new PlanGeometrySet();
        g.Slabs.Add(Box(0, 0, 1200, 800));
        g.Walls.Add(Wall(0, 0, 1200, 0));                     // on the edge
        g.Walls.Add(Wall(0, 804, 1200, 804));                 // a hand's width past it: on the floor
        g.Walls.Add(Wall(0, 900, 1200, 900));                 // a balcony rail — or a wall on a floor not read
        g.Columns.Add(Column(600, 400));                       // on the floor
        g.Columns.Add(Column(1300, 400));                      // beyond it
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)> { Sheet("A104_1_LEVEL 3 PLAN.dxf", "L3", g) };
        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets(parsed, Reach, SqFtPerUnit);

        Assert.Equal(3, g.Walls.Count);                                                        // nothing removed
        Assert.Equal([0.0, 804, 900], g.Walls.Select(w => w.Start.Y));                          // and nothing moved (audit F25)
        Assert.Equal([600.0, 1300], g.Columns.Select(c => c.Center.X));
        Assert.Equal(2, g.Columns.Count);
        Assert.Contains(warnings, w => w.StartsWith("0 ring(s) inside another sheet's floor are not second floors; 1 wall(s) and 1 column(s) stand beyond every plate", StringComparison.Ordinal));
        var line = Assert.Single(warnings, w => w.StartsWith("L3: 1 of 3 wall(s) and 1 of 2 column(s) stand beyond every plate read for the storey", StringComparison.Ordinal));
        Assert.Contains("nothing removed", line, StringComparison.Ordinal);
        Assert.Contains("wall at (600, 900) on A104_1_LEVEL 3 PLAN.dxf", line, StringComparison.Ordinal);
        Assert.Contains("column at (1,300, 400) on A104_1_LEVEL 3 PLAN.dxf", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoreyWithNoPlateHasNothingToStandOnAndIsNotMentioned()
    {
        var g = new PlanGeometrySet();
        g.Walls.Add(Wall(0, 0, 1200, 0)); g.Columns.Add(Column(5000, 5000));
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)> { Sheet("A102_1_LEVEL P1 PLAN.dxf", "P1", g) };
        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets(parsed, Reach, SqFtPerUnit);
        Assert.Single(g.Walls); Assert.Single(g.Columns);
        Assert.Empty(warnings);
    }

    [Fact]
    public void APlateTheLargestDoesNotContainIsLeftAndSaid()
    {
        var a = new PlanGeometrySet(); a.Slabs.Add(Box(0, 0, 1200, 800));
        var b = new PlanGeometrySet(); b.Slabs.Add(Box(1300, 0, 1500, 800));                 // a ramp, or the next building, beside the footprint
        b.Walls.Add(Wall(1300, 0, 1500, 0));                                                    // standing on it
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 1 PLAN.dxf", "L1", a), Sheet("A404_1_LEVEL 1 PLAN (SE).dxf", "L1", b),
        };
        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets(parsed, Reach, SqFtPerUnit);
        Assert.Single(a.Slabs); Assert.Single(b.Slabs); Assert.Single(b.Walls);
        Assert.Empty(b.Openings);
        var said = Assert.Single(warnings);
        Assert.Contains("L1: a 1,111 sq ft plate stands beyond the storey's 6,667 sq ft floor", said, StringComparison.Ordinal);
        Assert.Contains("left as read for the engineer", said, StringComparison.Ordinal);
        Assert.DoesNotContain("kept", said, StringComparison.Ordinal);                        // this pass cannot promise the model carries it
    }
}
