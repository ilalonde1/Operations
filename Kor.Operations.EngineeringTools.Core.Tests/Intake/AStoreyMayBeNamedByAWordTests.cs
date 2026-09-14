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
}
