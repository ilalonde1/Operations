using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A sheet whose title will not say which level it draws gets one from where it sits in the
/// sequence — a guess, used, and marked (intake step 144, 2026-09-24).
///
/// Ian: "rather than skipping an unknown completely … can AI please make a best guess, then LOOK at
/// the result as a sanity check, then proceed - whilst showing clearly in the question workbook that
/// this was an assumption."
///
/// ⚠ WHAT THESE TESTS COVER: that the stem groups two areas of one drawing; that a gap holding
/// exactly as many sheets as there are unclaimed levels is answered by arithmetic; that a gap whose
/// counts DISAGREE is declined rather than guessed; that a level another sheet already claims is
/// never handed out twice; that a run bounded by a parkade level or the roof is declined; and that
/// both views of a gap drawing get the same answer.
///
/// NOT COVERED, and it is the important half: **nothing here proves the ANSWER IS RIGHT.** These
/// assert the arithmetic, not the building. The inference rests on sheet numbers ascending with the
/// storeys, which is a convention, not a law — an office that numbers its plans top-down, or that
/// puts the podium after the tower, breaks every inference in a run at once and this file stays
/// green throughout.
///
/// Only the corpus back-test can see that, and it is what drew the line this class now tests:
/// leave-one-out over run 45's 1,776 drawings measured the exact case at 480 right / 1 wrong
/// (99.8%, and the one disagreement is a title the reader under-read) and spreading levels over a
/// gap at 56 / 39 — 58.9%. So the spread tier was written, measured and deleted, and the gaps it
/// would have filled go to the engineer as step 143's question. These tests are the floor under
/// that measurement, not a substitute for it.
///
/// A mezzanine is the known miss: a set numbering L1, L1M, L2 has a storey between two anchors that
/// no integer names, so the count comes out wrong and the run is declined — or, worse, comes out
/// right for the wrong reason and places a floor one storey off.
/// </summary>
public class ALevelIsTakenFromWhereTheSheetSitsTests
{
    private static PlanSheetInfo Sheet(string file, params int[] levels) =>
        new(file, null, levels, IsRoof: false, Label: file);

    private static PlanSheetInfo Parkade(string file, int p) =>
        new(file, null, [], IsRoof: false, Label: file) { ParkadeLevels = [p] };

    private static PlanSheetInfo Roof(string file) =>
        new(file, null, [], IsRoof: true, Label: file);

    /// <summary>
    /// 31005-01 as its own ledger has it. Only S2.05.1, S2.07.1/.2 and S2.09.x parsed a level; the
    /// rest came out of the scrambled title block with nothing.
    /// </summary>
    private static List<PlanSheetInfo> The31005Set() =>
    [
        Parkade("S2.01.1_1_PLAN.dxf", 1),
        Sheet("S2.01.2_1_PLAN.dxf"),
        Sheet("S2.02.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.02.2_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.03.1_1_PLAN OUTLINE MEZZ CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.03.2_1_PLAN.dxf"),
        Sheet("S2.04.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.04.2_1_PLAN.dxf"),
        Sheet("S2.05.1_1_LEVEL L03-04 PLAN CONCRETE OUTLINE.dxf", 3, 4),
        Sheet("S2.05.2_1_PLAN.dxf"),
        Sheet("S2.06.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.06.2_1_PLAN.dxf"),
        Sheet("S2.07.1_1_LEVEL 6-7 PLAN.dxf", 6, 7),
        Sheet("S2.07.2_1_LEVEL 6 PLAN.dxf", 6),
        Sheet("S2.08.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf"),
        Sheet("S2.08.2_1_PLAN.dxf"),
        Sheet("S2.09.1_1_LEVEL 9-16 PLAN.dxf", 9, 16),
        Roof("S2.11.1_1_ROOF PLAN.dxf"),
    ];

    [Fact]
    public void TheGapWithOneUnclaimedLevelIsAnsweredByArithmetic()
    {
        var found = LevelsFromSheetOrder.Infer(The31005Set());

        // S2.06 sits between S2.05 (max level 4) and S2.07 (min level 6). Level 5 is the only one
        // between them that no drawing claims, so both of S2.06's views are level 5.
        var six = found.Where(f => f.FileName.StartsWith("S2.06", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, six.Count);
        Assert.All(six, f => Assert.Equal([5], f.Levels));

        // The working, not just the answer — this sentence is what reaches the engineer.
        Assert.Equal(
            "sheet S2.06 sits between S2.05 (level 4) and S2.07 (level 6), and level 5 is the only "
            + "one between them that no drawing claims",
            six[0].Because);

        // S2.08 between S2.07 (max 7) and S2.09 (min 9): level 8, the same way.
        var eight = found.Where(f => f.FileName.StartsWith("S2.08", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, eight.Count);
        Assert.All(eight, f => Assert.Equal([8], f.Levels));

    }

    [Fact]
    public void ARunBoundedByAParkadeLevelIsDeclined()
    {
        // S2.02, S2.03 and S2.04 sit between a PARKADE level and level 3. How many storeys are in
        // that stretch is not something arithmetic knows — there may be a mezzanine, a transfer
        // level, or nothing. 143's question is the right answer here, not a guess.
        var found = LevelsFromSheetOrder.Infer(The31005Set());

        Assert.DoesNotContain(found, f => f.FileName.StartsWith("S2.02", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.FileName.StartsWith("S2.03", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.FileName.StartsWith("S2.04", StringComparison.Ordinal));
    }

    [Fact]
    public void ASheetTheTitleDidNameIsNeverOverwritten()
    {
        var found = LevelsFromSheetOrder.Infer(The31005Set());

        foreach (string named in new[] { "S2.05.1", "S2.07.1", "S2.07.2", "S2.09.1" })
            Assert.DoesNotContain(found, f => f.FileName.StartsWith(named, StringComparison.Ordinal));
    }

    [Fact]
    public void BothAreasOfOneDrawingGetTheSameLevel()
    {
        // THE STEM CARRIES THE STOREY. S2.05.1 parsed and S2.05.2 did not; they are one drawing in
        // two areas. Were the grouping per view instead, S2.05.2 would be a third candidate for
        // level 5 and nothing would be determined at all.
        Assert.Equal("S2.05", LevelsFromSheetOrder.StemOf("S2.05.2_1_PLAN.dxf"));
        Assert.Equal("S2.05", LevelsFromSheetOrder.StemOf("S2.05.1_1_LEVEL L03-04 PLAN CONCRETE OUTLINE.dxf"));

        // A two-part number is already a drawing and keeps both parts.
        Assert.Equal("S2.01", LevelsFromSheetOrder.StemOf("S2.01_1_LEVEL 1 PLAN.dxf"));

        // Not a sheet number at all.
        Assert.Null(LevelsFromSheetOrder.StemOf("some drawing.dxf"));
    }

    [Fact]
    public void ALevelAnotherSheetAlreadyDrawsIsNotHandedOutAgain()
    {
        // Levels 3 and 4 are drawn by a sheet further down the set, so the only level unaccounted
        // for between 2 and 6 is 5 — and one level cannot be shared between two gap sheets.
        var sheets = new List<PlanSheetInfo>
        {
            Sheet("S2.01_1_LEVEL 2 PLAN.dxf", 2),
            Sheet("S2.02_1_PLAN.dxf"),
            Sheet("S2.03_1_PLAN.dxf"),
            Sheet("S2.06_1_LEVEL 6 PLAN.dxf", 6),
            Sheet("S2.90_1_LEVEL 3-4 PLAN.dxf", 3, 4),
        };

        var found = LevelsFromSheetOrder.Infer(sheets);

        Assert.DoesNotContain(found, f => f.Levels.Contains(3) || f.Levels.Contains(4));
        Assert.Empty(found);
    }

    [Fact]
    public void WhereTheCountsDisagreeNothingIsGuessed()
    {
        // ⭐ THE MEASUREMENT DECIDED THIS, NOT TASTE. Leave-one-out over run 45's 1,776 drawings:
        // where the gap holds exactly as many sheets as there are unclaimed levels the answer is
        // 99.8% right (480 of 481, and the one disagreement is a title the reader under-read).
        // Where it does not, spreading the levels over the gap measured 58.9% — a coin flip that
        // puts a floor on a storey the engineer never drew, and a wrong floor asks nobody to check
        // it. Those go to her as step 143's question instead.
        //
        // Two gap sheets, three levels unaccounted for: declined.
        var sheets = new List<PlanSheetInfo>
        {
            Sheet("S2.01_1_LEVEL 1 PLAN.dxf", 1),
            Sheet("S2.02_1_PLAN.dxf"),
            Sheet("S2.03_1_PLAN.dxf"),
            Sheet("S2.04_1_LEVEL 5 PLAN.dxf", 5),
        };

        Assert.Empty(LevelsFromSheetOrder.Infer(sheets));
    }

    [Fact]
    public void ASetWhoseTitlesAllParsedIsLeftEntirelyAlone()
    {
        var sheets = new List<PlanSheetInfo>
        {
            Sheet("S2.01_1_LEVEL 1 PLAN.dxf", 1),
            Sheet("S2.02_1_LEVEL 2 PLAN.dxf", 2),
            Sheet("S2.03_1_LEVEL 3 PLAN.dxf", 3),
        };

        Assert.Empty(LevelsFromSheetOrder.Infer(sheets));
    }

    [Fact]
    public void SheetNumbersSortAsNumbersNotAsText()
    {
        // S2.9 before S2.10. Sorted as text the gap between them inverts and every inference in the
        // run comes out backwards.
        var sheets = new List<PlanSheetInfo>
        {
            Sheet("S2.9_1_LEVEL 9 PLAN.dxf", 9),
            Sheet("S2.10_1_PLAN.dxf"),
            Sheet("S2.11_1_LEVEL 11 PLAN.dxf", 11),
        };

        var one = Assert.Single(LevelsFromSheetOrder.Infer(sheets));
        Assert.Equal([10], one.Levels);

    }

    [Fact]
    public void ASeriesOfItsOwnDoesNotBoundAnotherSeriesGap()
    {
        // S1.21 is a loading diagram, not a plan in the S2 sequence. If the two series interleave,
        // an S1 sheet can land inside an S2 run and cut it in half.
        var sheets = new List<PlanSheetInfo>
        {
            Sheet("S1.21_1_LOADING DIAGRAM.dxf"),
            Sheet("S2.01_1_LEVEL 1 PLAN.dxf", 1),
            Sheet("S2.02_1_PLAN.dxf"),
            Sheet("S2.03_1_LEVEL 3 PLAN.dxf", 3),
        };

        var found = LevelsFromSheetOrder.Infer(sheets);
        var one = Assert.Single(found, f => f.FileName.StartsWith("S2.02", StringComparison.Ordinal));
        Assert.Equal([2], one.Levels);
        Assert.DoesNotContain(found, f => f.FileName.StartsWith("S1.", StringComparison.Ordinal));
    }
}
