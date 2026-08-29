using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE POINT OF THE WHOLE EXERCISE: a Bluebeam markup becomes a DXF that the ETABS intake can read.
///
/// > "Ultimately Omar doesn't really want the DXFs of his markup from a Bluebeam PDF — he wants to
/// > go to SAFE and ETABS etc with those DXFs. So get it to export DXF from Bluebeam (or Revit with
/// > our existing Bridge). Then it can be imported into any program."
///
/// The only thing a PDF cannot say is what a shape MEANS. The engineer says it, once, in the tool
/// he is already using — `PdfToSafeWindow.xaml` binds an ElementType per colour — and
/// `ReclassifyByColor` moves the shapes on his answer. Nothing is stored, nothing is banked, and the
/// answer applies to the drawing in front of him.
///
/// This asserts his answer survives all the way to a DXF layer, because that is where the ETABS side
/// reads it: `dxf.wall-layer-patterns` matches on `WALL`.
/// </summary>
public sealed class TheConvergenceProof
{
    private readonly ITestOutputHelper _out;
    public TheConvergenceProof(ITestOutputHelper output) => _out = output;

    [Fact]
    public void AnEngineersColourAssignmentReachesTheDxfLayer()
    {
        string pdf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(pdf)) { _out.WriteLine($"SKIPPED: not at {pdf}"); return; }

        var scale = PdfGeometryExtractor.DetectScaleForLoad(pdf, 1);
        var geometry = PdfGeometryExtractor.Extract(pdf, scale.Denominator!.Value, 1);

        _out.WriteLine($"his markup, read at 1:{scale.Denominator} from {scale.Source}: " +
                       $"{geometry.Slabs.Count} slab(s), {geometry.Columns.Count} column(s), {geometry.Lines.Count} line(s)");

        // What he does in the tool: "the red is shear wall". He labelled every one of them 12" x 30".
        var his = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
        {
            [((byte)0xF0, (byte)0x00, (byte)0x00)] = new SlabColorSettings { ElementType = "Wall" },
        };

        // EXPORTED AS DRAWN, WITH HIS ASSIGNMENT ON THE LAYER — not reclassified first.
        //
        // ReclassifyByColor turns his closed Bluebeam polygon into a CENTRELINE plus a section,
        // which is right for SAFE and unreadable by the DXF intake: WallOutlineDecomposer wants a
        // closed loop with two parallel faces. Exported that way, dxf-inspect showed WALL-MARKUP as
        // five OPEN segments with gaps of two metres, and the intake said "no structural outlines
        // found on the expected layers". A wall in a DRAWING is its two faces.
        var reclassified = PdfGeometryExtractor.ReclassifyByColor(geometry, his);
        _out.WriteLine($"for SAFE, reclassified : {reclassified.Slabs.Count} slab(s), " +
                       $"{reclassified.Columns.Count} column(s), {reclassified.Lines.Count} line(s) " +
                       "— a wall becomes a centreline plus a section");

        string dxf = Path.Combine(Path.GetTempPath(), "kor-convergence-markup-as-walls.dxf");
        DxfExporter.Export(geometry, dxf, colorSettings: his);

        // Read with the SHIPPED reader, not a hand-rolled scan of the group codes — the
        // question is whether the far side can use this file, so ask the far side's reader.
        var used = DxfFacts.Layers(dxf);
        _out.WriteLine($"what DxfPlanReader sees: {DxfFacts.Describe(dxf)}");
        _out.WriteLine($"layers carrying entities: {string.Join(", ", used)}");

        // dxf.wall-layer-patterns is "WALL", matched as a substring — so both of these land.
        Assert.Contains(used, l => l.Contains("WALL", StringComparison.Ordinal));
        _out.WriteLine("");
        _out.WriteLine("A layer containing WALL exists, which is what dxf.wall-layer-patterns matches.");
        _out.WriteLine($"file: {dxf}");
    }
}
