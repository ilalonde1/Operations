#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Every sheet is read at the scale it states; the request's scale is the fallback for a sheet that
/// states none (intake step 31).
///
/// The intake has read each sheet's stated scale since step 6 and then scaled every sheet by the
/// CLI's --scale anyway. The architect's set for 31170 draws each storey four ways — a floor plan
/// and a slab plan at 1/8", enlarged part plans (SW, NE, SE, NW) at 1/4" — and read at one scale the
/// part plans landed on the storeys at twice their size: 3,635 walls on nine storeys, 32 of 49 sheets
/// that could not be set on the grid. Read each at its own scale, 49 of 49 sit on the grid by name.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: an imperial note giving its denominator (1/4" = 1'-0" is 48), a metric ratio
/// (1 : 100), a sheet stating nothing giving null so the request's scale stands, and a title block
/// stating two different scales giving null rather than a guess. WHAT IT DOES NOT: the real sheets
/// (the six-set harness measures those); a view's own caption on an AS NOTED sheet, which is
/// <see cref="ViewCaptions"/>'s and applies to ladders, not to the whole sheet's geometry; and a
/// sheet whose stated scale is wrong for the plan drawn on it, which only the self-check can see.
/// </remarks>
public sealed class ASheetIsReadAtTheScaleItStatesTests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);

    /// <summary>A title block in the sheet's bottom-right corner stating the given scale.</summary>
    private static PC SheetStating(params string[] scaleTokens)
    {
        var words = new List<TT> { Tok("SCALE", 2700, 2050) };
        double x = 2760;
        foreach (var t in scaleTokens) { words.Add(Tok(t, x, 2050)); x += 30; }
        return new PC(1, W, H, words, new List<GP>());
    }

    [Fact]
    public void AnImperialNoteGivesItsDenominator()
    {
        Assert.Equal(48, DrawingIntake.StatedScaleDenominator(SheetStating("1/4\"", "=", "1'-0\"")));
        Assert.Equal(96, DrawingIntake.StatedScaleDenominator(SheetStating("1/8\"", "=", "1'-0\"")));
    }

    [Fact]
    public void AMetricRatioGivesItsDenominator()
    {
        Assert.Equal(100, DrawingIntake.StatedScaleDenominator(SheetStating("1", ":", "100")));
    }

    [Fact]
    public void ASheetStatingNothingGivesNullSoTheRequestsScaleStands()
    {
        var page = new PC(1, W, H, new List<TT> { Tok("LEVEL", 100, 100), Tok("3", 140, 100) }, new List<GP>());
        Assert.Null(DrawingIntake.StatedScaleDenominator(page));
    }

    [Fact]
    public void ATitleBlockStatingTwoDifferentScalesGivesNullRatherThanAGuess()
    {
        // two SCALE fields in the bottom corner (PDF y is up: the reader wants the bottom third) on their own
        // baselines, 1/4" and 1/8": neither is taken, and the
        // title-block-field fallback may not fill the refusal either (the summary claimed this; the audit
        // noted no test asserted it — F25)
        var words = new List<TT> { Tok("SCALE", 2700, 100), Tok("1/4\"", 2760, 100), Tok("=", 2790, 100), Tok("1'-0\"", 2820, 100),
                                   Tok("SCALE", 2700, 140), Tok("1/8\"", 2760, 140), Tok("=", 2790, 140), Tok("1'-0\"", 2820, 140) };
        var page = new PC(1, W, H, words, new List<GP>());
        Assert.True(SheetScaleReader.StatesConflictingScales(page));
        Assert.Null(DrawingIntake.StatedScaleDenominator(page));
        // and the same two fields AGREEING are one scale, so it is the disagreement that refuses, not the second field
        var agreeing = new List<TT> { Tok("SCALE", 2700, 100), Tok("1/4\"", 2760, 100), Tok("=", 2790, 100), Tok("1'-0\"", 2820, 100),
                                      Tok("SCALE", 2700, 140), Tok("1/4\"", 2760, 140), Tok("=", 2790, 140), Tok("1'-0\"", 2820, 140) };
        Assert.Equal(48, DrawingIntake.StatedScaleDenominator(new PC(1, W, H, agreeing, new List<GP>())));
    }
}
