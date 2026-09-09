#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A plan too wide for one sheet is split on a match line, and the sheet says so: the words MATCH
/// LINE beside a dash-dot line spanning the drawing (intake step 22). The furniture reads it, the
/// classifier reads the line's pieces as it, the DXF carries it on a MATCH layer, and the DXF side's
/// sheet join sees the same seam on both halves.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a stacked MATCH / LINE label in the margin beside a dashed horizontal line
/// spanning the page; a vertical one; no label, no match line; a label beside a short line, none;
/// the line's pieces fated MatchLine and not emitted as beams; the DXF's MATCHLINE layer and the
/// seam the DXF side reads from it. WHAT IT DOES NOT: the join itself on a real set (31168's P2
/// halves, measured in the PDF-only build); a match line drawn at an angle; two match lines on one
/// sheet with one label.
/// </remarks>
public sealed class APlanTooWideForOneSheetIsSplitOnAMatchLineTests
{
    private const double W = 3024, H = 2160;   // a 42 x 30 in sheet in points

    private static VectorPageReader.TextToken Word(string text, double cx, double cy, double h = 8)
        => new(text, cx, cy, cx - 2 * h, cy - h / 2, cx + 2 * h, cy + h / 2);

    private static VectorPageReader.GeomPath Seg(double x0, double y0, double x1, double y1)
        => new([(x0, y0), (x1, y1)], false, false, true, Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));

    private static IEnumerable<VectorPageReader.GeomPath> Dashed(double x0, double y0, double x1, double y1, int pieces = 40)
    {
        for (int i = 0; i < pieces; i++)
        {
            double t0 = (double)i / pieces, t1 = t0 + 0.6 / pieces;
            yield return Seg(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0, x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1);
        }
    }

    private static VectorPageReader.PageContent Page(IEnumerable<VectorPageReader.TextToken> words, IEnumerable<VectorPageReader.GeomPath> paths)
        => new(1, W, H, words.ToList(), paths.ToList());

    [Fact]
    public void AStackedLabelBesideADashedLineSpanningThePageIsTheMatchLine()
    {
        var page = Page([Word("MATCH", 2900, 1000), Word("LINE", 2900, 1010)], Dashed(100, 1000, 2850, 1000));
        var m = Assert.Single(SheetFurniture.MatchLines(page));
        Assert.Equal(1000, m.Y0, 1); Assert.Equal(1000, m.Y1, 1);
        Assert.InRange(m.X0, 99, 101); Assert.InRange(m.X1, 2800, 2850);
    }

    [Fact]
    public void ATurnedLabelBesideAVerticalLineReadsTheVerticalOne()
    {
        // 31065: one word MATCHLINE, written up the page (6 pt wide, 48 pt tall) at the seam's end,
        // with a longer horizontal line (the drawing's north edge) close by — the label's shape decides
        var turned = new VectorPageReader.TextToken("MATCHLINE", 1504, 2040, 1501, 2016, 1507, 2064);
        var page = Page([turned], Dashed(1500, 60, 1500, 2100).Concat(Dashed(100, 2070, 2900, 2070)));
        var m = Assert.Single(SheetFurniture.MatchLines(page));
        Assert.Equal(1500, m.X0, 1); Assert.Equal(1500, m.X1, 1);
    }

    [Fact]
    public void AFlatLabelBesideAHorizontalLineIgnoresAVerticalOneCloseBy()
    {
        var page = Page([Word("MATCH", 2900, 1000), Word("LINE", 2900, 1010)],
            Dashed(100, 1000, 2850, 1000).Concat(Dashed(2905, 60, 2905, 2100)));
        var m = Assert.Single(SheetFurniture.MatchLines(page));
        Assert.Equal(1000, m.Y0, 1); Assert.Equal(1000, m.Y1, 1);
    }

    [Fact]
    public void ALineDrawnAsTwoStrokesAPointApartIsOneLine()
    {
        // a thick pen drawn as two strokes, or dashes whose pieces sit either side of a whole
        // point: two buckets, neither spanning the page, and the seam was lost (Codex 31, F11)
        var page = Page([Word("MATCH", 2900, 1000), Word("LINE", 2900, 1010)],
            Dashed(100, 1000.4, 1400, 1000.4).Concat(Dashed(1400, 1000.6, 2850, 1000.6)));
        var m = Assert.Single(SheetFurniture.MatchLines(page));
        Assert.InRange(m.X0, 99, 101); Assert.InRange(m.X1, 2800, 2850);
    }

    [Fact]
    public void NoLabelNoMatchLine()
    {
        Assert.Empty(SheetFurniture.MatchLines(Page([Word("SLAB", 2900, 1000)], Dashed(100, 1000, 2850, 1000))));
    }

    [Fact]
    public void ALabelBesideAShortLineIsNotOne()
    {
        Assert.Empty(SheetFurniture.MatchLines(Page([Word("MATCH", 2900, 1000), Word("LINE", 2900, 1010)], Dashed(2600, 1000, 2850, 1000))));
    }

    [Fact]
    public void ThePiecesAreReadAsTheMatchLineAndTheDxfCarriesIt()
    {
        // the furniture in mm, as the classifier takes it (a 1:96 sheet: one point is 33.867 mm)
        double k = 96 * 25.4 / 72;
        var set = new SheetFurniture.Set([], [], [], 1.5, [], 1) { MatchLines = [new(100 * k, 1000 * k, 2850 * k, 1000 * k)] };
        var pieces = Enumerable.Range(0, 10).Select(i => FateFixture.Line(200 * k + i * 250 * k, 1000 * k, 350 * k + i * 250 * k, 1000 * k)).ToList();
        var (g, fates) = WallFixture.Read(pieces, furniture: set);
        Assert.All(fates, f => Assert.Equal(PathReason.MatchLine, f.Reason));
        Assert.All(fates, f => Assert.Equal(Disposition.Read, f.Disposition));
        Assert.Empty(g.Lines);
        var m = Assert.Single(g.MatchLines);
        Assert.Equal(100 * k, m.Start.X, 1);

        string path = Path.GetTempFileName();
        try
        {
            g.Walls.Add(new WallPanel([(0, 0), (6000, 0), (6000, 300), (0, 300)], (0, 150), (6000, 150), 300));   // something to centre on
            g.WallColors.Add((0, 0, 0)); g.WallIsAnnotation.Add(false);
            DxfExporter.Export(g, path, korLayers: true);
            var segments = DxfPlanReader.ReadSegments(path);
            var seam = MatchLineSheetJoin.SeamOf(segments);
            Assert.NotNull(seam);
            Assert.Equal(2750 * k, seam!.Start.DistanceTo(seam.End), 1);
            Assert.Contains(segments, s => s.Layer == "KOR_MATCHLINE");
        }
        finally { File.Delete(path); }
    }
}
