#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A wall Revit's own PDF export split is still a wall (intake step 174, 2026-10-22). The export draws every filled
/// polygon as a line path that ends on its start with no close command, and breaks a face wherever something touches
/// it: 31170-01's drafted S2.02.1 carried SWA as six points, one collinear on the west face, and the four-point wall
/// test refused all 42 walls on the two sheets (the stick-set plot of the same sheets, printed through a driver, drew
/// each wall as one rectangle and read 42).
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the page walk dropping the point that only returns to the start when no close command was drawn;
/// the corner ring dropping repeats and collinear split points and keeping mitres and notches; the classifier reading
/// the split wall as one wall of the drawn thickness and length, with the drawn outline kept; a genuine L-shaped ribbon
/// still not a wall. WHAT IT DOES NOT: the walk's own reading of a real export (the stick-set run on
/// 31170-01's drafted sheets is that: etabs-diff/README.md); the slab edges the same export leaves unclosed, which is
/// a separate fault. A fault it would NOT catch: a wall the exporter splits into two separate filled polygons at a join.
/// </remarks>
public sealed class AWallSplitByRevitsExportIsStillAWallTests
{
    // SWA as the export draws it, in mm: 508 x 7772, a split point on the west face, the start point repeated
    private static List<(double X, double Y)> SplitWall() =>
        [(0, 0), (7772, 0), (7772, 508), (0, 508), (0, 100), (0, 0)];

    [Fact]
    public void TheRingKeepsCornersAndDropsRepeatsAndSplitPoints()
    {
        var ring = GeometryFilterService.WallRing(SplitWall());
        Assert.Equal(4, ring.Count);
        Assert.Equal([(0.0, 0.0), (7772.0, 0.0), (7772.0, 508.0), (0.0, 508.0)], ring);
        // a mitred end still turns (33 degrees here) and stays a corner; a notch turns 90 and stays
        Assert.Equal(4, GeometryFilterService.WallRing([(0, 0), (7000, 0), (7772, 508), (0, 508), (0, 0)]).Count);
        Assert.Equal(6, GeometryFilterService.WallRing(WallFixture.Ribbon().Points).Count);
    }

    [Fact]
    public void TheSplitWallReadsAsOneWallOfTheDrawnThicknessAndLengthWithItsOutlineKept()
    {
        var path = WallFixture.Rect(20, 306) with { Points = SplitWall() };
        var (geometry, fates) = WallFixture.Read([path]);
        var wall = Assert.Single(geometry.Walls);
        Assert.Equal(508, wall.ThicknessMm, 3);
        Assert.Equal(PathReason.BecameWall, Assert.Single(fates).Reason);
        Assert.Same(path.Points, wall.Outline);
        Assert.Empty(geometry.Slabs);
        Assert.Empty(geometry.Columns);
    }

    [Fact]
    public void ARibbonIsStillNotAWall()
    {
        var (geometry, _) = WallFixture.Read([WallFixture.Ribbon()]);
        Assert.Empty(geometry.Walls);
    }

    /// <summary>The page walk keeps the returning point by contract (APageIsWalkedOnceTests); the wall rule's ring is
    /// where it is dropped, so a cached walk replays unchanged and the wall still reads.</summary>
    [Fact]
    public void TheWalkKeepsTheReturningPointAndTheWallRuleStillReadsTheWall()
    {
        var pts = new List<(double X, double Y)> { (10, 10), (240, 10), (240, 25), (10, 25), (10, 10) };
        var sub = new VectorPageReader.RawSubpath(0, 0, pts, HasCloseCommand: false, IsFilled: true, IsStroked: false,
            LineWidth: 0, Color: (0xD7, 0xD7, 0xD7), IsClipping: false);
        var geom = Assert.Single(VectorPageReader.Derive(new VectorPageReader.RawPage(1, 3024, 2160, [], [sub], []), includeAnnotations: false).Paths);
        Assert.True(geom.IsClosed);
        Assert.Equal(5, geom.Points.Count);
        Assert.Equal(4, GeometryFilterService.WallRing(geom.Points).Count);
    }
}
