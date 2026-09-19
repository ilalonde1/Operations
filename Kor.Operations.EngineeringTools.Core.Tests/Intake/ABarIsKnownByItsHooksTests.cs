using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A BAR IS KNOWN BY ITS HOOKS (intake step 127, 2026-09-19). The label form (step 126) catches 7-9 strokes a page; the
/// bars' signature on 31017 (63 hooked polylines, 23 double-ticked strokes on page 18) and 30838 (291 double-ticked
/// strokes on S2.28) is the hook the drafter draws at each end: a short stroke at a right angle whose other end touches
/// no long line. WHAT THIS COVERS: a polyline short-long-short is a bar; a two-point run with a free tick at each end is
/// a bar and its ticks go with it; a run with one hook stays a line; a wall's two faces with their end caps stay (a
/// cap's other end is the other face); a jog between two long lines stays (step 110); a dimension line - the same
/// shape with its number on it - stays a line; a run under 1,500 mm (a notch's profile) stays. WHAT IT DOES NOT: hooks
/// drawn through the run's end rather than to it; a bar bent at other than a right angle; the floor closing once the
/// bars are out (judged on 31017 and 30838 against her models).
/// </summary>
public sealed class ABarIsKnownByItsHooksTests
{
    private static RawSubpath Poly(params (double X, double Y)[] pts) => new([.. pts], false, (0, 0, 0), false, true, 0.5, false);
    private static RawSubpath L(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static ExtractedGeometry With(params (string Text, double MinX, double MinY, double MaxX, double MaxY)[] words)
    {
        var g = new ExtractedGeometry();
        foreach (var w in words) g.PageWordBoxes.Add(w);
        return g;
    }

    [Fact]
    public void AShortLongShortPolylineIsABar()
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify([Poly((5000, 10000), (5000, 10250), (9000, 10250), (9000, 10000))], fates);
        Assert.Equal(PathReason.BarRun, Assert.Single(fates).Reason);
        Assert.Empty(g.Lines);
    }

    [Fact]
    public void ARunWithAFreeTickAtEachEndIsABarAndItsTicksGoWithIt()
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify([L(5000, 12000, 9000, 12000), L(5000, 12000, 5000, 12250), L(9000, 12000, 9000, 12250)], fates);
        Assert.Equal(3, fates.Count);
        Assert.All(fates, f => Assert.Equal(PathReason.BarRun, f.Reason));
        Assert.Empty(g.Lines);
    }

    [Fact]
    public void ARunHookedAtOneEndOnlyStaysALine()
    {
        var fates = new List<PathFate>();
        FateFixture.Classify([L(5000, 12000, 9000, 12000), L(5000, 12000, 5000, 12250)], fates);
        Assert.Equal(PathReason.EmittedAsLine, fates[0].Reason);
    }

    [Fact]
    public void AWallsFacesWithTheirEndCapsAreNotBars()
    {
        // two faces 200 mm apart closed by caps: the cap's other end is on the other face, so it is no free tick
        var fates = new List<PathFate>();
        FateFixture.Classify([L(5000, 14000, 9000, 14000), L(5000, 14200, 9000, 14200), L(5000, 14000, 5000, 14200), L(9000, 14000, 9000, 14200)], fates);
        Assert.DoesNotContain(fates, f => f.Reason == PathReason.BarRun);
    }

    [Fact]
    public void AJogBetweenTwoLongLinesIsNotAHook()
    {
        // the slab edge steps 150 mm sideways between two long lines (step 110): the short stroke's both ends are on long lines
        var fates = new List<PathFate>();
        FateFixture.Classify([L(2000, 16000, 6000, 16000), L(6000, 16000, 6000, 16150), L(6000, 16150, 10000, 16150)], fates);
        Assert.DoesNotContain(fates, f => f.Reason == PathReason.BarRun);
    }

    [Fact]
    public void ADimensionLineHasTheBarsShapeAndItsNumberOnItAndStaysALine()
    {
        var fates = new List<PathFate>();
        FateFixture.Classify([L(5000, 18000, 9000, 18000), L(5000, 18000, 5000, 18250), L(9000, 18000, 9000, 18250)], fates,
            result: With(("13'-1\"", 6800, 18050, 7200, 18290)));
        Assert.Equal(PathReason.EmittedAsLine, fates[0].Reason);
    }

    [Fact]
    public void ARunShorterThanABarStaysALine()
    {
        // a notch's profile: short-long-short with a 900 mm run
        var fates = new List<PathFate>();
        FateFixture.Classify([Poly((5000, 20000), (5000, 20250), (5900, 20250), (5900, 20000))], fates);
        Assert.Equal(PathReason.EmittedAsLine, Assert.Single(fates).Reason);
    }
}
