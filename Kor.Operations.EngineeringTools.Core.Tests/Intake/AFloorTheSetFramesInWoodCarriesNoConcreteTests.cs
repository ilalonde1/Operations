#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A FLOOR THE SET FRAMES IN WOOD CARRIES NO CONCRETE PLATE (intake step 184, 2026-10-26). 21 of the corpus gate's 57
/// sets draw "LEVEL N SHOWING LEVEL N+1 FRAMING OVER" sheets, and the models built from them plated the wood storeys in
/// concrete where 19 of the 21 engineers' models stop at the concrete. <see cref="WoodFramedFloors"/> names the sheets
/// whose own floor the set draws as the framing over the level below. These are the corpus's own titles.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: an untagged set (31162) - every framed storey's sheet named, the lowest framing-over sheet and the
/// concrete plan kept; a set framed per building (31066) - one building's framing never frames another's floor; a clause
/// naming no storey (31082) frames nothing; floor words ranked by the set (GROUND, MAIN, UPPER). WHAT IT DOES NOT: the
/// cut itself in DxfToEtabsService (the six-set and 21-set replays measure that); a wood plan titled without a framing-
/// over clause; a storey half wood and half concrete on one sheet. A same-class fault it would not catch: a set that calls
/// a concrete transfer slab a level's "framing over" loses that floor.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AFloorTheSetFramesInWoodCarriesNoConcreteTests
{
    private static IReadOnlyList<WoodFramedFloors.Framed> Framed(params string[] files)
    {
        var voc = PlanSheetNaming.Vocabulary.WithFloorWordsRankedBy(files.Select(PlanSheetNaming.TitleOf));
        return WoodFramedFloors.SheetsOf(files.ToDictionary(f => f, f => PlanSheetNaming.Parse(f, voc)), voc);
    }

    [Fact]
    public void AFloorFramedByAFramedFloorIsWoodAndTheChainsFirstLinkIsKept()
    {
        // 31162-01's plans, as the sheet reader writes them. L1's sheet frames L2 - the concrete podium the engineer plates
        // at 3,976 sq ft - so L2 (S2.10) keeps its plate; L2's sheet is itself framed, so L3 (S2.11) is wood
        var framed = Framed(
            "S2.05.1_1_LEVEL 1 PLAN.dxf",
            "S2.06_1_LEVEL 2 PLAN.dxf",
            "S2.08_1_LEVEL 1 SHOWING LEVEL 2 FRAMING OVER.dxf",
            "S2.10_1_LEVEL 2 SHOWING LEVEL 3 FRAMING OVER.dxf",
            "S2.11_1_LEVEL 3 SHOWING LEVEL 4 FRAMING OVER.dxf",
            "S2.14_1_LEVEL 6 SHOWING ROOF FRAMING OVER.dxf");

        var only = Assert.Single(framed);
        Assert.Equal("S2.11_1_LEVEL 3 SHOWING LEVEL 4 FRAMING OVER.dxf", only.File);
        Assert.Equal("L3", only.OwnStorey);
        Assert.Equal("S2.10_1_LEVEL 2 SHOWING LEVEL 3 FRAMING OVER", only.FramedBy);
        // L6 is framed by nothing given here, so S2.14 keeps its plate - the rule reads only what the set draws
    }

    [Fact]
    public void OneBuildingsFramingNeverFramesAnothersFloor()
    {
        // 31066-01: building 1's chain starts at L2, building 2's at L1. Were building 2's "LEVEL 2 FRAMING OVER" read as
        // building 1's, building 1's L2 sheet would be framed, and its L3 (S2.08) taken as wood one link too low
        var framed = Framed(
            "S2.07_1_BUILDING 1 - LEVEL 2 SHOWING LEVEL 3 FRAMING OVER.dxf",
            "S2.08_1_BUILDING 1 - LEVEL 3 SHOWING LEVEL 4 FRAMING OVER.dxf",
            "S2.09_1_BUILDING 1 - LEVEL 4 SHOWING LEVEL 5 FRAMING OVER.dxf",
            "S2.14_1_BUILDING 2 - LEVEL 1 SHOWING LEVEL 2 FRAMING OVER.dxf",
            "S2.15_1_BUILDING 2 - LEVEL 2 SHOWING LEVEL 3 FRAMING OVER.dxf",
            "S2.16_1_BUILDING 2 - LEVEL 3 SHOWING LEVEL 4 FRAMING OVER.dxf");

        Assert.Equal(new[] { "S2.09_1_BUILDING 1 - LEVEL 4 SHOWING LEVEL 5 FRAMING OVER.dxf", "S2.16_1_BUILDING 2 - LEVEL 3 SHOWING LEVEL 4 FRAMING OVER.dxf" },
            framed.Select(f => f.File).ToArray());
    }

    [Fact]
    public void AClauseThatNamesNoStoreyFramesNothing()
    {
        // 31082-01's "LEVEL FRAMING OVER": the drafter left the number out, and a guess would drop a floor
        var framed = Framed(
            "S2.07_1_LEVEL 2 PLAN SHOWING LEVEL 3 FRAMING OVER.dxf",
            "S2.08_1_LEVEL 3 PLAN SHOWING LEVEL 4 FRAMING OVER.dxf",
            "S2.09_1_LEVEL 4 PLAN SHOWING LEVEL FRAMING OVER.dxf",
            "S2.10_1_LEVEL 5 PLAN SHOWING ROOF FRAMING OVER.dxf");

        Assert.Equal(new[] { "S2.09_1_LEVEL 4 PLAN SHOWING LEVEL FRAMING OVER.dxf" }, framed.Select(f => f.File).ToArray());
        Assert.Null(WoodFramedFloors.FramedPart("S2.11_1_LEVEL 6 PLAN SHOWING.dxf", PlanSheetNaming.Vocabulary));
    }

    [Fact]
    public void FloorsNamedByWordsAreFramedAsTheSetRanksThem()
    {
        // 31089-01's townhouses: GROUND, MAIN and UPPER are levels 1, 2 and 3 by the set's own chain (step 60)
        var framed = Framed(
            "S-5_1_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER.dxf",
            "S-6_1_MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER.dxf",
            "S-7_1_UPPER FLOOR SHOWING ROOF FRAMING OVER.dxf");

        // GROUND frames MAIN (the chain's first link, kept); MAIN, itself framed, frames UPPER - wood
        Assert.Equal("S-7_1_UPPER FLOOR SHOWING ROOF FRAMING OVER.dxf", Assert.Single(framed).File);
    }
}
