#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// When two sheets draw the same wall and one says what it is, the one that says wins (intake step
/// 34, composer side). A set that draws a storey several ways — a key plan at 1/8", enlargements at
/// 1/4" — draws its walls several times; the enlargements tag them, the key plan does not, and the
/// composer's duplicate check is an exact endpoint key a wall never satisfies across scales.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a storey with a tagging sheet taking its walls from tagging sheets only; an
/// untagged wall inside (or a hand's width from) a partition another sheet tags being left out; the
/// same wall drawn on two sheets modelled once, the first sheet's copy kept; a storey with no
/// tagging sheet losing nothing to the first clause; walls on different lines untouched; and the
/// warnings saying what was stood down. WHAT IT DOES NOT: the real sets (31170 walls 1,062 → 67,
/// the five KOR sets identical, 2026-09-10); columns and plates, which no clause touches; a wall
/// drawn twice at an angle greater than ten degrees; two concrete walls one drafter drew adjacent on
/// one sheet, which a sheet never dedupes against itself.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]     // PlanSheetNaming.Parse reads the shared vocabulary
public sealed class ASheetThatSaysWhatAWallIsWinsTests
{
    private static WallAxis Wall(double x0, double y0, double x1, double y1) => new(new DxfPoint(x0, y0), new DxfPoint(x1, y1), 8, "KOR_V-WALL");

    private static PlanLoop Box(double x0, double y0, double x1, double y1) =>
        new("KOR_PARTITION", [new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1)], true);

    private static (PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>) Sheet(string name, string storey, PlanGeometrySet g)
        => (PlanSheetNaming.Parse(name), g, [storey]);

    private static PlanGeometrySet Tagging(PlanGeometrySet g)
    {
        for (int i = 0; i < WallTypeTagging.TaggingSheetMinTags; i++)
            g.Tags.Add(new DxfPositionedTag("S8.1", new DxfPoint(i * 10, 0), "KOR_WALLTYPE", "S8.1"));
        return g;
    }

    [Fact]
    public void AStoreyWithATaggingSheetTakesItsWallsFromTheTaggingSheets()
    {
        var keyPlan = new PlanGeometrySet();                                      // 1/8": draws everything, tags nothing
        keyPlan.Walls.Add(Wall(0, 0, 200, 0)); keyPlan.Walls.Add(Wall(0, 100, 200, 100));
        var enlargement = Tagging(new PlanGeometrySet());                         // 1/4": tags its walls, keeps the concrete one
        enlargement.Walls.Add(Wall(0, 0, 200, 0));
        enlargement.Partitions.Add(Box(0, 98, 200, 102));

        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", "L3", keyPlan),
            Sheet("A415_1_LEVEL 3 PLAN (SW).dxf", "L3", enlargement),
        };
        var warnings = DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);

        Assert.Empty(keyPlan.Walls);                                              // the key plan's walls were a reference
        Assert.Single(enlargement.Walls);                                          // the enlargement's concrete wall stands
        Assert.Contains(warnings, w => w.Contains("2 wall(s) on sheet(s) that tag no walls", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUntaggedWallInsideAnotherSheetsPartitionIsThatPartition()
    {
        // neither sheet tags enough to count as a tagging sheet, but one carries partition footprints: one wall INSIDE
        // a footprint, one a hand's width (4, under the reach of 6) beside a footprint, one on a line of its own
        var a = new PlanGeometrySet(); a.Walls.Add(Wall(0, 100, 200, 100)); a.Walls.Add(Wall(0, 500, 200, 500)); a.Walls.Add(Wall(0, 900, 200, 900));
        var b = new PlanGeometrySet(); b.Partitions.Add(Box(0, 98, 200, 102)); b.Partitions.Add(Box(0, 904, 200, 908));
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", "L3", a), Sheet("A415_1_LEVEL 3 PLAN (SW).dxf", "L3", b),
        };
        var warnings = DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        var left = Assert.Single(a.Walls);
        Assert.Equal(500, left.Start.Y);                                          // the wall on another line is untouched; the one within reach is not
        Assert.Contains(warnings, w => w.Contains("2 wall(s) drawn untagged on one sheet lie inside a partition", StringComparison.Ordinal));   // inside, and within reach of (audit F25)
    }

    [Fact]
    public void TheSameWallOnTwoSheetsIsModelledOnceAndTheFirstSheetsCopyStays()
    {
        var first = new PlanGeometrySet(); first.Walls.Add(Wall(0, 0, 200, 0));
        var second = new PlanGeometrySet(); second.Walls.Add(Wall(2, 3, 198, 3));     // the same wall, drawn 3 in off at another scale
        second.Walls.Add(Wall(0, 0, 0, 200));                                          // and one across it, which is not the same wall
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", "L3", first), Sheet("A204_1_LEVEL 3 SLAB PLAN.dxf", "L3", second),
        };
        var warnings = DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Single(first.Walls);
        var kept = Assert.Single(second.Walls);
        Assert.Equal(200, kept.End.Y);                                            // the crossing wall stays
        Assert.Contains(warnings, w => w.Contains("1 wall(s) drawn on a second sheet", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoreyWithNoTaggingSheetAndNoPartitionsLosesNothingToThoseClauses()
    {
        var a = new PlanGeometrySet(); a.Walls.Add(Wall(0, 0, 200, 0));
        var b = new PlanGeometrySet(); b.Walls.Add(Wall(0, 400, 200, 400));
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("S2.20.1_1_LEVEL 3 PLAN.dxf", "L3", a), Sheet("S2.20.1_2_LEVEL 3 PLAN.dxf", "L3", b),
        };
        var warnings = DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Single(a.Walls); Assert.Single(b.Walls);
        Assert.Empty(warnings);
    }
}
