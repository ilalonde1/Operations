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
    public void ARingMostlyInsideAnotherSheetsFloorIsTheSameFloorReadAgain()
    {
        // step 103 (2026-09-16): 31170-arch's enlarged part plans each close a piece of the floor the overall plan draws
        // whole, and a balcony on the part plan pokes past the overall's drawn edge - nine of ten vertices inside is the
        // same floor read again. A ring with more than a tenth of its vertices out stays a plate of its own, and is said.
        var keyPlan = new PlanGeometrySet(); keyPlan.Slabs.Add(Box(0, 0, 1200, 800));
        var part = new PlanGeometrySet();
        part.Slabs.Add(new PlanLoop("KOR_C_SLABEDG",
            [new DxfPoint(0, 0), new DxfPoint(600, 0), new DxfPoint(600, 400), new DxfPoint(300, 400), new DxfPoint(300, 500), new DxfPoint(200, 500),
             new DxfPoint(200, 400), new DxfPoint(100, 400), new DxfPoint(100, -50), new DxfPoint(50, 400), new DxfPoint(0, 400)], true));   // 11 vertices, one of them 50 past the edge: ten of eleven in
        var wing = new PlanGeometrySet();
        wing.Slabs.Add(new PlanLoop("KOR_C_SLABEDG",
            [new DxfPoint(1100, 0), new DxfPoint(1500, 0), new DxfPoint(1500, 400), new DxfPoint(1100, 400)], true));                                          // half of it out: its own
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 1 PLAN.dxf", "L1", keyPlan), Sheet("A414_1_LEVEL 1 PLAN (NW).dxf", "L1", part), Sheet("A416_1_LEVEL 1 PLAN (NE).dxf", "L1", wing),
        };
        DxfToEtabsService.SettleFloorsAcrossSheets(parsed, Reach, SqFtPerUnit);
        Assert.Single(keyPlan.Slabs);
        Assert.Empty(part.Slabs);
        Assert.Single(wing.Slabs);
    }

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

    /// <summary>
    /// A SHEET'S LEGEND FRAME IS NOT A FLOOR (step 185, 2026-10-27): the office's sheets frame their key at the sheet's edge,
    /// the slab pass closes the frame, and its line-type sample reads as a wall - 35 plates on 20 of 56 gate models. The bare
    /// rectangle beyond the building goes, with its one sample stroke, and is said; a rectangle holding a column, an opening,
    /// or two strokes, or touching the building's box, is left as read; and the rule off leaves everything.
    ///
    /// AND A BARE ONE WITH NO STROKE STAYS (migration 126): "at most one stroke" took 40112-01's upper steel roof deck on its
    /// own replay - bare only because none of its steel was read. Nothing read on it is not evidence it is not a floor.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASheetsLegendFrameIsNotAFloor(bool on)
    {
        PlanLoop bare = Box(1300, -1200, 2300, -500);                                            // a roof whose steel was not read
        PlanGeometrySet Storey(out PlanLoop legend, out PlanLoop withColumn, out PlanLoop withTwo, out PlanLoop touching, out WallAxis sample)
        {
            var g = new PlanGeometrySet();
            g.Slabs.Add(Box(0, 0, 1200, 800));                                                   // the floor
            g.Walls.Add(Wall(0, 0, 1200, 0)); g.Walls.Add(Wall(0, 800, 1200, 800)); g.Columns.Add(Column(600, 400));
            legend = Box(0, -1200, 1000, -500); g.Slabs.Add(legend);                             // below the building: 4,861 sq ft
            sample = Wall(50, -550, 200, -550); g.Walls.Add(sample);                            // the key's wall sample
            withColumn = Box(1400, 0, 2400, 600); g.Slabs.Add(withColumn); g.Columns.Add(Column(1900, 300));   // beside it
            withTwo = Box(2600, 0, 3600, 600); g.Slabs.Add(withTwo);
            g.Walls.Add(Wall(2650, 550, 2800, 550)); g.Walls.Add(Wall(2650, 450, 2800, 450));
            touching = Box(1100, 700, 2100, 1400); g.Slabs.Add(touching);                        // overlaps the building's box
            g.Slabs.Add(bare);
            return g;
        }

        var g = Storey(out var legend, out var withColumn, out var withTwo, out var touching, out var sample);
        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets([Sheet("S2.03_1_LEVEL 2 PLAN.dxf", "L2", g)], Reach, SqFtPerUnit, legendFrameIsNotAFloor: on);

        Assert.Equal(!on, g.Slabs.Contains(legend));
        Assert.Equal(!on, g.Walls.Contains(sample));
        Assert.Contains(withColumn, g.Slabs); Assert.Contains(withTwo, g.Slabs); Assert.Contains(touching, g.Slabs);
        Assert.Contains(bare, g.Slabs);
        Assert.Equal(on ? 1 : 0, warnings.Count(w => w.Contains("the sheet's legend frame and its key's sample, not a floor", StringComparison.Ordinal)));
        if (on) Assert.Contains(warnings, w => w.Contains("4,861 sq ft rectangle on S2.03_1_LEVEL 2 PLAN.dxf", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE SAME FRAME ON ANOTHER SHEET OF THE STOREY, WITHOUT ITS STROKE, GOES WITH IT (2026-10-27). 31066-01's building 2 sheets
    /// draw building 1's legend frame at the very same place; building 2's key stroke was the same wall as building 1's and was
    /// modelled once, so its copy holds none. Once building 1's frame is taken, the copy at the very rectangle is that frame - and
    /// a bare rectangle anywhere else still stays (the 40112 roof's rule).
    /// </summary>
    [Fact]
    public void TheSameLegendFrameDrawnAgainOnAnotherSheetWithoutItsStrokeGoesToo()
    {
        var one = new PlanGeometrySet();
        one.Slabs.Add(Box(0, 0, 1200, 800));
        one.Walls.Add(Wall(0, 0, 1200, 0)); one.Columns.Add(Column(600, 400));
        var frame = Box(0, -1200, 1000, -500); one.Slabs.Add(frame);
        one.Walls.Add(Wall(50, -550, 200, -550));                                             // building 1's key stroke
        var two = new PlanGeometrySet();
        var copy = Box(0, -1200, 1000, -500); two.Slabs.Add(copy);                              // building 2's copy, no stroke
        var bare = Box(1300, -1200, 2300, -500); two.Slabs.Add(bare);                           // a bare rectangle elsewhere

        var warnings = DxfToEtabsService.SettleFloorsAcrossSheets(
            [Sheet("S2.07_1_BUILDING 1 - LEVEL 2.dxf", "L2", one), Sheet("S2.15_1_BUILDING 2 - LEVEL 2.dxf", "L2", two)],
            Reach, SqFtPerUnit, legendFrameIsNotAFloor: true);

        Assert.DoesNotContain(frame, one.Slabs);
        Assert.DoesNotContain(copy, two.Slabs);
        Assert.Contains(bare, two.Slabs);
        Assert.Contains(warnings, w => w.Contains("on S2.15_1_BUILDING 2 - LEVEL 2.dxf", StringComparison.Ordinal)
                                       && w.Contains("drawn again on another sheet", StringComparison.Ordinal));
    }
}
