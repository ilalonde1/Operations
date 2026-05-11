using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class WallOpeningDetectorTests
{
    // A 30 m × 30 m slab centred at origin, plus a single rectangular shaft
    // formed by 4 wall centerlines spanning x∈[-2000,2000], y∈[-2000,2000].
    private static (
        List<List<(double X, double Y)>> Lines,
        List<(double WidthMm, double DepthMm)?> Hints,
        List<List<(double X, double Y)>> Slabs) BuildShaftScenario()
    {
        var slab = new List<(double X, double Y)>
        {
            (-15000, -15000), (15000, -15000), (15000, 15000), (-15000, 15000)
        };

        // South wall centerline: long-axis = X, midY = -2000.
        // North: long-axis = X, midY = +2000.
        // West:  long-axis = Y, midX = -2000.
        // East:  long-axis = Y, midX = +2000.
        // Endpoint mismatch at corners (~150 mm) is intentional — mirrors what
        // ReducePolygonToWallCenterline produces from 300 mm wall polygons.
        var south = new List<(double X, double Y)> { (-2150, -2000), (2150, -2000) };
        var north = new List<(double X, double Y)> { (-2150, 2000),  (2150, 2000)  };
        var west  = new List<(double X, double Y)> { (-2000, -2150), (-2000, 2150) };
        var east  = new List<(double X, double Y)> { (2000, -2150),  (2000, 2150)  };

        var lines = new List<List<(double X, double Y)>> { south, north, west, east };
        var hints = new List<(double WidthMm, double DepthMm)?>
        {
            (300, 1000), (300, 1000), (300, 1000), (300, 1000)
        };
        return (lines, hints, new List<List<(double X, double Y)>> { slab });
    }

    [Fact]
    public void DetectRectangularOpenings_FourWallsEnclosing_EmitsOneOpening()
    {
        var (lines, hints, slabs) = BuildShaftScenario();

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, slabs);

        var opening = Assert.Single(openings);
        Assert.Equal(0, opening.ParentSlabIndex);
        Assert.Equal(4, opening.Polygon.Count);

        // Opening should be the centerline rectangle (-2000..2000, -2000..2000).
        Assert.Contains(opening.Polygon, p => p.X == -2000 && p.Y == -2000);
        Assert.Contains(opening.Polygon, p => p.X ==  2000 && p.Y ==  2000);
    }

    [Fact]
    public void DetectRectangularOpenings_NoWallHints_ReturnsEmpty()
    {
        var (lines, _, slabs) = BuildShaftScenario();
        var noHints = new List<(double WidthMm, double DepthMm)?> { null, null, null, null };

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, noHints, slabs);

        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_ThreeWallsOnly_ReturnsEmpty()
    {
        var (lines, hints, slabs) = BuildShaftScenario();
        // Drop the east wall — three-sided U-shape, not a closed shaft.
        lines.RemoveAt(3);
        hints.RemoveAt(3);

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, slabs);

        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_ShaftOutsideSlab_ReturnsEmpty()
    {
        var (lines, hints, _) = BuildShaftScenario();
        // Slab that doesn't contain the shaft centroid (which is at origin).
        var farSlab = new List<List<(double X, double Y)>>
        {
            new() { (20000, 20000), (40000, 20000), (40000, 40000), (20000, 40000) }
        };

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, farSlab);

        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_OpeningLargerThanSlab_Rejected()
    {
        var (lines, hints, _) = BuildShaftScenario();
        // Tiny slab that's smaller than the opening.
        var tinySlab = new List<List<(double X, double Y)>>
        {
            new() { (-100, -100), (100, -100), (100, 100), (-100, 100) }
        };

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, tinySlab);

        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_PerimeterWallsBracketingPartitions_DoesNotEmit()
    {
        // 30 m × 30 m slab. Two LONG perimeter cross-walls (north + south of the
        // entire building) plus two SHORT interior partitions. Without the
        // overshoot guard, the perimeter walls' bboxes trivially span the
        // partitions' X-range and the detector would emit a phantom shaft.
        var slab = new List<(double X, double Y)>
        {
            (-15000, -15000), (15000, -15000), (15000, 15000), (-15000, 15000)
        };
        var lines = new List<List<(double X, double Y)>>
        {
            // Long perimeter cross-walls running the full slab width.
            new() { (-15000, -2000), (15000, -2000) },
            new() { (-15000,  2000), (15000,  2000) },
            // Short interior partitions at x = ±2000, between the cross-walls.
            new() { (-2000, -2150), (-2000, 2150) },
            new() {  (2000, -2150),  (2000, 2150) },
        };
        var hints = new List<(double WidthMm, double DepthMm)?>
        {
            (300, 1000), (300, 1000), (300, 1000), (300, 1000)
        };

        var openings = WallOpeningDetector.DetectRectangularOpenings(
            lines, hints, new List<List<(double X, double Y)>> { slab });

        // Perimeter walls overshoot the 4 m shaft x-range by 13 m on each side
        // — far beyond maxOvershootRatio × shaftW = 1 m. Detector must reject.
        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_CornerOvershootWithinTolerance_StillEmits()
    {
        // Sanity-check the overshoot guard does NOT reject a clean shaft whose
        // walls extend a small amount past the corner (the ±150 mm offset that
        // ReducePolygonToWallCenterline naturally produces from real wall
        // polygons).  4 m shaft, 150 mm overshoot per side — well within the
        // 1 m budget at maxOvershootRatio = 0.25.
        var (lines, hints, slabs) = BuildShaftScenario();

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, slabs);

        Assert.Single(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_ExcessiveOvershoot_Rejects()
    {
        // Same shaft geometry but extend every wall by 5 m past the corner.
        // Real shaft walls never do this; only a perimeter wall or a wall that
        // doubles as an adjacent corridor would. Detector must reject.
        var slab = new List<(double X, double Y)>
        {
            (-15000, -15000), (15000, -15000), (15000, 15000), (-15000, 15000)
        };
        var lines = new List<List<(double X, double Y)>>
        {
            new() { (-7000, -2000), (7000, -2000) }, // south overshoot ±5 m
            new() { (-7000,  2000), (7000,  2000) }, // north overshoot ±5 m
            new() { (-2000, -7000), (-2000, 7000) }, // west  overshoot ±5 m
            new() { ( 2000, -7000), ( 2000, 7000) }, // east  overshoot ±5 m
        };
        var hints = new List<(double WidthMm, double DepthMm)?>
        {
            (300, 1000), (300, 1000), (300, 1000), (300, 1000)
        };

        var openings = WallOpeningDetector.DetectRectangularOpenings(
            lines, hints, new List<List<(double X, double Y)>> { slab });

        // shaftW = shaftH = 4000; maxOvershoot = 1000. Walls overshoot by 5000.
        Assert.Empty(openings);
    }

    [Fact]
    public void DetectRectangularOpenings_TwoShafts_EmitsTwoOpenings()
    {
        var slab = new List<(double X, double Y)>
        {
            (-30000, -15000), (30000, -15000), (30000, 15000), (-30000, 15000)
        };
        // Left shaft at x∈[-12000,-8000], y∈[-2000,2000]
        // Right shaft at x∈[8000,12000], y∈[-2000,2000]
        var lines = new List<List<(double X, double Y)>>
        {
            new() { (-12150, -2000), (-7850, -2000) }, // L south
            new() { (-12150, 2000),  (-7850, 2000)  }, // L north
            new() { (-12000, -2150), (-12000, 2150) }, // L west
            new() { (-8000, -2150),  (-8000, 2150)  }, // L east
            new() { (7850, -2000),   (12150, -2000) }, // R south
            new() { (7850, 2000),    (12150, 2000)  }, // R north
            new() { (8000, -2150),   (8000, 2150)   }, // R west
            new() { (12000, -2150),  (12000, 2150)  }, // R east
        };
        var hints = new List<(double WidthMm, double DepthMm)?>
        {
            (300, 1000), (300, 1000), (300, 1000), (300, 1000),
            (300, 1000), (300, 1000), (300, 1000), (300, 1000),
        };
        var slabs = new List<List<(double X, double Y)>> { slab };

        var openings = WallOpeningDetector.DetectRectangularOpenings(lines, hints, slabs);

        Assert.Equal(2, openings.Count);
        Assert.All(openings, o => Assert.Equal(0, o.ParentSlabIndex));
    }
}
