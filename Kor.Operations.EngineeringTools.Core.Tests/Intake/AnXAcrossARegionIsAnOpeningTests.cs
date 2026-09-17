#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// AN X ACROSS A SHAFT IS AN OPENING (intake step 104, 2026-09-16). The drafter's mark for a shaft or a stair is two
/// oblique lines of one length crossing at their midpoints. Measured on the six sets: 31202's
/// elevator shafts carry 12 ft X's on every plan, 31130's and 31138's 11 ft ones. An X inside a plate is written as a loop of its own, which the DXF side reads
/// as an opening and the composer cuts; the plate itself is the union's (keeping the X's cells out of it took 31202's
/// lower roof, whose 109 ft X marks a region labelled 9 in SLAB - so an X longer than a stair is no opening's mark).
/// WHAT THIS COVERS: the finder - two oblique arms of one length crossing at their midpoints are an X, a grid-like
/// crossing (an arm along an axis) is not, arms of unequal length are not, a crossing off the midpoints is not; a
/// floor with an X-marked shaft coming out as the whole plate plus the shaft's loop; an X over 30 ft marking nothing. WHAT IT DOES NOT: the cut itself (the DXF route's classifier and the composer); an X drawn as four
/// half-arms meeting at the centre (four lines, not two); the real sheets (the six-set gate).
/// </summary>
public sealed class AnXAcrossARegionIsAnOpeningTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 48000, Y1 = 26000;

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    private static ExtractedGeometry Lines(params (double x0, double y0, double x1, double y1)[] lines)
    {
        var g = new ExtractedGeometry();
        foreach (var (x0, y0, x1, y1) in lines) { g.Lines.Add([(x0, y0), (x1, y1)]); g.LineColors.Add((0, 0, 0)); g.LineWidths.Add(1); g.LineIsAnnotation.Add(false); }
        return g;
    }

    [Fact]
    public void TwoObliqueArmsOfOneLengthCrossingAtTheirMidpointsAreAnXAndNothingElseIs()
    {
        var x = GeometryFilterService.XMarks(Lines((0, 0, 3000, 3000), (0, 3000, 3000, 0)));
        var mark = Assert.Single(x);
        Assert.Equal(1500, mark.Centre.X, 1); Assert.Equal(1500, mark.Centre.Y, 1);
        Assert.Equal(4, mark.Region.Count);

        Assert.Empty(GeometryFilterService.XMarks(Lines((0, 1500, 3000, 1500), (1500, 0, 1500, 3000))));       // a grid crossing: arms along the axes
        Assert.Empty(GeometryFilterService.XMarks(Lines((0, 0, 3000, 3000), (0, 3000, 9000, -6000))));      // arms of unequal length
        Assert.Empty(GeometryFilterService.XMarks(Lines((0, 0, 3000, 3000), (2000, 3000, 5000, 0))));       // crossing off the midpoints
        Assert.Empty(GeometryFilterService.XMarks(Lines((0, 0, 1200, 1200), (0, 1200, 1200, 0))));          // under the arm minimum: a symbol
        Assert.True(GeometryFilterService.XMarks(Lines((0, 0, 30000, 30000), (0, 30000, 30000, 0))).Single().Reach > GeometryFilterService.XMarkMaxArmMm);   // found, but over the arm maximum: a region mark the slab pass ignores
    }

    [Fact]
    public void AFloorWithAnXMarkedShaftIsTheWholePlateAndTheShaftsLoop()
    {
        // step 78's floor (a balcony box against the south edge so the arrangement decides) with a 3 x 3 m shaft box
        // inside it crossed by its X: the plate is whole, and the shaft comes out as a second loop of its own extent
        double bx0 = 43000, bx1 = 45000, by = 18500;
        double sx0 = 42000, sy0 = 22000, sx1 = 45000, sy1 = 25000;
        var g = Read(Line(bx0, Y0, bx0, by), Line(bx0, by, bx1, by), Line(bx1, by, bx1, Y0), Line(bx1, Y0, bx0, Y0),
                     Line(X0, Y0, bx0, Y0), Line(bx1, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
                     Line(sx0, sy0, sx1, sy0), Line(sx1, sy0, sx1, sy1), Line(sx1, sy1, sx0, sy1), Line(sx0, sy1, sx0, sy0),
                     Line(sx0, sy0, sx1, sy1), Line(sx0, sy1, sx1, sy0),
                     Column(41000, 21000), Column(46500, 21000), Column(41000, 25500), Column(46500, 25500));
        Assert.Equal(2, g.Slabs.Count);
        Assert.Equal((X1 - X0) * (Y1 - Y0), Area(g.Slabs[0]), 1);
        Assert.Equal((sx1 - sx0) * (sy1 - sy0), Area(g.Slabs[1]), 1);
    }

    // ⛔ MEASURED AND REJECTED BY HER OWN MODELS (step 104, 16:00-16:55): "an X-box on the plate's edge is no shaft" (a corner
    // within 300 mm of the boundary) and "an X-box abutting a column is no shaft" - both written for 31168's L15-26, where a
    // 3.6 x 1.5 m X-box flanks every perimeter column on storeys no model of hers covers. On the storeys she does cover
    // (ModelYardstick.Openings, five sets), each rule took fifty of the seventy openings of ours she has: her shafts on
    // 31168's podium and building C stand within 300 mm of our plate's boundary and have columns in their corner walls.
    // The tests that held them are gone with them; the numbers are in §114 and beside the slab pass.
}
