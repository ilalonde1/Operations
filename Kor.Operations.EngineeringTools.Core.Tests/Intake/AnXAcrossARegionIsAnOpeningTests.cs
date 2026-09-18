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

    [Fact]
    public void ABigXWithTheWordsOfAVoidInsideItIsAVoid_WithASlabWordASlab_WithNeitherNothing()
    {
        // step 106: 31202's L6-L13 draw a 31 x 8.7 m region open to the deck below as an X of 105 ft arms with OPEN TO
        // BELOW inside it; its ROOF draws a 109 ft X over a region labelled 9" SLAB. A 40 m X over a 45 x 45 m floor here.
        const double fx0 = 0, fy0 = 0, fx1 = 45000, fy1 = 35000;   // 35 m tall: a line 60% of the fixture page's 70 m is read as the sheet's frame
        ExtractedGeometry Floor(params (string Text, double X, double Y)[] words)
        {
            var g = new ExtractedGeometry();
            foreach (var w in words) g.PageWords.Add(w);
            return FateFixture.Classify(
            [
                Line(fx0, fy0, fx1, fy0), Line(fx1, fy0, fx1, fy1), Line(fx1, fy1, fx0, fy1), Line(fx0, fy1, fx0, fy0),
                Line(8000, 8000, 37000, 27000), Line(8000, 27000, 37000, 8000),   // the big X, arms of 35 m over a third of the floor (over half is no hole)
                Column(2000, 2000), Column(43000, 2000), Column(2000, 33000), Column(43000, 33000), Column(22000, 33000),
            ], new List<PathFate>(), result: g);
        }
        Assert.Equal(2, Floor(("OPEN", 22000, 17500), ("TO", 23500, 17500), ("BELOW", 25500, 17500)).Slabs.Count);   // the plate and the void
        Assert.Single(Floor(("9\"", 22000, 17500), ("SLAB", 23500, 17500)).Slabs);                                     // a region mark: the plate alone
        Assert.Single(Floor(("OPEN", 22000, 17500), ("BELOW", 23500, 17500), ("SLAB", 25500, 17500)).Slabs);          // a slab word wins
        Assert.Single(Floor().Slabs);                                                                                  // no words: nothing
        Assert.Single(Floor(("OPEN", 44000, 34000)).Slabs);                                                            // the word outside the X: nothing
    }

    // ⛔ MEASURED AND REJECTED BY HER OWN MODELS (step 104, 16:00-16:55): "an X-box on the plate's edge is no shaft" (a corner
    // within 300 mm of the boundary) and "an X-box abutting a column is no shaft" - both written for 31168's L15-26, where a
    // 3.6 x 1.5 m X-box flanks every perimeter column on storeys no model of hers covers. On the storeys she does cover
    // (ModelYardstick.Openings, five sets), each rule took fifty of the seventy openings of ours she has: her shafts on
    // 31168's podium and building C stand within 300 mm of our plate's boundary and have columns in their corner walls.
    // The tests that held them are gone with them; the numbers are in §114 and beside the slab pass.

    /// <summary>
    /// A SLEEVE IS A BOX WITH ITS DIAGONALS (step 111, 2026-09-16). 31202's 1,118 x 382 mm chase, one a storey on L2-L13, is
    /// drawn as a 9 pt rectangle with its two diagonals - a shaft's mark at a sleeve's size - and step 104's 2 m arm minimum
    /// (a symbol's arms are under a metre) refused it; 1,895 of her 4,967 exported openings are under a metre on the short
    /// side. An X of a symbol's size is a mark when its four arm ends are the corners of a rectangle the page draws.
    /// WHAT THIS COVERS: the small X with its box found, Boxed, its region the box; the same X without the box not found;
    /// the box's floor cut with the sleeve as a loop; a box under 600 mm arms (a symbol's) not found. WHAT IT DOES NOT: a
    /// box drawn as one closed polyline (its sides are not two-point lines here: a rectangle of the slab layer is read as
    /// a ring by the ring rule instead); a sleeve with a column under it (step 107); the real sheets (the six-set gate).
    /// </summary>
    [Fact]
    public void ASmallXInsideADrawnBoxIsASleeve_AndWithoutTheBoxASymbol()
    {
        // a 1,118 x 382 mm box with its diagonals (arms 1,181 mm)
        double bx0 = 43000, by0 = 22000, bx1 = 44118, by1 = 22382;
        var boxed = GeometryFilterService.XMarks(Lines((bx0, by0, bx1, by1), (bx0, by1, bx1, by0),
            (bx0, by0, bx1, by0), (bx1, by0, bx1, by1), (bx1, by1, bx0, by1), (bx0, by1, bx0, by0)));
        var mark = Assert.Single(boxed);
        Assert.True(mark.Boxed);
        Assert.InRange(mark.Reach, 1170, 1190);
        Assert.Empty(GeometryFilterService.XMarks(Lines((bx0, by0, bx1, by1), (bx0, by1, bx1, by0))));   // the same X, no box: a symbol
        Assert.Empty(GeometryFilterService.XMarks(Lines((0, 0, 400, 400), (0, 400, 400, 0),                   // a 400 mm box with its X: a symbol's size
            (0, 0, 400, 0), (400, 0, 400, 400), (400, 400, 0, 400), (0, 400, 0, 0))));

        // the floor with the sleeve in it: the plate whole and the sleeve as a loop of its own
        var g = Read(Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1), Line(X1, Y1, X0, Y1), Line(X0, Y1, X0, Y0),
                     Line(bx0, by0, bx1, by0), Line(bx1, by0, bx1, by1), Line(bx1, by1, bx0, by1), Line(bx0, by1, bx0, by0),
                     Line(bx0, by0, bx1, by1), Line(bx0, by1, bx1, by0),
                     Column(41000, 21000), Column(46500, 21000), Column(41000, 25500), Column(46500, 25500));
        Assert.Equal(2, g.Slabs.Count);
        Assert.Equal((X1 - X0) * (Y1 - Y0), Area(g.Slabs[0]), 1);
        Assert.Equal((bx1 - bx0) * (by1 - by0), Area(g.Slabs[1]), 1);
    }
}
