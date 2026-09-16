#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A CURVE DRAWN AS SHORT STROKES IS ONE LINE, AND A CELL ENCLOSED BY THE FLOOR IS THE FLOOR (intake step 98,
/// 2026-09-16). 31138's tower outline turns its corners as arcs the PDF holds as runs of 9 pt strokes under
/// 200 mm long; each was "too short", the ring was open at every rounded corner, and fourteen storeys carried
/// no plate. Chained end to end they are the path the drafter drew. And once curves are lines, a curved wall's
/// two faces close a band across the floor that holds nothing, and the union of holding cells broke at it
/// (31202 L6, 19,533 → 13,868 + 4,721); the cells a floor encloses are the floor whether or not a column stands
/// in them - only a cell open to the page (a balcony box, a dimension strip) stays out.
/// WHAT THIS COVERS: eight short strokes end to end becoming one nine-point path, and only strokes under the
/// length gate (a long line stays the two-point line the wall reader pairs); a closed ring of short strokes (a
/// grid bubble) and a node where three strokes meet (a hatch) chaining nothing; a floor whose corners are such
/// curves closing to its area; a strip across a floor whose outline is doubled, holding nothing, joining the floor.
/// WHAT IT DOES NOT: a stroke claimed by a footing (its own tests); an opening drawn as a closed loop inside the
/// floor, which this rule fills; a strip that reaches the outline (between two tendons), which touches the
/// outside and stays out unless the outline is doubled there; the real sheets (the six-set gate). A same-class fault it would not catch: two
/// curves of different pens meeting end to end, which stay two.
/// </summary>
public sealed class ACurveDrawnAsShortStrokesIsOneLineTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 48000, Y1 = 26000;
    private const double Gate = 300;   // the length gate the fixture classifies with, in mm

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    // a quarter circle from the start angle (degrees) as n short strokes
    private static IEnumerable<RawSubpath> Arc(double cx, double cy, double r, int n, double startDegrees = 0)
    {
        double start = Math.PI * startDegrees / 180;
        for (int i = 0; i < n; i++)
        {
            double a0 = start + Math.PI / 2 * i / n, a1 = start + Math.PI / 2 * (i + 1) / n;
            yield return Line(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0), cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));
        }
    }

    [Fact]
    public void ShortStrokesEndToEndAreOnePathAndLongOnesStayLines()
    {
        var strokes = Arc(0, 0, 1000, 8).ToList();                       // 8 strokes of ~196 mm: under the gate
        var curves = GeometryFilterService.CurvesOfShortStrokes(strokes, Gate);
        var curve = Assert.Single(curves);
        Assert.Equal(9, curve.Path.Points.Count);
        Assert.Equal(Enumerable.Range(0, 8), curve.Members);

        var longOnes = Arc(0, 0, 4000, 8).ToList();                      // ~785 mm each: over the gate, left alone
        Assert.Empty(GeometryFilterService.CurvesOfShortStrokes(longOnes, Gate));
    }

    [Fact]
    public void ABubbleAndAHatchJunctionChainNothing()
    {
        // a closed ring of short strokes has no free end: a grid bubble, not a curve
        var ring = Enumerable.Range(0, 12).Select(i => Line(Math.Cos(Math.PI / 6 * i) * 400, Math.Sin(Math.PI / 6 * i) * 400,
                                                             Math.Cos(Math.PI / 6 * (i + 1)) * 400, Math.Sin(Math.PI / 6 * (i + 1)) * 400)).ToList();
        Assert.Empty(GeometryFilterService.CurvesOfShortStrokes(ring, Gate));

        // three strokes meeting at one point are a hatch, not a curve
        RawSubpath[] star = [Line(0, 0, 200, 0), Line(0, 0, 0, 200), Line(0, 0, -150, -150)];
        Assert.Empty(GeometryFilterService.CurvesOfShortStrokes(star, Gate));
    }

    [Fact]
    public void AFloorWithRoundedCornersDrawnAsStrokesCloses()
    {
        // a rectangle whose four corners are quarter circles of 2 m radius, each drawn as 16 strokes of ~196 mm -
        // under the fixture's 200 mm gate, and a corner the carry (4 ft) cannot reach across without them
        double r = 2000;
        var paths = new List<RawSubpath>
        {
            Line(X0 + r, Y0, X1 - r, Y0), Line(X1, Y0 + r, X1, Y1 - r), Line(X1 - r, Y1, X0 + r, Y1), Line(X0, Y1 - r, X0, Y0 + r),
            Column(43000, 22500), Column(45000, 23500),
        };
        paths.AddRange(Arc(X1 - r, Y1 - r, r, 16, 0));     // north-east
        paths.AddRange(Arc(X0 + r, Y1 - r, r, 16, 90));    // north-west
        paths.AddRange(Arc(X0 + r, Y0 + r, r, 16, 180));   // south-west
        paths.AddRange(Arc(X1 - r, Y0 + r, r, 16, 270));   // south-east
        var g = Read(paths.ToArray());
        var plate = Assert.Single(g.Slabs);
        double expected = (X1 - X0) * (Y1 - Y0) - 4 * (r * r - Math.PI * r * r / 4);
        Assert.InRange(Area(plate), expected * 0.98, expected * 1.02);   // the arcs are chords: within 2%
    }

    [Fact]
    public void ACellTheFloorEnclosesIsTheFloorWithoutAColumnInIt()
    {
        // the outline drawn twice, 100 mm apart (a curb line beside the edge - too thin to be a wall, so both lines
        // stay), and a strip across the floor between two lines that run inner edge to inner edge - a band no wall
        // reader took. The strip holds nothing and touches only the band and the floor, never the page; without
        // this rule it splits the floor into two unions of two columns each and the neighbourhood gate refuses
        // both. A closet box against the inner south edge and a balcony box against the outer one, each drawn
        // first and sharing that edge's piece, keep the walk from closing either ring (step 78's case) so the
        // arrangement is what decides.
        double ix0 = X0 + 100, iy0 = Y0 + 100, ix1 = X1 - 100, iy1 = Y1 - 100;
        double cx0 = 43000, cx1 = 45000, cy = 21000, by = 18500;
        var g = Read(Line(cx0, iy0, cx0, cy), Line(cx0, cy, cx1, cy), Line(cx1, cy, cx1, iy0), Line(cx1, iy0, cx0, iy0),   // the closet, first
                     Line(cx0, Y0, cx0, by), Line(cx0, by, cx1, by), Line(cx1, by, cx1, Y0), Line(cx1, Y0, cx0, Y0),       // the balcony, first
                     Line(ix0, iy0, cx0, iy0), Line(cx1, iy0, ix1, iy0), Line(ix1, iy0, ix1, iy1), Line(ix1, iy1, ix0, iy1), Line(ix0, iy1, ix0, iy0),
                     Line(X0, Y0, cx0, Y0), Line(cx1, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
                     Line(44000, iy0, 44000, iy1), Line(44400, iy0, 44400, iy1),
                     Column(41500, 22500), Column(42500, 24500), Column(46500, 22500), Column(47000, 24500));
        var plate = Assert.Single(g.Slabs);
        Assert.Equal((ix1 - ix0) * (iy1 - iy0), Area(plate), 1);
    }

}
