using System;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// A LABEL IS TEXT ON A DRAWING. ITS HEIGHT COMES FROM THE SHEET, NOT FROM THE THING IT DESCRIBES.
///
/// A page WORD has a bounding box and that box's height IS the text height. A Bluebeam annotation's
/// /Contents has no box — Bluebeam shows it in a popup, never on the sheet — and `ann.Rectangle` is
/// the extent of THE SHAPE HE DREW. Reading the height from it set every `12" x 30"` as tall as the
/// wall it labels: 796 mm of text on a 796 mm wall segment, 956 on the dimension string. Opened in
/// CAD it looked, accurately, like a three-year-old had done it, and it shipped that way because the
/// only check run on it was on the OTHER file — the tower plan, whose text comes from page words and
/// was correct all along.
///
/// So the rule has two halves and this gate holds both:
///
///   MARKUP LABELS take a drawing text height — 2.5 mm on paper times the sheet scale. One value for
///   every label, and small against the geometry, because that is what annotation is.
///
///   PAGE WORDS keep their own measured heights, which vary and already were right. A fix that
///   flattened those too would trade one wrong drawing for another.
/// </summary>
public sealed class MarkupLabelsAreDrawingTextGate
{
    private readonly ITestOutputHelper _out;
    public MarkupLabelsAreDrawingTextGate(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ALabelIsSizedBySheetScaleAndAPageWordByItsOwnBox()
    {
        string pdf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "OAP-parcel11-arch-markup.pdf");
        if (!File.Exists(pdf)) { _out.WriteLine($"SKIPPED: not at {pdf}"); return; }

        var detected = PdfGeometryExtractor.DetectScaleForLoad(pdf, 1);
        Assert.True(detected.Denominator == 96, "this sheet states 1/8\" = 1'-0\"");
        int denominator = detected.Denominator!.Value;

        ExtractedGeometry g;
        using (var s = File.OpenRead(pdf))
            g = PdfGeometryExtractor.Extract(s, scaleDenominator: denominator, pageNumber: 1);

        var labels = g.TextAnnotations.Where(t => t.HeightMm > 0).ToList();
        Assert.True(labels.Count > 0, "no markup labels — his 12\" x 30\" notes should be here");

        double scale = denominator * PdfToSafeConstants.PointsToMm;
        double expected = PdfToSafeConstants.PaperTextHeightMm * scale / PdfToSafeConstants.PointsToMm;
        var heights = labels.Select(t => t.HeightMm).OrderBy(h => h).ToList();
        _out.WriteLine($"{labels.Count} markup label(s), height {heights[0]:N0}-{heights[^1]:N0} mm, " +
                       $"expected {expected:N0} ({PdfToSafeConstants.PaperTextHeightMm} mm on paper at 1:{denominator})");

        foreach (var t in labels)
            Assert.True(Math.Abs(t.HeightMm - expected) < 1.0,
                $"\"{t.Text}\" is {t.HeightMm:N0} mm; a label is drawing text at {expected:N0} mm, " +
                "not the size of the shape it annotates");

        // AND SMALL AGAINST WHAT IT LABELS. The identity above would still pass if the paper height
        // were set to something absurd, so measure the labels against the shapes they annotate — his
        // shapes, by origin, not the whole page.
        // Slabs and lines, not columns — a column in this geometry is an insertion POINT and has no
        // extent to compare against. His shear walls and core walls are closed regions.
        var spans = new System.Collections.Generic.List<double>();
        for (int i = 0; i < g.Slabs.Count; i++)
            if (i < g.SlabIsAnnotation.Count && g.SlabIsAnnotation[i] && g.Slabs[i].Count > 1)
                spans.Add(Span(g.Slabs[i]));
        for (int i = 0; i < g.Lines.Count; i++)
            if (i < g.LineIsAnnotation.Count && g.LineIsAnnotation[i] && g.Lines[i].Count > 1)
                spans.Add(Span(g.Lines[i]));

        Assert.True(spans.Count > 0, "no markup shapes to measure against");
        spans.Sort();
        double medianShape = spans[spans.Count / 2];
        _out.WriteLine($"{spans.Count} markup shape(s), median {medianShape:N0} mm across");
        Assert.True(expected < medianShape / 2.0,
            $"a {expected:N0} mm label on a {medianShape:N0} mm shape is not annotation, it is overprinting");

        // THE OTHER HALF: page words keep their own heights. Same page, read the other way.
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var words = PdfGeometryParser.ExtractTextAnnotations(doc.GetPage(1), scale);
        var wordHeights = words.Select(w => w.HeightMm).Where(h => h > 0).OrderBy(h => h).ToList();
        _out.WriteLine($"{wordHeights.Count} page word(s), height {wordHeights[0]:N0}-{wordHeights[^1]:N0} mm, " +
                       $"median {wordHeights[wordHeights.Count / 2]:N0}");
        Assert.True(wordHeights.Distinct().Count() > 1,
            "page words are measured from their own boxes and must not be flattened to one height");
    }

    /// <summary>The longer side of a shape's bounding box.</summary>
    private static double Span(System.Collections.Generic.IReadOnlyList<(double X, double Y)> p)
        => Math.Max(p.Max(q => q.X) - p.Min(q => q.X), p.Max(q => q.Y) - p.Min(q => q.Y));
}
