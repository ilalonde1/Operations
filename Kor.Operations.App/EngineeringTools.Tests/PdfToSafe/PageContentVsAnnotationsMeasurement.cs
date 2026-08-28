using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE MEASUREMENT BEHIND A DECISION, NOT A PASS/FAIL GATE.
///
/// PdfToSafe reads Bluebeam markup annotations and throws the page away —
/// <c>GeometryFilterService.Classify(annotationsOnly: true)</c>, and nothing anywhere passes false.
/// The parser already reads the page's own vectors (<c>IsAnnotation: false</c>) and the filter
/// discards them on one <c>continue</c>.
///
/// That single line decides whether the PDF path can be a drawing reader at all, or stays a
/// markup-to-model tool that requires the engineer to trace the structure in Bluebeam first. If the
/// page content yields recognisable structure, a PDF could feed the DXF engine's classifier and
/// inherit the rules database, the questionnaire and the shipped-model invariants. If it yields
/// thousands of paths of text, hatch, dimensions and title block, it cannot, and the honest answer
/// is that PDF-to-model needs classification work nobody has costed.
///
/// So this counts, on real sets from THREE different projects, rather than arguing. It writes its
/// findings to test output and never fails on the numbers — the numbers are the point.
/// </summary>
public sealed class PageContentVsAnnotationsMeasurement
{
    private readonly ITestOutputHelper _out;
    public PageContentVsAnnotationsMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Real sets, deliberately from different projects and different origins: an issued
    /// IFC set, the earlier addendum of the same job, a reinforcing set carrying a reviewer's
    /// markups, and an engineer's own parking markup.</summary>
    public static IEnumerable<object[]> Sets()
    {
        yield return new object[] { "31065 IFC (5380 Heather)", Path.Combine(Desktop, "Structural Quantity Takeoff Demo", "Inputs", "31065 - AFTER (IFC 2026-03-06).pdf") };
        yield return new object[] { "31065 IFT addendum", Path.Combine(Desktop, "Structural Quantity Takeoff Demo", "Inputs", "31065 - BEFORE (IFT Addendum 2025-10-07).pdf") };
        yield return new object[] { "31202-01 reinforcing + markup", Path.Combine(Desktop, "31202-01 - Reinforcing Sheets - REVISED per JD markup 2026-07-27.pdf") };
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void WhatThePageHoldsThatTheAnnotationsOnlyFilterThrowsAway(string label, string path)
    {
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED {label}: not at {path}"); return; }

        _out.WriteLine($"===== {label}");
        _out.WriteLine($"      {Path.GetFileName(path)}  ({new FileInfo(path).Length / 1_000_000.0:N1} MB)");

        int pages = 0;
        var rows = new List<string>();

        for (int page = 1; page <= 6; page++)
        {
            ExtractedGeometry annotated, whole;
            try
            {
                using (var s = File.OpenRead(path)) annotated = PdfGeometryExtractor.Extract(s, scaleDenominator: 100, pageNumber: page);
                using (var s = File.OpenRead(path)) whole = ExtractWholePage(s, pageNumber: page, scaleDenominator: 100);
            }
            catch (Exception ex) { _out.WriteLine($"  page {page}: {ex.GetType().Name} — {ex.Message}"); break; }

            pages++;
            rows.Add(
                $"  p{page,-2} raw paths {annotated.RawPathCount,7:N0}   vector? {(annotated.IsVectorPdf ? "yes" : "no "),3}   " +
                $"ANNOTATIONS slabs {annotated.Slabs.Count,5} cols {annotated.Columns.Count,5} lines {annotated.Lines.Count,6}   " +
                $"WHOLE PAGE slabs {whole.Slabs.Count,6} cols {whole.Columns.Count,6} lines {whole.Lines.Count,7}");
        }

        foreach (string r in rows) _out.WriteLine(r);
        _out.WriteLine($"      {pages} page(s) read.");

        // ARE THOSE SHAPES STRUCTURE, OR TABLE CELLS?
        //
        // "126 slabs" on a reinforcing sheet is only good news if the shapes are the size of slabs.
        // A schedule grid, a title block and a notes box are all closed rectangles too, and the
        // classifier separates slab from column on size alone. So print the sizes: a real column is
        // roughly 200-1500 mm across, a real slab tens of metres. Anything clustering at a few
        // hundred mm in a tidy grid is a table.
        if (pages > 0)
        {
            int probe = Math.Min(4, pages);
            using var s2 = File.OpenRead(path);
            var g = ExtractWholePage(s2, pageNumber: probe, scaleDenominator: 100);

            _out.WriteLine($"      --- page {probe}, what the shapes actually measure (mm) ---");
            _out.WriteLine("      slab diagonals   : " + Spread(g.Slabs.Select(Diagonal)));
            _out.WriteLine("      column width     : " + Spread(g.ColumnSizes.Select(c => c.WidthMm)));
            _out.WriteLine("      column depth     : " + Spread(g.ColumnSizes.Select(c => c.DepthMm)));
            _out.WriteLine("      line lengths     : " + Spread(g.Lines.Select(Diagonal)));
        }

        Assert.True(pages >= 0);   // reporting only — the numbers are the deliverable
    }

    private static double Diagonal(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count == 0) return 0;
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        return Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }

    /// <summary>Where the sizes actually sit, so a tidy cluster reads as a table and a wide spread
    /// reads as a drawing.</summary>
    private static string Spread(IEnumerable<double> values)
    {
        var v = values.Where(x => x > 0).OrderBy(x => x).ToList();
        if (v.Count == 0) return "none";
        string At(double q) => $"{v[(int)Math.Min(v.Count - 1, q * v.Count)]:N0}";
        return $"n={v.Count,5}  min {v[0]:N0}  p25 {At(0.25)}  median {At(0.50)}  p75 {At(0.75)}  max {v[^1]:N0}";
    }

    /// <summary>
    /// The same extraction with the page's own geometry kept. PdfGeometryExtractor hard-codes
    /// annotations-only, so this reproduces its pipeline with the one flag flipped — the smallest
    /// possible change that answers the question, and the reason it is here rather than in the
    /// product: nothing ships until the numbers say it should.
    /// </summary>
    private static ExtractedGeometry ExtractWholePage(Stream pdf, int pageNumber, int scaleDenominator)
    {
        var result = new ExtractedGeometry { ScaleDenominator = scaleDenominator };
        double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var page = doc.GetPage(pageNumber);
        result.PageWidthPts = page.Width;
        result.PageHeightPts = page.Height;
        result.PageCount = doc.NumberOfPages;
        result.TextAnnotations = PdfGeometryParser.ExtractTextAnnotations(page, scale);

        var raw = PdfGeometryParser.ParsePage(page, scale);
        result.RawPathCount = raw.Count;
        if (raw.Count == 0) return result;

        GeometryFilterService.Classify(
            raw, result,
            PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            PdfToSafeConstants.DefaultLineMinLengthMm,
            excludeGridLines: false,
            pageWidthMm: page.Width * scale,
            pageHeightMm: page.Height * scale,
            annotationsOnly: false);

        return result;
    }
}
