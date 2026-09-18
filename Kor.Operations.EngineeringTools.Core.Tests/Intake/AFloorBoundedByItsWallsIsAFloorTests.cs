#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A FLOOR BOUNDED BY ITS WALLS IS A FLOOR (intake step 116, 2026-09-17). A filled wall leaves no line for the slab pass -
/// its faces are the fill's edges - so a floor whose perimeter is its walls (a parkade behind retaining walls: 31168's
/// P1/P2 read 897 of her 87,035 sq ft) had no outline where the walls stand. The walls' outlines, doors closed, are in
/// the floor's arrangement as they have been in the stair wells' since step 105, and the engineer's own rule follows:
/// the plate runs to the perimeter walls' OUTER face (a wall's band cell holds the wall and joins the union). Two things
/// the walls must not do in the arrangement, found on 31065's L3 north (14,924 sq ft that fell to the page the moment
/// the walls came in): a slab edge's bridge may cross a wall's outline, and an end is an end of the DRAWN lines, walls
/// aside - a slab edge stopping at a wall's face meets the outline there and would otherwise be no end at all.
/// WHAT THIS COVERS: a 16 x 10 m floor whose north and south edges are drawn lines stopping at the inner faces of two
/// filled walls on its west and east; the walk cannot close it (two sides have no line) and the arrangement without
/// the walls holds nothing; with them the floor is one plate to the walls' outer faces, 16 x 10 m (15.5 to the inner faces).
/// WHAT IT DOES NOT: the real sets (the six-set gate, the corpus); a floor with walls on every side (its slab edges
/// are then the walls' faces alone); the bridge across a wall and the end at a wall's face on a drawn outline that
/// continues past the wall - judged on 31065's L3 north by the gate, not here.
/// </summary>
public sealed class AFloorBoundedByItsWallsIsAFloorTests
{
    private const double X0 = 40000, Y0 = 18000, X1 = 56000, Y1 = 28000, T = 250;   // the outer faces; walls T thick

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static RawSubpath Wall(double x, double y, double w, double h) => FateFixture.Rect(w, h, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double AreaM2(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring)) / 1e6;

    [Fact]
    public void TheFloorRunsToTheWallsOuterFaces()
    {
        var g = Read(
            Line(X0 + T, Y1, X1 - T, Y1), Line(X0 + T, Y0, X1 - T, Y0),      // north and south edges, stopping at the walls' inner faces
            Line(48000, Y0, 48000, Y1), Line(X0 + T, 23000, X1 - T, 23000),  // a step and a band across the floor (the slab pass wants four lines to start; each cell holds a column)
            Wall(X0, Y0, T, Y1 - Y0), Wall(X1 - T, Y0, T, Y1 - Y0),          // the west and east walls, filled
            Column(43000, 20000), Column(52000, 20000), Column(43000, 25500), Column(52000, 25500));
        Assert.Equal(2, g.Walls.Count);
        var plate = Assert.Single(g.Slabs);
        Assert.InRange(AreaM2(plate), 160 * 0.99, 160 * 1.01);   // 16 x 10 m: to the walls' outer faces (155 to their inner faces)
    }
}
