#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A RIM CELL FACING THE PAGE THROUGH THE OUTLINE'S OWN PEN IS INSIDE THE OUTLINE (intake step 115, 2026-09-17). The rim
/// rule (a cell touching the outside is not the floor unless it holds structure) was written for balcony boxes and
/// dimension strips outside the outline; a line drawn across the floor to the slab edge closes a cell at the rim INSIDE
/// the outline, and that cell was dropped with them (31065's typical tower: 6,691 sq ft of her 7,766). The outline is
/// drawn with one pen; a rim cell whose every outward edge lies on a line of that pen is inside it.
/// WHAT THIS COVERS: a 20 x 10 m floor outlined at one pen, open at its east edge so the walk cannot close it, cut by
/// thin lines into a west strip holding nothing, a middle holding the columns, and an east part open to the page;
/// a balcony box outside the west edge drawn thin, and one outside the south edge drawn with the outline's pen. The
/// plate takes the west strip (rim, no structure, faces the page through the outline) and the outline-pen balcony
/// (the drawing says slab), and not the thin one; without the rule the plate is the middle alone.
/// WHAT IT DOES NOT: the rule's second half - a rim cell is inside the outline only where it is reached from a
/// floor-sized structure-holding cell through cells that are floor - which a fixture cannot isolate (a detached box
/// at the outline's pen holding nothing is dropped by the neighbourhood gate whatever this rule says, and one holding
/// a wall is a plate by the holding alone); it is judged on 31065's south tower L7, whose core box stood as a 671 sq ft
/// plate without it (the six-set gate). Nor the real sets at large (the corpus); a page whose structure-holding cells
/// face the page with two pens (the mode wins); a rim cell reached through a carried or bridged edge that lies on no
/// drawn line (no pen: not inside).
/// </summary>
public sealed class ARimCellFacingThePageThroughTheOutlinesPenIsInsideTheOutlineTests
{
    private const double X0 = 40000, Y0 = 18000, X1 = 60000, Y1 = 28000;   // the floor, 20 x 10 m (off the fixture's grid axes at 30,000)
    private const double OutlinePen = 1.0, ThinPen = 0.5;

    private static RawSubpath Line(double x0, double y0, double x1, double y1, double pen = ThinPen) => FateFixture.Line(x0, y0, x1, y1) with { LineWidth = pen };
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double AreaM2(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring)) / 1e6;

    private static IEnumerable<RawSubpath> Page(double balconyPen)
    {
        // the outline at the outline's pen, its east edge open 300 mm in its middle (the walk cannot close it, and the
        // east cells open to the page through the gap)
        yield return Line(X0, Y0, X1, Y0, OutlinePen);
        yield return Line(X1, Y0, X1, 22850, OutlinePen); yield return Line(X1, 23150, X1, Y1, OutlinePen);
        yield return Line(X1, Y1, X0, Y1, OutlinePen);
        yield return Line(X0, Y1, X0, Y0, OutlinePen);
        // thin lines across the floor: a west strip 6 m wide holding nothing, a middle 8 m wide holding the columns (its
        // cells 8 x 5 m, over the 400 sq ft a floor cell must have to name the outline's pen)
        yield return Line(46000, Y0, 46000, Y1);
        yield return Line(54000, Y0, 54000, Y1);
        yield return Line(X0, 23000, X1, 23000);
        foreach (var (x, y) in new[] { (48000.0, 20000.0), (52000.0, 20000.0), (48000.0, 25000.0), (52000.0, 25000.0) })
            yield return Column(x, y);
        // a balcony box outside the west edge, drawn thin: faces the page through its own lines
        yield return Line(X0, 21000, 37000, 21000); yield return Line(37000, 21000, 37000, 24000); yield return Line(37000, 24000, X0, 24000);
        // a balcony box outside the south edge, drawn at the given pen
        yield return Line(44000, Y0, 44000, 16000, balconyPen); yield return Line(44000, 16000, 48000, 16000, balconyPen); yield return Line(48000, 16000, 48000, Y0, balconyPen);
    }

    [Fact]
    public void TheWestStripAndTheOutlinePenBalconyAreFloor_TheThinBalconyIsNot()
    {
        var g = Read(Page(OutlinePen).ToArray());
        var plate = Assert.Single(g.Slabs);
        // the west strip (6 x 10) + the middle (8 x 10) + the south balcony (4 x 2): 148 m²; not the east part, not the thin balcony
        Assert.InRange(AreaM2(plate), 148 * 0.98, 148 * 1.02);

        var thin = Read(Page(ThinPen).ToArray());
        var withoutBalcony = Assert.Single(thin.Slabs);
        Assert.InRange(AreaM2(withoutBalcony), 140 * 0.98, 140 * 1.02);   // the strip and the middle; the balcony faces the page with a thin pen
    }
}
