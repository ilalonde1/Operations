using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A LINE THAT CARRIES ITS BAR MARK IS A BAR (intake step 126, 2026-09-18). 31017's outline sheets draw the diaphragm
/// bars over the slab edge (140 bar-mark words on page 18) and 30838's "CONCRETE OUTLINE AND DIAPHRAGM REINFORCING"
/// views the same (128 on S2.28), at the same pens as the edge and the grid - five pens on one page - so no pen names
/// them; the label does. Every bar reaching the rim cut the floor's arrangement into cells (31017: 1,768 of her
/// 64,354 sq ft on L1). WHAT THIS COVERS: a straight line 600 mm or longer with a bar-mark word whose box sits on it
/// (its centre within 0.8 text heights, over the line's length) is BarRun and reaches no reader; the label a height
/// off, a non-bar word on the line, a label beyond the line's end, a stroke under 600 mm all stay lines; a label
/// written along a vertical bar reads by its short side; the grammar. WHAT IT DOES NOT: the floor closing once the bars
/// are out (judged on 31017 and 30838 against her models); a bar drawn as a polyline of more than two points; an end
/// tick or hook as the second witness where the label sits off the bar.
/// </summary>
public sealed class ABarCarryingItsMarkIsABarTests
{
    private static ExtractedGeometry With(params (string Text, double MinX, double MinY, double MaxX, double MaxY)[] words)
    {
        var g = new ExtractedGeometry();
        foreach (var w in words) g.PageWordBoxes.Add(w);
        return g;
    }

    [Fact]
    public void ALineWithItsBarMarkOnItIsABarAndReachesNoReader()
    {
        var fates = new List<PathFate>();
        // a 10 m bar, its label «12-15M12.6» 240 mm tall sitting 50 mm above it
        var g = FateFixture.Classify([FateFixture.Line(5000, 10000, 15000, 10000)], fates, result: With(("12-15M12.6", 8000, 10050, 9500, 10290)));
        var fate = Assert.Single(fates);
        Assert.Equal(PathReason.BarRun, fate.Reason);
        Assert.Equal(Disposition.Discarded, fate.Disposition);
        Assert.Empty(g.Lines);
        Assert.Empty(g.StrokesOnGrid);
    }

    [Fact]
    public void ALabelAlongAVerticalBarReadsByItsShortSide()
    {
        var fates = new List<PathFate>();
        // the label runs up the bar: its box is 240 wide and 1,500 tall, centred 120 mm off the line
        var g = FateFixture.Classify([FateFixture.Line(10000, 5000, 10000, 15000)], fates, result: With(("C15M18.0", 10040, 8000, 10280, 9500)));
        Assert.Equal(PathReason.BarRun, Assert.Single(fates).Reason);
        Assert.Empty(g.Lines);
    }

    [Theory]
    [InlineData("12-15M12.6", 8000, 10400, 9500, 10640, 5000, 15000, "a label a line-height off the line is a leader's or a dimension's")]
    [InlineData("SLAB", 8000, 10050, 9500, 10290, 5000, 15000, "a word that is no bar mark names no bar")]
    [InlineData("12-15M12.6", 15100, 10050, 16600, 10290, 5000, 15000, "a label beyond the line's end is another line's")]
    [InlineData("12-15M12.6", 5100, 10050, 5400, 10290, 5000, 5500, "a stroke under 600 mm is a tick or a leader, not a run")]
    public void ALineStaysALineWithoutItsMarkOnIt(string text, double x0, double y0, double x1, double y1, double lineX0, double lineX1, string because)
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify([FateFixture.Line(lineX0, 10000, lineX1, 10000)], fates, result: With((text, x0, y0, x1, y1)));
        Assert.True(Assert.Single(fates).Reason == PathReason.EmittedAsLine, because);
        Assert.Single(g.Lines);
    }

    [Theory]
    [InlineData("15M", true)]
    [InlineData("20M", true)]
    [InlineData("12-15M12.6", true)]
    [InlineData("C15M18.0", true)]
    [InlineData("6-C15M21.4", true)]
    [InlineData("15M@12", true)]
    [InlineData("#5@12\"", true)]
    [InlineData("#5@12", true)]
    [InlineData("#5", false)]                       // a bare number sign is the architect's keynote (31170: #1-#4, 82 of them)
    [InlineData("#3", false)]
    [InlineData("12'-6\"", false)]
    [InlineData("8\"", false)]
    [InlineData("SLAB", false)]
    [InlineData("M15", false)]
    [InlineData("150", false)]
    [InlineData("15MM", false)]
    [InlineData("S2.03", false)]
    public void TheBarGrammarIsTheOffices(string word, bool isABarMark)
        => Assert.Equal(isABarMark, GeometryFilterService.BarMark.IsMatch(word));
}
