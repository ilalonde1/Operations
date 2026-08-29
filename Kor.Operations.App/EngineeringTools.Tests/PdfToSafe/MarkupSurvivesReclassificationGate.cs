using System;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// AN ENGINEER'S RED AND HIS ARCHITECT'S RED ARE THE SAME RED.
///
/// On Parcel 11 both are #F00000 — Omar's shear walls and the property line sweeping round the site.
/// No colour rule can separate them; origin can, and only origin can. `ExtractedGeometry` carries
/// SlabIsAnnotation / ColumnIsAnnotation / LineIsAnnotation for exactly that, and `DxfExporter`
/// writes the `-MARKUP` suffix from them.
///
/// `ReclassifyByColor` used to rebuild the geometry and copy the colours while dropping those flags,
/// so any colour override collapsed `PDF-F00000-MARKUP` back into `PDF-F00000` and welded his markup
/// onto the architect's boundary again. **The app exports the RECLASSIFIED geometry**, so that path
/// was the one that shipped.
///
/// This asserts the flags survive the trip, which a grep of the source cannot.
/// </summary>
public sealed class MarkupSurvivesReclassificationGate
{
    private readonly ITestOutputHelper _out;
    public MarkupSurvivesReclassificationGate(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ReclassificationKeepsWhichShapesWereMarkup()
    {
        string pdf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(pdf)) { _out.WriteLine($"SKIPPED: not at {pdf}"); return; }

        ExtractedGeometry before;
        using (var s = File.OpenRead(pdf))
            before = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, 96);

        int markedBefore = before.SlabIsAnnotation.Count(x => x)
                         + before.ColumnIsAnnotation.Count(x => x)
                         + before.LineIsAnnotation.Count(x => x);

        // A colour override of the kind the window applies before every export.
        var settings = new System.Collections.Generic.Dictionary<(byte R, byte G, byte B), SlabColorSettings>
        {
            [((byte)0xF0, (byte)0x00, (byte)0x00)] = new SlabColorSettings { ElementType = "Wall" },
        };

        var after = PdfGeometryExtractor.ReclassifyByColor(before, settings);

        int markedAfter = after.SlabIsAnnotation.Count(x => x)
                        + after.ColumnIsAnnotation.Count(x => x)
                        + after.LineIsAnnotation.Count(x => x);

        _out.WriteLine($"markup shapes before reclassification: {markedBefore}");
        _out.WriteLine($"markup shapes after  reclassification: {markedAfter}");

        Assert.True(markedBefore > 0, "nothing was marked as markup to begin with — the gate proves nothing");
        Assert.True(markedAfter == markedBefore,
            $"{markedBefore} shapes were markup and {markedAfter} still are. Reclassification lost the " +
            "one thing that separates an engineer's red from his architect's red, so the exported DXF " +
            "welds his markup onto the property line.");

        // And the flags must stay PARALLEL to the lists they describe, or they describe the wrong shape.
        Assert.Equal(after.Slabs.Count, after.SlabIsAnnotation.Count);
        Assert.Equal(after.Columns.Count, after.ColumnIsAnnotation.Count);
        Assert.Equal(after.Lines.Count, after.LineIsAnnotation.Count);

        // The layer the engineer actually opens.
        string dxf = Path.Combine(Path.GetTempPath(), "kor-reclassified-markup.dxf");
        DxfExporter.Export(after, dxf, layerByColour: true);
        var layers = File.ReadAllLines(dxf);
        int start = Array.FindIndex(layers, l => l.Trim() == "ENTITIES");
        bool hasMarkupLayer = false;
        for (int i = Math.Max(start, 0); i < layers.Length - 1; i++)
            if (layers[i].Trim() == "8" && layers[i + 1].Trim().EndsWith("-MARKUP", StringComparison.Ordinal))
                hasMarkupLayer = true;

        Assert.True(hasMarkupLayer,
            "the exported DXF has no -MARKUP layer after reclassification, so the engineer cannot " +
            "isolate his own work from the architect's.");
    }
}
