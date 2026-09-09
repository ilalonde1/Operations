using System.Globalization;
using System.Text;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Reads a real, synthetic PDF with a column schedule/furniture box, column and plan mark,
/// labelled grid axis, free line, annotation and link. Also compares the old and new reader's
/// geometry and exported DXF bytes on this fixture. Does not replace the stick-file baseline.
/// </summary>
public sealed class DrawingIntakeTests
{
    [Fact]
    public void TheRecordRetainsWhatTheFixtureDrewAndReportsAfterTheDocumentCloses()
    {
        SheetRecord sheet;
        using (var doc = PdfDocument.Open(Pdf()))
            sheet = DrawingIntake.ReadSheet(doc, 1, new(96, PdfIntakeOptions.Default), DocumentFacts.From(doc));

        Assert.Equal(1, sheet.PageNumber);
        Assert.Equal(1200, sheet.WidthPts);
        Assert.Equal(900, sheet.HeightPts);
        Assert.Equal(96, sheet.ScaleDenominator);
        var schedule = Assert.Single(sheet.Schedules, s => s.Kind == "column");
        Assert.Equal("COLUMN SCHEDULE", schedule.Heading);
        var row = Assert.Single(schedule.Rows);
        Assert.Equal("C1", row.Mark);
        Assert.Equal("600", row.Cells["WidthMm"]);
        Assert.Equal("600", row.Cells["DepthMm"]);
        Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder.ToString(), row.Route);
        var mark = Assert.Single(sheet.Marks);
        Assert.Equal("C1", mark.Text);
        Assert.InRange(mark.X, 425, 440);
        Assert.InRange(mark.Y, 410, 420);
        Assert.Contains(sheet.Furniture.Regions, r => r.Kind == "schedule: COLUMN SCHEDULE");
        var grid = Assert.Single(sheet.Grid.Bubbles);
        Assert.Equal("A", grid.Label);
        Assert.True(grid.OnVerticalAxis);
        Assert.Equal(200, Assert.Single(sheet.Grid.VerticalAxesX));
        var axis = Assert.Single(sheet.Grid.Axes);
        Assert.Equal("A", axis.Name); Assert.True(axis.Vertical); Assert.Equal(200, axis.At, 0.5);
        var exported = Assert.Single(sheet.Geometry.GridAxes);
        Assert.Equal("A", exported.Name); Assert.True(exported.Vertical);
        Assert.Single(sheet.Geometry.Columns);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.BecameColumnByDeclaredSize);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.FurnitureRegion);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.GridAxis);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.PaperFill);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.EmittedAsLine && f.Disposition == Disposition.Unaccounted);
        Assert.Equal(Enumerable.Range(0, sheet.Content.Paths.Count), sheet.PathFates.Select(f => f.PathIndex));
        Assert.Equal(Enumerable.Range(0, sheet.Content.Words.Count), sheet.WordFates.Select(f => f.WordIndex));
        Assert.Contains(sheet.WordFates, f => f.Kind == "words: in a schedule a reader read rows of" && f.Disposition == Disposition.Read);
        Assert.Contains(sheet.WordFates, f => f.Kind == "words: grid axis names" && f.Disposition == Disposition.Read);
        Assert.Contains(sheet.PathFates, f => f.Reason == PathReason.GridAxis && f.Disposition == Disposition.Read);
        var note = Assert.Single(sheet.Markup);
        Assert.Equal("Check this column", note.Text);
        Assert.Equal("Ian", note.Author);
        Assert.Equal(1, sheet.Links);
        Assert.Equal(1, sheet.Context.LinksWithTarget);

        var ledger = SheetInventory.Of(sheet);
        Assert.Equal(sheet.Content.Paths.Count, ledger.Rows.Where(r => r.Primary && r.Class.StartsWith("paths:", StringComparison.Ordinal)).Sum(r => r.Count));
        Assert.Equal(sheet.Content.Words.Count, ledger.Rows.Where(r => r.Primary && r.Class.StartsWith("words", StringComparison.Ordinal)).Sum(r => r.Count));
        Assert.DoesNotContain(ledger.Rows, r => r.Class.Contains("undifferentiated", StringComparison.Ordinal));
        // With a classifier run every path's FATE is the primary row; the ink groups are context.
        // The first report kept "no ink: discarded" primary and hid 518 slabs emitted from
        // clipping rectangles on 31130 behind it (SheetInventory remarks).
        Assert.Contains(ledger.Rows, r => r.Primary && r.Class == "paths: PaperFill" && r.Count == 1);
        Assert.Contains(ledger.Rows, r => !r.Primary && r.Class == "paths: paper-coloured fill, no stroke (invisible ink)" && r.Count == 1);
        Assert.Contains(ledger.Rows, r => r.Class == "annotation text (engineer's comments)" && r.Disposition == Disposition.Read);
    }

    [Fact]
    public void NoScaleRetainsContentButDoesNotInventClassification()
    {
        using var doc = PdfDocument.Open(Pdf());
        var sheet = DrawingIntake.ReadSheet(doc, 1, new(null, PdfIntakeOptions.Default), DocumentFacts.From(doc));
        Assert.Null(sheet.ScaleDenominator);
        Assert.Empty(sheet.PathFates);
        Assert.Empty(sheet.Geometry.Columns);
        Assert.NotEmpty(sheet.Content.Paths);
        Assert.NotEmpty(sheet.Schedules);
        Assert.NotEmpty(sheet.Marks);
        var ledger = SheetInventory.Of(sheet);
        Assert.Equal(sheet.Content.Paths.Count, ledger.Rows.Where(r => r.Primary && r.Class.StartsWith("paths:", StringComparison.Ordinal)).Sum(r => r.Count));
        Assert.Contains(ledger.Rows, r => r.Class == "paths: inked, not classified (no scale given)" && r.Count > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNewEntryRetainsTheOldGeometryAndDxfBytes(bool markupOnly)
    {
        using var doc = PdfDocument.Open(Pdf(polygon: true));
        var expected = PdfPlanReader.Read(doc, 96, 1, PdfIntakeOptions.Default, markupOnly);
        var sheet = DrawingIntake.ReadSheet(doc, 1, new(96, PdfIntakeOptions.Default, markupOnly), DocumentFacts.From(doc));
        TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(expected, sheet.Geometry);
        Assert.Contains(sheet.Content.Paths, p => p.IsAnnotation);
        Assert.Equal(sheet.Content.Paths.Count, SheetInventory.Of(sheet).Rows
            .Where(r => r.Primary && r.Class.StartsWith("paths:", StringComparison.Ordinal)).Sum(r => r.Count));
        if (markupOnly)
            Assert.All(sheet.PathFates.Where(f => !sheet.Content.Paths[f.PathIndex].IsAnnotation),
                f => Assert.Equal(PathReason.MarkupOnlyMode, f.Reason));

        string before = Path.GetTempFileName(), after = Path.GetTempFileName();
        try
        {
            foreach (bool korLayers in new[] { false, true })
            {
                DxfExporter.Export(expected, before, korLayers: korLayers);
                DxfExporter.Export(sheet.Geometry, after, korLayers: korLayers);
                Assert.Equal(File.ReadAllBytes(before), File.ReadAllBytes(after));
            }
        }
        finally { File.Delete(before); File.Delete(after); }
    }

    [Fact]
    public void TheDocumentEntryReturnsItsFactsAndSheet()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Pdf());
            var set = DrawingIntake.Read(path, new(96, PdfIntakeOptions.Default));
            Assert.Equal(1, set.Facts.Pages);
            Assert.Equal(1, Assert.Single(set.Sheets).PageNumber);
        }
        finally { File.Delete(path); }
    }

    // An ordinary PDF content stream makes the exact path order and annotation dictionaries
    // reviewable without a binary fixture or another PDF library.
    private static byte[] Pdf(bool polygon = false)
    {
        const string content = """
            0 G 0 g 0.5 w
            750 600 300 100 re S
            750 680 m 1050 680 l S
            800 600 m 800 700 l S
            BT /F1 10 Tf 755 712 Td (COLUMN SCHEDULE) Tj ET
            BT /F1 10 Tf 760 684 Td (MARK) Tj ET
            BT /F1 10 Tf 815 684 Td (SIZE) Tj ET
            BT /F1 10 Tf 760 660 Td (C1) Tj ET
            BT /F1 10 Tf 815 660 Td (600 x 600) Tj ET
            0.8 g 400 400 17.7165354331 17.7165354331 re f
            0 g BT /F1 10 Tf 425 410 Td (C1) Tj ET
            212 780 m
            212 786.627417 206.627417 792 200 792 c
            193.372583 792 188 786.627417 188 780 c
            188 773.372583 193.372583 768 200 768 c
            206.627417 768 212 773.372583 212 780 c h S
            BT /F1 10 Tf 197 777 Td (A) Tj ET
            200 300 m 200 780 l S
            300 300 m 500 350 l S
            1 1 1 rg 100 100 10 10 re f
            """;
        string annotation = polygon
            ? "<< /Type /Annot /Subtype /Polygon /Rect [600 100 620 120] /Vertices [600 100 620 100 620 120 600 120] /C [1 0 0] /Contents (Check this column) /T (Ian) >>"
            : "<< /Type /Annot /Subtype /Text /Rect [600 100 620 120] /Contents (Check this column) /T (Ian) >>";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1200 900] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R /Annots [6 0 R 7 0 R] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            annotation,
            "<< /Type /Annot /Subtype /Link /Rect [500 100 520 120] /Dest [3 0 R /Fit] >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
