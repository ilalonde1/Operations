#nullable enable
using System;
using System.IO;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Brief 23: one page, one reader. Whatever the window shows for a sheet is what
/// <see cref="PdfPlanReader.Read"/> returns for that sheet in the same mode, and the mode defaults
/// to the mark-up, which is what the window always did.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the extractor's counts equal the Core reader's in both modes on the regent
/// fixture and, when the local stick file is present, on 31168 p14 (slabs, columns, lines, walls,
/// footings, grid axes, and the page facts the window's status reads); the default mode is the
/// mark-up; a clean issued set reads as empty in mark-up mode and as structure in drawing mode.
/// WHAT IT DOES NOT: the window itself — the selector, the status line and the re-analyse on
/// change are WPF and are checked by running the window; the text annotations the extractor adds
/// beyond the Core reader.
/// </remarks>
public sealed class TheWindowReadsWhatTheCliReadsTests
{
    private static string? Fixture(string name)
    {
        string dir = Path.GetDirectoryName(typeof(TheWindowReadsWhatTheCliReadsTests).Assembly.Location)!;
        string path = Path.Combine(dir, "PdfToSafe", "Fixtures", name);
        if (!File.Exists(path)) path = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "PdfToSafe", "Fixtures", name));
        return File.Exists(path) ? path : null;
    }

    private static string? LocalStickFile(string job)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "kor-drawings", "stickfiles", job + ".pdf");
        return File.Exists(path) ? path : null;
    }

    private static void AssertSameRead(ExtractedGeometry window, ExtractedGeometry cli)
    {
        Assert.Equal(cli.IsVectorPdf, window.IsVectorPdf);
        Assert.Equal(cli.RawPathCount, window.RawPathCount);
        Assert.Equal(cli.PageCount, window.PageCount);
        Assert.Equal(cli.Slabs.Count, window.Slabs.Count);
        Assert.Equal(cli.Columns.Count, window.Columns.Count);
        Assert.Equal(cli.Lines.Count, window.Lines.Count);
        Assert.Equal(cli.Walls.Count, window.Walls.Count);
        Assert.Equal(cli.Footings.Count, window.Footings.Count);
        Assert.Equal(cli.GridAxes.Count, window.GridAxes.Count);
    }

    [Fact]
    public void TheRegentFixtureReadsTheSameThroughTheWindowAndTheCliInBothModes()
    {
        var pdf = Fixture("regent_typ_floor.pdf");
        if (pdf is null) return;
        AssertSameRead(PdfGeometryExtractor.Extract(pdf, 100, 1, annotationsOnly: false), PdfPlanReader.Read(pdf, 100, 1, annotationsOnly: false));
        AssertSameRead(PdfGeometryExtractor.Extract(pdf, 100, 1, annotationsOnly: true), PdfPlanReader.Read(pdf, 100, 1, annotationsOnly: true));
        // the default is the mark-up, as the window always read
        AssertSameRead(PdfGeometryExtractor.Extract(pdf, 100, 1), PdfPlanReader.Read(pdf, 100, 1, annotationsOnly: true));
    }

    [Fact]
    public void ACleanIssuedSheetIsEmptyAsMarkUpAndStructureAsDrawing()
    {
        var pdf = LocalStickFile("31168-01");
        if (pdf is null) return;
        var drawing = PdfGeometryExtractor.Extract(pdf, 96, 14, annotationsOnly: false);
        AssertSameRead(drawing, PdfPlanReader.Read(pdf, 96, 14, annotationsOnly: false));
        Assert.True(drawing.Walls.Count > 0 && drawing.Columns.Count > 0 && drawing.GridAxes.Count > 0,
            $"drawing mode on 31168 p14 read walls {drawing.Walls.Count}, columns {drawing.Columns.Count}, grid axes {drawing.GridAxes.Count}");

        var markUp = PdfGeometryExtractor.Extract(pdf, 96, 14);
        AssertSameRead(markUp, PdfPlanReader.Read(pdf, 96, 14, annotationsOnly: true));
        Assert.Equal(0, markUp.Slabs.Count + markUp.Columns.Count + markUp.Lines.Count + markUp.Walls.Count);
    }
}
