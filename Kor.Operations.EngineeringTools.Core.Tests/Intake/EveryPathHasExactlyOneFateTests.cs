using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>Exercises each classifier decision, including both axes, both frame edges and fallthroughs.</summary>
public sealed class EveryPathHasExactlyOneFateTests
{
    [Fact]
    public void EveryInputIndexHasOneDecisionAndTheRightObjectIndex()
    {
        var cases = FateFixture.Cases();
        var fates = new List<PathFate>();
        var geometry = FateFixture.Classify(cases.Select(c => c.Path).ToList(), fates);
        Assert.Equal(Enumerable.Range(0, cases.Count), fates.Select(f => f.PathIndex).OrderBy(i => i));
        Assert.Equal(cases.Select(c => c.Reason), fates.Select(f => f.Reason));
        foreach (var fate in fates)
        {
            Assert.Equal(PathFate.DispositionOf(fate.Reason), fate.Disposition);
            var path = cases[fate.PathIndex].Path;
            switch (fate.Reason)
            {
                case PathReason.BecameSlab:
                    Assert.Same(path.Points, geometry.Slabs[fate.ObjectIndex!.Value]);
                    break;
                case PathReason.BecameWall:
                    Assert.Same(path.Points, geometry.Walls[fate.ObjectIndex!.Value].Outline);
                    break;
                case PathReason.BecameColumnByDeclaredSize:
                case PathReason.BecameColumnByShape:
                    Assert.Equal(PolygonProcessor.Centroid(path.Points), geometry.Columns[fate.ObjectIndex!.Value]);
                    break;
                case PathReason.EmittedAsLine:
                    Assert.Same(path.Points, geometry.Lines[fate.ObjectIndex!.Value]);
                    break;
                default:
                    Assert.Null(fate.ObjectIndex);
                    break;
            }
        }
    }

    [Fact]
    public void ModeAndGridExclusionHaveTheirOwnReasons()
    {
        var fates = new List<PathFate>();
        FateFixture.Classify([FateFixture.Rect(600, 600)], fates, markupOnly: true);
        Assert.Equal(PathReason.MarkupOnlyMode, Assert.Single(fates).Reason);
        fates.Clear();
        FateFixture.Classify([FateFixture.Line(0, 0, 70000, 10000)], fates, excludeGrid: true);
        Assert.Equal(PathReason.GridLineExcluded, Assert.Single(fates).Reason);

        var reached = FateFixture.Cases().Select(c => c.Reason)
            // CollapsedByThinning is never recorded by Classify: the read drops those paths before it
            // runs, and DrawingIntake.RemapToPopulation assigns it (ThePopulationIsTheUnthinnedReadTests).
            .Append(PathReason.MarkupOnlyMode).Append(PathReason.GridLineExcluded).Append(PathReason.CollapsedByThinning).Distinct().OrderBy(r => r);
        Assert.Equal(Enum.GetValues<PathReason>().OrderBy(r => r), reached);
    }

    [Fact]
    public void DispositionIsDefinedByReasonAlone()
    {
        foreach (var reason in Enum.GetValues<PathReason>())
        {
            var expected = reason switch
            {
                PathReason.BecameSlab or PathReason.BecameColumnByDeclaredSize or PathReason.BecameColumnByShape or PathReason.BecameWall => Disposition.Read,
                PathReason.EmittedAsLine => Disposition.Unaccounted,
                _ => Disposition.Discarded,
            };
            Assert.Equal(expected, PathFate.DispositionOf(reason));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFate.DispositionOf((PathReason)999));
    }

    [Fact]
    public void FatesAppendAndObjectIndicesReferToTheSuppliedGeometry()
    {
        var geometry = new ExtractedGeometry();
        geometry.Columns.Add((-1, -1));
        var fates = new List<PathFate> { new(-1, Disposition.Discarded, PathReason.TooShort, null) };
        FateFixture.Classify([FateFixture.Rect(600, 600)], fates, result: geometry);
        Assert.Equal(2, fates.Count);
        Assert.Equal(0, fates[1].PathIndex);
        Assert.Equal(1, fates[1].ObjectIndex);
    }
}

internal static class FateFixture
{
    internal static RawSubpath Rect(double w, double h, double x = 0, double y = 0)
        => new([(x, y), (x + w, y), (x + w, y + h), (x, y + h)],
            true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false);

    internal static RawSubpath Line(double x0, double y0, double x1, double y1)
        => new([(x0, y0), (x1, y1)], false, (0, 0, 0), false, true, 0.5, false);

    internal static List<(RawSubpath Path, PathReason Reason)> Cases() =>
    [
        (Rect(5000, 4000), PathReason.BecameSlab),
        (Rect(5000, 4000) with { IsFilled = false, IsStroked = false }, PathReason.NoInk),
        (Rect(1800, 400), PathReason.BecameColumnByDeclaredSize),
        (Rect(600, 800), PathReason.BecameColumnByShape),
        (Line(1000, 1000, 4000, 1500), PathReason.EmittedAsLine),
        (Rect(600, 600, 20000, 20000), PathReason.FurnitureRegion),
        (Line(30000, 1000, 30000, 3000), PathReason.GridAxis),
        (Line(1000, 30000, 3000, 30000), PathReason.GridAxis),
        (Line(1000, 2000, 3000, 2000), PathReason.Underline),
        (Rect(600, 600) with { Color = (0xF0, 0xF0, 0xF0) }, PathReason.PaperFill),
        (Rect(70000, 50000), PathReason.SheetFrame),
        (Line(0, 0, 70000, 0), PathReason.FrameEdgeLine),
        (Line(0, 0, 0, 50000), PathReason.FrameEdgeLine),
        (Rect(100, 500), PathReason.ColumnTooSmall),
        (Rect(600, 600) with { IsFilled = false, IsStroked = true, Color = (0, 0, 0) }, PathReason.UnfilledSmallShape),
        (Rect(250, 1000), PathReason.ColumnAspect),
        (Line(0, 0, 100, 0), PathReason.TooShort),
        (Rect(2000, 1000), PathReason.BecameWall),
        (Rect(2000, 1600), PathReason.TooShort),
        (Line(0, 0, 0, 0) with { Points = [] }, PathReason.TooFewPoints),
        (Line(0, 0, 0, 0) with { Points = [(0, 0)] }, PathReason.TooFewPoints),
        // Markup bypasses furniture, paper, frame, minimum-size and aspect rules.
        (Rect(100, 500, 20000, 20000) with { IsAnnotation = true }, PathReason.BecameColumnByShape),
        (Rect(600, 600) with { IsAnnotation = true, Color = (0xF0, 0xF0, 0xF0) }, PathReason.BecameColumnByShape),
        (Rect(70000, 50000) with { IsAnnotation = true }, PathReason.BecameSlab),
        (Line(0, 0, 70000, 0) with { IsAnnotation = true }, PathReason.EmittedAsLine),
    ];

    internal static ExtractedGeometry Classify(IReadOnlyList<RawSubpath> paths, IList<PathFate>? fates,
        bool markupOnly = false, bool excludeGrid = false, ExtractedGeometry? result = null,
        double slabMinimum = 3000)
    {
        result ??= new ExtractedGeometry();
        var furniture = new SheetFurniture.Set(
            [new("schedule: COLUMN SCHEDULE", 20000, 20000, 22000, 22000)],
            [30000], [30000], 1.5, [(1800, 400)], 1)
        {
            Underlines = [new(1000, 3000, 2000)],
        };
        GeometryFilterService.Classify(paths, result, slabMinimum, 200, excludeGrid, 100000, 70000,
            markupOnly, furniture: furniture, fates: fates);
        return result;
    }
}
