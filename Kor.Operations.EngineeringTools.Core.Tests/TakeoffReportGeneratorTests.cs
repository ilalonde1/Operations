#nullable enable

using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class TakeoffReportGeneratorTests
{
    [Fact]
    public void BuildXlsxProducesDeltaWorksheetWithTotals()
    {
        var bytes = TakeoffReportGenerator.BuildXlsx(BuildModel());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Delta");

        Assert.NotNull(worksheet);

        var totalRow = worksheet.RowsUsed().Single(r => r.Cell(1).GetString() == "TOTAL");
        Assert.Equal(4, totalRow.Cell(8).GetValue<double>());    // Δ Concrete (m³)
        Assert.Equal("Status", worksheet.Cell(5, 9).GetString()); // rebar columns removed; Status now col 9
    }

    [Fact]
    public void BuildDocxProducesReadableReportContent()
    {
        var bytes = TakeoffReportGenerator.BuildDocx(BuildModel());

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("31065-01", text);
        Assert.Contains("for information only", text);
        Assert.Contains("P1P", text);
        Assert.Contains("4", text);
    }

    [Fact]
    public void BuildHtmlProducesReadableReportContent()
    {
        var html = TakeoffReportGenerator.BuildHtml(BuildModel());

        Assert.Contains("for information only", html);
        Assert.Contains("TOTAL", html);
        Assert.Contains("4", html);
        Assert.Contains("P1P", html);
    }

    [Fact]
    public void BuildHtmlIncludesBasisMismatchWarningOnlyWhenNeeded()
    {
        Assert.Contains("different bases", TakeoffReportGenerator.BuildHtml(BuildModel(basisMismatch: true)));
        Assert.DoesNotContain("different bases", TakeoffReportGenerator.BuildHtml(BuildModel(basisMismatch: false)));
    }

    [Fact]
    public void BuildHtmlIsDeterministicForSameModel()
    {
        var model = BuildModel();

        Assert.Equal(
            TakeoffReportGenerator.BuildHtml(model),
            TakeoffReportGenerator.BuildHtml(model));
    }

    private static TakeoffReportModel BuildModel(bool basisMismatch = false)
    {
        var line = new TakeoffDiffLine(
            "L1",
            TakeoffElementType.Slab,
            "C30",
            ConcreteBeforeM3: 10,
            ConcreteAfterM3: 14,
            ConcreteDeltaM3: 4,
            FormworkBeforeM2: 0,
            FormworkAfterM2: 5,
            FormworkDeltaM2: 5,
            RebarBeforeKg: 0,
            RebarAfterKg: 400,
            RebarDeltaKg: 400,
            TakeoffDiffStatus.Matched);

        var diff = new TakeoffDiff(
            new[] { line },
            TotalConcreteDeltaM3: 4,
            TotalFormworkDeltaM2: 5,
            TotalRebarDeltaTonnes: 0.4,
            AddedLevels: new[] { "P1P" },
            RemovedLevels: Array.Empty<string>(),
            BasisMismatch: basisMismatch);

        return new TakeoffReportModel(
            ProjectWbs1: "31065-01",
            ProjectName: "Test",
            IssueBeforeLabel: "IFT",
            IssueAfterLabel: "IFC",
            GeneratedAtUtc: new DateTime(2026, 4, 6),
            Diff: diff);
    }
}