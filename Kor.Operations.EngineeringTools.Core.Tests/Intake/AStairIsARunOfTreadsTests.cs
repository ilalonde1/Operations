#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A STAIR IS A RUN OF TREADS (intake step 105, 2026-09-16). The drafter draws a stair as its treads - on 31202 a
/// paper-filled rectangle 1,187 x 280 mm, eight to a flight - between the stair's walls, with UP and DN and no X; her
/// model cuts the well, two flights and the landing, 2 x 5.5 m (44 of them on four sets that the X rule could not
/// see). A flight is five or more paper fills of one tread size (the building code's: 900-1,700 mm wide, 220-340 mm
/// deep) stacked along the flight at their own depth; the well is the arrangement's cell holding the flights, built
/// with the walls' outlines and the closed-again doorways in it and the stair's own symbol (the break line, the arrow,
/// anything strictly inside the flights' box) out of it. Measured on 31202 against her export: her stair wells found
/// 16 of 16 on the storeys she covers (ours she has 25 of 25; hers we have 13% -> 37%); 31168, 31138 unchanged.
/// WHAT THIS COVERS: the finder (a run of eight is a flight, four is not, a run of the wrong depth is not); a floor with a
/// walled stair well holding a flight coming out as the plate plus the well's loop of the walls' inside extent.
/// WHAT IT DOES NOT: treads drawn as lines (31138, 31130, 31065 draw them another way - the openings figure in
/// every yardstick.txt counts what they still lack); the landing beyond a wall the well shares with a corridor; the cut
/// itself (the DXF route's classifier and the composer); the real sheets (the six-set gate).
/// </summary>
public sealed class AStairIsARunOfTreadsTests
{
    private const double X0 = 40000, Y0 = 20000, X1 = 48000, Y1 = 26000;

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static RawSubpath Wall(double w, double h, double x, double y) => FateFixture.Rect(w, h, x, y);
    private static RawSubpath Tread(double w, double h, double x, double y) => FateFixture.Rect(w, h, x, y) with { Color = (0xF0, 0xF0, 0xF0) };
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double Area(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring));

    private static IEnumerable<RawSubpath> Flight(double x, double y, int treads, double w = 1187, double d = 280)
        => Enumerable.Range(0, treads).Select(i => Tread(w, d, x, y + i * d));

    [Fact]
    public void FiveOrMorePaperTreadsOfOneSizeStackedAtTheirDepthAreAFlight()
    {
        var eight = GeometryFilterService.StairFlights(Flight(0, 0, 8).Select(p => p.Points));
        var f = Assert.Single(eight);
        Assert.Equal(8, f.Treads);
        Assert.Equal(1187, f.X1 - f.X0, 1);
        Assert.Equal(8 * 280, f.Y1 - f.Y0, 1);

        Assert.Empty(GeometryFilterService.StairFlights(Flight(0, 0, 4).Select(p => p.Points)));                 // four is a step
        Assert.Empty(GeometryFilterService.StairFlights(Flight(0, 0, 8, d: 600).Select(p => p.Points)));         // 600 deep is a landing, not a going
        Assert.Empty(GeometryFilterService.StairFlights(Flight(0, 0, 8, w: 2500).Select(p => p.Points)));        // 2.5 m wide is a corridor
        // a gap of a tread's depth between the fifth and the sixth breaks the run: five and three, one flight
        var broken = Flight(0, 0, 5).Concat(Flight(0, 5 * 280 + 400, 3)).Select(p => p.Points);
        Assert.Single(GeometryFilterService.StairFlights(broken));
    }

    [Fact]
    public void TwelveStrokesAcrossTheFlightAtAGoingsPitchAreAFlightToo_UnlessTheSheetDrawsPaperTreads()
    {
        // 31138 draws a stroke per tread, twelve to a flight, no fill: a line tread has no depth and its pitch is the going
        static List<(double X, double Y)> Stroke(double x, double y, double w) => [(x, y), (x + w, y)];
        var strokes = Enumerable.Range(0, 12).Select(i => Stroke(0, i * 280, 1200)).ToList();
        var f = Assert.Single(GeometryFilterService.StairFlights([], strokes));
        Assert.Equal(12, f.Treads);
        Assert.Equal(11 * 280, f.Y1 - f.Y0, 1);
        // at a pitch no going has (600 mm) they are a hatch, not a flight; four at the right pitch are a step
        Assert.Empty(GeometryFilterService.StairFlights([], Enumerable.Range(0, 12).Select(i => Stroke(0, i * 600, 1200)).ToList()));
        Assert.Empty(GeometryFilterService.StairFlights([], Enumerable.Range(0, 4).Select(i => Stroke(0, i * 280, 1200)).ToList()));
        // a sheet draws its treads one way: beside paper treads, strokes are the fills' edges and the outline, not treads
        // (31202 draws both; read as treads its strokes broke every paper run - eight of sixteen wells lost)
        var paper = Flight(5000, 0, 8).Select(p => p.Points).ToList();
        var both = GeometryFilterService.StairFlights(paper, strokes);
        Assert.Single(both);
        Assert.Equal(8, both[0].Treads);
    }

    [Fact]
    public void AFloorWithAWalledStairWellIsThePlateAndTheWellsLoop()
    {
        // the floor (the walk finds its ring) with a stair well in its middle: four 200 mm walls round a 2.6 x 4 m well,
        // the near wall with a 1,040 mm doorway knocked out of it (a paper fill over the wall, step 14); two flights of
        // eight treads side by side inside. The well's loop is the walls' inside extent.
        double wx0 = 43000, wy0 = 21500, wx1 = 45600, wy1 = 25500;   // the well's inside
        RawSubpath[] Room() =>
        [
            Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
            Wall(200, wy1 - wy0, wx0 - 200, wy0), Wall(200, wy1 - wy0, wx1, wy0),                          // the sides
            Wall(wx1 - wx0 + 400, 200, wx0 - 200, wy1), Wall(wx1 - wx0 + 400, 200, wx0 - 200, wy0 - 200),   // the ends, corner to corner
            Tread(1040, 240, wx0 + 300, wy0 - 220),   // the doorway: paper over the near wall, 20 mm proud each side
            Column(41000, 21000), Column(46500, 21000), Column(41000, 25000), Column(46500, 25000),
        ];
        var g = Read(Room());
        var flights = Flight(wx0 + 100, wy0 + 300, 8).Concat(Flight(wx1 - 100 - 1187, wy0 + 300, 8)).ToArray();
        var withStair = Read(Room().Concat(flights).ToArray());

        Assert.Single(g.Slabs);                       // no treads: the plate alone
        Assert.Equal(2, withStair.StairFlights.Count);
        Assert.Equal(2, withStair.Slabs.Count);       // the plate and the well
        Assert.Equal((X1 - X0) * (Y1 - Y0), Area(withStair.Slabs[0]), 1);
        double well = Area(withStair.Slabs[1]);
        Assert.InRange(well, 0.8 * (wx1 - wx0) * (wy1 - wy0), 1.3 * (wx1 - wx0) * (wy1 - wy0));
    }

    [Fact]
    public void ADoorDrawnAsAGapBetweenTwoWallPiecesStillClosesTheWell()
    {
        // 31065: the stair's near wall stops either side of the door - two pieces, a 900 mm gap, no paper fill over a
        // wall for the doorway reader (step 14) to see; the well leaked into the plate (6,700 sq ft) until the gap was
        // closed for the well's arrangement. The same well as above with the near wall in two pieces.
        // (a wall is 1,219 mm and longer - dxf.min-wall-length - so the well is 4 m wide here: two 1.5 m pieces and a 1 m door)
        double wx0 = 42000, wy0 = 21500, wx1 = 46000, wy1 = 25500;
        RawSubpath[] Room() =>
        [
            Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
            Wall(200, wy1 - wy0, wx0 - 200, wy0), Wall(200, wy1 - wy0, wx1, wy0),
            Wall(wx1 - wx0 + 400, 200, wx0 - 200, wy1),
            Wall(1700, 200, wx0 - 200, wy0 - 200), Wall(1700, 200, wx0 - 200 + 1700 + 1000, wy0 - 200),   // the near wall in two pieces, a 1,000 mm door between them
            Column(41000, 21000), Column(46500, 21000), Column(41000, 25000), Column(46500, 25000),
        ];
        var flights = Flight(wx0 + 100, wy0 + 300, 8).Concat(Flight(wx1 - 100 - 1187, wy0 + 300, 8)).ToArray();
        var withStair = Read(Room().Concat(flights).ToArray());
        Assert.Equal(2, withStair.Slabs.Count);
        double well = Area(withStair.Slabs[1]);
        Assert.InRange(well, 0.8 * (wx1 - wx0) * (wy1 - wy0), 1.3 * (wx1 - wx0) * (wy1 - wy0));
    }

    /// <summary>
    /// THE LANDING BETWEEN THE FLIGHTS IS THE WELL'S (step 105d, 2026-09-16). 31065's L7-L17 draw a break line across the
    /// stair as a zig-zag of short strokes; once step 110 keeps its pieces the line closes, and the cell between the two
    /// pairs of flights - the landing, no flight's centre in it - fell out of the well (2.4 x 7.1 m -> 2.4 x 3.3). A cell no
    /// bigger than the stair's box whose centroid lies inside that box is the well's. WHAT THIS COVERS: a walled well with
    /// flights at both ends and two lines across it wall to wall between them (a break line drawn twice), the landing cell
    /// between joining the well into one loop of the well's area. WHAT IT DOES NOT: a line across the flights themselves
    /// (a tread, excluded before this); a landing outside the flights' box (an L-shaped well's turn); the real sheets.
    /// </summary>
    [Fact]
    public void TheLandingBetweenTheFlightsIsTheWells()
    {
        double wx0 = 43000, wy0 = 21500, wx1 = 45600, wy1 = 25500;
        RawSubpath[] Room() =>
        [
            Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
            Wall(200, wy1 - wy0, wx0 - 200, wy0), Wall(200, wy1 - wy0, wx1, wy0),
            Wall(wx1 - wx0 + 400, 200, wx0 - 200, wy1), Wall(wx1 - wx0 + 400, 200, wx0 - 200, wy0 - 200),
            Tread(1040, 240, wx0 + 300, wy0 - 220),
            Column(41000, 21000), Column(46500, 21000), Column(41000, 25000), Column(46500, 25000),
            Line(wx0, 23400, wx1, 23400), Line(wx0, 23900, wx1, 23900),   // the break line, drawn twice, wall to wall
        ];
        // two flights of five at the bottom of the well, two at the top: the landing between holds no flight's centre
        var flights = Flight(wx0 + 100, wy0 + 300, 5).Concat(Flight(wx1 - 100 - 1187, wy0 + 300, 5))
            .Concat(Flight(wx0 + 100, 24100, 5)).Concat(Flight(wx1 - 100 - 1187, 24100, 5)).ToArray();
        var g = Read(Room().Concat(flights).ToArray());
        Assert.Equal(4, g.StairFlights.Count);
        Assert.Equal(2, g.Slabs.Count);                                  // the plate and ONE well, the landing in it
        double well = Area(g.Slabs[1]);
        Assert.InRange(well, 0.8 * (wx1 - wx0) * (wy1 - wy0), 1.3 * (wx1 - wx0) * (wy1 - wy0));
    }
}
