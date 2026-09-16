#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WORDS ARE ON ONE BASELINE WHEN THEIR BASELINES ARE CLOSE, NOT WHEN THEY ROUND TO THE SAME POINT (step 81).
/// 01379's S231.3: "Concrete Outline - Level 23 Plan B - West Tower" with "23" at 1201.506 pt and the rest at
/// 1201.478 pt split into two lines under Math.Round, neither named a plan, and the sheet's outline view was lost
/// into its reinforcing view's file - which the composer then set 37.6 m off. Reproduced here at the page's own
/// numbers.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: <see cref="TextBaselines.Lines"/> joining tokens a fraction of a height apart across an integer
/// boundary; a dash a quarter-height above the words staying its own line (as it did before, so view titles keep
/// their names); a gap of more than two heights splitting a baseline into two lines; <see cref="SheetViews.Titles"/>
/// finding the title from those tokens with its underline. WHAT IT DOES NOT: the ten other readers that still group
/// by a rounded bin (the schedule readers at 6 pt, the grid reader at 12 pt, the title reader at a line height) -
/// named in the plan as this class's remaining instances.
/// </remarks>
public sealed class WordsOnOneBaselineAreOneLineTests
{
    private static VectorPageReader.TextToken Tok(string text, double x, double cy, double h = 14.0, double w = 40) =>
        new(text, x + w / 2, cy, x, cy - h / 2, x + w, cy + h / 2);

    [Fact]
    public void TokensAThirtiethOfAPointApartAcrossAHalfAreOneLineAndADashIsNot()
    {
        // S231.3's title, as the page draws it (MinX, Cy, height, width in points): the dashes 4 pt above the words
        var words = new[]
        {
            Tok("Concrete", 2168.0, 1201.597, 14.23, 78.5), Tok("Outline", 2251.8, 1201.602, 14.24, 61.3), Tok("-", 2318.3, 1197.445, 5.93, 6.5),
            Tok("Level", 2330.0, 1201.478, 13.99, 46.3), Tok("23", 2381.5, 1201.506, 14.05, 21.7), Tok("Plan", 2408.4, 1201.478, 13.99, 38.7),
            Tok("B", 2452.3, 1201.478, 13.99, 13.0), Tok("-", 2470.6, 1197.445, 5.93, 6.5), Tok("West", 2482.3, 1201.478, 13.99, 44.3), Tok("Tower", 2531.9, 1201.478, 13.99, 53.8),
            Tok("1/8\"", 2171.0, 1182.402, 6.80, 16.2),
        };

        var lines = TextBaselines.Lines(words).Select(l => string.Join(" ", l.Select(t => t.Text))).ToList();

        Assert.Contains("Concrete Outline Level 23 Plan B West Tower", lines);
        Assert.Equal(2, lines.Count(l => l == "-"));   // the dashes: 4 pt above the words, their own lines (152 pt apart, not one run)
        Assert.Contains("1/8\"", lines);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void AGapWiderThanTwoHeightsSplitsABaselineIntoTwoLines()
    {
        var words = new[] { Tok("LEVEL", 100, 50.2, 10, 40), Tok("3", 145, 49.9, 10, 10), Tok("LEVEL", 400, 50.0, 10, 40), Tok("4", 445, 50.3, 10, 10) };
        var lines = TextBaselines.Lines(words).Select(l => string.Join(" ", l.Select(t => t.Text))).ToList();
        Assert.Equal(new[] { "LEVEL 3", "LEVEL 4" }, lines);
    }

    [Fact]
    public void TheSheetsSecondViewIsFoundFromTokensThatStraddleTheRounding()
    {
        var words = new List<VectorPageReader.TextToken>
        {
            Tok("Concrete", 2168.0, 1201.597, 14.23, 78.5), Tok("Outline", 2251.8, 1201.602, 14.24, 61.3), Tok("-", 2318.3, 1197.445, 5.93, 6.5),
            Tok("Level", 2330.0, 1201.478, 13.99, 46.3), Tok("23", 2381.5, 1201.506, 14.05, 21.7), Tok("Plan", 2408.4, 1201.478, 13.99, 38.7),
            Tok("B", 2452.3, 1201.478, 13.99, 13.0), Tok("-", 2470.6, 1197.445, 5.93, 6.5), Tok("West", 2482.3, 1201.478, 13.99, 44.3), Tok("Tower", 2531.9, 1201.478, 13.99, 53.8),
            Tok("Reinforcing", 813.1, 89.316, 14.23, 97.6), Tok("-", 916.0, 85.164, 5.93, 6.5), Tok("Level", 927.7, 89.197, 13.99, 46.5), Tok("23", 979.3, 89.225, 14.05, 21.5),
            Tok("Plan", 1006.2, 89.197, 13.99, 38.6), Tok("B", 1050.0, 89.197, 13.99, 13.0), Tok("-", 1068.4, 85.164, 5.93, 6.5), Tok("West", 1080.1, 89.197, 13.99, 44.2), Tok("Tower", 1129.7, 89.197, 13.99, 53.8),
        };
        // the two underlines, as the page draws them (the second stops 14 pt short of the words' end)
        var paths = new List<VectorPageReader.GeomPath>
        {
            Stroke(2166, 1190.3, 2589, 1190.3),
            Stroke(811, 78.0, 1179, 78.0),
        };
        var page = new VectorPageReader.PageContent(138, 3456, 2592, words, paths);

        var views = SheetViews.Titles(page);

        Assert.Equal(new[] { "Reinforcing Level 23 Plan B West Tower", "Concrete Outline Level 23 Plan B West Tower" }, views.Select(v => v.Title));
    }

    private static VectorPageReader.GeomPath Stroke(double x0, double y0, double x1, double y1) =>
        new(new List<(double X, double Y)> { (x0, y0), (x1, y1) }, IsClosed: false, IsFilled: false, IsStroked: true,
            Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
}
