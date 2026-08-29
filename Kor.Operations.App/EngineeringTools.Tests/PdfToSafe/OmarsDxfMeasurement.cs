using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE ACTUAL DELIVERABLE, WRITTEN AND MEASURED — not an argument about whether it could be.
///
/// "I would like to get a dxf for this pdf. The tower outline, the red markups I did and the
/// balcony outline." — Omar Alcazar, 28 August, Parcel 11.
///
/// <see cref="ColourIsTheSelectorMeasurement"/> showed his red is a single value, #F00000, and that
/// the architect's content splits across ten other colours. This writes the DXF that follows from
/// that: the whole page, layered BY COLOUR, so every colour the draughtsman separated arrives as a
/// layer he can turn on and off in AutoCAD instead of a selection someone else made for him.
///
/// Written to the Desktop so it can be opened and looked at. Reporting only.
/// </summary>
public sealed class OmarsDxfMeasurement
{
    private readonly ITestOutputHelper _out;
    public OmarsDxfMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [Fact]
    public void WriteTheDxfAndSayWhatIsInIt()
    {
        string path = Path.Combine(Desktop, "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        // 1:96, not the 100 anyone would type. The title block says SCALE: 1/8" = 1'-0", and
        // TheScaleIsPrintedOnTheSheetMeasurement beside this one confirms it against the suite areas
        // the architect printed: 1:96 reproduces seven of eight within 0.6%, 1:100 misses by 8%.
        // Everything here is linear in that number, so getting it wrong is a DXF 4% oversize with
        // nothing anywhere to say so.
        const int scale = 96;

        ExtractedGeometry whole;
        using (var s = File.OpenRead(path))
            whole = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, scale);

        _out.WriteLine($"page 1: {whole.Slabs.Count} slab(s), {whole.Columns.Count} column(s), " +
                       $"{whole.Lines.Count} line(s), {whole.RawPathCount:N0} raw path(s)");

        string outPath = Path.Combine(Desktop, "OAP-parcel11-FROM-PDF.dxf");
        DxfExporter.Export(whole, outPath, layerByColour: true);

        var info = new FileInfo(outPath);
        _out.WriteLine($"wrote {info.Name}  ({info.Length / 1024.0:N0} KB)");

        // WHAT LANDED, read back out of the file rather than out of the writer. A DXF that lists
        // entities the exporter believes it wrote is not evidence; the repo has been caught by that
        // before, so the count comes from the artifact.
        var text = File.ReadAllLines(outPath);
        int polylines = text.Count(l => l.Trim() == "POLYLINE");
        int vertices = text.Count(l => l.Trim() == "VERTEX");
        
        // Only inside ENTITIES. Group code 8 means "layer" there; in the LAYER table the same digit
        // turns up as an AutoCAD colour VALUE — grey is 8 — and reading the whole file invented a
        // layer called "6" out of the group code that followed it.
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int start = Array.FindIndex(text, l => l.Trim() == "ENTITIES");
        for (int i = Math.Max(start, 0); i < text.Length - 1; i++)
            if (text[i].Trim() == "8") layers.Add(text[i + 1].Trim());

        _out.WriteLine($"read back: {polylines:N0} POLYLINE, {vertices:N0} VERTEX");
        _out.WriteLine($"layers ({layers.Count}): {string.Join(", ", layers.OrderBy(x => x))}");

        Assert.True(File.Exists(outPath));
    }
}
