using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Compares Classify with and without fate recording: ordered slab/line vertices, column centroids
/// and sizes, colours, annotation flags, drop-panel candidates, section hints, text and page metadata.
/// Does not compare PDF parsing, schedule interpretation, DXF bytes or any real drawing baseline.
/// A changed parser closure tolerance, or a threshold wrong in BOTH runs, would not be caught here.
/// </summary>
public sealed class TheLedgerChangesNothingButTheLedgerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FateRecordingDoesNotChangeGeometry(bool markupOnly, bool excludeGrid)
    {
        var paths = FateFixture.Cases().Select(c => c.Path).ToList();
        paths.Add(FateFixture.Line(0, 0, 70000, 10000));
        var fates = new List<PathFate>();
        AssertGeometryEqual(FateFixture.Classify(paths, null, markupOnly, excludeGrid),
            FateFixture.Classify(paths.Select(p => p with { Points = p.Points.ToList() }).ToList(),
                fates, markupOnly, excludeGrid));
        Assert.Equal(paths.Count, fates.Count);
    }

    [Fact]
    public void DropPanelCandidatesAreAlsoUnchanged()
    {
        var paths = new[] { FateFixture.Rect(1600, 300) };
        var without = FateFixture.Classify(paths, null, slabMinimum: 1000);
        var with = FateFixture.Classify(paths.Select(p => p with { Points = p.Points.ToList() }).ToList(),
            new List<PathFate>(), slabMinimum: 1000);
        Assert.Single(with.DropPanelCandidates);
        AssertGeometryEqual(without, with);
    }

    internal static void AssertGeometryEqual(ExtractedGeometry expected, ExtractedGeometry actual)
    {
        static void Points(IReadOnlyList<List<(double X, double Y)>> a, IReadOnlyList<List<(double X, double Y)>> b)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.Equal(a[i].ToArray(), b[i].ToArray());
        }
        Points(expected.Slabs, actual.Slabs);
        Points(expected.Lines, actual.Lines);
        Points(expected.DropPanelCandidates, actual.DropPanelCandidates);
        Assert.Equal(expected.Columns.ToArray(), actual.Columns.ToArray());
        Assert.Equal(expected.ColumnSizes.ToArray(), actual.ColumnSizes.ToArray());
        Assert.Equal(expected.SlabColors.ToArray(), actual.SlabColors.ToArray());
        Assert.Equal(expected.ColumnColors.ToArray(), actual.ColumnColors.ToArray());
        Assert.Equal(expected.LineColors.ToArray(), actual.LineColors.ToArray());
        Assert.Equal(expected.SlabIsAnnotation.ToArray(), actual.SlabIsAnnotation.ToArray());
        Assert.Equal(expected.ColumnIsAnnotation.ToArray(), actual.ColumnIsAnnotation.ToArray());
        Assert.Equal(expected.LineIsAnnotation.ToArray(), actual.LineIsAnnotation.ToArray());
        Assert.Equal(expected.LineSectionHints.ToArray(), actual.LineSectionHints.ToArray());
        Assert.Equal(expected.TextAnnotations.ToArray(), actual.TextAnnotations.ToArray());
        Assert.Equal(expected.PageWidthPts, actual.PageWidthPts);
        Assert.Equal(expected.PageHeightPts, actual.PageHeightPts);
        Assert.Equal(expected.PageCount, actual.PageCount);
        Assert.Equal(expected.ScaleDenominator, actual.ScaleDenominator);
        Assert.Equal(expected.RawPathCount, actual.RawPathCount);
        Assert.Equal(expected.IsVectorPdf, actual.IsVectorPdf);
    }
}
