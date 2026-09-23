using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A BANKED VOCABULARY ROW REACHES THE READER EVEN IF THE DEFAULT WAS USED FIRST (2026-09-23).
///
/// <see cref="DrawingVocabulary"/> is a record, and its patterns used to be cached in FIELDS on it. `with` copies
/// fields as well as properties, so a copy carrying new words carried the old instance's compiled pattern too —
/// built before those words existed. That is precisely how the office's vocabulary is loaded:
/// `DxfToEtabsService.ApplyRules(DrawingVocabulary.Default, banked)` is a `with` over Default, and
/// `PlanSheetNaming.Vocabulary` falls back to Default until something assigns it. So any title parsed before the
/// rules are read warms Default's patterns, and every KorStandards vocabulary row — dxf.level-words,
/// dxf.parkade-words, dxf.floor-nouns, dxf.building-words, dxf.range-words — is then silently ignored. The row
/// loads, the property holds it, and the regex that does the work was built without it. Nothing says a word.
///
/// It was found on 2026-09-23 the way this repo keeps finding things: a test that passed alone and failed beside
/// its neighbours. The neighbour parsed a title with the default first.
///
/// WHAT THIS COVERS: the copy, for the patterns a sheet title is read with — a floor noun, a level word, a parkade
/// word, a building word. WHAT IT DOES NOT: it does not prove any particular production ORDER hits the stale copy
/// (it proves the copy cannot be stale, which is stronger and cheaper to keep true); it says nothing about a
/// vocabulary built from a row that is itself wrong; and a pattern that ignores a property altogether would pass
/// this and still not read the row.
///
/// ⚠ AND ONE MORE OF THE SAME KIND, FOUND AND LEFT ALONE: <c>PlanSheetNaming.StripSheetNumber</c> hard-codes the
/// strings "LEVEL" and "ROOF" rather than reading <c>LevelWords</c> and <c>RoofWords</c>, so another office's word
/// for a storey is not honoured there either. That one is a behaviour change on every set, not a copy fault, so it
/// wants its own gate run and is written down here rather than mended in passing.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AVocabularyCopiedFromAnotherDoesNotInheritItsPatternsTests
{
    [Fact]
    public void AFloorNounAddedToACopyIsReadEvenWhenTheOriginalHasAlreadyParsed()
    {
        // warm the original, exactly as any parse before the rules load does
        Assert.Equal([1], PlanSheetNaming.Parse("p07_1_1st Floor Plan.dxf", DrawingVocabulary.Default).Levels);

        var widened = DrawingVocabulary.Default with { FloorNouns = ["FLOOR", "LEVEL", "STOREY", "STORY", "FLR"] };
        Assert.Equal([2], PlanSheetNaming.Parse("p12_1_2nd Flr. Plan Showing 3rd Flr. Framing Over (West).dxf", widened).Levels);
        // and the original is untouched by its copy
        Assert.Empty(PlanSheetNaming.Parse("p12_1_2nd Flr. Plan Showing 3rd Flr. Framing Over (West).dxf", DrawingVocabulary.Default).Levels);
    }

    [Fact]
    public void ALevelWordAddedToACopyIsReadEvenWhenTheOriginalHasAlreadyParsed()
    {
        Assert.Equal([9], PlanSheetNaming.Parse("S2.09_1_LEVEL 9 PLAN.dxf", DrawingVocabulary.Default).Levels);

        var another = DrawingVocabulary.Default with { LevelWords = ["LEVEL", "L", "ETAGE"] };
        Assert.Equal([4], PlanSheetNaming.Parse("S2.04_1_ETAGE 4 PLAN.dxf", another).Levels);
        Assert.Empty(PlanSheetNaming.Parse("S2.04_1_ETAGE 4 PLAN.dxf", DrawingVocabulary.Default).Levels);
    }

    [Fact]
    public void AParkadeWordAndABuildingWordAddedToACopyAreReadToo()
    {
        Assert.Equal([2], PlanSheetNaming.Parse("S2.02_1_LEVEL P2 PLAN.dxf", DrawingVocabulary.Default).ParkadeLevels);
        Assert.Equal(["A"], PlanSheetNaming.Parse("S2.02_1_LEVEL 2 PLAN - BLDG A.dxf", DrawingVocabulary.Default).BuildingTags);

        var another = DrawingVocabulary.Default with { ParkadeWords = ["P", "B", "SS"], BuildingWords = ["BLDG", "BUILDING", "TOWER"] };
        Assert.Equal([3], PlanSheetNaming.Parse("S2.03_1_LEVEL SS3 PLAN.dxf", another).ParkadeLevels);
        Assert.Equal(["B"], PlanSheetNaming.Parse("S2.02_1_LEVEL 2 PLAN - TOWER B.dxf", another).BuildingTags);
    }

    /// <summary>
    /// The same mistake one level up: Parse(name, vocabulary) read the SHARED static for the mezzanine words
    /// while every line around it read the vocabulary it was handed. 31104's "LEVEL 1 MEZZ AND LEVEL 2 CANOPY" is
    /// the title that needs them, and another office's word for a mezzanine was never consulted on it.
    /// </summary>
    [Fact]
    public void TheMezzanineWordsComeFromTheVocabularyHandedIn()
    {
        var another = DrawingVocabulary.Default with { MezzanineWords = ["ENTRESOL"] };
        var sheet = PlanSheetNaming.Parse("S2.04_1_LEVEL 1 ENTRESOL AND LEVEL 2 CANOPY.dxf", another);
        Assert.True(sheet.IsMezzanine);
        Assert.Equal([1], sheet.MezzanineLevels);
        // this office's own word means nothing to that vocabulary
        Assert.False(PlanSheetNaming.Parse("S2.04_1_LEVEL 1 MEZZ AND LEVEL 2 CANOPY.dxf", another).IsMezzanine);
    }

    /// <summary>
    /// The office's vocabulary, built the way the composer builds it, over a Default that has already been used.
    /// This is the production shape of the fault, in one assertion.
    /// </summary>
    [Fact]
    public void TheOfficeVocabularyBuiltOverAWarmDefaultStillCarriesItsRows()
    {
        _ = PlanSheetNaming.Parse("S2.01_1_LEVEL 1 PLAN.dxf", DrawingVocabulary.Default);
        var rows = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase)
        {
            ["dxf.floor-nouns"] = new RuleSetting("dxf.floor-nouns", double.NaN, RuleSettings.TextUnits, "measured", "KOR", "the test's own row")
            {
                Text = "FLOOR;LEVEL;STOREY;STORY;FLR",
            },
        };
        var office = DxfToEtabsService.ApplyRules(DrawingVocabulary.Default, rows);
        Assert.Contains("FLR", office.FloorNouns);
        Assert.Equal([3], PlanSheetNaming.Parse("p14_1_3rd Flr. Plan Showing Roof Framing Over (West).dxf", office).Levels);
    }
}
