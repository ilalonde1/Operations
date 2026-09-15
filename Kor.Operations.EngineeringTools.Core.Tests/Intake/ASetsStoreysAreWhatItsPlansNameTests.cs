#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A set's storeys are what its plans name; the elevations give their heights when the set has
/// them, and the heights it does not state are assumed and said (intake step 45). Written from the
/// corpus analyzer's first run: 225 of 292 sets read their plans and built no model because the
/// ladder came only from shear-wall elevations, which most of the office's sets do not draw.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: no elevations at all — parkade, numbered and roof plans ordered P2 P1 L1 L2 L3
/// ROOF at the assumed height, every height but the datum marked assumed; elevations stating some
/// storeys — a stated elevation stands, a plan-only storey between two stated ones is spaced evenly
/// between them, one above the top stated storey rises the set's own typical height; a set whose
/// elevations name every storey the plans name — the ladder unchanged (the six banked models are the
/// gate for that); a foundation plan naming no storey; the levels file carrying the assumption in
/// words. WHAT IT DOES NOT: heights from sections or the architect's set; storeys named by a word
/// alone (GROUND, MAIN — step 46); the 292 sets (the analyzer's next run counts them).
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class ASetsStoreysAreWhatItsPlansNameTests
{
    private static SetStoreys.Chain Chain(params (string Name, double ElevationMm)[] levels)
        => new(levels.Select(l => new SetStoreys.Level(l.Name, l.ElevationMm, "stated on S3.01")).ToList(), ["P2"], [], [], [],
               levels.Length >= 2 ? levels[1].ElevationMm - levels[0].ElevationMm : null);

    [Fact]
    public void WithNoElevationsThePlansNameTheStoreysAndEveryHeightIsAssumedAndSaid()
    {
        var ladder = StoreysFromPlans.Merge(null,
            ["S2.01_1_FOUNDATION PLAN.dxf", "S2.02_1_LEVEL P2 PLAN.dxf", "S2.03_1_LEVEL P1 PLAN.dxf", "S2.04_1_LEVEL 1 PLAN.dxf", "S2.05_1_LEVEL 2 PLAN.dxf", "S2.06_1_LEVEL 3 PLAN.dxf", "S2.07_1_ROOF PLAN.dxf"],
            assumedHeightMm: 3000);

        Assert.Equal(["P2", "P1", "L1", "L2", "L3", "ROOF"], ladder.Storeys.Select(s => s.Name));
        Assert.Equal([0.0, 3000, 6000, 9000, 12000, 15000], ladder.Storeys.Select(s => s.ElevationMm));
        Assert.Equal(0, ladder.FromElevations); Assert.Equal(6, ladder.FromPlansOnly); Assert.Equal(5, ladder.Assumed);
        Assert.False(ladder.Storeys[0].Assumed);                                        // the datum is not an assumption
        Assert.All(ladder.Storeys.Skip(1), s => Assert.True(s.Assumed));
        var lines = StoreysFromPlans.LevelsFileLines(ladder);
        Assert.Contains(lines, l => l.StartsWith("# ASSUMED: 5 storey height(s)", StringComparison.Ordinal) && l.Contains("3000 mm", StringComparison.Ordinal));
        Assert.Contains("L2,9000", lines);                                              // P2 P1 L1 L2: the fourth storey
        Assert.Contains("ROOF,15000", lines);
        Assert.Contains("5 height(s) ASSUMED", StoreysFromPlans.Summary(ladder), StringComparison.Ordinal);
    }

    [Fact]
    public void AStatedElevationStandsAndAPlanOnlyStoreyIsSpacedBetweenTheStatedOnes()
    {
        // the elevations state P1, L1 and L3; the plans also name L2 (between) and ROOF (above)
        var chain = Chain(("P1", 0), ("L1", 3500), ("L3", 9700));
        var ladder = StoreysFromPlans.Merge(chain,
            ["S2.03_1_LEVEL P1 PLAN.dxf", "S2.04_1_LEVEL 1 PLAN.dxf", "S2.05_1_LEVEL 2 PLAN.dxf", "S2.06_1_LEVEL 3 PLAN.dxf", "S2.07_1_ROOF PLAN.dxf"],
            assumedHeightMm: 3000);

        Assert.Equal(["P1", "L1", "L2", "L3", "ROOF"], ladder.Storeys.Select(s => s.Name));
        Assert.Equal(3500, ladder.Storeys[1].ElevationMm, 0.5);                         // stated
        Assert.Equal(6600, ladder.Storeys[2].ElevationMm, 0.5);                         // L2: evenly between L1 3500 and L3 9700
        Assert.Equal(9700, ladder.Storeys[3].ElevationMm, 0.5);                         // stated, untouched by the assumption below it
        Assert.Equal(9700 + 3500, ladder.Storeys[4].ElevationMm, 0.5);                  // ROOF: the set's own typical (3,500) over L3, not the assumed 3,000
        Assert.Equal(3, ladder.FromElevations); Assert.Equal(2, ladder.FromPlansOnly); Assert.Equal(2, ladder.Assumed);
        Assert.False(ladder.Storeys[3].Assumed); Assert.True(ladder.Storeys[2].Assumed); Assert.True(ladder.Storeys[4].Assumed);
        Assert.Contains("spaced evenly between L1 and L3", ladder.Storeys[2].From, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevationsNamingEveryStoreyThePlansNameLeaveTheLadderAsItWas()
    {
        var chain = Chain(("P2", 0), ("P1", 2900), ("L1", 6100), ("L2", 9050), ("ROOF", 12000));
        var ladder = StoreysFromPlans.Merge(chain, ["S2.02_1_LEVEL P2 PLAN.dxf", "S2.03_1_LEVEL P1 PLAN.dxf", "S2.04_1_LEVEL 1 PLAN.dxf", "S2.05_1_LEVEL 2 PLAN.dxf", "S2.07_1_ROOF PLAN.dxf"]);

        Assert.Equal(chain.Levels.Select(l => l.Name), ladder.Storeys.Select(s => s.Name));
        Assert.Equal(chain.Levels.Select(l => l.ElevationMm), ladder.Storeys.Select(s => s.ElevationMm));
        Assert.Equal(0, ladder.Assumed);
        Assert.DoesNotContain(StoreysFromPlans.LevelsFileLines(ladder), l => l.StartsWith("# ASSUMED", StringComparison.Ordinal));
        // and a level only the elevations name (a mezzanine) keeps its place among the plans' storeys
        var withMezz = StoreysFromPlans.Merge(Chain(("P1", 0), ("L1", 3000), ("L1M", 5200), ("L2", 7400)), ["S2.03_1_LEVEL P1 PLAN.dxf", "S2.04_1_LEVEL 1 PLAN.dxf", "S2.05_1_LEVEL 2 PLAN.dxf"]);
        Assert.Equal(["P1", "L1", "L1M", "L2"], withMezz.Storeys.Select(s => s.Name));
    }

    [Fact]
    public void AFoundationPlanAloneNamesNoStorey()
    {
        var ladder = StoreysFromPlans.Merge(null, ["S2.01_1_FOUNDATION PLAN.dxf"]);
        Assert.True(ladder.IsEmpty);
        Assert.StartsWith("no storeys", StoreysFromPlans.Summary(ladder), StringComparison.Ordinal);
    }

    [Fact]
    public void PlansBelowTheFirstStatedLevelStepDownFromIt()
    {
        // the elevations state L1 = 0 and L2 = 3,000; the plans also name P1 and P2 beneath them. Walked up from
        // zero they met L1 at zero and the ladder folded - P2 = 0, P1 = 3,000, L1 = 0 (Codex audit 2026-09-13, F3)
        var chain = Chain(("L1", 0), ("L2", 3000));
        var ladder = StoreysFromPlans.Merge(chain,
            ["S2.02_1_LEVEL P2 PLAN.dxf", "S2.03_1_LEVEL P1 PLAN.dxf", "S2.04_1_LEVEL 1 PLAN.dxf", "S2.05_1_LEVEL 2 PLAN.dxf"],
            assumedHeightMm: 2800);
        Assert.Equal(["P2", "P1", "L1", "L2"], ladder.Storeys.Select(s => s.Name));
        // the lowest is the datum, so P2 = 0, P1 = 3,000 (the set's typical), L1 = 6,000, L2 = 9,000: every storey above the last
        var e = ladder.Storeys.Select(s => s.ElevationMm).ToList();
        Assert.True(e[0] < e[1] && e[1] < e[2] && e[2] < e[3], string.Join(" ", e));
        Assert.Equal(3000, e[3] - e[2], 0.5);                                        // L2 over L1 as stated
        Assert.Equal(e[2] - e[1], e[1] - e[0], 0.5);                                 // the two assumed steps are equal
        Assert.True(ladder.Storeys[0].Assumed && ladder.Storeys[1].Assumed && !ladder.Storeys[2].Assumed);
        Assert.Contains("below L1", ladder.Storeys[1].From, StringComparison.Ordinal);
    }

    /// <summary>
    /// ONE PLAN NAMING NO STOREY IS A ONE-STOREY BUILDING (step 66, 2026-09-14): the small jobs draw the whole
    /// structure on one plan titled PLAN, PLANS, PLAN AND DETAILS, GENERAL NOTES AND PLAN - nine of run 11's
    /// 21 "no storeys" sets - and that plan is L1. Two unnamed plans stay unnamed (a foundation plan and a
    /// framing plan of one storey are not two storeys). WHAT THIS DOES NOT COVER: putting the plan's members
    /// on L1 (the composer's matching, measured on the nine sets).
    /// </summary>
    [Theory]
    [InlineData("S1.01_1_GENERAL NOTES AND PLAN.dxf")]
    [InlineData("S2.01_1_PLANS.dxf")]
    [InlineData("S1.02_1_PLAN AND DETAILS.dxf")]
    public void OnePlanNamingNoStoreyIsAOneStoreyBuilding(string plan)
    {
        var ladder = StoreysFromPlans.Merge(null, [plan], assumedHeightMm: 3000);
        Assert.Equal(["L1"], ladder.Storeys.Select(s => s.Name));
        Assert.Equal(1, ladder.FromPlansOnly);
        Assert.True(StoreysFromPlans.Merge(null, ["S2.01_1_FOUNDATION PLAN.dxf", "S2.02_1_DECK FRAMING PLAN.dxf"], assumedHeightMm: 3000).IsEmpty);
    }

    /// <summary>
    /// NUMBERED BUILDINGS ON ONE PLAN-NAMED LADDER SHARE ITS STOREYS AND ITS ROOF (step 69, 2026-09-14). 31185 draws
    /// five buildings - BUILDING 1 .. BUILDING 5 - each with a LEVEL 1, a LEVEL 2 and a roof plan; their storeys are
    /// L1, L2, ROOF, not five roofs stacked five storeys high (which the first cut of numbered tags produced: 8
    /// storeys, P1 L1 L2 5-ROOF 4-ROOF 3-ROOF 2-ROOF 1-ROOF). One building's tagged roof over shared storeys keeps
    /// its name (B6: C-ROOF, C-ELEVATOR ROOF). WHAT THIS DOES NOT COVER: buildings with their own storey names
    /// (31168's towers - the elevations name them; the roof rule of step 61 stands there).
    /// </summary>
    [Fact]
    public void NumberedBuildingsOnOnePlanNamedLadderShareItsStoreysAndItsRoof()
    {
        var plans = new List<string>();
        for (int b = 1; b <= 5; b++)
        {
            plans.Add($"S2.06_{b}_BUILDING {b} LEVEL 1 SHOWING LEVEL 2 FRAMING OVER.dxf");
            plans.Add($"S2.07_{b}_BUILDING {b} LEVEL 2 SHOWING ROOF OVER.dxf");
            plans.Add($"S2.08_{b}_BUILDING {b} ROOF PLAN.dxf");
        }
        var ladder = StoreysFromPlans.Merge(null, plans, assumedHeightMm: 3000);
        Assert.Equal(["L1", "L2", "ROOF"], ladder.Storeys.Select(s => s.Name));
        // and every building's sheet lands on the shared storeys (the composer's half: MatchStories)
        var sheet = PlanSheetNaming.Parse("S2.06_3_BUILDING 3 LEVEL 1 SHOWING LEVEL 2 FRAMING OVER.dxf");
        Assert.Equal(["3"], sheet.BuildingTags);
        Assert.Equal(["L1"], PlanSheetNaming.MatchStories(sheet, ["L1", "L2", "ROOF"]));
        Assert.Equal(["ROOF"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2.08_3_BUILDING 3 ROOF PLAN.dxf"), ["L1", "L2", "ROOF"]));
    }

    /// <summary>
    /// NUMBERED BLOCKS NAMING THEIR FLOORS BY WORDS HAVE ONE ORDER (step 70, 2026-09-14 night, run 14): 30988-01's
    /// townhouse blocks 5, 11, 12, 22, 23, 24 draw GROUND / MAIN / UPPER floors with framing-over clauses, "- BLDG n"
    /// on most titles; block 22's GROUND plan is on an untagged sheet. Run 13 (no tags read) built L1 L2 L3; run 14
    /// (tags read, step 68) built L1 L2 - the per-building chains "disagreed" and the row stood. WHAT THIS COVERS:
    /// the ladder and every block's sheets on the three storeys. WHAT IT DOES NOT: the foundation plans' storey
    /// (they go to the lowest storey, as before); a block whose words genuinely differ.
    /// </summary>
    [Fact]
    public void NumberedBlocksNamingTheirFloorsByWordsHaveOneOrder()
    {
        string[] plans =
        [
            "S2.01_1_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER - BLDG 5.dxf", "S2.01_2_FOUNDATION PLAN - BLDG 5.dxf",
            "S2.02_1_MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER - BLDG 5.dxf", "S2.02_2_UPPER FLOOR SHOWING ROOF FRAMING OVER - BLDG 5.dxf",
            "S2.07_1_FOUNDATION PLAN.dxf", "S2.07_2_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER.dxf",
            "S2.08_1_MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER - BLDG 22.dxf", "S2.08_2_UPPER FLOOR SHOWING ROOF FRAMING OVER - BLDG 22.dxf",
        ];
        var ladder = StoreysFromPlans.Merge(null, plans, assumedHeightMm: 3000);
        Assert.Equal(["L1", "L2", "L3"], ladder.Storeys.Select(s => s.Name));
        var vocabulary = DrawingVocabulary.Default.WithFloorWordsRankedBy(plans.Select(PlanSheetNaming.TitleOf));
        var set = plans.Select(n => PlanSheetNaming.Parse(n, vocabulary)).ToList();
        string[] names = ["L1", "L2", "L3"];
        Assert.Equal(["L1"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse(plans[0], vocabulary), names, set));
        Assert.Equal(["L2"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse(plans[2], vocabulary), names, set));
        Assert.Equal(["L3"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse(plans[3], vocabulary), names, set));
        Assert.Equal(["L2"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse(plans[6], vocabulary), names, set));   // block 22's MAIN is the set's MAIN
    }
}
