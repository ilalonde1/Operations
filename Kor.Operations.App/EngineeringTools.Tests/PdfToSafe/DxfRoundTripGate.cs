using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE GATE THE ADVERSARIAL AUDIT SAID MUST BE GREEN BEFORE ANYTHING IS DELETED.
///
/// The convergence plan claims a PDF-derived DXF can feed the existing DXF intake. That claim was
/// false when it was written, in two ways nobody had checked: the exporter emitted no `$INSUNITS`,
/// so `DxfToEtabsService` refuses the file outright — *"there is no way to know whether it is drawn
/// in inches, feet or millimetres. Every size rule and every coordinate depends on that"* — and it
/// emitted `POLYLINE`/`VERTEX` and nothing else, so every slab-thickness and section call-out the
/// PDF carried was dropped in transit.
///
/// So this does not ask whether the writer believes it wrote those things. It writes a DXF from a
/// real PDF, reads it back through the SHIPPED reader — `DxfPlanReader`, the one the ETABS side
/// uses — and asserts the drawing survived: units, text, geometry, extent.
///
/// The text-inside-the-box assertion is the sharp one. `Export` recentres every shape on a weighted
/// centroid; text recentred by anything else lands offset from the plate it labels, which is worse
/// than dropping it because it looks right and reads wrong.
/// </summary>
public sealed class DxfRoundTripGate
{
    private readonly ITestOutputHelper _out;
    public DxfRoundTripGate(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static IEnumerable<object[]> Sets()
    {
        yield return new object[] { "Parcel 11 arch sheet + engineer markup", "OAP-parcel11-arch-markup.pdf", true };
        yield return new object[] { "Parcel 11, structural layering (default path)", "OAP-parcel11-arch-markup.pdf", false };
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void AnExportedDxfSurvivesTheShippedReader(string label, string file, bool layerByColour)
    {
        string pdf = Path.Combine(Desktop, file);
        if (!File.Exists(pdf)) { _out.WriteLine($"SKIPPED {label}: not at {pdf}"); return; }

        ExtractedGeometry g;
        using (var s = File.OpenRead(pdf))
            g = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, 96);

        string dxf = Path.Combine(Path.GetTempPath(), $"kor-roundtrip-{(layerByColour ? "colour" : "structural")}.dxf");
        DxfExporter.Export(g, dxf, layerByColour: layerByColour);

        _out.WriteLine($"{label}");
        _out.WriteLine($"  exported {new FileInfo(dxf).Length / 1024.0:N0} KB from {g.Slabs.Count} slab(s), " +
                       $"{g.Columns.Count} column(s), {g.Lines.Count} line(s), {g.TextAnnotations.Count} word(s)");

        // ---- units -----------------------------------------------------------------------------
        double? unit = DxfPlanReader.UnitInInches(dxf);
        _out.WriteLine($"  units read back    : {(unit is null ? "NONE" : $"{unit:0.######} in per unit")}");
        Assert.True(unit is not null,
            "The exported DXF declares no $INSUNITS, so DxfToEtabsService will refuse it and no size " +
            "rule downstream can be applied. See its own error text.");
        Assert.Equal(1.0 / 25.4, unit!.Value, 9);   // geometry is written in millimetres

        // ---- text ------------------------------------------------------------------------------
        var tags = DxfPlanReader.ReadPositionedTags(dxf);
        _out.WriteLine($"  words in  : {g.TextAnnotations.Count,6}     tags read back : {tags.Count,6}");
        Assert.True(tags.Count == g.TextAnnotations.Count,
            $"{g.TextAnnotations.Count} word(s) went in and {tags.Count} came back. Slab thickness on " +
            "the ETABS side is read from text sitting inside a plate, so a word lost here is a " +
            "thickness lost there.");

        // ---- geometry --------------------------------------------------------------------------
        var segments = DxfPlanReader.ReadSegments(dxf);
        Assert.True(segments.Count > 0, "No geometry survived the round trip.");

        // Every polyline the exporter wrote becomes (n-1) segments open, or n closed. Rather than
        // re-derive that arithmetic, assert the extent — which is what a wrong transform moves.
        double rx0 = segments.Min(s => Math.Min(s.Start.X, s.End.X));
        double rx1 = segments.Max(s => Math.Max(s.Start.X, s.End.X));
        double ry0 = segments.Min(s => Math.Min(s.Start.Y, s.End.Y));
        double ry1 = segments.Max(s => Math.Max(s.Start.Y, s.End.Y));

        var wrote = g.Slabs.Concat(g.Lines).SelectMany(p => p).ToList();
        wrote.AddRange(g.Columns);
        double wx = wrote.Max(p => p.X) - wrote.Min(p => p.X);
        double wy = wrote.Max(p => p.Y) - wrote.Min(p => p.Y);

        _out.WriteLine($"  extent in  : {wx / 1000,8:N1} x {wy / 1000,-8:N1} m");
        _out.WriteLine($"  extent out : {(rx1 - rx0) / 1000,8:N1} x {(ry1 - ry0) / 1000,-8:N1} m");

        Assert.True(Math.Abs((rx1 - rx0) - wx) < 100 && Math.Abs((ry1 - ry0) - wy) < 100,
            $"The drawing changed size on the way through: {wx / 1000:N1} x {wy / 1000:N1} m went in, " +
            $"{(rx1 - rx0) / 1000:N1} x {(ry1 - ry0) / 1000:N1} m came back.");

        // ---- text sits where the geometry sits ---------------------------------------------------
        // The centroid trap: Export recentres geometry on a weighted centroid, and text recentred by
        // anything else lands somewhere plausible and wrong.
        if (tags.Count > 0)
        {
            double margin = Math.Max(wx, wy) * 0.05;
            var strays = tags
                .Where(t => t.Point.X < rx0 - margin || t.Point.X > rx1 + margin ||
                            t.Point.Y < ry0 - margin || t.Point.Y > ry1 + margin)
                .ToList();

            _out.WriteLine($"  text outside the geometry's extent: {strays.Count}");
            foreach (var t in strays.Take(5))
                _out.WriteLine($"     \"{t.Text}\" at ({t.Point.X / 1000:N1}, {t.Point.Y / 1000:N1}) m");

            Assert.True(strays.Count == 0,
                $"{strays.Count} of {tags.Count} words landed outside the geometry they annotate. " +
                "Export recentres shapes on a weighted centroid; text must be recentred by the same " +
                "cx, cy or every call-out is offset from the plate it labels.");
        }
    }
}
