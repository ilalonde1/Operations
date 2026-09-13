#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A view's name is unique within the set (2026-09-12). 31168's 2026-09-10 issue names
/// "S2.01 - LEVEL P3 PLAN FOUNDATIONS PLAN BLDG C" on two pages; the in-memory handoff threw on the
/// duplicate key and the set built nothing in corpus run 5, where the disk handoff before it had
/// silently kept only the second. The second of a name carries its page, and the name it carries
/// still reads as the same storeys — the view's name is the storey reader's input.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the first of a name keeps it; the second carries "(page N)"; a third the same
/// page is still distinct; both spellings parse to the same storeys, building and parkade level.
/// WHAT IT DOES NOT: the two pages' geometry (both views are written; which storey each feeds is
/// the composer's, by the name); a set whose duplicate names are different views of one plan.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AViewsNameIsUniqueWithinTheSetTests
{
    private const string Name = "S2.01_1_LEVEL P3 PLAN FOUNDATIONS PLAN BLDG C -.dxf";

    [Fact]
    public void TheSecondOfANameCarriesItsPage()
    {
        var names = new UniqueViewNames();
        Assert.Equal(Name, names.Claim(Name, 11));
        Assert.Equal("S2.01_1_LEVEL P3 PLAN FOUNDATIONS PLAN BLDG C - (page 12).dxf", names.Claim(Name, 12));
        Assert.Equal("S2.01_1_LEVEL P3 PLAN FOUNDATIONS PLAN BLDG C - (page 12, 2).dxf", names.Claim(Name, 12));
        Assert.Equal("S2.02_1_LEVEL P2 PLAN.dxf", names.Claim("S2.02_1_LEVEL P2 PLAN.dxf", 13));
        Assert.Equal(2, names.Renamed.Count);
    }

    [Fact]
    public void TheCarriedPageIsNotAStorey()
    {
        var names = new UniqueViewNames();
        names.Claim(Name, 11);
        string again = names.Claim(Name, 12);

        var first = PlanSheetNaming.Parse(Name);
        var second = PlanSheetNaming.Parse(again);
        Assert.Equal(first.Levels, second.Levels);
        Assert.Equal(first.ParkadeLevels, second.ParkadeLevels);
        Assert.Equal(first.BuildingTags, second.BuildingTags);
        Assert.Equal(first.IsFoundation, second.IsFoundation);
        Assert.Equal([3], second.ParkadeLevels);
    }
}
