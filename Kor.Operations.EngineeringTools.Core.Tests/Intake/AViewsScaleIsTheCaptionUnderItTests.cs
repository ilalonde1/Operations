#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A view's scale is the ratio in the caption under it (brief 28). On an AS NOTED sheet the title
/// block states nothing and each view's caption does; a ladder belongs to the caption nearest below it.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the imperial caption split by the word extractor into "1/8"" "=" "1'-0"", the
/// metric "1 : 100", the title-block fifth excluded, the choice of the caption below and nearest,
/// one ratio for the whole sheet when every caption agrees, and a ladder read at the caption's scale
/// with no sheet scale. WHAT IT DOES NOT: a real AS NOTED sheet — FiveStickFilesTests banks 31138
/// p53 — and a caption the extractor splits in a way this reader does not rejoin.
/// </remarks>
public sealed class AViewsScaleIsTheCaptionUnderItTests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);

    [Fact]
    public void AnImperialCaptionIsRejoinedAndAMetricOneReadWhole()
    {
        var page = new PC(1, W, H, new List<TT>
        {
            Tok("3", 900, 100), Tok("S3.11", 940, 100), Tok("1/8\"", 990, 100), Tok("=", 1012, 100), Tok("1'-0\"", 1040, 100),
            Tok("1 : 100", 1500, 100),
            Tok("1/8\"", 2900, 200), Tok("=", 2922, 200), Tok("1'-0\"", 2950, 200),   // the title block's own, excluded
        }, new List<GP>());
        var captions = ViewCaptions.Read(page);
        Assert.Equal(2, captions.Count);
        Assert.Contains(captions, c => c.Note == "1/8\" = 1'-0\"" && Math.Abs(c.XPts - 1015) < 1 && c.YPts == 100);
        Assert.Contains(captions, c => c.Note == "1:100" && c.XPts == 1500);
    }

    [Fact]
    public void ALadderTakesTheCaptionBelowAndNearestWhenTheSheetSaysAsNoted()
    {
        var captions = new List<ViewCaptions.Caption> { new("1/8\" = 1'-0\"", 1000, 80), new("1/4\" = 1'-0\"", 2000, 80), new("3/16\" = 1'-0\"", 1000, 1500) };
        Assert.Equal("1/8\" = 1'-0\"", ViewCaptions.For(captions, 1100, 300));       // below and nearest in x
        Assert.Equal("1/4\" = 1'-0\"", ViewCaptions.For(captions, 1900, 300));
        Assert.Null(ViewCaptions.For(captions, 1000, 50));                          // nothing below it
        var one = new List<ViewCaptions.Caption> { new("1/8\" = 1'-0\"", 1000, 80), new("1/8\" = 1'-0\"", 2000, 80) };
        Assert.Equal("1/8\" = 1'-0\"", ViewCaptions.For(one, 5, 5));                  // one ratio on the sheet: wherever the thing sits
    }

    [Fact]
    public void AnAsNotedSheetsLadderIsReadAtItsCaptionsScale()
    {
        var words = new List<TT>();
        foreach (var (level, y) in new[] { ("3", 1000.0), ("2", 900.0), ("1", 700.0) }) { words.Add(Tok("LEVEL", 1200, y)); words.Add(Tok(level, 1240, y)); }
        words.AddRange(new[] { Tok("1/8\"", 1180, 600), Tok("=", 1202, 600), Tok("1'-0\"", 1230, 600) });
        var page = new PC(1, W, H, words, new List<GP>());
        Assert.Empty(StoreyLadder.Read(page, null));                                   // no sheet scale, no captions: nothing
        var storeys = StoreyLadder.Read(page, null, ViewCaptions.Read(page));
        Assert.Equal(2, storeys.Count);
        Assert.Equal(3387, storeys[0].HeightMm, 0);                                    // 100 pt at 1:96
    }
}
