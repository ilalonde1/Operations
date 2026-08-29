using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// IS THE DXF THE RIGHT SIZE? Checked against numbers the architect printed on his own drawing.
///
/// Everything downstream of the parser is linear in one number, the scale denominator, and it is
/// currently supplied by whoever opens the tool. Parcel 11's title block says SCALE: 1/8" = 1'-0",
/// which is 1:96 — and 100 is the obvious thing to type. A DXF built at 100 from a 96 drawing is
/// 4.2% oversize everywhere, silently: nothing looks wrong, no check fails, and a 20'-5" core wall
/// arrives as 21'-3". That is the kind of error that reaches an engineer and costs him a morning.
///
/// The sheet carries its own answer twice over — the scale in the title block, and a unit schedule
/// printing the area of every suite in both ft² and m². So this does not argue about the right
/// denominator; it extracts at several and asks which one reproduces the areas the architect
/// printed. Reporting only.
/// </summary>
public sealed class TheScaleIsPrintedOnTheSheetMeasurement
{
    private readonly ITestOutputHelper _out;
    public TheScaleIsPrintedOnTheSheetMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The suite areas printed on Parcel 11's L08-15 plan, in m².</summary>
    private static readonly double[] PrintedAreas =
        { 80.9, 51.1, 51.0, 81.1, 47.9, 47.9, 101.7, 101.7, 54.1, 93.1 };

    [Fact]
    public void WhichDenominatorReproducesTheAreasTheArchitectPrinted()
    {
        string path = Path.Combine(Desktop, "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        _out.WriteLine("title block says SCALE: 1/8\" = 1'-0\", which is 1:96");
        _out.WriteLine($"suite areas printed on the sheet (m2): {string.Join(", ", PrintedAreas)}");
        _out.WriteLine("");
        _out.WriteLine($"{"1:n",5} {"matched",8} {"median error",14}   nearest extracted area to each printed one");

        foreach (int denominator in new[] { 96, 100, 110 })
        {
            ExtractedGeometry g;
            using (var s = File.OpenRead(path))
                g = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, 1, denominator);

            // Every closed shape's area in m2, by the shoelace formula on its own ring.
            var areas = g.Slabs
                .Select(Area)
                .Where(a => a > 20 && a < 200)     // suite-sized only; the sheet border is not a suite
                .OrderBy(a => a)
                .ToList();

            if (areas.Count == 0) { _out.WriteLine($"{denominator,5} — nothing suite-sized"); continue; }

            var errors = new List<double>();
            var shown = new List<string>();
            foreach (double printed in PrintedAreas.Distinct())
            {
                double nearest = areas.OrderBy(a => Math.Abs(a - printed)).First();
                double error = Math.Abs(nearest - printed) / printed;
                errors.Add(error);
                shown.Add($"{printed:0.0}->{nearest:0.0}");
            }

            errors.Sort();
            double median = errors[errors.Count / 2];
            int matched = errors.Count(e => e < 0.02);

            _out.WriteLine($"{denominator,5} {matched,3}/{errors.Count,-4} {median * 100,12:0.00}%   " +
                           string.Join("  ", shown.Take(6)));
        }
    }

    private static double Area(List<(double X, double Y)> pts)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2.0 / 1_000_000.0;   // mm2 -> m2
    }
}
