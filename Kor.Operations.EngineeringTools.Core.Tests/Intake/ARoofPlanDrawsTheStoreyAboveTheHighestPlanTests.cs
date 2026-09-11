#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A roof plan draws the storey above the highest storey the set's numbered plans draw (intake
/// step 36). "Roof = the topmost storey" held on KOR's sets because their ladders end at the roof;
/// the architect's set for 31170 draws plans P1, 1–6 and ROOF while its sections state L1–L8 (L8
/// the elevator overrun), and the roof landed on L8 with L7 left without a floor.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a roof plan going to the storey above the set's highest numbered plan when the
/// ladder runs higher; the topmost storey when the ladder ends where the plans do (KOR's shape,
/// unchanged); a storey NAMED roof winning over both; an elevator roof taking the storey above the
/// roof's; a roof plan tagged for one building counting only that building's plans; no set given
/// (a caller without the sheets) behaving as before. WHAT IT DOES NOT: the real sets (31170 ROOF L8
/// → L7, five KOR sets identical, 2026-09-10); a roof plan that carries its own level number, which
/// matches by number and never reaches this rule; an "UPPER ROOF" (not in the vocabulary as an
/// elevator roof); a set whose highest numbered plan is itself mistitled.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class ARoofPlanDrawsTheStoreyAboveTheHighestPlanTests
{
    private static PlanSheetInfo Sheet(string name) => PlanSheetNaming.Parse(name);

    private static readonly PlanSheetInfo[] ArchitectsSet =
    [
        Sheet("A101_1_LEVEL P1 PLAN.dxf"), Sheet("A102_1_LEVEL 1 PLAN.dxf"), Sheet("A103_1_LEVEL 2 PLAN.dxf"),
        Sheet("A107_1_LEVEL 6 PLAN.dxf"), Sheet("A108_1_ROOF PLAN.dxf"), Sheet("A432_1_ROOF PLAN (NE).dxf"),
    ];

    [Fact]
    public void ARoofPlanIsTheStoreyAboveTheHighestNumberedPlan()
    {
        var stories = new[] { "L8", "L7", "L6", "L5", "L4", "L3", "L2", "L1", "P1" };     // the ladder: L8 is the overrun
        Assert.Equal(["L7"], PlanSheetNaming.MatchStories(Sheet("A108_1_ROOF PLAN.dxf"), stories, ArchitectsSet));
        Assert.Equal(["L7"], PlanSheetNaming.MatchStories(Sheet("A432_1_ROOF PLAN (NE).dxf"), stories, ArchitectsSet));
    }

    [Fact]
    public void WhereTheLadderEndsWithThePlansTheRoofIsStillTheTopmostStorey()
    {
        var set = new[] { Sheet("S2.08.1_1_LEVEL 12 PLAN.dxf"), Sheet("S2.09.1_1_LEVEL 13 PLAN -CONCRETE OUTLINE.dxf"), Sheet("S2.10.1_1_ROOF PLAN -CONCRETE OUTLINE.dxf") };
        var stories = new[] { "L13", "L12", "L11" };
        Assert.Equal(["L13"], PlanSheetNaming.MatchStories(Sheet("S2.10.1_1_ROOF PLAN -CONCRETE OUTLINE.dxf"), stories, set));
        Assert.Equal(["L13"], PlanSheetNaming.MatchStories(Sheet("S2.10.1_1_ROOF PLAN -CONCRETE OUTLINE.dxf"), stories));   // no set: as before
    }

    [Fact]
    public void AStoreyNamedRoofWinsOverTheCount()
    {
        var stories = new[] { "ELV", "Roof", "L6", "L5" };
        Assert.Equal(["Roof"], PlanSheetNaming.MatchStories(Sheet("A108_1_ROOF PLAN.dxf"), stories, ArchitectsSet));
    }

    [Fact]
    public void AnElevatorRoofTakesTheStoreyAboveTheRoofs()
    {
        var set = new[] { Sheet("S2.10_1_LEVEL 19 PLAN.dxf"), Sheet("S2.11.1_1_ELEVATOR ROOF PLAN.dxf"), Sheet("S2.18.1_2_ROOF PLAN CONCRETE OUTLINE ST.dxf") };
        var stories = new[] { "L21", "L20", "L19", "L18" };
        Assert.Equal(["L20"], PlanSheetNaming.MatchStories(Sheet("S2.18.1_2_ROOF PLAN CONCRETE OUTLINE ST.dxf"), stories, set));
        Assert.Equal(["L21"], PlanSheetNaming.MatchStories(Sheet("S2.11.1_1_ELEVATOR ROOF PLAN.dxf"), stories, set));

        var shorter = new[] { "L20", "L19", "L18" };                                       // no L21: the elevator roof shares L20
        Assert.Equal(["L20"], PlanSheetNaming.MatchStories(Sheet("S2.11.1_1_ELEVATOR ROOF PLAN.dxf"), shorter, set));
    }

    [Fact]
    public void ARoofPlanForOneBuildingCountsOnlyThatBuildingsPlans()
    {
        var set = new[]
        {
            Sheet("S2.30.1_1_LEVEL 38 PLAN CONCRETE OUTLINE BLDG B.dxf"),
            Sheet("S2.40.1_1_LEVEL 8 PLAN CONCRETE OUTLINE BLDG C.dxf"),
            Sheet("S2.41.1_3_ROOF PLAN CONCRETE OUTLINE BLDG C.dxf"),
        };
        var stories = new[] { "B-L40", "B-L39", "B-L38", "C-L10", "C-L9", "C-L8" };
        Assert.Equal(["C-L9"], PlanSheetNaming.MatchStories(Sheet("S2.41.1_3_ROOF PLAN CONCRETE OUTLINE BLDG C.dxf"), stories, set));
    }
}
