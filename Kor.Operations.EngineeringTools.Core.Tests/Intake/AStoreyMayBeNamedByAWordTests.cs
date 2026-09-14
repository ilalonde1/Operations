#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A storey may be named by a word (intake step 47). KOR's small jobs name their plans "MAIN FLOOR
/// PLAN SHOWING 2ND FLOOR FRAMING OVER", "UPPER FLOOR PLAN", "BASEMENT FLOOR PLAN SHOWING MAIN
/// FLOOR FRAMING OVER", "LOFT PLAN SHOWING ROOF FRAMING OVER" — no LEVEL, no number — and 68 of the
/// corpus's 86 sets without a model (383 plans, 2026-09-13) were named so and read no storey. And
/// a small job's title carries its own sheet number, "S-6 - MAIN FLOOR PLAN", in a form the page
/// reader does not take for one, so the view fell back to the stem and page and lost the title.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a floor word (MAIN, GROUND, UPPER) and an ordinal (2ND, SECOND) name a level;
/// BASEMENT and LOWER name the parkade level under the main floor; the framing-over clause is not the
/// plan's own storey; a loft ranks above the highest numbered plan in its set; a title that begins
/// with a hyphenated sheet number names the view by that number and the rest; a title with no
/// number at all still names the view. WHAT IT DOES NOT: an office whose MAIN is not level 1 (the
/// row `dxf.floor-words` says what a word means); two lofts; a plan named by a word in another
/// language; a title the page reader did not read at all (186 of the 383 plans).
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AStoreyMayBeNamedByAWordTests
{
    [Theory]
    [InlineData("S-7_1_MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER.dxf", 1)]
    [InlineData("S-8_1_2ND FLOOR PLAN SHOWING LOFT FRAMING OVER.dxf", 2)]
    [InlineData("S-9_1_HOTEL - THIRD FLOOR PLAN SHOWING FOURTH FLOOR FRAMING OVER.dxf", 3)]
    [InlineData("S-8_1_UPPER FLOOR PLAN SHOWING ROOF FRAMING OVER.dxf", 2)]
    [InlineData("S1.02_1_MAIN LEVEL PLAN.dxf", 1)]
    [InlineData("S-13_1_7TH FLOOR PLAN.dxf", 7)]
    public void AFloorWordOrAnOrdinalNamesTheLevelBeforeTheFramingOverClause(string file, int level)
    {
        var sheet = PlanSheetNaming.Parse(file);
        Assert.Equal([level], sheet.Levels);
        Assert.Empty(sheet.ParkadeLevels);
        Assert.False(sheet.IsTopFloor);
    }

    [Fact]
    public void ABasementIsTheParkadeUnderTheMainFloorAndALoftIsTheTopFloor()
    {
        var basement = PlanSheetNaming.Parse("S-6_1_BASEMENT FLOOR PLAN SHOWING MAIN FLOOR FRAMING OVER.dxf");
        Assert.Empty(basement.Levels);
        Assert.Equal([1], basement.ParkadeLevels);
        var loft = PlanSheetNaming.Parse("S-9_1_LOFT PLAN SHOWING ROOF FRAMING OVER.dxf");
        Assert.Empty(loft.Levels);
        Assert.True(loft.IsTopFloor);
        Assert.False(loft.IsRoof);                                          // the roof is the framing over, not this plan
        // a numbered level beside a word: the number is the level and the word is left alone
        Assert.Equal([2], PlanSheetNaming.Parse("S-8_1_LEVEL 2 PLAN (MAIN FLOOR).dxf").Levels);
    }

    [Fact]
    public void TheLadderRanksTheWordsAsTheirNumbersAndTheLoftAboveTheHighest()
    {
        var ladder = StoreysFromPlans.Merge(null,
            ["S-6_1_FOUNDATION PLAN.dxf", "S-7_1_MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER.dxf",
             "S-8_1_2ND FLOOR PLAN SHOWING LOFT FRAMING OVER.dxf", "S-9_1_LOFT PLAN SHOWING ROOF FRAMING OVER.dxf"],
            assumedHeightMm: 3000);
        Assert.Equal(["L1", "L2", "L3"], ladder.Storeys.Select(s => s.Name));
        var withBasement = StoreysFromPlans.Merge(null,
            ["S-6_1_BASEMENT FLOOR PLAN SHOWING MAIN FLOOR FRAMING OVER.dxf", "S-7_1_MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER.dxf",
             "S-8_1_UPPER FLOOR PLAN SHOWING ROOF FRAMING OVER.dxf"],
            assumedHeightMm: 3000);
        Assert.Equal(["P1", "L1", "L2"], withBasement.Storeys.Select(s => s.Name));
    }

    [Fact]
    public void ASmallJobsTitleCarriesItsOwnNumberAndATitleWithNoneStillNamesTheView()
    {
        var none = new Dictionary<string, string>();
        Assert.Equal("S-7_1_MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER.dxf",
            SheetDxfName.For(null, none, "01389-p07", null, "S-7 - MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER"));
        Assert.Equal("S-5_1_FOUNDATION PLAN.dxf", SheetDxfName.For(null, none, "01375-p08", null, "S-5FOUNDATION PLAN"));
        // no number anywhere: the stem stands where the number would, and the title is kept
        Assert.Equal("01389-p10_1_GARAGE PLANS.dxf", SheetDxfName.For(null, none, "01389-p10", null, "GARAGE PLANS"));
        // and the page reader's own number still wins over the title's
        Assert.Equal("S2.20.1_1_LEVEL 4 PLAN.dxf", SheetDxfName.For("S2.20.1", none, "x-p03", null, "S2.20.1 - LEVEL 4 PLAN"));
    }

    /// <summary>
    /// Step 60 (2026-09-13): the row says MAIN and GROUND are both level 1, and 31089-01's townhouses draw
    /// "GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER", "UPPER
    /// FLOOR SHOWING ROOF FRAMING OVER" - three floors whose order the framing-over clauses state. The set's
    /// chain ranks its words; the ladder and the composer read the same names the same way. WHAT THIS COVERS:
    /// the chain of three; a set that uses MAIN alone keeps the row's 1; a chain anchored by a number above it;
    /// two stories about one word leave the row alone. WHAT IT DOES NOT: a basement in the chain (a basement
    /// word is a parkade level, ranked below by its own rule); two buildings with different chains in one set.
    /// </summary>
    [Fact]
    public void TheFramingOverClausesRankTheSetsFloorWords()
    {
        var voc = DrawingVocabulary.Default;
        string[] townhouse =
        {
            "S2.01_1_FOUNDATION PLAN", "S2.01_2_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER",
            "S2.02_1_MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER", "S2.02_2_UPPER FLOOR SHOWING ROOF FRAMING OVER",
        };
        var ranked = voc.WithFloorWordsRankedBy(townhouse.Select(PlanSheetNaming.TitleOf));
        Assert.Equal(1, ranked.LevelOfWord("GROUND"));
        Assert.Equal(2, ranked.LevelOfWord("MAIN"));
        Assert.Equal(3, ranked.LevelOfWord("UPPER"));
        Assert.Equal([2], PlanSheetNaming.Parse(townhouse[2] + ".dxf", ranked).Levels);
        var ladder = StoreysFromPlans.Merge(null, townhouse.Select(t => t + ".dxf"), 3000);
        Assert.Equal(["L1", "L2", "L3"], ladder.Storeys.Select(s => s.Name));

        // MAIN alone, showing a numbered floor over it: the row's 1 stands, anchored by the 2
        var house = voc.WithFloorWordsRankedBy(["MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER", "FOUNDATION PLAN"]);
        Assert.Equal(1, house.LevelOfWord("MAIN"));
        Assert.Equal(1, house.LevelOfWord("GROUND"));
        // no framing-over clause at all: nothing said, the row stands
        Assert.Same(voc, voc.WithFloorWordsRankedBy(["MAIN FLOOR PLAN", "UPPER FLOOR PLAN"]));
        // two stories about one floor: the row stands
        Assert.Same(voc, voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", "GROUND FLOOR SHOWING UPPER FLOOR FRAMING OVER"]));
    }

    /// <summary>
    /// The second audit's brief B (2026-09-14), findings 1, 4, 5 and 9 on the words. WHAT THIS COVERS: the numbers
    /// are read from the part before the framing-over clause ("MAIN FLOOR PLAN SHOWING LEVEL 2 FRAMING OVER" is the
    /// main floor's, level 1, as it is with "2ND FLOOR" in the clause); "LEVEL 4" over a word anchors the chain as
    /// "4TH FLOOR" does; two buildings' chains are two chains and must agree; a cycle with a tail and a floor shown
    /// over itself leave the row alone; a dotted name without .dxf keeps its title. WHAT IT DOES NOT: a set whose
    /// buildings genuinely order their floors differently (the row stands for both - stated).
    /// </summary>
    [Fact]
    public void TheNumbersAreReadBeforeTheClauseAndTheChainsAreOnePerBuilding()
    {
        var voc = DrawingVocabulary.Default;
        Assert.Equal([1], PlanSheetNaming.Parse("S1_1_MAIN FLOOR PLAN SHOWING LEVEL 2 FRAMING OVER.dxf", voc).Levels);
        Assert.Equal([1], PlanSheetNaming.Parse("S1_1_MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER.dxf", voc).Levels);
        Assert.Equal([1], PlanSheetNaming.Parse("S1_1_LEVEL 1 PLAN SHOWING LEVEL 2 FRAMING OVER.dxf", voc).Levels);
        Assert.Equal([1], PlanSheetNaming.Parse("S2.01_1_MAIN FLOOR PLAN", voc).Levels);                     // B9: no .dxf, a dot in the number

        // "LEVEL 4" over the top word anchors the chain: GROUND 2, MAIN 3
        var anchored = voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", "MAIN FLOOR SHOWING LEVEL 4 FRAMING OVER"]);
        Assert.Equal(2, anchored.LevelOfWord("GROUND"));
        Assert.Equal(3, anchored.LevelOfWord("MAIN"));

        // two buildings, two chains: A says GROUND -> MAIN, B says MAIN -> UPPER; they disagree on MAIN, the row stands
        Assert.Same(voc, voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER BLDG A", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER BLDG B"]));
        // two buildings whose chains agree rank alike
        var agree = voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER BLDG A", "GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER BLDG B", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER BLDG B"]);
        Assert.Equal(2, agree.LevelOfWord("MAIN"));
        // a cycle with a tail, and a floor shown over itself beside a chain: two stories, the row stands (B5)
        Assert.Same(voc, voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER", "UPPER FLOOR SHOWING MAIN FLOOR FRAMING OVER"]));
        Assert.Same(voc, voc.WithFloorWordsRankedBy(["GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER", "MAIN FLOOR SHOWING MAIN FLOOR FRAMING OVER"]));
    }

    /// <summary>
    /// Brief B's findings 3 and 6 on a building's roof. WHAT THIS COVERS: a roof plan tagged for a building none
    /// of whose storeys the model names goes NOWHERE, not onto another building; a building's elevator roof plan
    /// names a storey above its roof, not the same one. WHAT IT DOES NOT: a set with one untagged roof plan.
    /// </summary>
    [Fact]
    public void ABuildingsRoofGoesNowhereWithoutItsBuildingAndItsElevatorRoofIsAboveIt()
    {
        Assert.Empty(PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2_1_ROOF PLAN BLDG C.dxf"), ["B-L39", "B-L40"], null));
        var ladder = StoreysFromPlans.Merge(null, ["S2_1_LEVEL 1 PLAN BLDG C.dxf", "S2_2_ROOF PLAN BLDG C.dxf", "S2_3_ELEVATOR ROOF PLAN BLDG C.dxf"], 3000);
        Assert.Equal(["L1", "C-ROOF", "C-ELEVATOR ROOF"], ladder.Storeys.Select(s => s.Name));
        var roof = PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2_2_ROOF PLAN BLDG C.dxf"), ["C-ELEVATOR ROOF", "C-ROOF", "L1"], null);
        var elevator = PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2_3_ELEVATOR ROOF PLAN BLDG C.dxf"), ["C-ELEVATOR ROOF", "C-ROOF", "L1"], null);
        Assert.Equal(["C-ROOF"], roof);
        Assert.Equal(["C-ELEVATOR ROOF"], elevator);
    }
}
