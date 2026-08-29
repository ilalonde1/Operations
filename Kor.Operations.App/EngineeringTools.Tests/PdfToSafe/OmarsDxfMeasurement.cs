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

        // READ, NOT HARD-CODED. This said `const int scale = 96` for a day, with a comment
        // explaining that the title block says 1/8" = 1'-0" and the tool could not see it. The tool
        // can see it now — that sheet leaves its title-block SCALE field empty and states the scale
        // once under the viewport, and the load reads it and says where it came from.
        var detected = PdfGeometryExtractor.DetectScaleForLoad(path, 1);
        _out.WriteLine($"scale: 1:{detected.Denominator} from {detected.Source} (\"{detected.Note}\")");
        Assert.True(detected.Denominator == 96,
            "this sheet states 1/8\" = 1'-0\" and every coordinate below is linear in that number.");
        int scale = detected.Denominator!.Value;

        ExtractedGeometry whole;
        using (var s = File.OpenRead(path))
            whole = PdfGeometryExtractor.Extract(s, scaleDenominator: scale, pageNumber: 1);

        _out.WriteLine($"page 1: {whole.Slabs.Count} slab(s), {whole.Columns.Count} column(s), " +
                       $"{whole.Lines.Count} line(s), {whole.RawPathCount:N0} raw path(s)");

        string outPath = Path.Combine(Desktop, "OAP-parcel11-FROM-PDF.dxf");
        DxfExporter.Export(whole, outPath);

        var info = new FileInfo(outPath);
        _out.WriteLine($"wrote {info.Name}  ({info.Length / 1024.0:N0} KB)");

        // WHAT LANDED, read with the reader the ETABS side uses. A DXF verified by the code
        // that wrote it proves only that the writer agrees with itself; this asks the consumer.
        _out.WriteLine($"units      : {DxfFacts.UnitInInches(outPath)?.ToString("0.#####") ?? "NONE"} in per unit");
        _out.WriteLine($"as read    : {DxfFacts.Describe(outPath)}");

        Assert.True(File.Exists(outPath));
    }
}
