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
            .Kpis(
                new KpiItem("18", "Pursue", ChipTone.Positive),
                new KpiItem("$2.3B", "Pipeline (CAD)"))
            .Chips(new ChipItem("PURSUE_URGENT", ChipTone.Urgent))
            .B("PURSUE — open opportunities: ", "18")
            .P("Plain narrative paragraph with special chars: < > & \"quotes\".")
            .P(string.Empty)
            .H3("1. Plant and Animal Health Centre", KorReportLinks.Mpi(6585))
            .B("Architect: ", "Dikeakos", KorReportLinks.Org(42))
            .Table(
                new[] { "Id", "Project", "Province" },
                new[]
                {
                    new[] { "6585", "Plant and Animal Health Centre", "BC" },
                    new[] { "7036", "NACIC Edmonton" }, // short row — must pad
                },
                new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left },
                new[]
                {
                    new string?[] { null, "kor://mpi/6585", null },
                    new string?[] { null, "kor://mpi/7036", null },
                })
            .Build();

    [Fact]
    public void Docx_RoundTrips_WithNativeHeadingStylesAndContent()
    {
        var bytes = DocxBuilder.Render(SampleDocument());

        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);

        var main = word.MainDocumentPart!;
        var body = main.Document!.Body!;
        var styles = main.StyleDefinitionsPart!.Styles!
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

        // kor:// links are a preview-only affordance: DOCX renders the same
        // text with NO hyperlinks, so preview and export content stay equal.
        Assert.Contains(paragraphs, p => p.InnerText == "1. Plant and Animal Health Centre");
        Assert.Contains(paragraphs, p => p.InnerText == "Architect: Dikeakos");
        Assert.Empty(body.Descendants<Hyperlink>());

        // Label-value: bold label run followed by regular value run.
        var labelValue = paragraphs.Single(p => p.InnerText == "PURSUE — open opportunities: 18");
        var runs = labelValue.Elements<Run>().ToList();
        Assert.Equal(2, runs.Count);
        Assert.NotNull(runs[0].RunProperties?.GetFirstChild<Bold>());
        Assert.Null(runs[1].RunProperties?.GetFirstChild<Bold>());

        // Two tables now: the KPI strip and the data table (the data table is
        // the one with a 100%-width tblW).
        var tables = body.Elements<Table>().ToList();
        Assert.Equal(2, tables.Count);

        var kpiStrip = tables[0];
        var kpiCells = kpiStrip.Elements<TableRow>().Single().Elements<TableCell>().ToList();
        Assert.Equal(2, kpiCells.Count);
        Assert.Contains("18", kpiCells[0].InnerText);
        Assert.Contains("Pursue", kpiCells[0].InnerText);
        // Toned KPI: value run picks up the chip palette color.
        var kpiValueRun = kpiCells[0].Descendants<Run>().First();
        Assert.Equal(KorReportStyles.ChipPositiveColor, kpiValueRun.RunProperties?.GetFirstChild<Color>()?.Val?.Value);

        // Chip row: shaded bold white run on the urgent tone.
        var chipRun = body.Elements<Paragraph>()
            .SelectMany(p => p.Elements<Run>())
            .Single(r => r.RunProperties?.GetFirstChild<Shading>()?.Fill?.Value == KorReportStyles.ChipUrgentColor);
        Assert.Contains("PURSUE_URGENT", chipRun.InnerText);
        Assert.Equal("FFFFFF", chipRun.RunProperties?.GetFirstChild<Color>()?.Val?.Value);

        var table = tables[1];
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(3, rows.Count); // header + 2 data rows
        Assert.Equal(3, rows[2].Elements<TableCell>().Count()); // short row padded
        Assert.Contains("6585", rows[1].InnerText);

        // Header row is brand-shaded with white text; Id column right-aligns.
        var headerCell = rows[0].Elements<TableCell>().First();
        Assert.Equal(KorReportStyles.BrandColor, headerCell.GetFirstChild<TableCellProperties>()?.GetFirstChild<Shading>()?.Fill?.Value);
        var idCellJustification = rows[1].Elements<TableCell>().First()
            .Descendants<Justification>().Single();
        Assert.Equal(JustificationValues.Right, idCellJustification.Val?.Value);

        // Zebra shading lands on the second data row, matching nth-child(even).
        Assert.Equal(KorReportStyles.ZebraFillColor,
            rows[2].Elements<TableCell>().First().GetFirstChild<TableCellProperties>()?.GetFirstChild<Shading>()?.Fill?.Value);
        Assert.Null(rows[1].Elements<TableCell>().First().GetFirstChild<TableCellProperties>()?.GetFirstChild<Shading>());
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
        Assert.Contains("<p><b>PURSUE — open opportunities: </b>18</p>", html);

        // kor:// drill-down anchors: linked heading, linked label value,
        // linked table cell; unlinked cells stay plain.
        Assert.Contains("<h3><a href=\"kor://mpi/6585\">1. Plant and Animal Health Centre</a></h3>", html);
        Assert.Contains("<p><b>Architect: </b><a href=\"kor://org/42\">Dikeakos</a></p>", html);
        Assert.Contains("<td><a href=\"kor://mpi/7036\">NACIC Edmonton</a></td>", html);
        Assert.Contains("<p class=\"note\">Compiled from honing verification.</p>", html);
        Assert.Contains("&lt; &gt; &amp; &quot;quotes&quot;", html);
        Assert.DoesNotContain("<quotes", html);
        Assert.Contains("<th>Project</th>", html);

        // Id column carries the right-align class on header + data cells.
        Assert.Contains("<th class=\"r\">Id</th>", html);
        Assert.Contains("<td class=\"r\">6585</td>", html);

        // Short table row padded to header width: NACIC row gets 3 cells
        // (the padded empty cell follows the linked project cell).
        Assert.Contains("<td><a href=\"kor://mpi/7036\">NACIC Edmonton</a></td><td></td>", html);

        // KPI strip: card with value + uppercase-styled label; toned card
        // picks up the chip palette color.
        Assert.Contains("class=\"kpis\"", html);
        Assert.Contains($"style=\"border-top-color:#{KorReportStyles.ChipPositiveColor}\"", html);
        Assert.Contains(">18</div><div class=\"l\">Pursue</div>", html);
        Assert.Contains(">$2.3B</div><div class=\"l\">Pipeline (CAD)</div>", html);

        // Chip row: urgent-tone pill.
        Assert.Contains($"<span class=\"chip\" style=\"background:#{KorReportStyles.ChipUrgentColor}\">PURSUE_URGENT</span>", html);
    }

    [Fact]
    public void Html_RaggedCellLinks_AndUnparseableLinks_DegradeToPlainText()
    {
        var doc = new BdReportDocumentBuilder("T")
            // Unknown link kind: renders plain, no anchor.
            .H3("No Surface Person", "kor://bogus/9")
            .B("Empty value: ", string.Empty, "kor://mpi/1") // empty inner -> no invisible anchor
            .Table(
                new[] { "A", "B" },
                new[]
                {
                    new[] { "r1a", "r1b" },
                    new[] { "r2a", "r2b" },
                },
                alignments: null,
                // Ragged matrix: covers only row 0, and only its first cell.
                cellLinks: new[] { (IReadOnlyList<string?>)new string?[] { "kor://mpi/5" } })
            .Build();

        var html = HtmlPreviewBuilder.Render(doc);

        Assert.Contains("<h3>No Surface Person</h3>", html);
        Assert.DoesNotContain("kor://bogus", html);
        Assert.Contains("<p><b>Empty value: </b></p>", html);
        Assert.Contains("<td><a href=\"kor://mpi/5\">r1a</a></td>", html);
        Assert.Contains("<td>r1b</td>", html); // beyond the short link row
        Assert.Contains("<td>r2a</td>", html); // beyond the short matrix
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
