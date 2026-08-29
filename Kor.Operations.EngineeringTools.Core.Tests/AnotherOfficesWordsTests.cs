using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A firm that does not use KOR's words still gets its building read.
///
/// Layer names and every threshold have been rules for months. What a drawing is CALLED was not:
/// seven regexes in PlanSheetNaming encoded one office's titles, and the same words sat as string
/// literals in four other files. That is the part a practice actually differs on, and getting it
/// wrong is silent — a sheet that matches no storey is a floor that is simply not in the model.
/// PlanSheetNaming's own comment records it happening: "the whole parkade went missing for want of
/// a prefix."
///
/// So this test writes a drawing set in somebody else's language — FLOOR instead of LEVEL,
/// BUILDING instead of BLDG, B for below grade instead of P — and asks for the same answers.
/// Before the vocabulary became data, it could not have been written.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]
public class AnotherOfficesWordsTests : IDisposable
{
    private readonly ITestOutputHelper _out;

    public AnotherOfficesWordsTests(ITestOutputHelper output) => _out = output;

    // Every test in this class swaps a static, so it is put back whatever happens.
    public void Dispose() => PlanSheetNaming.Vocabulary = DrawingVocabulary.Default;

    private static readonly DrawingVocabulary AnotherOffice = new()
    {
        LevelWords = new[] { "FLOOR", "FL" },
        BuildingWords = new[] { "BUILDING", "BLK" },
        ParkadeWords = new[] { "B" },                 // B1, B2 below grade
        RangeWords = new[] { "-", "TO" },
        RoofWords = new[] { "ROOF DECK", "ROOF" },
        MezzanineWords = new[] { "MEZZANINE" },
        FoundationWords = new[] { "FOOTING", "FOUNDATION" },
        ElevatorRoofWords = new[] { "LIFT OVERRUN" },
    };

    [Fact]
    public void KorsOwnWordsStillRead()
    {
        var sheet = PlanSheetNaming.Parse(@"C:\x\--Structural Plan - S2.40.1_1_LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C.dxf");

        Assert.Equal("C", sheet.BuildingTag);
        Assert.Contains(3, sheet.Levels);
    }

    [Fact]
    public void AnotherOfficesWordsReadTheSameBuildingAndFloor()
    {
        PlanSheetNaming.Vocabulary = AnotherOffice;

        var sheet = PlanSheetNaming.Parse(@"C:\x\S2.40 - FLOOR 3 PLAN - CONCRETE OUTLINE - BUILDING C.dxf");

        _out.WriteLine($"building={sheet.BuildingTag} levels=[{string.Join(",", sheet.Levels)}]");

        Assert.Equal("C", sheet.BuildingTag);
        Assert.Contains(3, sheet.Levels);
    }

    [Fact]
    public void AFloorRangeInAnotherOfficesWords()
    {
        PlanSheetNaming.Vocabulary = AnotherOffice;

        var sheet = PlanSheetNaming.Parse(@"C:\x\FLOOR 4 TO 8 PLAN - BUILDING A.dxf");

        _out.WriteLine($"levels=[{string.Join(",", sheet.Levels)}]");

        Assert.Equal(new[] { 4, 5, 6, 7, 8 }, sheet.Levels);
        Assert.Equal("A", sheet.BuildingTag);
    }

    /// <summary>
    /// The one that cost a whole parkade. A firm numbering below grade B1/B2 gets them read;
    /// with KOR's vocabulary the same names are nothing at all.
    /// </summary>
    [Fact]
    public void BelowGradeStoreysInAnotherOfficesNumbering()
    {
        PlanSheetNaming.Vocabulary = AnotherOffice;

        var sheet = PlanSheetNaming.Parse(@"C:\x\FLOOR B2 PLAN - CONCRETE OUTLINE.dxf");

        _out.WriteLine($"parkade=[{string.Join(",", sheet.ParkadeLevels)}]");

        Assert.Contains(2, sheet.ParkadeLevels);
    }

    [Fact]
    public void RoofMezzanineAndFoundationAreWhateverTheOfficeCallsThem()
    {
        PlanSheetNaming.Vocabulary = AnotherOffice;

        Assert.True(PlanSheetNaming.Parse(@"C:\x\ROOF DECK PLAN.dxf").IsRoof);
        Assert.True(PlanSheetNaming.Parse(@"C:\x\FLOOR 1 MEZZANINE PLAN.dxf").IsMezzanine);
        Assert.True(PlanSheetNaming.Parse(@"C:\x\FLOOR B3 FOOTING PLAN.dxf").IsFoundation);
        Assert.True(PlanSheetNaming.Parse(@"C:\x\LIFT OVERRUN PLAN.dxf").IsElevatorRoof);

        // And KOR's words are not magic to this office: MEZZ alone is not a mezzanine here.
        Assert.False(PlanSheetNaming.Parse(@"C:\x\FLOOR 1 MEZZ PLAN.dxf").IsMezzanine);
    }

    /// <summary>
    /// The longest word wins. With LEVEL and L both meaning a storey, "LEVEL 3" must read as
    /// level 3 and not as L followed by "EVEL 3" — an ordering bug that would only show up on a
    /// vocabulary where one word is a prefix of another, which is every one of them.
    /// </summary>
    [Fact]
    public void ALongerWordIsPreferredToAShorterOneItStartsWith()
    {
        var sheet = PlanSheetNaming.Parse(@"C:\x\LEVEL 12 PLAN.dxf");
        Assert.Equal(new[] { 12 }, sheet.Levels);

        PlanSheetNaming.Vocabulary = AnotherOffice;
        Assert.Equal(new[] { 12 }, PlanSheetNaming.Parse(@"C:\x\FLOOR 12 PLAN.dxf").Levels);
    }
}
