using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class GeometryFilterServiceTests
{
    /// <summary>
    /// Helper: convert old-style tuple to RawSubpath with structural-markup defaults
    /// (filled, stroked, lineWidth=1, not annotation) so pre-filters pass.
    /// </summary>
    private static List<RawSubpath> ToSubpaths(
        IEnumerable<(List<(double X, double Y)> Points, bool IsClosed, (byte R, byte G, byte B) Color)> tuples)
        => tuples.Select(t => new RawSubpath(t.Points, t.IsClosed, t.Color,
            IsFilled: t.IsClosed, IsStroked: true, LineWidth: 1, IsAnnotation: true)).ToList();

    //  BoundingBoxDiagonal

    [Fact]
    public void BoundingBoxDiagonal_UnitSquare_ReturnsSqrt2()
    {
        var pts = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        double diag = GeometryFilterService.BoundingBoxDiagonal(pts);
        Assert.Equal(1000 * System.Math.Sqrt(2), diag, precision: 3);
    }

    //  Classify: closed large polygon  Slab

    [Fact]
    public void Classify_LargeClosedPolygon_ClassifiedAsSlab()
    {
        var slab = new List<(double, double)> { (0, 0), (5000, 0), (5000, 5000), (0, 5000) };
        var subpaths = ToSubpaths(new[] { (slab, true, ((byte)255, (byte)0, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Slabs);
        Assert.Empty(result.Columns);
        Assert.Equal((byte)255, result.SlabColors[0].R);
    }

    //  Classify: closed small polygon  Column

    [Fact]
    public void Classify_SmallClosedPolygon_ClassifiedAsColumn()
    {
        var col = new List<(double, double)> { (0, 0), (500, 0), (500, 500), (0, 500) };
        var subpaths = ToSubpaths(new[] { (col, true, ((byte)0, (byte)0, (byte)255)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Slabs);
        Assert.Single(result.Columns);
        Assert.Single(result.ColumnSizes);
        Assert.Equal(500, result.ColumnSizes[0].WidthMm, precision: 1);
        Assert.Equal(500, result.ColumnSizes[0].DepthMm, precision: 1);
    }

    [Fact]
    public void Classify_TinyClosedPath_SkippedAsNoise()
    {
        // 50mm x 50mm annotation box — too small to be any structural element
        var box = new List<(double, double)> { (0, 0), (50, 0), (50, 50), (0, 50) };
        var subpaths = ToSubpaths(new[] { (box, true, ((byte)0, (byte)0, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Slabs);
        Assert.Empty(result.Columns);
    }

    [Fact]
    public void Classify_ThinClosedPath_SkippedAsNoise()
    {
        // 80mm x 500mm annotation frame — one dimension below 100mm minimum
        var box = new List<(double, double)> { (0, 0), (500, 0), (500, 80), (0, 80) };
        var subpaths = ToSubpaths(new[] { (box, true, ((byte)0, (byte)0, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Columns);
    }

    [Fact]
    public void Classify_ElongatedClosedPath_SkippedAsNoise()
    {
        // 200mm x 800mm room tag — aspect ratio 4:1 > 2.5 threshold
        var box = new List<(double, double)> { (0, 0), (800, 0), (800, 200), (0, 200) };
        var subpaths = ToSubpaths(new[] { (box, true, ((byte)0, (byte)0, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Columns);
    }

    [Fact]
    public void Classify_RectangularColumn_Accepted()
    {
        // 400mm x 800mm rectangular column — aspect ratio 2:1, both dims > 100mm
        var col = new List<(double, double)> { (0, 0), (800, 0), (800, 400), (0, 400) };
        var subpaths = ToSubpaths(new[] { (col, true, ((byte)0, (byte)0, (byte)255)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Columns);
    }

    //  Classify: open path  Line

    [Fact]
    public void Classify_LongOpenPath_ClassifiedAsLine()
    {
        var beam = new List<(double, double)> { (0, 0), (3000, 0) };
        var subpaths = ToSubpaths(new[] { (beam, false, ((byte)0, (byte)255, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Slabs);
        Assert.Empty(result.Columns);
        Assert.Single(result.Lines);
        Assert.Equal((byte)0, result.LineColors[0].R);
        Assert.Equal((byte)255, result.LineColors[0].G);
    }

    //  Classify: short open path  ignored

    [Fact]
    public void Classify_ShortOpenPath_Ignored()
    {
        var tiny = new List<(double, double)> { (0, 0), (50, 0) }; // 50mm < 200mm
        // Use a non-gray color so the pre-filter doesn't catch it for the wrong reason
        var subpaths = ToSubpaths(new[] { (tiny, false, ((byte)0, (byte)255, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Lines);
    }

    //  Classify: grid line exclusion

    [Fact]
    public void Classify_LongTwoPointLine_ExcludedAsGridLine()
    {
        var grid = new List<(double, double)> { (0, 0), (15000, 0) };
        // Use non-gray color so it passes the black/gray pre-filter
        var subpaths = ToSubpaths(new[] { (grid, false, ((byte)0, (byte)255, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: true, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Classify_LongTwoPointLine_KeptWhenExcludeGridLinesOff()
    {
        var grid = new List<(double, double)> { (0, 0), (15000, 0) };
        // Use non-gray color so it passes the black/gray pre-filter
        var subpaths = ToSubpaths(new[] { (grid, false, ((byte)0, (byte)255, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Lines);
    }

    //  Classify: drop panel candidate detection

    [Fact]
    public void Classify_MidSizeClosedPolygon_AddedAsDropPanelCandidate()
    {
        // 1600x400mm rectangle: bboxW=1600 > 1500 -> not column.
        // Diagonal = sqrt(1600^2+400^2) ~ 1649mm (within 300-2000 -> drop panel candidate)
        var mid = new List<(double, double)> { (0, 0), (1600, 0), (1600, 400), (0, 400) };
        var subpaths = ToSubpaths(new[] { (mid, true, ((byte)128, (byte)128, (byte)0)) });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Slabs);
        Assert.Single(result.DropPanelCandidates);
    }

    //  Classify: mixed input

    [Fact]
    public void Classify_MixedInput_CorrectCounts()
    {
        var slab = new List<(double, double)> { (0, 0), (5000, 0), (5000, 5000), (0, 5000) };
        var col = new List<(double, double)> { (100, 100), (400, 100), (400, 400), (100, 400) };
        var beam = new List<(double, double)> { (0, 0), (3000, 0) };
        var tiny = new List<(double, double)> { (0, 0), (10, 0) }; // too short

        var subpaths = ToSubpaths(new (List<(double, double)>, bool, (byte, byte, byte))[]
        {
            (slab, true, (255, 0, 0)),
            (col, true, (0, 0, 255)),
            (beam, false, (0, 255, 0)),
            (tiny, false, (0, 255, 0))
        });

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Slabs);
        Assert.Single(result.Columns);
        Assert.Single(result.Lines);
        Assert.Equal(3, result.SlabColors.Count + result.ColumnColors.Count + result.LineColors.Count);
    }

    //  New: black/gray stroke-only lines filtered as architectural noise

    [Fact]
    public void Classify_PageContentLine_FilteredInAnnotationsOnlyMode()
    {
        var line = new List<(double, double)> { (0, 0), (3000, 0) };
        // Page content (not annotation) = skipped in annotations-only mode
        var subpaths = new List<RawSubpath>
        {
            new(line, false, (0, 255, 0), IsFilled: false, IsStroked: true, LineWidth: 1, IsAnnotation: false)
        };

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Classify_AnnotationBlackLine_KeptAsStructural()
    {
        var line = new List<(double, double)> { (0, 0), (3000, 0) };
        // Annotation-sourced = Bluebeam markup, kept even if black
        var subpaths = new List<RawSubpath>
        {
            new(line, false, (0, 0, 0), IsFilled: false, IsStroked: true, LineWidth: 1, IsAnnotation: true)
        };

        var result = new ExtractedGeometry();
        GeometryFilterService.Classify(subpaths, result,
            slabMinDiagonalMm: 1000, lineMinLengthMm: 200,
            excludeGridLines: false, pageWidthMm: 20000, pageHeightMm: 15000);

        Assert.Single(result.Lines);
    }
}

public class GeometryExclusionStateTests
{
    //  Index-based exclusion

    [Fact]
    public void IsSlabExcluded_ByIndex_ReturnsTrue()
    {
        var excl = new GeometryExclusionState();
        excl.Slabs.Add(2);
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0), (0, 0, 255) };

        Assert.False(excl.IsSlabExcluded(0, colors));
        Assert.False(excl.IsSlabExcluded(1, colors));
        Assert.True(excl.IsSlabExcluded(2, colors));
    }

    //  Color-based exclusion

    [Fact]
    public void IsSlabExcluded_ByColor_ReturnsTrue()
    {
        var excl = new GeometryExclusionState();
        excl.Colors.Add((255, 0, 0));
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        Assert.True(excl.IsSlabExcluded(0, colors));
        Assert.False(excl.IsSlabExcluded(1, colors));
    }

    [Fact]
    public void IsLineExcluded_ByColor_ReturnsTrue()
    {
        var excl = new GeometryExclusionState();
        excl.Colors.Add((0, 255, 0));
        var colors = new List<(byte, byte, byte)> { (0, 255, 0) };

        Assert.True(excl.IsLineExcluded(0, colors));
    }

    [Fact]
    public void IsColumnExcluded_ByIndex_ReturnsTrue()
    {
        var excl = new GeometryExclusionState();
        excl.Columns.Add(0);
        var colors = new List<(byte, byte, byte)> { (128, 128, 128) };

        Assert.True(excl.IsColumnExcluded(0, colors));
    }

    //  HasIndexExclusions

    [Fact]
    public void HasIndexExclusions_Empty_ReturnsFalse()
    {
        Assert.False(new GeometryExclusionState().HasIndexExclusions);
    }

    [Fact]
    public void HasIndexExclusions_WithSlabIndex_ReturnsTrue()
    {
        var excl = new GeometryExclusionState();
        excl.Slabs.Add(0);
        Assert.True(excl.HasIndexExclusions);
    }

    //  Clear

    [Fact]
    public void Clear_ResetsAllSets()
    {
        var excl = new GeometryExclusionState();
        excl.Slabs.Add(0);
        excl.Lines.Add(1);
        excl.Columns.Add(2);
        excl.Colors.Add((255, 0, 0));

        excl.Clear();

        Assert.Empty(excl.Slabs);
        Assert.Empty(excl.Lines);
        Assert.Empty(excl.Columns);
        Assert.Empty(excl.Colors);
    }

    //  FilterGeometry

    [Fact]
    public void FilterGeometry_NoColors_ReturnsSameInstance()
    {
        var excl = new GeometryExclusionState();
        var geom = new ExtractedGeometry();
        Assert.Same(geom, excl.FilterGeometry(geom));
    }

    [Fact]
    public void FilterGeometry_ExcludedColor_RemovesMatchingElements()
    {
        var geom = new ExtractedGeometry();
        geom.Slabs.Add(new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) });
        geom.SlabColors.Add((255, 0, 0));
        geom.Slabs.Add(new List<(double, double)> { (2000, 0), (3000, 0), (3000, 1000), (2000, 1000) });
        geom.SlabColors.Add((0, 255, 0));

        geom.Lines.Add(new List<(double, double)> { (0, 0), (5000, 0) });
        geom.LineColors.Add((255, 0, 0));

        geom.Columns.Add((500, 500));
        geom.ColumnColors.Add((0, 0, 255));

        var excl = new GeometryExclusionState();
        excl.Colors.Add((255, 0, 0)); // exclude red

        var filtered = excl.FilterGeometry(geom);

        Assert.NotSame(geom, filtered);
        Assert.Single(filtered.Slabs);          // green slab kept
        Assert.Equal((byte)0, filtered.SlabColors[0].R);
        Assert.Equal((byte)255, filtered.SlabColors[0].G);
        Assert.Empty(filtered.Lines);            // red line removed
        Assert.Single(filtered.Columns);         // blue column kept
    }
}
