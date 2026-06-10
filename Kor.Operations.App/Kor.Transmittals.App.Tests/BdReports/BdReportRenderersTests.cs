#nullable enable
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class BdReportRenderersTests
{
    private static BdReportDocument SampleDocument() =>
        new BdReportDocumentBuilder("KOR Structural — Test BD Report")
            .Italic("Compiled from honing verification.")
            .H2("Executive Summary")
            .B("PURSUE — open opportunities: ", "18")
            .P("Plain narrative paragraph with special chars: < > & \"quotes\".")
            .P(string.Empty)
            .H3("1. Plant and Animal Health Centre")
            .Table(
                new[] { "Id", "Project", "Province" },
                new[]
                {
                    new[] { "6585", "Plant and Animal Health Centre", "BC" },
                    new[] { "7036", "NACIC Edmonton" }, // short row — must pad
                })
            .Build();

    [Fact]
    public void Docx_RoundTrips_WithNativeHeadingStylesAndContent()
    {
        var bytes = DocxBuilder.Render(SampleDocument());

        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);

        var body = word.MainDocumentPart!.Document.Body!;
        var styles = word.MainDocumentPart.StyleDefinitionsPart!.Styles!
            .Elements<Style>().Select(s => s.StyleId!.Value).ToList();

        Assert.Contains("Normal", styles);
        Assert.Contains("Heading1", styles);
        Assert.Contains("Heading2", styles);
        Assert.Contains("Heading3", styles);

        var paragraphs = body.Elements<Paragraph>().ToList();
        Assert.Equal("Heading1", paragraphs[0].ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        Assert.Equal("KOR Structural — Test BD Report", paragraphs[0].InnerText);

        var h2 = paragraphs.Single(p => p.InnerText == "Executive Summary");
        Assert.Equal("Heading2", h2.ParagraphProperties?.ParagraphStyleId?.Val?.Value);

        // Label-value: bold label run followed by regular value run.
        var labelValue = paragraphs.Single(p => p.InnerText == "PURSUE — open opportunities: 18");
        var runs = labelValue.Elements<Run>().ToList();
        Assert.Equal(2, runs.Count);
        Assert.NotNull(runs[0].RunProperties?.GetFirstChild<Bold>());
        Assert.Null(runs[1].RunProperties?.GetFirstChild<Bold>());

        var table = body.Elements<Table>().Single();
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(3, rows.Count); // header + 2 data rows
        Assert.Equal(3, rows[2].Elements<TableCell>().Count()); // short row padded
        Assert.Contains("6585", rows[1].InnerText);
    }

    [Fact]
    public void Docx_PassesOpenXmlSchemaValidation()
    {
        var bytes = DocxBuilder.Render(SampleDocument());

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);

        var errors = new OpenXmlValidator().Validate(word).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void Html_RendersAllBlocks_AndEncodesText()
    {
        var html = HtmlPreviewBuilder.Render(SampleDocument());

        Assert.Contains("<h1>KOR Structural — Test BD Report</h1>", html);
        Assert.Contains("<h2>Executive Summary</h2>", html);
        Assert.Contains("<h3>1. Plant and Animal Health Centre</h3>", html);
        Assert.Contains("<p><b>PURSUE — open opportunities: </b>18</p>", html);
        Assert.Contains("<p class=\"note\">Compiled from honing verification.</p>", html);
        Assert.Contains("&lt; &gt; &amp; &quot;quotes&quot;", html);
        Assert.DoesNotContain("<quotes", html);
        Assert.Contains("<th>Project</th>", html);
        Assert.Contains("<td>6585</td>", html);

        // Short table row padded to header width: NACIC row gets 3 cells.
        Assert.Contains("<td>NACIC Edmonton</td><td></td>", html);
    }

    [Fact]
    public void Html_EncodesInjectionAttemptsFromData()
    {
        var doc = new BdReportDocumentBuilder("T")
            .P("<script>alert(1)</script>")
            .Build();

        var html = HtmlPreviewBuilder.Render(doc);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
