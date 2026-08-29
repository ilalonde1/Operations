using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// DID EVERY MARKUP LAND WHERE IT WAS DRAWN?
///
/// Opened in AutoCAD, most of Omar's red sits correctly on the tower and a handful of it — corner
/// columns and at least one wall — sits in empty space off to the LEFT of the sheet, outside the
/// title block. Something in the read puts SOME annotations somewhere else, and an engineer opening
/// a DXF with members scattered outside the drawing has been handed a defect, not a deliverable.
///
/// So: every markup shape, with where it landed, and how far that is from the rest of them.
/// </summary>
public sealed class WhereTheMarkupLandedMeasurement
{
    private readonly ITestOutputHelper _out;
    public WhereTheMarkupLandedMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [Fact]
    public void EveryMarkupShapeAndWhereItLanded()
    {
        string path = Path.Combine(Desktop, "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        ExtractedGeometry g;
        using (var s = File.OpenRead(path))
            g = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, 96);

        var marks = new List<(string Kind, double X, double Y, double W, double H)>();

        for (int i = 0; i < g.Slabs.Count; i++)
            if (i < g.SlabIsAnnotation.Count && g.SlabIsAnnotation[i])
            {
                var p = g.Slabs[i];
                marks.Add(("slab", p.Average(q => q.X) / 1000, p.Average(q => q.Y) / 1000,
                           (p.Max(q => q.X) - p.Min(q => q.X)) / 1000, (p.Max(q => q.Y) - p.Min(q => q.Y)) / 1000));
            }

        for (int i = 0; i < g.Columns.Count; i++)
            if (i < g.ColumnIsAnnotation.Count && g.ColumnIsAnnotation[i])
            {
                var (x, y) = g.Columns[i];
                var size = i < g.ColumnSizes.Count ? g.ColumnSizes[i] : (WidthMm: 0.0, DepthMm: 0.0);
                marks.Add(("column", x / 1000, y / 1000, size.WidthMm / 1000, size.DepthMm / 1000));
            }

        for (int i = 0; i < g.Lines.Count; i++)
            if (i < g.LineIsAnnotation.Count && g.LineIsAnnotation[i])
            {
                var p = g.Lines[i];
                marks.Add(("line", p.Average(q => q.X) / 1000, p.Average(q => q.Y) / 1000,
                           (p.Max(q => q.X) - p.Min(q => q.X)) / 1000, (p.Max(q => q.Y) - p.Min(q => q.Y)) / 1000));
            }

        if (marks.Count == 0) { _out.WriteLine("no markup found"); return; }

        // Where the body of the markup sits, by median — one outlier cannot drag a median.
        double midX = marks.Select(m => m.X).OrderBy(v => v).ToList()[marks.Count / 2];
        double midY = marks.Select(m => m.Y).OrderBy(v => v).ToList()[marks.Count / 2];
        _out.WriteLine($"{marks.Count} markup shape(s); the body of them sits at ({midX:N1}, {midY:N1}) m");
        _out.WriteLine("");
        _out.WriteLine($"{"kind",-7} {"x m",8} {"y m",8} {"w m",7} {"h m",7} {"from body",10}");

        foreach (var m in marks.OrderByDescending(m => Math.Sqrt((m.X - midX) * (m.X - midX) + (m.Y - midY) * (m.Y - midY))))
        {
            double away = Math.Sqrt((m.X - midX) * (m.X - midX) + (m.Y - midY) * (m.Y - midY));
            string flag = away > 20 ? "   <-- OFF THE DRAWING" : "";
            _out.WriteLine($"{m.Kind,-7} {m.X,8:N1} {m.Y,8:N1} {m.W,7:N2} {m.H,7:N2} {away,10:N1}{flag}");
        }
    }
}
