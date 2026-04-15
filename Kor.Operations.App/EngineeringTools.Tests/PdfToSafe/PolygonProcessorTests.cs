using System;
using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class PolygonProcessorTests
{
    //  Distance 

    [Fact]
    public void Distance_345Triangle_Returns5()
    {
        Assert.Equal(5.0, PolygonProcessor.Distance((0, 0), (3, 4)), precision: 10);
    }

    [Fact]
    public void Distance_SamePoint_ReturnsZero()
    {
        Assert.Equal(0.0, PolygonProcessor.Distance((7, 3), (7, 3)), precision: 10);
    }

    [Fact]
    public void Distance_AxisAligned_ReturnsAbsDelta()
    {
        Assert.Equal(1000.0, PolygonProcessor.Distance((0, 0), (1000, 0)), precision: 10);
        Assert.Equal(500.0, PolygonProcessor.Distance((0, 0), (0, 500)), precision: 10);
    }

    //  PathLength / PolylineLengthMm 

    [Fact]
    public void PathLength_ThreeSegmentL_ReturnsSumOfSegments()
    {
        var pts = new List<(double, double)> { (0, 0), (1000, 0), (1000, 2000) };
        Assert.Equal(3000.0, PolygonProcessor.PathLength(pts), precision: 10);
    }

    [Fact]
    public void PathLength_SinglePoint_ReturnsZero()
    {
        var pts = new List<(double, double)> { (5, 5) };
        Assert.Equal(0.0, PolygonProcessor.PathLength(pts), precision: 10);
    }

    [Fact]
    public void PathLength_Empty_ReturnsZero()
    {
        Assert.Equal(0.0, PolygonProcessor.PathLength(new List<(double, double)>()), precision: 10);
    }

    [Fact]
    public void PolylineLengthMm_MatchesPathLength()
    {
        var pts = new List<(double, double)> { (0, 0), (300, 400) };
        Assert.Equal(
            PolygonProcessor.PathLength(pts),
            PolygonProcessor.PolylineLengthMm(pts),
            precision: 10);
    }

    //  Centroid (segment-length-weighted) 

    [Fact]
    public void Centroid_SymmetricHorizontalLine_ReturnsMidpoint()
    {
        var pts = new List<(double, double)> { (0, 0), (1000, 0) };
        var (cx, cy) = PolygonProcessor.Centroid(pts);
        Assert.Equal(500.0, cx, precision: 6);
        Assert.Equal(0.0, cy, precision: 6);
    }

    [Fact]
    public void Centroid_Empty_ReturnsOrigin()
    {
        var (cx, cy) = PolygonProcessor.Centroid(new List<(double, double)>());
        Assert.Equal(0.0, cx, precision: 10);
        Assert.Equal(0.0, cy, precision: 10);
    }

    [Fact]
    public void Centroid_SinglePoint_ReturnsThatPoint()
    {
        var pts = new List<(double, double)> { (42, 99) };
        var (cx, cy) = PolygonProcessor.Centroid(pts);
        Assert.Equal(42.0, cx, precision: 10);
        Assert.Equal(99.0, cy, precision: 10);
    }

    //  PolygonAreaMm2 

    [Fact]
    public void PolygonAreaMm2_UnitSquare_ReturnsCorrectArea()
    {
        // 1000mm  1000mm square, CCW
        var sq = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        Assert.Equal(1_000_000.0, PolygonProcessor.PolygonAreaMm2(sq), precision: 6);
    }

    [Fact]
    public void PolygonAreaMm2_RightTriangle_ReturnsHalfBaseTimesHeight()
    {
        // base=6000, height=4000  area=12,000,000
        var tri = new List<(double, double)> { (0, 0), (6000, 0), (0, 4000) };
        Assert.Equal(12_000_000.0, PolygonProcessor.PolygonAreaMm2(tri), precision: 6);
    }

    [Fact]
    public void PolygonAreaMm2_CWPolygon_StillReturnsPositive()
    {
        // Same square but CW winding
        var sq = new List<(double, double)> { (0, 0), (0, 1000), (1000, 1000), (1000, 0) };
        Assert.Equal(1_000_000.0, PolygonProcessor.PolygonAreaMm2(sq), precision: 6);
    }

    [Fact]
    public void PolygonAreaMm2_LessThan3Points_ReturnsZero()
    {
        Assert.Equal(0.0, PolygonProcessor.PolygonAreaMm2(new List<(double, double)> { (0, 0), (1, 1) }));
    }

    //  PolygonAreaCentroid 

    [Fact]
    public void PolygonAreaCentroid_Square_ReturnsCentre()
    {
        var sq = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        var (cx, cy) = PolygonProcessor.PolygonAreaCentroid(sq);
        Assert.Equal(500.0, cx, precision: 6);
        Assert.Equal(500.0, cy, precision: 6);
    }

    [Fact]
    public void PolygonAreaCentroid_RightTriangle_ReturnsThirdPoints()
    {
        // Centroid of triangle at (0,0),(6000,0),(0,4000) = (2000, 1333.333...)
        var tri = new List<(double, double)> { (0, 0), (6000, 0), (0, 4000) };
        var (cx, cy) = PolygonProcessor.PolygonAreaCentroid(tri);
        Assert.Equal(2000.0, cx, precision: 3);
        Assert.Equal(4000.0 / 3.0, cy, precision: 3);
    }

    [Fact]
    public void PolygonAreaCentroid_DegenerateCollinear_FallsBackToLineCentroid()
    {
        // Three collinear points  zero area  falls back to Centroid()
        var pts = new List<(double, double)> { (0, 0), (500, 0), (1000, 0) };
        var (cx, _) = PolygonProcessor.PolygonAreaCentroid(pts);
        Assert.Equal(500.0, cx, precision: 3);
    }

    //  EnsureCCW 

    [Fact]
    public void EnsureCCW_CWSquare_Reverses()
    {
        var cw = new List<(double, double)> { (0, 0), (0, 1000), (1000, 1000), (1000, 0) };
        var result = PolygonProcessor.EnsureCCW(cw);
        // Should be reversed to CCW
        Assert.Equal((1000, 0), result[0]);
        Assert.Equal((0, 0), result[^1]);
    }

    [Fact]
    public void EnsureCCW_AlreadyCCW_ReturnsSameInstance()
    {
        var ccw = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        var result = PolygonProcessor.EnsureCCW(ccw);
        Assert.Same(ccw, result); // No new allocation
    }

    [Fact]
    public void EnsureCCW_LessThan3Points_ReturnsSameInstance()
    {
        var pts = new List<(double, double)> { (0, 0), (1, 1) };
        Assert.Same(pts, PolygonProcessor.EnsureCCW(pts));
    }

    //  DouglasPeucker 

    [Fact]
    public void DouglasPeucker_CollinearNoise_ReducesToEndpoints()
    {
        // Straight line with tiny deviation (1mm) at midpoint, epsilon=5mm
        var pts = new List<(double, double)> { (0, 0), (500, 1), (1000, 0) };
        var result = PolygonProcessor.DouglasPeucker(pts, 5.0);
        Assert.Equal(2, result.Count);
        Assert.Equal((0.0, 0.0), result[0]);
        Assert.Equal((1000.0, 0.0), result[1]);
    }

    [Fact]
    public void DouglasPeucker_SharpCorner_PreservesAllPoints()
    {
        // 90-degree bend  midpoint deviates far beyond epsilon
        var pts = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000) };
        var result = PolygonProcessor.DouglasPeucker(pts, 5.0);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void DouglasPeucker_TwoOrFewerPoints_ReturnsCopy()
    {
        var pts = new List<(double, double)> { (0, 0), (1000, 0) };
        var result = PolygonProcessor.DouglasPeucker(pts, 5.0);
        Assert.Equal(2, result.Count);
        Assert.NotSame(pts, result); // Returns new list
    }

    //  DouglasPeuckerClosed 

    [Fact]
    public void DouglasPeuckerClosed_SquareWithMidpointNoise_KeepsCorners()
    {
        // Square with tiny midpoint bumps on each edge
        var pts = new List<(double, double)>
        {
            (0, 0), (500, 1), (1000, 0),
            (1001, 500), (1000, 1000),
            (500, 999), (0, 1000),
            (-1, 500)
        };
        var result = PolygonProcessor.DouglasPeuckerClosed(pts, 5.0);
        // Should reduce to 4 corners
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void DouglasPeuckerClosed_TriangleOrLess_ReturnsCopy()
    {
        var tri = new List<(double, double)> { (0, 0), (1000, 0), (500, 1000) };
        var result = PolygonProcessor.DouglasPeuckerClosed(tri, 5.0);
        Assert.Equal(3, result.Count);
    }

    //  FilterPoints 

    [Fact]
    public void FilterPoints_RemovesDuplicatesWithinTolerance()
    {
        var pts = new List<(double, double)> { (0, 0), (0.1, 0.1), (1000, 0) };
        var result = PolygonProcessor.FilterPoints(pts, 0.5);
        Assert.Equal(2, result.Count);
        Assert.Equal((0.0, 0.0), result[0]);
        Assert.Equal((1000.0, 0.0), result[1]);
    }

    [Fact]
    public void FilterPoints_RemovesNaNAndInfinity()
    {
        var pts = new List<(double, double)>
        {
            (0, 0),
            (double.NaN, 5),
            (5, double.PositiveInfinity),
            (1000, 0)
        };
        var result = PolygonProcessor.FilterPoints(pts, 0.5);
        Assert.Equal(2, result.Count);
        Assert.Equal((0.0, 0.0), result[0]);
        Assert.Equal((1000.0, 0.0), result[1]);
    }

    [Fact]
    public void FilterPoints_KeepsDistinctPoints()
    {
        var pts = new List<(double, double)> { (0, 0), (100, 0), (200, 0) };
        var result = PolygonProcessor.FilterPoints(pts, 0.5);
        Assert.Equal(3, result.Count);
    }

    //  PointInPolygon 

    [Fact]
    public void PointInPolygon_InsideSquare_ReturnsTrue()
    {
        var sq = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        Assert.True(PolygonProcessor.PointInPolygon((500, 500), sq));
    }

    [Fact]
    public void PointInPolygon_OutsideSquare_ReturnsFalse()
    {
        var sq = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        Assert.False(PolygonProcessor.PointInPolygon((2000, 500), sq));
    }

    [Fact]
    public void PointInPolygon_InsideConcaveL_ReturnsTrue()
    {
        // L-shaped polygon (concave)
        var lShape = new List<(double, double)>
        {
            (0, 0), (2000, 0), (2000, 1000),
            (1000, 1000), (1000, 2000), (0, 2000)
        };
        // Inside the bottom arm
        Assert.True(PolygonProcessor.PointInPolygon((1500, 500), lShape));
        // Inside the left arm
        Assert.True(PolygonProcessor.PointInPolygon((500, 1500), lShape));
        // In the concave notch (outside)
        Assert.False(PolygonProcessor.PointInPolygon((1500, 1500), lShape));
    }

    [Fact]
    public void PointInPolygon_FarOutside_ReturnsFalse()
    {
        var sq = new List<(double, double)> { (0, 0), (100, 0), (100, 100), (0, 100) };
        Assert.False(PolygonProcessor.PointInPolygon((-500, -500), sq));
    }

    //  DetectOpenings 

    [Fact]
    public void DetectOpenings_SmallInsideLarge_DetectsParentChild()
    {
        var outer = new List<(double, double)> { (0, 0), (10000, 0), (10000, 10000), (0, 10000) };
        var inner = new List<(double, double)> { (2000, 2000), (4000, 2000), (4000, 4000), (2000, 4000) };
        var polys = new List<List<(double, double)>> { outer, inner };

        var openings = PolygonProcessor.DetectOpenings(polys);
        Assert.Single(openings);
        Assert.Equal(0, openings[0].ParentIndex);
        Assert.Equal(1, openings[0].ChildIndex);
    }

    [Fact]
    public void DetectOpenings_NonOverlapping_ReturnsEmpty()
    {
        var a = new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) };
        var b = new List<(double, double)> { (5000, 0), (6000, 0), (6000, 1000), (5000, 1000) };
        var polys = new List<List<(double, double)>> { a, b };

        Assert.Empty(PolygonProcessor.DetectOpenings(polys));
    }

    //  DetectDropPanels 

    [Fact]
    public void DetectDropPanels_CandidateNearColumnInsideSlab_Detected()
    {
        var slab = new List<(double, double)> { (0, 0), (10000, 0), (10000, 10000), (0, 10000) };
        var slabs = new List<List<(double, double)>> { slab };
        var columns = new List<(double, double)> { (5000, 5000) };

        // 800mm square drop panel centred on column  diagonal  1131mm (within 300-2000)
        var dp = new List<(double, double)>
        {
            (4600, 4600), (5400, 4600), (5400, 5400), (4600, 5400)
        };
        var candidates = new List<List<(double, double)>> { dp };

        var result = PolygonProcessor.DetectDropPanels(slabs, columns, candidates);
        Assert.Single(result);
        Assert.Equal(0, result[0].SlabIndex);
        Assert.Equal(0, result[0].ColIndex);
    }

    [Fact]
    public void DetectDropPanels_CandidateTooFarFromColumn_NotDetected()
    {
        var slab = new List<(double, double)> { (0, 0), (20000, 0), (20000, 20000), (0, 20000) };
        var slabs = new List<List<(double, double)>> { slab };
        var columns = new List<(double, double)> { (1000, 1000) };

        // Drop panel far from column (centred at 15000,15000)
        var dp = new List<(double, double)>
        {
            (14600, 14600), (15400, 14600), (15400, 15400), (14600, 15400)
        };
        var candidates = new List<List<(double, double)>> { dp };

        Assert.Empty(PolygonProcessor.DetectDropPanels(slabs, columns, candidates));
    }

    //  ProcessSlabs 

    [Fact]
    public void ProcessSlabs_SimplifiesAndNormalizesCCW()
    {
        // CW square with midpoint noise
        var raw = new List<List<(double, double)>>
        {
            new() { (0, 0), (0, 500), (0, 1000), (500, 1001), (1000, 1000), (1000, 500), (1000, 0), (500, -1) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };

        var (slabs, outColors) = PolygonProcessor.ProcessSlabs(raw, colors, 5.0);

        Assert.Single(slabs);
        Assert.Single(outColors);
        Assert.Equal((byte)255, outColors[0].Item1);
        // Should be 4 corners after simplification
        Assert.Equal(4, slabs[0].Count);
        // Should be CCW (positive signed area)
        double area2 = 0;
        var s = slabs[0];
        for (int i = 0; i < s.Count; i++)
        {
            int j = (i + 1) % s.Count;
            area2 += s[i].X * s[j].Y - s[j].X * s[i].Y;
        }
        Assert.True(area2 > 0, "Processed slab should have CCW winding (positive signed area)");
    }

    [Fact]
    public void ProcessSlabs_DegeneratePolygon_Removed()
    {
        // Two "slabs": one valid square, one degenerate line
        var raw = new List<List<(double, double)>>
        {
            new() { (0, 0), (1000, 0), (1000, 1000), (0, 1000) },
            new() { (0, 0), (250, 0), (500, 0), (750, 0), (1000, 0) } // collinear, >3 pts so D-P runs and reduces to 2 endpoints
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        var (slabs, outColors) = PolygonProcessor.ProcessSlabs(raw, colors, 5.0);

        Assert.Single(slabs);
        Assert.Single(outColors);
        Assert.Equal((byte)255, outColors[0].Item1); // Red one survived
    }

    [Fact]
    public void ProcessSlabs_DuplicatePolygons_Deduplicated()
    {
        // Three copies of the same square  should collapse to one
        var sq = new List<(double, double)> { (0, 0), (5000, 0), (5000, 5000), (0, 5000) };
        var raw = new List<List<(double, double)>> { new(sq), new(sq), new(sq) };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (255, 0, 0), (255, 0, 0) };

        var (slabs, outColors) = PolygonProcessor.ProcessSlabs(raw, colors, 5.0);

        Assert.Single(slabs);
        Assert.Single(outColors);
    }

    [Fact]
    public void ProcessSlabs_NearDuplicates_Deduplicated()
    {
        // Two squares differing by < 1mm  should deduplicate
        var sq1 = new List<(double, double)> { (0, 0), (5000, 0), (5000, 5000), (0, 5000) };
        var sq2 = new List<(double, double)> { (0.3, 0.3), (5000.3, 0.3), (5000.3, 5000.3), (0.3, 5000.3) };
        var raw = new List<List<(double, double)>> { sq1, sq2 };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        var (slabs, _) = PolygonProcessor.ProcessSlabs(raw, colors, 5.0);

        Assert.Single(slabs);
    }

    [Fact]
    public void ProcessSlabs_DistinctPolygons_BothKept()
    {
        // Two genuinely different slabs  both should survive
        var sq1 = new List<(double, double)> { (0, 0), (5000, 0), (5000, 5000), (0, 5000) };
        var sq2 = new List<(double, double)> { (10000, 0), (15000, 0), (15000, 5000), (10000, 5000) };
        var raw = new List<List<(double, double)>> { sq1, sq2 };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        var (slabs, _) = PolygonProcessor.ProcessSlabs(raw, colors, 5.0);

        Assert.Equal(2, slabs.Count);
    }

    //  ReducePolygonToWallCenterline  ─────────────────────────────────────

    [Fact]
    public void ReducePolygonToWallCenterline_TallNarrowRectangle_CenterlineRunsVertical()
    {
        // 200mm wide x 5000mm tall wall footprint (vertical long axis).
        var pts = new List<(double, double)>
        {
            (100, 0), (300, 0), (300, 5000), (100, 5000)
        };

        var (start, end, width, depth) = PolygonProcessor.ReducePolygonToWallCenterline(pts, defaultDepthMm: 1000);

        // Centerline at X = 200 (midpoint), running from Y=0 to Y=5000.
        Assert.Equal(200, start.Item1, 3);
        Assert.Equal(0, start.Item2, 3);
        Assert.Equal(200, end.Item1, 3);
        Assert.Equal(5000, end.Item2, 3);
        Assert.Equal(200, width, 3);
        Assert.Equal(1000, depth, 3);
    }

    [Fact]
    public void ReducePolygonToWallCenterline_WideShortRectangle_CenterlineRunsHorizontal()
    {
        // 8000mm wide x 300mm tall wall footprint (horizontal long axis).
        var pts = new List<(double, double)>
        {
            (0, 1000), (8000, 1000), (8000, 1300), (0, 1300)
        };

        var (start, end, width, depth) = PolygonProcessor.ReducePolygonToWallCenterline(pts);

        Assert.Equal(0, start.Item1, 3);
        Assert.Equal(1150, start.Item2, 3);
        Assert.Equal(8000, end.Item1, 3);
        Assert.Equal(1150, end.Item2, 3);
        Assert.Equal(300, width, 3);
        Assert.Equal(1000, depth, 3);
    }

    [Fact]
    public void ReducePolygonToWallCenterline_LShapedPolygon_UsesBoundingBox()
    {
        // L-shape with bbox 2000mm x 4000mm.
        var pts = new List<(double, double)>
        {
            (0, 0), (2000, 0), (2000, 1000),
            (1000, 1000), (1000, 4000), (0, 4000)
        };

        var (start, end, width, depth) = PolygonProcessor.ReducePolygonToWallCenterline(pts, defaultDepthMm: 500);

        Assert.Equal(1000, start.Item1, 3);
        Assert.Equal(0, start.Item2, 3);
        Assert.Equal(1000, end.Item1, 3);
        Assert.Equal(4000, end.Item2, 3);
        Assert.Equal(2000, width, 3);
        Assert.Equal(500, depth, 3);
    }

    [Fact]
    public void ReducePolygonToWallCenterline_DegenerateInput_Throws()
    {
        var pts = new List<(double, double)> { (0, 0) };
        Assert.Throws<System.ArgumentException>(
            () => PolygonProcessor.ReducePolygonToWallCenterline(pts));
    }
}
