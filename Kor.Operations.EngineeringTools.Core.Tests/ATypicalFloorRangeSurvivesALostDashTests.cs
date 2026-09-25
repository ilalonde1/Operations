using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// "LEVEL 9 19 PLAN" is eleven storeys, not two (intake step 145, 2026-09-24).
///
/// 31005-01 draws L9 through L19 on one sheet. The dash was lost on the way out of the PDF, so the
/// title reaches the reader with nothing between the numbers: <c>Range</c> needs a range word,
/// <c>LevelList</c> needs a comma or ampersand, and it falls through to two <c>SingleLevel</c>
/// matches. L10 to L18 were never drawn.
///
/// ⚠ WHY IT HID FOR SO LONG, which is the part worth carrying forward: at two wide the two readings
/// are the SAME ANSWER. "LEVEL L03-04" is 3 and 4 whether you read the endpoints or the range, and
/// every narrow case in the corpus looked correct. Only a wide range separates them — and then it
/// separates them by nine storeys.
///
/// ⚠ WHAT THESE TESTS COVER: that a bare wide range expands; that a gap of one is left alone; that
/// an ODD/EVEN alternating-floor sheet is never expanded; that a proper range, a list and a single
/// level are all unaffected; and that the knob turns it off.
///
/// NOT COVERED: whether "LEVEL a b" really WAS a dash in the original PDF. Nothing here reads the
/// PDF. The evidence for 31005-01 is that its own index series writes the same drawing as
/// "LEVEL L09-19", and that a two-number title with a wide gap is not a plausible list — but a set
/// that genuinely means "level 9 and level 19, nothing between" will be read wrongly by this and
/// every test below will still pass. That is what the corpus guard sets are for: 30972-01 and
/// 30993-01 are already floored 20/20 and 39/40, and if either moves, this rule is wrong.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]   // PlanSheetNaming.Parse reads the shared vocabulary
public class ATypicalFloorRangeSurvivesALostDashTests
{
    private static IReadOnlyList<int> LevelsOf(string title) =>
        PlanSheetNaming.Parse(title, DrawingVocabulary.Default).Levels;

    [Fact]
    public void ADashlessWideRangeIsEveryStoreyBetweenItsEnds()
    {
        // The witness, exactly as extraction writes it.
        Assert.Equal(
            [9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19],
            LevelsOf("S2.09.1_1_LEVEL 9 19 PLAN.dxf"));
    }

    [Fact]
    public void TheDashedFormReadsTheSameWayItAlwaysDid()
    {
        // The set's own index series writes the same drawing with the dash. Both must agree, or the
        // two copies of one drawing land on different storeys.
        Assert.Equal(
            LevelsOf("S2.09.1_1_LEVEL 9 19 PLAN.dxf"),
            LevelsOf("S0.00_14_LEVEL L09-19 PLAN CONCRETE OUTLINE.dxf"));
    }

    [Fact]
    public void AnOddOrEvenSheetIsNeverExpanded()
    {
        // 30864-01 draws its market tower twice, odd storeys on one sheet and even on the other.
        // Expanding either as contiguous puts a floor on ten storeys that drawing does not serve.
        // A wrong floor is worse than a missing one: these are declined and asked about instead.
        foreach (string title in new[]
                 {
                     "S2.19_1_LEVEL 7 25 PLAN ODD NUMBERS MARKET TOWER.dxf",
                     "S2.19_2_LEVEL 6 26 PLAN EVEN NUMBERS MARKET TOWER.dxf",
                 })
        {
            var levels = LevelsOf(title);
            Assert.True(levels.Count <= 2, $"{title} expanded to {levels.Count} levels: {string.Join(",", levels)}");
        }
    }

    [Fact]
    public void ADashLostAtOneWideCostsAStoreyToo()
    {
        // ⚠ WRITTEN AS THE OPPOSITE OF THIS AND THE TEST CORRECTED ME. The rule first required a gap
        // of two or more, because "LEVEL 6 7" surely read as 6 and 7 either way and there was nothing
        // to win. It did not: the reader matches only the number that FOLLOWS the level word, so
        // "LEVEL 6 7" was reading as [6] and quietly losing the 7.
        //
        // So the narrow case was never harmless — it was the same fault, one storey at a time, which
        // is exactly why it survived: nobody counts a single missing floor on a set that otherwise
        // looks right.
        Assert.Equal([6, 7], LevelsOf("S2.07_1_LEVEL 6 7 PLAN.dxf"));
    }

    [Fact]
    public void EveryReadingThatAlreadyWorkedStillDoes()
    {
        // This is the last numeric reading tried and only runs when the others found nothing.
        Assert.Equal([3, 4], LevelsOf("S2.05.1_1_LEVEL L03-04 PLAN CONCRETE OUTLINE.dxf"));
        Assert.Equal([9, 16], LevelsOf("S2.09.1_2_LEVEL 9 & 16 PARTIAL PLAN.dxf"));
        Assert.Equal([2], LevelsOf("S2.02_1_LEVEL 2 PLAN.dxf"));
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], LevelsOf("S2.10_1_LEVEL 4 TO 14 PLAN.dxf"));
    }

    [Fact]
    public void TheKnobTurnsItOff()
    {
        // Rule 11's instrument: a bisect knob that does not change the reading answers the same
        // either way and cannot attribute a loss.
        Environment.SetEnvironmentVariable("KOR_STEP145_OFF", "1");
        try
        {
            var levels = LevelsOf("S2.09.1_1_LEVEL 9 19 PLAN.dxf");
            Assert.True(levels.Count <= 2, $"knob did not turn the rule off: {string.Join(",", levels)}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KOR_STEP145_OFF", null);
        }
    }
}
