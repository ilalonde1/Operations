using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE OTHER TWO THINGS OMAR ASKED FOR.
///
/// *"I would like to get a dxf for this pdf. The tower outline, the red markups I did and the
/// balcony outline."* He has the markup. This is the tower and the balconies.
///
/// No new geometry engine. A raster union was started for this once and stopped — *"Are you not
/// overcomplicating this? It's one page."* — because the drawing already answers it:
///
///   THE TOWER OUTLINE is the outer edge of the FILLS. The suites and the core tile the floor plate
///   edge to edge, so their union IS the plate and its boundary is the outline. Four colours,
///   rendered and confirmed. In AutoCAD it is one BOUNDARY click.
///
///   THE BALCONIES are the black linework lying OUTSIDE those fills. That is what a balcony is on
///   this sheet — the bit drawn past the floor plate. Furniture, doors and dimension strings are
///   inside it; the title block, the podium and the site boundary are far away from it.
///
/// So this is a SELECTION, and `DxfExporter` already takes exclusions and colour layers. Nothing is
/// invented and nothing new is built.
/// </summary>
public sealed class OmarsOutlinesDeliverable
{
    private readonly ITestOutputHelper _out;
    public OmarsOutlinesDeliverable(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The suite and core fills. Measured off the sheet, not guessed.</summary>
    private static readonly HashSet<(byte R, byte G, byte B)> FillColours = new()
    {
        (0xF0, 0xC0, 0x70), (0xF0, 0xD0, 0x80), (0xF0, 0xD0, 0xA0), (0xF0, 0xE0, 0xF0),
    };

    /// <summary>How far past the plate a balcony reaches. Generous — the point is to exclude the
    /// title block and the podium sixty metres away, not to trim a balcony.</summary>
    private const double BalconyReachMm = 8000.0;

    [Fact]
    public void WriteTheTowerAndBalconyOutlines()
    {
        string pdf = Path.Combine(Desktop, "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(pdf)) { _out.WriteLine($"SKIPPED: not at {pdf}"); return; }

        var detected = PdfGeometryExtractor.DetectScaleForLoad(pdf, 1);
        Assert.True(detected.Denominator == 96, "the sheet states 1/8\" = 1'-0\"");

        ExtractedGeometry g;
        using (var s = File.OpenRead(pdf))
            g = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, detected.Denominator!.Value);

        // ---- the plate ---------------------------------------------------------------------
        var plate = new List<List<(double X, double Y)>>();
        for (int i = 0; i < g.Slabs.Count; i++)
            if (i < g.SlabColors.Count && FillColours.Contains(g.SlabColors[i])) plate.Add(g.Slabs[i]);

        Assert.True(plate.Count > 0, "no fills found — the tower outline is derived from them");

        double px0 = plate.Min(p => p.Min(q => q.X)), px1 = plate.Max(p => p.Max(q => q.X));
        double py0 = plate.Min(p => p.Min(q => q.Y)), py1 = plate.Max(p => p.Max(q => q.Y));
        _out.WriteLine($"tower plate: {plate.Count} fill region(s), " +
                       $"{(px1 - px0) / 1000:N1} x {(py1 - py0) / 1000:N1} m");

        static bool Inside((double X, double Y) p, List<List<(double X, double Y)>> polys)
            => polys.Any(poly => InPolygon(p, poly));

        // ---- what to keep ------------------------------------------------------------------
        var dropSlabs = new HashSet<int>();
        for (int i = 0; i < g.Slabs.Count; i++)
        {
            bool isFill = i < g.SlabColors.Count && FillColours.Contains(g.SlabColors[i]);
            bool isMarkup = i < g.SlabIsAnnotation.Count && g.SlabIsAnnotation[i];
            if (!isFill && !isMarkup) dropSlabs.Add(i);
        }

        var dropLines = new HashSet<int>();
        int balconyLines = 0;
        for (int i = 0; i < g.Lines.Count; i++)
        {
            if (i < g.LineIsAnnotation.Count && g.LineIsAnnotation[i]) continue;   // his markup stays

            var pts = g.Lines[i];
            // Outside the plate, and near it. Inside is furniture; far away is the title block,
            // the podium and the property line.
            bool anyInside = pts.Any(p => Inside(p, plate));
            bool near = pts.Any(p => p.X > px0 - BalconyReachMm && p.X < px1 + BalconyReachMm
                                  && p.Y > py0 - BalconyReachMm && p.Y < py1 + BalconyReachMm);

            if (anyInside || !near) dropLines.Add(i);
            else balconyLines++;
        }

        var dropColumns = new HashSet<int>();
        for (int i = 0; i < g.Columns.Count; i++)
            if (i >= g.ColumnIsAnnotation.Count || !g.ColumnIsAnnotation[i]) dropColumns.Add(i);

        _out.WriteLine($"kept: {plate.Count} fill region(s), {balconyLines} balcony/edge line(s), " +
                       $"{g.SlabIsAnnotation.Count(x => x) + g.ColumnIsAnnotation.Count(x => x)} markup shape(s)");
        _out.WriteLine($"dropped: {dropSlabs.Count} slab(s), {dropLines.Count} line(s), {dropColumns.Count} column(s)");

        // TEXT GETS THE SAME RULE AS THE LINEWORK, or the title block rides along.
        //
        // This file needs the page's geometry, and a whole-page read carries every word on the
        // sheet with it — so without this it arrives holding the architect's practice name and
        // postal address, which is the complaint that started all of this. Words near the plate are
        // the suite labels and room names and are worth having; words sixty metres away are the
        // title block.
        int wordsBefore = g.TextAnnotations.Count;
        g.TextAnnotations = g.TextAnnotations
            .Where(t => t.X > px0 - BalconyReachMm && t.X < px1 + BalconyReachMm
                     && t.Y > py0 - BalconyReachMm && t.Y < py1 + BalconyReachMm)
            .ToList();
        _out.WriteLine($"text: {g.TextAnnotations.Count} kept of {wordsBefore} " +
                       "(the rest is title block and general notes, far from the plate)");

        string outPath = Path.Combine(Desktop, "OAP-parcel11-TOWER-AND-BALCONIES.dxf");
        DxfExporter.Export(g, outPath, dropSlabs, dropLines, dropColumns);

        _out.WriteLine($"wrote {Path.GetFileName(outPath)} ({new FileInfo(outPath).Length / 1024.0:N0} KB)");
        _out.WriteLine($"as read    : {DxfFacts.Describe(outPath)}");

        Assert.True(balconyLines > 0, "no linework outside the plate — the balconies are missing");
    }

    private static bool InPolygon((double X, double Y) t, List<(double X, double Y)> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if (a.Y > t.Y != b.Y > t.Y && t.X < (b.X - a.X) * (t.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
