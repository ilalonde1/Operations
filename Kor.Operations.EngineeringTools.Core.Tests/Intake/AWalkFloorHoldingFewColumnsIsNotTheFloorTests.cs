#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A FLOOR THE WALK FOUND THAT HOLDS UNDER HALF THE PAGE'S COLUMNS IS NOT THE PAGE'S FLOOR (intake step 113, 2026-09-17).
/// 60061-03's typical plan walked the stud-rail schedule's border into a 1,953 sq ft floor on every storey of a hotel
/// and never built the arrangement, which reads the real outline (4,484 sq ft, 26 of 28 columns). A walk floor holding
/// fewer than half the page's columns has the arrangement built as well, and where the arrangement's floor holds more
/// columns the walk's stands down. WHAT THIS COVERS: a closed box with one column beside a floor whose outline the
/// walk cannot close (a 1 ft gap in the middle of an edge) and the arrangement's union of enclosed cells reads most of, with six columns: the floor is read (the box, holding a
/// column, stays a plate of its own - the arrangement reads it too);
/// the same page without the gap (the walk closes the floor itself) unchanged; a box holding most of the columns kept
/// as the floor. WHAT IT DOES NOT: the composer's fold of two readings of one floor (AFloorReadTwiceIsOneFloorTests);
/// the real sheets (60061-03 in the next corpus run; 31138's L17/L18/L20 and 31130's P3 on the six).
/// </summary>
public sealed class AWalkFloorHoldingFewColumnsIsNotTheFloorTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 52000, Y1 = 28000;   // the floor, 12 x 8 m

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    private static IEnumerable<RawSubpath> Floor(double gapMm)
    {
        // the outline, its north edge broken in its middle by the gap - wider than the bridge (6 in) and with no corner
        // for the carry to make, so neither the walk nor a bridge closes it - and two lines across the floor (a step, a
        // band) cutting it into four cells: the three away from the gap are enclosed, and their union is most of the
        // floor. Only the arrangement reads that (step 98's cells); the walk reads nothing here
        yield return Line(X0, Y0, X1, Y0);
        yield return Line(X1, Y0, X1, Y1);
        if (gapMm > 0) { yield return Line(X1, Y1, 49000 + gapMm / 2, Y1); yield return Line(49000 - gapMm / 2, Y1, X0, Y1); }
        else yield return Line(X1, Y1, X0, Y1);
        yield return Line(X0, Y1, X0, Y0);
        yield return Line(46000, Y0, 46000, Y1);
        yield return Line(X0, 24000, X1, 24000);
        foreach (var (x, y) in new[] { (42000.0, 22000.0), (44000.0, 26000.0), (48000.0, 22000.0), (50000.0, 22000.0), (42000.0, 26500.0), (50000.0, 26500.0) })
            yield return Column(x, y);
    }

    private static IEnumerable<RawSubpath> ScheduleBox()
    {
        // a closed box the walk closes at once, south of the floor, with one column in it
        double bx0 = 40000, by0 = 10000, bx1 = 50000, by1 = 16000;   // 10 x 6 m: a floor's size on its own
        yield return Line(bx0, by0, bx1, by0); yield return Line(bx1, by0, bx1, by1); yield return Line(bx1, by1, bx0, by1); yield return Line(bx0, by1, bx0, by0);
        yield return Column(45000, 13000);
    }

    [Fact]
    public void TheArrangementIsBuiltWhenTheWalksFloorHoldsFewColumns()
    {
        // (no FaceTrace here: it is a static the suite's classes share, and a differential test running beside this one
        // read its own two runs differently while it was set - 02:44)
        var g = Read(ScheduleBox().Concat(Floor(300)).ToArray());
        Assert.Contains(g.Slabs, s => Area(s) >= (X1 - X0) * (Y1 - Y0) * 0.7);   // the floor's enclosed cells, united: most of it - the arrangement's, which the walk's small floor no longer forestalls
        Assert.Equal(2, g.Slabs.Count);   // the box, holding a column, stays a plate of its own - the arrangement reads it too
    }

    [Fact]
    public void AWalkFloorHoldingMostColumnsIsLeftAlone()
    {
        var whole = Read(ScheduleBox().Concat(Floor(0)).ToArray());                // the walk closes the floor itself
        Assert.Contains(whole.Slabs, s => Area(s) >= (X1 - X0) * (Y1 - Y0) * 0.98);   // the whole floor, and the box beside it
        var boxOnly = Read(ScheduleBox().ToArray());                              // one box, one column: the walk's floor holds them all
        var only = Assert.Single(boxOnly.Slabs);
        Assert.InRange(Area(only), 10000 * 6000 * 0.98, 10000 * 6000 * 1.02);
    }
}
