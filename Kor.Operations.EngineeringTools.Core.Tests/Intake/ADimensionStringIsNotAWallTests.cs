#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A dimension string is not a wall (intake step 35). Two stacked dimension lines run an inch apart
/// on paper — four feet at 1/4" — and the two-face reader pairs them as a wall four feet thick and a
/// bay long, outside the building. A wall carries its thickness inside its faces, never its length:
/// a wall whose outline holds a length written along it is a dimension string, flagged so nothing
/// writes it, tags it or counts it.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a feet-and-inches string inside a horizontal pair flagged; a wall with no word
/// inside it not; a thickness written inside a wall ("8"", equal to its thickness) not; a vertical
/// string inside a vertical pair flagged and a horizontal word inside a vertical pair not; the
/// count returned; the tagging leaving a flagged wall untyped and no partition. WHAT IT DOES NOT:
/// the real sheets (31170: two above and three below every storey's plate, 2026-09-10, and the
/// six-set run); the exporter leaving the wall out (`DxfExporter`, measured on the DXF census, not
/// banked here); a string whose text the tokeniser split; a real wall with its length written
/// inside it, which this rule would flag.
/// </remarks>
public sealed class ADimensionStringIsNotAWallTests
{
    private const double W = 3024, H = 2160;
    private const int Scale = 48;
    private const double MmPerPt = Scale * PdfToSafeConstants.PointsToMm;
    private static double Pt(double mm) => mm / MmPerPt;
    private static TT Word(string text, double xMm, double yMm, bool vertical = false) =>
        vertical ? new(text, Pt(xMm), Pt(yMm), Pt(xMm) - 3, Pt(yMm) - 12, Pt(xMm) + 3, Pt(yMm) + 12)
                 : new(text, Pt(xMm), Pt(yMm), Pt(xMm) - 12, Pt(yMm) - 3, Pt(xMm) + 12, Pt(yMm) + 3);

    /// <summary>A wall (or a pair of dimension lines) from (x0,y0) to (x1,y1), t mm thick, as the two-face reader would box it.</summary>
    private static WallPanel Pair(double x0, double y0, double x1, double y1, double t)
    {
        bool vertical = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
        return vertical
            ? new([(x0 - t / 2, y0), (x0 + t / 2, y0), (x1 + t / 2, y1), (x1 - t / 2, y1)], (x0, y0), (x1, y1), t)
            : new([(x0, y0 - t / 2), (x1, y1 - t / 2), (x1, y1 + t / 2), (x0, y0 + t / 2)], (x0, y0), (x1, y1), t);
    }

    private static (ExtractedGeometry, IReadOnlyList<DimensionStrings.Dimension>) Read(IEnumerable<WallPanel> walls, params TT[] words)
    {
        var g = new ExtractedGeometry { ScaleDenominator = Scale };
        g.Walls.AddRange(walls);
        var page = new PC(1, W, H, words.ToList(), new List<GP>());
        var dims = DimensionStrings.Read(page, [], MmPerPt);
        return (g, dims);
    }

    [Fact]
    public void ALengthWrittenAlongAPairMakesItADimensionString()
    {
        var (g, dims) = Read([Pair(0, 0, 8900, 0, 1219), Pair(0, 6000, 8900, 6000, 300)],   // two stacked strings 4 ft apart; a real wall
                             Word("29'-3\"", 4450, 200));                                    // the length, between the two lines
        int n = DimensionStrings.StandDownWalls(g, dims);
        Assert.Equal(1, n);
        Assert.Equal([true, false], g.WallIsDimensionString);
    }

    [Fact]
    public void AThicknessWrittenInsideAWallIsTheWallsOwn()
    {
        var (g, dims) = Read([Pair(0, 0, 8900, 0, 203)], Word("8\"", 4450, 0));            // 8" inside an 8" wall: its thickness, not a length
        Assert.Equal(0, DimensionStrings.StandDownWalls(g, dims));
        Assert.Equal([false], g.WallIsDimensionString);
    }

    [Fact]
    public void TheWordMustRunTheWallsWay()
    {
        var (g, dims) = Read([Pair(0, 0, 0, 5000, 610), Pair(2000, 0, 2000, 5000, 610)],
                             Word("13'-3\"", 0, 2500, vertical: true),                       // reads down the vertical string
                             Word("13'-3\"", 2000, 2500));                                   // a horizontal word across a vertical pair: not its string
        Assert.Equal(1, DimensionStrings.StandDownWalls(g, dims));
        Assert.Equal([true, false], g.WallIsDimensionString);
    }

    [Fact]
    public void AFlaggedWallTakesNoTagAndIsNoPartition()
    {
        var legend = new List<AssemblySchedule.Assembly>
        {
            new("WALL", "S8.1", "STEEL STUD PARTY WALL", ["STEEL STUDS"], new Dictionary<string, string>(), [], [], AssemblySchedule.Material.Stud, 41, 3),
        };
        var (g, dims) = Read([Pair(0, 0, 8900, 0, 1219)], Word("29'-3\"", 4450, 200), Word("S8.1", 4450, 900));
        DimensionStrings.StandDownWalls(g, dims);
        var words = new List<TT> { Word("29'-3\"", 4450, 200), Word("S8.1", 4450, 900) };
        for (int i = 0; i < WallTypeTagging.TaggingSheetMinTags; i++) words.Add(Word("S8.1", 20000 + i * 50, 20000));   // a sheet that tags
        var page = new PC(1, W, H, words, new List<GP>());
        WallTypeTagging.Apply(g, page, SheetFurniture.On(page, 0), legend);
        Assert.Equal([null], g.WallTypeCodes);                                              // not typed by the tag beside it
        Assert.Equal([false], g.WallIsPartition);                                            // and not "untagged on a tagging sheet" either
    }
}
