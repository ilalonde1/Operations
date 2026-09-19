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

    /// <summary>
    /// A WALL'S DRAWN FACES STAY IN THE ARRANGEMENT (intake step 124, 2026-09-18). 31202's L2-L4 rims are face pairs read
    /// as 45-in walls; at the north-west corner the two inner faces stop short, the two wall PANELS meet as rectangles
    /// that do not overlap, and the drawn faces - the slab edge among them - were the walls' now, not the slab pass's:
    /// "the outside reaches it through a gap 17 in wide at (44.3, 224.4) ft", every cell open to the page, 1,437 of her
    /// 38,105 sq ft. The outer face is the rim and closes the corner whatever the panel does. WHAT THIS COVERS: two rim
    /// walls from face pairs whose inner faces stop 432 mm short of the corner; the floor still forms to the outer faces.
    /// WHAT IT DOES NOT: filled walls (their faces are the fill's edges, in the arrangement since step 116), a rim pair
    /// that is not a wall at all (the band's own reading, another rule).
    /// </summary>
    [Fact]
    public void TheWallsDrawnFacesCloseTheCornerTheirPanelsLeaveOpen()
    {
        const double gap = 432;
        // the cut pen: a filled wall (a stair's) with its two faces drawn as lines at the wall pen, so the reader knows
        // which pen draws a wall's faces and pairs the rim's at that pen
        RawSubpath Face(double x0, double y0, double x1, double y1) => Line(x0, y0, x1, y1) with { LineWidth = 0.7 };
        var g = Read(
            Wall(50000, 20500, T, 3000), Face(50000, 20500, 50000, 23500), Face(50000 + T, 20500, 50000 + T, 23500),
            Face(X0, Y0, X0, Y1), Face(X0 + T, Y0, X0 + T, Y1 - T - gap),                  // the west rim: outer face whole, inner face short of the corner
            Face(X0, Y1, X1, Y1), Face(X0 + T + gap, Y1 - T, X1, Y1 - T),                  // the north rim: outer face whole, inner face short of the corner
            Line(X0, Y0, X1, Y0), Line(X1, Y0, X1, Y1),                                    // the south and east slab edges
            Line(48000, Y0, 48000, Y1), Line(X0 + T, 23000, X1, 23000),                    // a step and a band, so each cell holds a column
            Column(43000, 20000), Column(52000, 20000), Column(43000, 25500), Column(52000, 25500));
        Assert.Equal(3, g.Walls.Count);
        var plate = Assert.Single(g.Slabs);
        Assert.InRange(AreaM2(plate), 157, 161);   // 16 x 10 m to the outer faces, the corner closed (158.2: the stair wall's own cell and the corner's slivers are not the plate's; 121 with the corner open)
    }
}
