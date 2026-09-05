#nullable enable
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A schedule reader must read the drawing in front of it, not the one its author had.
/// </summary>
/// <remarks>
/// FootingScheduleReader's size pattern was <c>^(\d{3,4})\s*[xX×]\s*(\d{3,4})...(?:DEEP|DP)</c> —
/// three-to-four-digit integers, which is millimetres. It is a deterministic takeoff and it was
/// correct, for metric sets.
///
/// Swept over five KOR jobs on 2026-09-02 it returned a number on ONE: 31065, 1,174 cy, the only
/// metric set. 31138 and 31130 print feet and inches and returned "0 cy" — silently, which reads
/// exactly like a job with no footings. Nobody would have looked, because a deterministic tool
/// reporting a total does not look broken.
///
/// The fix was not a better pattern per reader. It was one PrintedLength that reads a length the way
/// a drawing prints it, so the unit stops being a property of the tool and goes back to being a
/// property of the drawing, where it is printed.
///
/// WHAT THIS COVERS: the imperial and metric forms KOR's own schedules actually use, taken verbatim
/// off 31130, 31138 and 31065; that a size cell still parses with reinforcing prose trailing it;
/// that prose alone is refused rather than guessed at; and that the metric set that already worked
/// still reads identically, which is the regression that would matter most.
///
/// WHAT IT DOES NOT COVER: whether the numbers are the RIGHT ones for the job — that the third
/// dimension is the depth and not a width is the schedule's convention and stays with the reader.
/// It says nothing about placement counting on the plan, which is the other half of a takeoff and
/// where 31168 still returns zero. And a drawing printing a unit neither imperial nor metric —
/// centimetres, say — would parse as millimetres here and be wrong by ten with nothing to catch it.
/// </remarks>
public sealed class ADrawingStatesItsOwnUnitsTests
{
    private const double Tol = 0.6;   // mm; 1/64 inch is 0.4

    [Theory]
    // ── imperial, exactly as 31130 and 31138 print it ────────────────────────
    [InlineData("4' - 0\"", 1219.2)]
    [InlineData("4'-0\"", 1219.2)]
    [InlineData("12' - 0\"", 3657.6)]
    [InlineData("26\"", 660.4)]
    [InlineData("60\"", 1524.0)]
    [InlineData("18\"", 457.2)]
    [InlineData("3 1/2\"", 88.9)]
    [InlineData("26.5\"", 673.1)]
    // ── the typographic quotes a CAD title block often carries ───────────────
    [InlineData("4’ - 0”", 1219.2)]
    // ── metric, exactly as 31065 prints it ───────────────────────────────────
    [InlineData("2500", 2500.0)]
    [InlineData("900", 900.0)]
    [InlineData("1300 mm", 1300.0)]
    public void APrintedLengthReadsAsMillimetres(string printed, double expected)
        => Assert.Equal(expected, PrintedLength.LeadingMm(printed)!.Value, Tol);

    [Theory]
    [InlineData("EACH WAY BOT.")]
    [InlineData("DEEP")]
    [InlineData("")]
    [InlineData("   ")]
    public void ProseIsRefusedRatherThanGuessedAt(string text)
        => Assert.Null(PrintedLength.TryParseMm(text));

    /// <summary>31130's F1 row, verbatim, reinforcing text and all.</summary>
    [Fact]
    public void AnImperialFootingRowReadsThroughItsTrailingReinforcing()
    {
        var mm = PrintedLength.TryParseSizeMm("4' - 0\" x 4' - 0\" x 26\" DEEP 7-20M@3.6 EACH WAY BOT. H.2.E");

        Assert.NotNull(mm);
        Assert.Equal(3, mm!.Count);
        Assert.Equal(1219.2, mm[0], Tol);
        Assert.Equal(1219.2, mm[1], Tol);
        Assert.Equal(660.4,  mm[2], Tol);
    }

    /// <summary>31065's F-row, which already worked and must keep working.</summary>
    [Fact]
    public void TheMetricRowThatAlreadyWorkedStillReads()
    {
        var mm = PrintedLength.TryParseSizeMm("2500 x 2500 x 900 DEEP");

        Assert.NotNull(mm);
        Assert.Equal(new[] { 2500.0, 2500.0, 900.0 }, mm!);
    }

    /// <summary>
    /// 31130's strip footing, where the keyword sits BETWEEN the dimensions rather than after them.
    /// Truncating at the first keyword would lose the second number and the row with it.
    /// </summary>
    [Fact]
    public void AStripFootingReadsWithAKeywordBetweenItsDimensions()
    {
        var mm = PrintedLength.TryParseSizeMm("18\" WIDE x 12\" DEEP");

        Assert.NotNull(mm);
        Assert.Equal(2, mm!.Count);
        Assert.Equal(457.2, mm[0], Tol);
        Assert.Equal(304.8, mm[1], Tol);
    }

    [Fact]
    public void SomethingWithOneDimensionIsNotASize()
        => Assert.Null(PrintedLength.TryParseSizeMm("26\" DEEP"));

    [Fact]
    public void InchesAreOfferedForTheSidesThatWorkInThem()
        => Assert.Equal(48.0, PrintedLength.TryParseInches("4' - 0\"")!.Value, 0.05);
}
