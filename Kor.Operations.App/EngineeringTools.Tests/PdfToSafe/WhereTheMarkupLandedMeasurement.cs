using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// DID EVERY MARKUP LAND ON THE DRAWING IT WAS DRAWN ON?
///
/// Omar's Parcel 11 markup went to him with five shapes — four corner columns and a 20'-5"
/// dimension — sitting in empty space off the left edge of the sheet, while the other 38 were
/// correct. Cause: PdfPig normalises everything it parses, including Annotation.Rectangle, but
/// /Vertices, /L and /InkList are read straight out of the annotation dictionary and stay in the
/// file's own user space. This sheet's raw MediaBox starts at -1728, -1296.12, so anything on those
/// three readers arrived 58 m from where it belonged.
///
/// MEASURED AGAINST THE PAGE, NOT AGAINST THE OTHER MARKUPS. The first version of this asked how far
/// each shape sat from the median of the rest, and once the fix landed it flagged the two corner
/// columns furthest from the middle — for being at the corners. A column at the corner of a 28 m
/// building is 21 m from its centre and that is what a corner column IS. What "off the drawing"
/// means is OUTSIDE THE DRAWING, so that is what is asked.
/// </summary>
public sealed class WhereTheMarkupLandedMeasurement
{
    private readonly ITestOutputHelper _out;
    public WhereTheMarkupLandedMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// Two sheets whose raw page box starts at the CENTRE, which is the fault, and two whose box
    /// starts at 0,0, which must be untouched by the fix for it.
    ///
    /// ⚠ HONEST COVERAGE: only Parcel 11 actually exercises the fix. 31202-01 has the same raw
    /// origin — (-1512.12, -1080.12), a different office and a different job, so the centre-origin
    /// sheet is NOT a freak — but its reviewer markup was flattened into page content before it was
    /// filed, so it carries no annotations to misplace. It is kept because the day someone files
    /// that job's UNflattened markup, this gate is already pointed at it.
    ///
    /// On both centre-origin files the MediaBox sits on the /Pages node rather than the page —
    /// inherited, which is legal and common. A reader that consults only the page dictionary finds
    /// nothing there and silently does nothing, so the fix depends on PdfPig resolving inheritance
    /// into page.Dictionary. It does today; this gate is what would catch it if that changed.
    /// </summary>
    public static IEnumerable<object[]> Sets()
    {
        yield return new object[] { "Parcel 11 — raw origin is the sheet CENTRE", "OAP-parcel11-arch-markup.pdf" };
        yield return new object[] { "31202-01 reinforcing + reviewer markup — CENTRE origin too",
                                    "31202-01 - Reinforcing Sheets - REVISED per JD markup 2026-07-27.pdf" };
        yield return new object[] { "31065 IFC — origin already 0,0, must be a no-op",
                                    Path.Combine("Structural Quantity Takeoff Demo", "Inputs", "31065 - AFTER (IFC 2026-03-06).pdf") };
        yield return new object[] { "Andrea's parking markup", "AN-parking-markup.pdf" };
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void NoMarkupLandsOutsideTheDrawing(string label, string file)
    {
        string path = Path.Combine(Desktop, file);
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED {label}: not at {path}"); return; }

        // THE MARKED-UP SHEET IS WHEREVER THE REVIEWER PUT IT. Reading page 1 and reporting "0
        // markup shapes" is how this repo once concluded a working reader extracted nothing from
        // anything — from a 41-page set whose markup is on page 12. A gate that reports zeros is
        // not a gate.
        ExtractedGeometry? g = null;
        int onPage = 0, mostMarks = 0;
        for (int p = 1; p <= 16; p++)
        {
            ExtractedGeometry candidate;
            try
            {
                using var s = File.OpenRead(path);
                candidate = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, p, 96);
            }
            catch (ArgumentOutOfRangeException) { break; }
            catch (Exception) { break; }

            int found = candidate.SlabIsAnnotation.Count(x => x)
                      + candidate.ColumnIsAnnotation.Count(x => x)
                      + candidate.LineIsAnnotation.Count(x => x);
            if (found > mostMarks) { mostMarks = found; g = candidate; onPage = p; }
            if (g is null) { g = candidate; onPage = p; }
        }

        if (g is null) { _out.WriteLine($"{label}: no readable page"); return; }
        _out.WriteLine($"{label}   (most markup on page {onPage})");

        // The architect's own drawing, which is where any markup on it must be.
        var page = new List<(double X, double Y)>();
        for (int i = 0; i < g.Slabs.Count; i++)
            if (i >= g.SlabIsAnnotation.Count || !g.SlabIsAnnotation[i]) page.AddRange(g.Slabs[i]);
        for (int i = 0; i < g.Lines.Count; i++)
            if (i >= g.LineIsAnnotation.Count || !g.LineIsAnnotation[i]) page.AddRange(g.Lines[i]);

        if (page.Count == 0) { _out.WriteLine($"{label}: no page content to measure against"); return; }

        double px0 = page.Min(p => p.X) / 1000, px1 = page.Max(p => p.X) / 1000;
        double py0 = page.Min(p => p.Y) / 1000, py1 = page.Max(p => p.Y) / 1000;

        var marks = new List<(string Kind, double X, double Y)>();
        for (int i = 0; i < g.Slabs.Count; i++)
            if (i < g.SlabIsAnnotation.Count && g.SlabIsAnnotation[i])
                marks.Add(("slab", g.Slabs[i].Average(q => q.X) / 1000, g.Slabs[i].Average(q => q.Y) / 1000));
        for (int i = 0; i < g.Columns.Count; i++)
            if (i < g.ColumnIsAnnotation.Count && g.ColumnIsAnnotation[i])
                marks.Add(("column", g.Columns[i].X / 1000, g.Columns[i].Y / 1000));
        for (int i = 0; i < g.Lines.Count; i++)
            if (i < g.LineIsAnnotation.Count && g.LineIsAnnotation[i])
                marks.Add(("line", g.Lines[i].Average(q => q.X) / 1000, g.Lines[i].Average(q => q.Y) / 1000));

        _out.WriteLine($"  page content spans x {px0:N1}..{px1:N1}  y {py0:N1}..{py1:N1} m");
        _out.WriteLine($"  {marks.Count} markup shape(s)");

        var outside = marks
            .Where(m => m.X < px0 || m.X > px1 || m.Y < py0 || m.Y > py1)
            .ToList();

        foreach (var m in outside)
            _out.WriteLine($"  OUTSIDE  {m.Kind,-7} ({m.X:N1}, {m.Y:N1}) m");

        Assert.True(outside.Count == 0,
            $"{outside.Count} of {marks.Count} markup shape(s) landed outside the drawing, which spans " +
            $"x {px0:N1}..{px1:N1} y {py0:N1}..{py1:N1} m. A markup is drawn ON a sheet; if it reads " +
            "outside it, a reader put it in the wrong coordinate space.");
    }
}
