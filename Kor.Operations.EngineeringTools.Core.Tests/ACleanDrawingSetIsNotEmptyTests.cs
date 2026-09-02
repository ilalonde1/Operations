#nullable enable
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A drawing set with no Bluebeam markup must not read as an empty drawing.
/// </summary>
/// <remarks>
/// `GeometryFilterService.Classify` takes an `annotationsOnly` flag that defaults to true, because
/// PdfToSafe was built for the markup workflow: the engineer's redlines ARE the model and the
/// architect's page underneath is noise. That is a good default for that job.
///
/// The fault was that NO CALLER ANYWHERE PASSED IT. Two mentions existed in the whole solution — the
/// declaration and the use — so the mode was not a mode, it was the only behaviour. A clean issued
/// set, which carries no markup at all, therefore returned nothing at all and said nothing about
/// why. 31130-01's stick file is that case: 60 pages, every one carrying between 326 and 19,520
/// vector subpaths, every one returning zero slabs, zero columns and zero lines.
///
/// Nothing was wrong with the reader. The switch was simply unreachable, and the symptom — an empty
/// result — is indistinguishable from a genuinely blank page unless someone counts the raw paths.
///
/// WHAT THIS COVERS: that the flag reaches the classifier and changes what comes out; that
/// markup-only really does drop page content; that page-content mode keeps annotations too, so
/// turning it on never loses a markup the old mode would have found; and that RawPathCount is
/// non-zero in the case that used to look empty, which is the number that tells a blank page from a
/// discarded one.
///
/// WHAT IT DOES NOT COVER: it never opens a PDF, so it cannot catch a fault in PdfPig parsing,
/// in the mm scaling, or in whether `IsAnnotation` is set correctly by VectorPageReader — a subpath
/// wrongly marked as an annotation would pass every assertion here. It also says nothing about the
/// DXF that gets written afterwards. A same-class fault it would NOT catch: if `PdfPlanReader.Read`
/// stopped forwarding the flag and hard-coded true again, `Classify` would still behave correctly
/// and every test below would stay green — <see cref="ThePlanReaderForwardsTheFlagItWasGivenTests"/>
/// is the one that pins that.
/// </remarks>
public sealed class ACleanDrawingSetIsNotEmptyTests
{
    private const double PageWmm = 100_000, PageHmm = 70_000;

    private static RawSubpath Rect(double w, double h, bool annotation, bool filled = true)
        => new(
            new List<(double X, double Y)> { (0, 0), (w, 0), (w, h), (0, h) },
            IsClosed: true,
            Color: (0xC8, 0x10, 0x10),          // a red, so the black/gray symbol filter is not in play
            IsFilled: filled,
            IsStroked: true,
            LineWidth: 1.0,
            IsAnnotation: annotation);

    private static RawSubpath Run(double length, bool annotation)
        => new(
            new List<(double X, double Y)> { (0, 0), (length, 0) },
            IsClosed: false,
            Color: (0xC8, 0x10, 0x10),
            IsFilled: false,
            IsStroked: true,
            LineWidth: 1.0,
            IsAnnotation: annotation);

    /// <summary>A sheet the engineer never marked up: one slab, one column, one beam run.</summary>
    private static List<RawSubpath> CleanSheet() =>
    [
        Rect(5_000, 4_000, annotation: false),   // slab: too big to be a column, diagonal over 1 m
        Rect(600, 600, annotation: false),       // column: inside 1.5 m, over 200 mm, square
        Run(3_000, annotation: false),           // a line, over the 200 mm minimum
    ];

    private static ExtractedGeometry Classify(List<RawSubpath> paths, bool annotationsOnly)
    {
        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(
            paths, result,
            PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            PdfToSafeConstants.DefaultLineMinLengthMm,
            excludeGridLines: false,
            pageWidthMm: PageWmm, pageHeightMm: PageHmm,
            annotationsOnly: annotationsOnly);
        return result;
    }

    /// <summary>The exact symptom: a real drawing, and nothing comes back.</summary>
    [Fact]
    public void MarkupOnlyModeReturnsNothingForASheetWithNoMarkup()
    {
        var geo = Classify(CleanSheet(), annotationsOnly: true);

        Assert.Empty(geo.Slabs);
        Assert.Empty(geo.Columns);
        Assert.Empty(geo.Lines);
    }

    /// <summary>And the same paths, read as the drawing they are.</summary>
    [Fact]
    public void ReadingTheDrawingFindsTheStructureThatWasAlwaysThere()
    {
        var geo = Classify(CleanSheet(), annotationsOnly: false);

        Assert.Single(geo.Slabs);
        Assert.Single(geo.Columns);
        Assert.Single(geo.Lines);
    }

    /// <summary>
    /// Turning page content on must not cost a markup. If it did, the markup workflow would have to
    /// choose between the two, and every existing caller relies on annotations being found.
    /// </summary>
    [Fact]
    public void ReadingTheDrawingStillFindsEveryMarkupMarkupOnlyWouldHave()
    {
        var mixed = new List<RawSubpath>(CleanSheet())
        {
            Rect(5_000, 4_000, annotation: true),
            Rect(600, 600, annotation: true),
            Run(3_000, annotation: true),
        };

        var markupOnly = Classify(mixed, annotationsOnly: true);
        var everything = Classify(mixed, annotationsOnly: false);

        Assert.Equal(
            (1, 1, 1),
            (markupOnly.Slabs.Count, markupOnly.Columns.Count, markupOnly.Lines.Count));

        // every annotation the strict mode found is still found, plus the drawing's own
        Assert.Equal(
            (2, 2, 2),
            (everything.Slabs.Count, everything.Columns.Count, everything.Lines.Count));
        Assert.Equal(3, everything.SlabIsAnnotation.Count(x => x) + everything.ColumnIsAnnotation.Count(x => x) + everything.LineIsAnnotation.Count(x => x));
    }

    /// <summary>
    /// The default is still markup-only, deliberately. Changing it would silently alter what every
    /// existing caller sees, and the app's whole workflow is built on redlines being the model.
    /// </summary>
    [Fact]
    public void TheDefaultIsStillMarkupOnly()
    {
        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(
            CleanSheet(), result,
            PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            PdfToSafeConstants.DefaultLineMinLengthMm,
            excludeGridLines: false,
            pageWidthMm: PageWmm, pageHeightMm: PageHmm);

        Assert.Empty(result.Slabs);
    }
}

/// <summary>
/// The flag must survive the trip from the caller to the classifier.
/// </summary>
/// <remarks>
/// Separated from the tests above on purpose. Those prove the classifier honours the flag; this
/// proves the reader hands it over. The original fault was entirely in the second half — the
/// classifier was always correct and always had the parameter, and no one could reach it.
///
/// WHAT THIS COVERS: `PdfPlanReader.Read`'s signature keeps an `annotationsOnly` parameter, still
/// defaulting to true. WHAT IT DOES NOT: that the value is actually forwarded at the call site —
/// only opening a real drawing proves that, and there is no PDF fixture in this project.
/// </remarks>
public sealed class ThePlanReaderForwardsTheFlagItWasGivenTests
{
    [Fact]
    public void ReadStillExposesAnnotationsOnlyAndStillDefaultsToTrue()
    {
        var read = typeof(PdfPlanReader)
            .GetMethods()
            .Where(m => m.Name == nameof(PdfPlanReader.Read))
            .ToList();

        Assert.NotEmpty(read);
        foreach (var overload in read)
        {
            var flag = overload.GetParameters().SingleOrDefault(p => p.Name == "annotationsOnly");
            Assert.NotNull(flag);
            Assert.True(flag!.HasDefaultValue);
            Assert.Equal(true, flag.DefaultValue);
        }
    }
}
