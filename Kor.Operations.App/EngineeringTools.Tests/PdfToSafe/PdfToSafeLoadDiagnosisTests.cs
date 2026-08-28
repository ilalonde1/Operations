#nullable enable
using System;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// A tool that finds nothing has to say what it looked for.
///
/// Three real issued sets — 31065 IFC, its IFT addendum, and a 31202 reinforcing set — each held
/// thousands of vector paths and yielded ZERO slabs, columns and lines, because this reader takes
/// Bluebeam markup annotations and an issued drawing has none. The window showed
/// "Vector PDF detected. Ready for configuration and export." in green, and then produced an empty
/// model. That is indistinguishable from broken, and it is why the tool sat unused while an
/// engineer traced prelims by hand.
/// </summary>
public sealed class PdfToSafeLoadDiagnosisTests
{
    private static ExtractedGeometry Vector(int rawPaths) =>
        new() { IsVectorPdf = true, RawPathCount = rawPaths };

    [Fact]
    public void APageFullOfVectorsThatYieldsNothingSaysWhatItLookedFor()
    {
        string said = PdfToSafeWindow.DiagnoseLoad(Vector(7_098));

        Assert.Contains("7,098 vector paths", said);
        Assert.Contains("no page in this document carries markups", said);
        Assert.Contains("Bluebeam", said);

        // And it points at the tool that CAN read an issued set.
        Assert.Contains("Drawings to ETABS Model", said);

        // The one thing it must never do is call that success.
        Assert.DoesNotContain("Ready for configuration and export", said);
    }

    [Fact]
    public void APageThatYieldsMarkupsSaysHowMuchItRead()
    {
        var g = Vector(4_200);
        g.Slabs.Add(new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) });
        g.Columns.Add((500, 500));
        g.ColumnSizes.Add((400, 400));

        string said = PdfToSafeWindow.DiagnoseLoad(g);

        Assert.Contains("1 slab", said);
        Assert.Contains("1 column", said);
        Assert.Contains("Ready for configuration and export", said);
    }

    [Fact]
    public void ABarePageNamesThePageThatDoesCarryTheMarkup()
    {
        // Andrea's parking mark-up is 41 pages: nothing on the first eleven, 216 annotations on
        // page 12. Telling someone standing on page 1 that the document has no markups is wrong,
        // and it is why they stop trying.
        string said = PdfToSafeWindow.DiagnoseLoad(Vector(292), markedPages: new[] { 12, 18 }, currentPage: 1);

        Assert.Contains("pages 12, 18 do", said);
        Assert.Contains("go to that page", said);
        Assert.DoesNotContain("no page in this document", said);
    }

    [Fact]
    public void OnePageWithMarkupIsNamedInTheSingular()
    {
        string said = PdfToSafeWindow.DiagnoseLoad(Vector(292), markedPages: new[] { 12 }, currentPage: 1);
        Assert.Contains("page 12 does", said);
    }

    [Fact]
    public void ADocumentWithNoMarkupAnywhereSaysSoRatherThanSendingYouHunting()
    {
        string said = PdfToSafeWindow.DiagnoseLoad(Vector(7_098), markedPages: Array.Empty<int>(), currentPage: 1);

        Assert.Contains("no page in this document carries markups", said);
        Assert.Contains("Drawings to ETABS Model", said);
    }

    [Fact]
    public void TheMarkedPageDoesNotTellYouToGoToItself()
    {
        // Standing on page 12 with the markup filtered out by scale or size, "go to page 12" is noise.
        string said = PdfToSafeWindow.DiagnoseLoad(Vector(3_686), markedPages: new[] { 12 }, currentPage: 12);
        Assert.Contains("no page in this document carries markups", said);
    }

    [Fact]
    public void ARasterPdfStillSaysItIsRaster()
    {
        string said = PdfToSafeWindow.DiagnoseLoad(new ExtractedGeometry { IsVectorPdf = false });

        Assert.Contains("Raster or image-only", said);
        Assert.DoesNotContain("Bluebeam", said);
    }
}
