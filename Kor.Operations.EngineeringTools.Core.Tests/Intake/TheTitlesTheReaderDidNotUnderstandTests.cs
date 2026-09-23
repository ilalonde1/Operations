using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE TITLES THE READER DID NOT UNDERSTAND, AS THE CORPUS WROTE THEM (2026-09-23).
///
/// `takeoff corpus-query dropped` counts every plan sheet that read a slab and gave the model no storey. On run 43
/// that is 1,269 sheets; 57 of them (3,117 slabs) carry a level in their own title, are not a reinforcing or a load
/// plan, and gave the model nothing. They are not 57 problems. These sixteen are the real file names, copied from
/// the sheet ledger, and they separate the fault in two:
///
///   NINE read NOTHING off their own title (1,888 slabs), in exactly two shapes —
///     "1st Flr. Plan Showing 2nd Flr. Framing Over"   FLR is not one of the FloorNouns (FLOOR, LEVEL, STOREY,
///                                                    STORY). Still true; migration 099 is Ian's to apply.
///     "Phase 2a &amp; 2b Parkade Plan - P1", and a view named just "P2"
///                                                    ParkadeStory was anchored to the WHOLE title (^…$) and
///                                                    ParkadeLevel demands the word LEVEL before the P, so a level
///                                                    that arrived last, after a dash, was read by neither.
///                                                    ✅ MENDED by intake step 137 (DrawingVocabulary
///                                                    .TrailingParkade): six of the nine now read, 967 slabs, and
///                                                    their expectations below have been moved. Three are left.
///     ⚠ The anchor is not an oversight: ParkadeWords are P and B, so an unanchored "B\s*\d" reads "SLAB 2" as
///       parkade level 2. Step 137 earns the END of a title, after a dash or at the very start, and nothing else.
///
///   SEVEN read their level CORRECTLY and still gave the model no storey (320 slabs) — "HOTEL FOURTH FLOOR PLAN
///   SHOWING FIFTH FLOOR FRAMING OVER" reads L4, "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER - BLDG 10" reads L1
///   of building 10, "3RD FLOOR HOLD DOWN PLAN - EAST" reads L3. So for those the title reader is not the fault
///   and the composer is: the storey they name was taken by another sheet, or does not exist under that name.
///
/// THIS IS A RATCHET. Every name is asserted to read exactly what it reads TODAY. A fix makes this test fail until
/// its expectation is moved — which is the point: the nine may only shrink, and nothing may join them quietly.
///
/// ⚠ TWO READERS READ THESE TITLES AND THEY DISAGREE. The corpus ledger's `level` column is
/// <c>SheetTitleReader</c>'s reading of the PAGE's printed title; the composer places by this one,
/// <see cref="PlanSheetNaming.Parse(string)"/> on the DXF FILE NAME. Every one of the seven below that reads a
/// level here is BLANK in the ledger — so `corpus-query dropped` put them in its "states no level" class while
/// the composer knew exactly which storey they were. One reader is reported and the other is obeyed, and until
/// this test existed nothing compared them.
///
/// WHAT THIS COVERS: <see cref="PlanSheetNaming.Parse(string)"/> on the file name, which is what the composer
/// matches storeys with. WHAT IT DOES NOT: whether a level that IS read reaches a storey (the seven prove it need
/// not); <c>SheetTitleReader</c>, the other half of the disagreement, which is not exercised here at all; the 460
/// sheets whose titles name no level at all; and it is this office's vocabulary only, since Parse reads the
/// static <see cref="PlanSheetNaming.Vocabulary"/> — which is why this class sits in the collection that
/// serialises every test touching it.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class TheTitlesTheReaderDidNotUnderstandTests
{
    private readonly ITestOutputHelper _out;
    public TheTitlesTheReaderDidNotUnderstandTests(ITestOutputHelper output) => _out = output;

    /// <summary>A real DXF name from the corpus: the set, the storey a human reads, the slabs behind it, and what the reader makes of it today.</summary>
    public sealed record Title(string Job, string FileName, string HumanReads, int Slabs, string ReaderMakes);

    /// <summary>"(nothing)" — the reading that costs a sheet its storey.</summary>
    public const string Nothing = "(nothing)";

    public static readonly IReadOnlyList<Title> FromTheCorpus =
    [
        // 1. A FLOOR PLAN SHOWING THE FLOOR ABOVE'S FRAMING is the LOWER floor's plan. "Flr." is not a floor noun.
        new("30985-01", "30985-01 2022-06-24 Rock Ridge Stickfile-p10_1_1st Flr. Plan Showing 2nd Flr. Framing Over (West).dxf", "level 1", 330, Nothing),
        new("30985-01", "30985-01 2022-06-24 Rock Ridge Stickfile-p12_1_2nd Flr. Plan Showing 3rd Flr. Framing Over (West).dxf", "level 2", 293, Nothing),
        new("30985-01", "30985-01 2022-06-24 Rock Ridge Stickfile-p14_1_3rd Flr. Plan Showing Roof Framing Over (West).dxf", "level 3", 298, Nothing),
        // the same shape spelled out in full, which the reader DOES understand - the level it takes is the first named, correctly
        new("60065-01", "60065-01 2026-07-23 - Vaughan - Stickfile-p14_1_S-9 - HOTEL FOURTH FLOOR PLAN SHOWING FIFTH FLOOR FRAMING OVER.dxf", "level 4", 76, "L4"),
        new("60065-01", "60065-01 2026-07-23 - Vaughan - Stickfile-p12_1_S-7 - HOTEL SECOND FLOOR PLAN SHOWING THIRD FLOOR FRAMING OVER.dxf", "level 2", 61, "L2"),
        new("31089-01", "S2.19_1_MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER - BLDG 10.dxf", "level 1, building 10", 44, "L1, bldg 10"),
        new("31089-01", "S2.19_2_UPPER FLOOR SHOWING ROOF FRAMING OVER - BLDG 10.dxf", "level 2, building 10", 44, "L2, bldg 10"),
        new("01389-01", "01389 Struc Stickfile 2019-07-04-p09_1_S-9 - LOFT PLAN SHOWING ROOF FRAMING OVER.dxf", "the loft, under the roof", 36, "top floor"),

        // 2. A PHASE IS NOT A LEVEL, and the level arrives last, after a dash.
        new("30824-01", "S2.04.1_1_Phase 2a & 2b Floor Plan - P0(Concrete Outline).dxf", "parkade level 0", 317, "P0"),
        new("30824-01", "S2.03_1_Phase 2a & 2b Parkade Plan - P1.dxf", "parkade level 1", 281, "P1"),
        new("30824-01", "S2.02_1_Phase 2a & 2b Parkade Plan - P2.dxf", "parkade level 2", 174, "P2"),
        new("30827-01", "S2.22_1_Phase 2c Parkade Plan - P2.dxf", "parkade level 2", 117, "P2"),

        // 3. THE VIEW IS NAMED FOR ITS STOREY AND NOTHING ELSE - and the sheet number in front of it defeats the anchor.
        new("30905-01", "S2.01.1_1_P2.dxf", "parkade level 2", 42, "P2"),
        new("30905-01", "S2.01.2_1_P2.dxf", "parkade level 2", 36, "P2"),

        // 4. READ CORRECTLY AND STILL DROPPED: the mezzanine over a parkade level, on a part plan.
        new("01379-01", "S207.2_1_LEVEL P1 MEZZANINE CONCRETE OUTLINE PLAN B.dxf", "the mezzanine over parkade level 1", 30, "P1, mezzanine"),

        // 5. READ CORRECTLY AND STILL DROPPED: an ordinal floor on a hold-down plan.
        new("30798-06", "S2.11_1_3RD FLOOR HOLD DOWN PLAN - EAST.dxf", "level 3", 29, "L3"),
    ];

    [Fact]
    public void EachTitleReadsWhatTheLedgerSaysItReads()
    {
        var wrong = new List<string>();
        foreach (var t in FromTheCorpus)
        {
            string got = Describe(PlanSheetNaming.Parse(t.FileName));
            _out.WriteLine($"{(got == Nothing ? "  -  " : "reads"),-6} {t.Job,-10} {got,-34} human: {t.HumanReads,-36} {t.Slabs,5} slabs");
            if (got != t.ReaderMakes) wrong.Add($"{t.Job} {System.IO.Path.GetFileName(t.FileName)}: banked \"{t.ReaderMakes}\", reads \"{got}\"");
        }
        Assert.True(wrong.Count == 0,
            "The title reader has changed. If it reads MORE, move the expectation and say so; if it reads less, that is a regression:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// The ratchet's number, in the build rather than in a note. It was NINE of these sixteen titles and 1,888
    /// slabs when this was written at 00:45; intake step 137 took six of them, and what is left is the three
    /// "1st Flr." sheets and 921 slabs, waiting on migration 099. It may go down; a rise is a fault.
    /// </summary>
    [Fact]
    public void ThreeOfThemReadNothingAndThatMayOnlyGoDown()
    {
        var blind = FromTheCorpus.Where(t => t.ReaderMakes == Nothing).ToList();
        Assert.Equal(blind.Count, FromTheCorpus.Count(t => Describe(PlanSheetNaming.Parse(t.FileName)) == Nothing));
        Assert.True(blind.Count <= 3, $"{blind.Count} titles read nothing; the banked count is 3 and it may only fall.");
        Assert.True(blind.Sum(t => t.Slabs) <= 921, $"{blind.Sum(t => t.Slabs)} slabs sit behind a title that reads nothing; the banked figure is 921.");
    }

    /// <summary>
    /// INTAKE STEP 137's own case, and the word it must NOT read. The anchor is the whole rule: a parkade word is
    /// P or B, so anything looser reads "SLAB 2" as parkade level 2 and puts a floor underground.
    /// </summary>
    [Fact]
    public void ALevelThatArrivesLastIsReadAndTheMiddleOfAWordIsNot()
    {
        Assert.Equal([1], PlanSheetNaming.Parse("S2.03_1_Phase 2a & 2b Parkade Plan - P1.dxf").ParkadeLevels);
        Assert.Equal([0], PlanSheetNaming.Parse("S2.04.1_1_Phase 2a & 2b Floor Plan - P0(Concrete Outline).dxf").ParkadeLevels);
        Assert.Equal([2], PlanSheetNaming.Parse("S2.01.1_1_P2.dxf").ParkadeLevels);

        // A PAGE NUMBER WELDED TO THE NAME IS NOT A PARKADE LEVEL. The first cut of step 137 took any dash and read
        // "job-p01" - and every "…Stickfile-p07" in the corpus - as parkade level 1. ASheetIsItsViewsTests failed
        // within a minute and is why the dash must carry a space on both sides.
        Assert.Empty(PlanSheetNaming.Parse("job-p01").ParkadeLevels);
        Assert.Empty(PlanSheetNaming.Parse("30985-01 2022-06-24 Rock Ridge Stickfile-p07").ParkadeLevels);

        // the middle of a word, the middle of a title, and a trailing token that is not a parkade word
        Assert.Empty(PlanSheetNaming.Parse("S2.09_1_TYPICAL SLAB 2 DETAIL PLAN.dxf").ParkadeLevels);
        Assert.Empty(PlanSheetNaming.Parse("S2.09_1_PLAN - P1 TO P3 TYPICAL DETAILS AND NOTES.dxf").ParkadeLevels);
        Assert.Empty(PlanSheetNaming.Parse("S2.09_1_FOUNDATION PLAN - SOUTH.dxf").ParkadeLevels);

        // and it never speaks over a title that already said what it is
        Assert.Equal([4], PlanSheetNaming.Parse("S2.09_1_LEVEL 4 PLAN - P2.dxf").Levels);
        Assert.Empty(PlanSheetNaming.Parse("S2.09_1_LEVEL 4 PLAN - P2.dxf").ParkadeLevels);
    }

    /// <summary>
    /// THE ROW THAT WOULD READ THE FIRST SIX (migration 099, `dxf.floor-nouns` gains FLR — Ian's to apply).
    ///
    /// 089 banked the rule that a storey may be named by a word, for exactly this grammar: "MAIN FLOOR PLAN
    /// SHOWING 2ND FLOOR FRAMING OVER". Its own test for a row is "a vocabulary that can only widen", and this is
    /// that: one abbreviation. 30985-01 writes every one of its plans "1st Flr. Plan Showing 2nd Flr. Framing
    /// Over", so its 3-storey building is modelled with ONE storey, L1 — the two sheets that spell "1st Floor" in
    /// full are the only ones that read. Six sheets and 1,805 slabs, the whole of the corpus's use of it.
    ///
    /// This proves the reader WOULD read them, against the widened vocabulary and without the database. What it
    /// cannot prove is the corpus effect, which needs the row: the prediction is 30985-01 going from 1 storey to
    /// 3, and nothing else moving, since no other set in run 43 writes an ordinal against FLR.
    /// </summary>
    [Fact]
    public void WithFlrAsAFloorNounTheThirtyNineEightyFiveSheetsRead()
    {
        var widened = DrawingVocabulary.Default with { FloorNouns = ["FLOOR", "LEVEL", "STOREY", "STORY", "FLR"] };
        var expected = new (string Name, int Level)[]
        {
            ("30985-01 2022-06-24 Rock Ridge Stickfile-p10_1_1st Flr. Plan Showing 2nd Flr. Framing Over (West).dxf", 1),
            ("30985-01 2022-06-24 Rock Ridge Stickfile-p12_1_2nd Flr. Plan Showing 3rd Flr. Framing Over (West).dxf", 2),
            ("30985-01 2022-06-24 Rock Ridge Stickfile-p14_1_3rd Flr. Plan Showing Roof Framing Over (West).dxf", 3),
        };
        foreach (var (name, level) in expected)
        {
            Assert.Empty(PlanSheetNaming.Parse(name, DrawingVocabulary.Default).Levels);
            Assert.Equal([level], PlanSheetNaming.Parse(name, widened).Levels);
        }
        // and it stays a widening: the words that read today still read the same
        Assert.Equal([1], PlanSheetNaming.Parse("30985-01 2022-06-24 Rock Ridge Stickfile-p07_1_1st Floor Plan.dxf", widened).Levels);
        Assert.Equal([4], PlanSheetNaming.Parse("S-9 - HOTEL FOURTH FLOOR PLAN SHOWING FIFTH FLOOR FRAMING OVER.dxf", widened).Levels);
    }

    private static string Describe(PlanSheetInfo s)
    {
        var parts = new List<string>();
        if (s.Levels.Count > 0) parts.Add("L" + string.Join("+", s.Levels));
        if (s.ParkadeLevels.Count > 0) parts.Add("P" + string.Join("+", s.ParkadeLevels));
        if (s.IsRoof) parts.Add("roof");
        if (s.IsFoundation) parts.Add("foundation");
        if (s.IsTopFloor) parts.Add("top floor");
        if (s.IsMezzanine) parts.Add("mezzanine");
        if (s.BuildingTags.Count > 0) parts.Add("bldg " + string.Join("+", s.BuildingTags));
        return parts.Count == 0 ? Nothing : string.Join(", ", parts);
    }
}
