#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Kor.Operations.StandardDetails;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Locks the shape of the on-demand review set: cover/index pages, then exactly one page per detail,
/// each detail page sized to its own stored art plus the header strip; a detail with no stored PDF or
/// a corrupt one still gets a page (placeholder) so the set stays complete and the gap is visible.
///
/// COVERS: page count against ExpectedPageCount for small and index-spilling sets; per-page size
/// follows the art; missing and corrupt art degrade to a page, not a throw; an empty set is refused.
/// DOES NOT COVER (no rasteriser here): that the header text, index text or stamp are legible or
/// positioned; that the art lands unclipped under the header. Verified by opening the emitted PDF
/// (written to %TEMP%\reviewset_verify.pdf) and LOOKING.
/// A same-class fault it would NOT catch: two items with the same DetailNumber, which produce two
/// identical pages and a duplicated index line.
/// </summary>
public sealed class ReviewSetComposerTests
{
    private static byte[] SyntheticPdf(double widthPt, double heightPt)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(widthPt);
        page.Height = XUnit.FromPoint(heightPt);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 200, 220, 240)), 0, 0, widthPt, heightPt);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static ReviewSetItem Item(string number, byte[]? pdf, string title = "A DETAIL")
        => new(number, title, "Concrete", "typical", "Approved", pdf);

    private static ReviewSetSpec Spec(params ReviewSetItem[] items)
        => new("Standard Details — Concrete", "Concrete · typical · all", "tester", new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc), items);

    [Fact]
    public void One_cover_page_then_one_page_per_detail_sized_to_its_art()
    {
        var bytes = ReviewSetComposer.Build(Spec(
            Item("KOR-D-00001", SyntheticPdf(400, 300)),
            Item("KOR-D-00002", SyntheticPdf(600, 200))));

        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(ReviewSetComposer.ExpectedPageCount(2), doc.PageCount);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(400, doc.Pages[1].Width.Point, 1);
        Assert.Equal(300 + 34, doc.Pages[1].Height.Point, 1);
        Assert.Equal(600, doc.Pages[2].Width.Point, 1);
        Assert.Equal(200 + 34, doc.Pages[2].Height.Point, 1);

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "reviewset_verify.pdf"), bytes);
    }

    [Fact]
    public void Missing_and_corrupt_art_still_get_a_page()
    {
        var bytes = ReviewSetComposer.Build(Spec(
            Item("KOR-D-00001", null),
            Item("KOR-D-00002", new byte[] { 1, 2, 3, 4, 5 }),
            Item("KOR-D-00003", SyntheticPdf(300, 300))));

        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(4, doc.PageCount);
    }

    [Fact]
    public void A_long_set_spills_its_index_onto_more_cover_pages()
    {
        var items = new List<ReviewSetItem>();
        for (var i = 1; i <= 80; i++)
        {
            items.Add(Item($"KOR-D-{i:00000}", null));
        }

        var bytes = ReviewSetComposer.Build(Spec(items.ToArray()));
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        Assert.Equal(ReviewSetComposer.ExpectedPageCount(80), doc.PageCount);
        Assert.Equal(3 + 80, doc.PageCount);
    }

    [Fact]
    public void An_empty_set_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => ReviewSetComposer.Build(Spec()));
    }
}
