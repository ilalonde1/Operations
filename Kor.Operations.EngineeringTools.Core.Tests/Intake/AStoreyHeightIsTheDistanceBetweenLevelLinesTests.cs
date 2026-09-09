#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A storey height is the distance between two level lines on an elevation drawn to scale (brief 25).
/// The level ladder gives the lines' y; the sheet's scale turns each gap into millimetres.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: three level lines at a stated scale give two storeys of the right height in the
/// right order; no scale, or fewer than three lines, gives nothing; the typical height is the most
/// repeated one. WHAT IT DOES NOT: real sheets — FiveStickFilesTests banks 31168 p37 and 31130 p53
/// against the engineer's model — and the sheet-type gate, which is DrawingIntake's.
/// </remarks>
public sealed class AStoreyHeightIsTheDistanceBetweenLevelLinesTests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);

    private static PC Ladder(params (string Level, double Y)[] rows)
    {
        var words = new List<TT>();
        foreach (var (level, y) in rows) { words.Add(Tok("LEVEL", 2400, y)); words.Add(Tok(level, 2440, y)); }
        return new PC(1, W, H, words, new List<GP>());
    }

    [Fact]
    public void ThreeLevelLinesAtAnEighthInchScaleAreTwoStoreysOfTheDrawnHeight()
    {
        // 1/8" = 1'-0" is 1:96; 100 pt on paper = 100 × 25.4/72 × 96 = 3,387 mm
        var storeys = StoreyLadder.Read(Ladder(("3", 1000), ("2", 900), ("1", 700)), "1/8\" = 1'-0\"");
        Assert.Equal(2, storeys.Count);
        // names are the level reader's normalised form, whatever it is ("L3"), so the test states it through the same function
        Assert.Equal(ScheduleTakeoff.NormalizeLevel("LEVEL 3"), storeys[0].Level); Assert.Equal(ScheduleTakeoff.NormalizeLevel("LEVEL 2"), storeys[0].LevelBelow);
        Assert.Equal(3387, storeys[0].HeightMm, 0);
        Assert.Equal(ScheduleTakeoff.NormalizeLevel("LEVEL 2"), storeys[1].Level); Assert.Equal(ScheduleTakeoff.NormalizeLevel("LEVEL 1"), storeys[1].LevelBelow);
        Assert.Equal(6773, storeys[1].HeightMm, 0);
    }

    [Fact]
    public void NoScaleOrFewerThanThreeLinesGivesNoStorey()
    {
        Assert.Empty(StoreyLadder.Read(Ladder(("3", 1000), ("2", 900), ("1", 700)), null));
        Assert.Empty(StoreyLadder.Read(Ladder(("3", 1000), ("2", 900), ("1", 700)), "AS NOTED"));
        Assert.Empty(StoreyLadder.Read(Ladder(("2", 900), ("1", 700)), "1 : 100"));
    }

    [Fact]
    public void TheTypicalStoreyIsTheMostRepeatedHeight()
    {
        var storeys = StoreyLadder.Read(Ladder(("5", 1300), ("4", 1200), ("3", 1100), ("2", 1000), ("1", 800)), "1 : 100");
        // 100 pt at 1:100 = 3,528 mm three times; 200 pt once
        Assert.Equal(4, storeys.Count);
        Assert.Equal(3530, StoreyLadder.Typical(storeys)!.Value, 0);
    }
}
