#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A wall is what a fill pattern fills (intake step 38), in three clauses the P1 plan of an
/// architect's set needed: a face drawn in pieces is one face; a line inside a fill pattern's
/// cells is a cut line whatever its pen; a pattern's stripes are not walls, and a wall's end may be
/// mitred. (A stipple beside a line was tried as a fourth clause and refused: the note is in
/// WallsFromFaceLines, where it was.)
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: collinear same-pen pieces meeting end to end joined into one line with the
/// pieces' fates re-pointed at it, a gap wider than an inch left, a different pen left; two light
/// faces inside a run of pattern cells pairing as a wall while the same faces with no pattern do
/// not; three parallel stripes at one pitch leaving
/// the walls while two do not; the mitred end (in `TheAuditsCounterexamplesTests.F1`). WHAT IT DOES
/// NOT: the real sheet (31170 A101: walls 14 → 51, the perimeter read, 2026-09-10) and the six-set
/// run; a face broken by a doorway; a Z-jog
/// wall with two mitres of opposite sense, which reads as a stripe if two siblings share its pitch.
/// </remarks>
public sealed class AWallIsWhatAFillPatternFillsTests
{
    private static RawSubpath Line(double x0, double y0, double x1, double y1, double pen = 0.6)
        => new([(x0, y0), (x1, y1)], false, (0, 0, 0), false, true, pen, false);
    private static RawSubpath Cell(double x, double y) => FateFixture.Rect(900, 1200, x, y) with { Color = (0, 0, 0) };

    private static (ExtractedGeometry, List<PathFate>) Read(params RawSubpath[] paths)
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify(paths, fates);
        return (g, fates);
    }

    [Fact]
    public void AFaceInPiecesIsOneFaceAndItsPiecesFatesPointAtIt()
    {
        // a run of pattern cells along y 31000 (the fill the pieces run through), an annotation line
        // first so the lines the pass considers are not numbered like the lines it rebuilds (the first
        // cut indexed one list by the other and threw on 53 of 67 sheets, 2026-09-10), then the pieces
        var cells = new[] { Cell(60000, 30400), Cell(60900, 30400), Cell(61800, 30400), Cell(62700, 30400) };   // 900 x 1200 boxes, y 30400..31600
        var (g, fates) = Read(cells.Concat(new[]
                              {
                                  Line(50000, 40000, 52000, 40000) with { IsAnnotation = true },
                                  Line(60000, 31000, 61200, 31000), Line(61200, 31000, 62400, 31000), Line(62400, 31000, 63600, 31000),   // in the cells: one face
                                  Line(60000, 31100, 61200, 31100), Line(61300, 31100, 62500, 31100),        // a 100 mm gap: a break the drafter meant
                                  Line(60000, 31200, 61200, 31200), Line(61200, 31200, 62400, 31200, pen: 1.2),  // a different pen
                                  Line(60000, 36000, 61200, 36000), Line(61200, 36000, 62400, 36000),        // touching, but outside any cell: two walls' faces, left alone
                              }).ToArray());
        Assert.Contains(g.Lines, l => Math.Abs(l[0].X - 60000) < 1 && Math.Abs(l[1].X - 63600) < 1 && Math.Abs(l[0].Y - 31000) < 1);
        Assert.Equal(2, g.Lines.Count(l => Math.Abs(l[0].Y - 31100) < 1));
        Assert.Equal(2, g.Lines.Count(l => Math.Abs(l[0].Y - 31200) < 1));
        Assert.Equal(2, g.Lines.Count(l => Math.Abs(l[0].Y - 36000) < 1));
        Assert.Equal(2, g.LinePiecesJoined);
        int joined = g.Lines.FindIndex(l => Math.Abs(l[1].X - 63600) < 1);
        var pieceFates = fates.Where(f => f.PathIndex >= cells.Length + 1 && f.PathIndex < cells.Length + 4).ToList();
        Assert.Equal(3, pieceFates.Count(f => f.Reason == PathReason.EmittedAsLine && f.ObjectIndex == joined));
        Assert.Equal(0, fates.Single(f => f.PathIndex == cells.Length).ObjectIndex);               // the annotation line is still line 0
        Assert.Equal(g.Lines.Count, g.LineWidths.Count); Assert.Equal(g.Lines.Count, g.LineColors.Count); Assert.Equal(g.Lines.Count, g.LineIsAnnotation.Count);
    }

    [Fact]
    public void LightFacesInsideARunOfPatternCellsPairAsAWall()
    {
        // three cells along y, and two faces 200 mm apart running the length of the run, in a light pen
        var cells = new[] { Cell(60000, 30000), Cell(60000, 31200), Cell(60000, 32400) };
        var faces = new[] { Line(60300, 30000, 60300, 33600), Line(60500, 30000, 60500, 33600) };
        var (with, _) = Read(cells.Concat(faces).ToArray());
        var wall = Assert.Single(with.Walls);
        Assert.InRange(wall.ThicknessMm, 190, 210);
        Assert.Equal(3, with.PatternCells.Count);

        var (without, _) = Read(faces);                                                   // the same faces, no fill: no wall
        Assert.Empty(without.Walls);
    }

    [Fact]
    public void ThreeStripesAtOnePitchAreAPatternAndTwoAreNot()
    {
        static RawSubpath Stripe(double y) => FateFixture.Rect(1800, 300, 60000, y) with { Color = (0xE0, 0xE0, 0xE0) };   // 1800 x 400 is the fixture's declared column
        var (three, fates) = Read(Stripe(30000), Stripe(30600), Stripe(31200));
        Assert.Empty(three.Walls);
        Assert.Equal(3, fates.Count(f => f.Reason == PathReason.PatternCell));
        var (two, _) = Read(Stripe(30000), Stripe(30600));
        Assert.Equal(2, two.Walls.Count);
        var (apart, _) = Read(Stripe(30000), Stripe(34000), Stripe(38000));                  // a room apart: three walls
        Assert.Equal(3, apart.Walls.Count);
    }
}
