#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A PLATE ON EVERY STOREY is the engineers' bar (plan WP6a, 2026-09-15); PlateCoverage is its instrument. WHAT THIS
/// COVERS: a storey with a FLOOR area is a plate; one with no sheet placed is NoSheetPlaced; one whose placed
/// sheets read no ring is NoRingRead; one whose sheets read rings that became no plate is RingsReadNoPlate; the
/// storeys come out in the .e2k's order; a non-floor area (a wall) is not a plate. WHAT IT DOES NOT: a plate on the
/// wrong storey (counted where the model puts it); why a ring did not close; the corpus totals (corpus-query plates).
/// </summary>
public sealed class APlateOnEveryStoreyIsMeasuredTests
{
    private static CorpusAnalyzer.SheetRow Sheet(int page, string storeys, int slabs) =>
        new(Guid.Empty, "31065-01", page, $"S2.0{page}", "plan", null, null, null, 96, slabs, 10, 5, 100, $"S2.0{page}_1_LEVEL {page} PLAN.dxf", null, true, storeys, null, null);

    [Fact]
    public void EveryStoreyIsClassedByWhereItsPlateWasLost()
    {
        string[] e2k =
        [
            "  STORY \"L4\"  HEIGHT 3000",
            "  STORY \"L3\"  HEIGHT 3000",
            "  STORY \"L2\"  HEIGHT 3000",
            "  STORY \"L1\"  HEIGHT 3000",
            "  STORY \"Base\"  ELEV -3000",
            "  AREA \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"  0  0  0  0",
            "  AREA \"KW1\"  PANEL  4  \"KP5\"  \"KP6\"  \"KP6\"  \"KP5\"  2  2  0  0",
            "  AREAASSIGN  \"KF1\"  \"L4\"  SECTION \"KOR-S12\"  DIAPHRAGM \"D1\"",
            "  AREAASSIGN  \"KW1\"  \"L3\"  SECTION \"KOR-W8\"  PIER  \"KPIER1\"",
        ];
        var sheets = new[]
        {
            Sheet(4, "L4", 1),              // rings read and a plate composed
            Sheet(3, "L3", 3),              // rings read, only a wall on the storey - no plate
            Sheet(2, "L2", 0),              // placed, nothing closed
            // nothing placed on L1
        };
        var storeys = PlateCoverage.Classify(e2k, sheets);
        Assert.Equal(["L4", "L3", "L2", "L1"], storeys.Select(s => s.Name));            // ETABS's Base is not a storey the drawings draw
        Assert.Equal(PlateCoverage.Class.Plate, storeys[0].Class);
        Assert.Equal(1, storeys[0].Plates);
        Assert.Equal(PlateCoverage.Class.RingsReadNoPlate, storeys[1].Class);
        Assert.Equal(3, storeys[1].RingsRead);
        Assert.Equal(PlateCoverage.Class.NoRingRead, storeys[2].Class);
        Assert.Equal(PlateCoverage.Class.NoSheetPlaced, storeys[3].Class);
        // a sheet placed on two storeys counts on both
        var two = PlateCoverage.Classify(e2k, new[] { Sheet(2, "L2,L1", 2) });
        Assert.Equal(PlateCoverage.Class.RingsReadNoPlate, two.Single(s => s.Name == "L1").Class);
    }

    /// <summary>
    /// AN AREA IN A MESSAGE IS SQUARE FEET WHATEVER THE DRAWING COUNTS IN (step 74, 2026-09-15): thirty-three messages
    /// divided by 144 as if every area were square inches, and every PDF-route report (millimetres) stated areas 645
    /// times too small - "closed ring of 72,188 sq ft ... too small for a floor plate". The option carries its unit.
    /// </summary>
    [Fact]
    public void AnAreaInAMessageIsSquareFeetInAnyUnit()
    {
        var inches = new PlanClassificationOptions();
        Assert.Equal("400", inches.SqFt(57600));                                   // 57,600 sq in
        var mm = inches.InUnitOf(1.0 / 25.4);
        Assert.Equal(1.0 / 25.4, mm.UnitInInches, 12);
        Assert.Equal("400", mm.SqFt(57600 * 25.4 * 25.4));                       // the same floor in mm²
        Assert.Equal("112", mm.SqFt(72188 * 144.0, "0"));                        // what 01379's "72,188 sq ft" ring was: 10.4 m²
        Assert.True(mm.SqFt(mm.MinPlateArea) == "400", mm.SqFt(mm.MinPlateArea)); // the converted threshold reads as itself
    }
}
