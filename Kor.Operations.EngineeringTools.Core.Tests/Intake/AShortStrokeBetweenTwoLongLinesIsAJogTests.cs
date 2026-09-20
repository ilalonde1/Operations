#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A SHORT STROKE BETWEEN TWO LONG LINES OF ONE PEN IS A JOG OF THE EDGE (intake step 110, 2026-09-16). 30838's
/// concrete-outline plans step their slab edge sideways by 150 mm at grid 2 - an 8 pt stroke 150 mm long between two
/// 8 pt verticals of 3.9 m and 2.1 m, end to end. Step 98 chains short strokes with each other and leaves a long line
/// alone, so the jog alone was "too short" and the ring stood open on every tower storey. A stroke under the length
/// gate whose both ends meet an end of a long line of the same pen and colour is that line's jog, kept as a line.
/// WHAT THIS COVERS: the jog found by its two ends, a step and a U alike (31130 L17's outline notches round a column as
/// a U, and the form that refused U's lost that plate); a short stroke with one free end not one; a stroke of another
/// pen not one; a floor whose west edge steps by a jog wider than the chain bridge closing to its area, and the same floor
/// without the rule standing open (proved by breaking). WHAT IT DOES NOT: a jog drawn as two strokes (step 98 chains
/// them, then neither end is a long line's); a jog whose ends miss the long lines by a hair (exact ends, as step 98);
/// the real sheets (30838 in the corpus, where the edge also stops beside its corner columns - the 31138 class).
/// </summary>
public sealed class AShortStrokeBetweenTwoLongLinesIsAJogTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 48000, Y1 = 26000;
    private const double Gate = 300;
    private const double Jog = 190;   // over the chain bridge (6 in), under the fixture's length gate

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    [Fact]
    public void TheJogIsFoundByItsTwoEndsAndAFreeEndOrAnotherPenIsNot()
    {
        double ym = (Y0 + Y1) / 2;
        var paths = new List<RawSubpath>
        {
            Line(X0, Y0, X0, ym),                 // 0: the lower west edge
            Line(X0, ym, X0 + Jog, ym),           // 1: the jog
            Line(X0 + Jog, ym, X0 + Jog, Y1),     // 2: the upper west edge
            Line(X0 + 3000, ym, X0 + 3000 + Jog, ym),   // 3: a short stroke with both ends free
            Line(X0 + 5000, Y0, X0 + 5000 + Jog, Y0),   // 4: one end on the long south edge below, the other free
            Line(X0, Y0, X1, Y0),                 // 5: the south edge
            Line(X0 + 6000, Y0 + 1000, X0 + 6000, Y0 + 4000),         // 6: an outline's notch round a column: down ...
            Line(X0 + 6000 + Jog, Y0 + 1000, X0 + 6000 + Jog, Y0 + 4000),   // 7: ... and up again (31130 L17's, 126 mm)
            Line(X0 + 6000, Y0 + 1000, X0 + 6000 + Jog, Y0 + 1000),   // 8: the short stroke between them - a U, kept
        };
        var jogs = GeometryFilterService.JogsBetweenLongLines(paths, Gate, new HashSet<int>());
        Assert.Equal([1, 8], jogs.Order());

        var other = FateFixture.Line(X0, ym, X0 + Jog, ym) with { LineWidth = paths[0].LineWidth + 3 };   // another pen
        paths[1] = other;
        Assert.Equal([8], GeometryFilterService.JogsBetweenLongLines(paths, Gate, new HashSet<int>()).Order());   // the jog of another pen is gone, the notch stays
    }

    /// <summary>
    /// A JOG LANDS ON WHATEVER LINE IT REACHES (intake step 133, 2026-09-19): a short stroke of a long line's pen, end
    /// to end with that line, whose other end meets an end of a long line of ANOTHER pen is that line's jog. 30990's
    /// LEVEL 5 turns its east edge (0.96 pt) onto the stair's band (0.60 pt) by a 191 mm stroke at the edge's pen: on
    /// LEVEL 3/4 the same stroke is 250 mm and passes the length gate on its own; on LEVEL 5 it was TooShort, the
    /// jog rule asked both ends for the jog's pen, and the floor leaked through the 191 mm. WHAT THIS COVERS: the jog
    /// found with one side of another pen; a short stroke of a pen NEITHER line has, still not one. WHAT IT DOES NOT:
    /// another colour (a mark-up's stroke landing on the outline stays out).
    /// </summary>
    [Fact]
    public void AJogOfOneLinesPenLandingOnALineOfAnotherPenIsAJog()
    {
        double ym = (Y0 + Y1) / 2;
        var band = FateFixture.Line(X0 + Jog, ym, X0 + Jog, Y1) with { LineWidth = FateFixture.Line(0, 0, 1, 0).LineWidth / 2 };   // the upper run, a lighter pen
        var paths = new List<RawSubpath>
        {
            Line(X0, Y0, X0, ym),                 // 0: the lower edge, the outline's pen
            Line(X0, ym, X0 + Jog, ym),           // 1: the jog, the outline's pen
            band,                                 // 2: the band it lands on, another pen
        };
        Assert.Equal([1], GeometryFilterService.JogsBetweenLongLines(paths, Gate, new HashSet<int>()).Order());

        paths[1] = paths[1] with { LineWidth = paths[0].LineWidth + 3 };   // a pen neither line has
        Assert.Empty(GeometryFilterService.JogsBetweenLongLines(paths, Gate, new HashSet<int>()));
    }

    [Fact]
    public void AFloorWhoseEdgeStepsByAJogCloses()
    {
        double ym = (Y0 + Y1) / 2;
        var g = Read(
            Line(X0, Y0, X0, ym), Line(X0, ym, X0 + Jog, ym), Line(X0 + Jog, ym, X0 + Jog, Y1),
            Line(X0 + Jog, Y1, X1, Y1), Line(X1, Y1, X1, Y0), Line(X1, Y0, X0, Y0),
            Column(43000, 22500), Column(45000, 23500));
        var plate = Assert.Single(g.Slabs);
        double expected = (X1 - X0) * (Y1 - Y0) - Jog * (Y1 - ym);
        Assert.InRange(Area(plate), expected * 0.99, expected * 1.01);
    }
}
