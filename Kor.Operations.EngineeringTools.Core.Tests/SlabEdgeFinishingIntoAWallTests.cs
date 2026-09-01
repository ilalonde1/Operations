using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A slab edge that finishes INTO a wall face is still closed; a wall standing in the middle of a
/// floor is not a slab edge and must never be borrowed as one.
/// </summary>
/// <remarks>
/// SlabEdgeClosure completes an open chain from linework the tool reads but does not model — that
/// is where C-LEVEL 3's chamfer was, on JBP_C_B_STRUCT. It is not where the YMCA mezzanine's edge
/// is: there the drawing simply stops drawing a slab edge along the stretch a core wall stands on,
/// because the draftsman does not draw the same line twice. So walls were added to the borrowable
/// set.
///
/// That was measured as changing nothing on 31168, which proves it is not breaking that job and
/// proves nothing about what it might do. What keeps it safe is EXACT continuity: a borrowed piece
/// must meet a loose end within the ordinary join tolerance, and a closure may use only a corner or
/// two. These pin that, so the safety is a test rather than a hope.
/// </remarks>
public class SlabEdgeFinishingIntoAWallTests
{
    private const string Slab = "JBP_C_SLABEDG";
    private const string Wall = "JBP_V-WALL";

    private static PlanClassificationOptions Options() => new()
    {
        SlabLayerPatterns = new[] { Slab },
        WallLayerPatterns = new[] { Wall },
        ColumnLayerPatterns = new[] { "JBP_V_COL" },
    };

    private static DxfPositionedTag Slab14(double x, double y)
        => new("14\" SLAB", new DxfPoint(x, y), "A-FLOR-IDEN", "14\" SLAB");

    /// <summary>
    /// Three sides drawn on the slab layer; the fourth is the face of a wall, meeting both loose
    /// ends exactly. The ring closes and the floor is the whole rectangle.
    /// </summary>
    [Fact]
    public void AnEdgeThatFinishesIntoAWallFaceCloses()
    {
        var segments = new List<DxfSegment>
        {
            new(Slab, new DxfPoint(0, 0), new DxfPoint(1200, 0)),
            new(Slab, new DxfPoint(1200, 0), new DxfPoint(1200, 900)),
            new(Slab, new DxfPoint(1200, 900), new DxfPoint(0, 900)),
            new(Wall, new DxfPoint(0, 900), new DxfPoint(0, 0)),
        };

        var set = StructuralPlanClassifier.Classify(
            segments, Options(), sheet: null, tags: new[] { Slab14(600, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.True(Math.Abs(plate.Area - 1_200 * 900) < 1_000, $"{plate.Area:N0} sq in");
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A wall crossing the middle of the floor touches no loose end, so it
    /// cannot be borrowed — a slab cut in half at an internal wall is not a slab this tool may
    /// invent, and every core wall in every building is this shape.
    /// </summary>
    [Fact]
    public void AWallInsideTheFloorIsNotBorrowedAsAnEdge()
    {
        var segments = new List<DxfSegment>
        {
            new(Slab, new DxfPoint(0, 0), new DxfPoint(1200, 0)),
            new(Slab, new DxfPoint(1200, 0), new DxfPoint(1200, 900)),
            new(Slab, new DxfPoint(1200, 900), new DxfPoint(0, 900)),
            new(Wall, new DxfPoint(0, 900), new DxfPoint(0, 0)),
            new(Wall, new DxfPoint(600, 200), new DxfPoint(600, 700)),   // a core wall, mid-floor
        };

        var set = StructuralPlanClassifier.Classify(
            segments, Options(), sheet: null, tags: new[] { Slab14(300, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.True(Math.Abs(plate.Area - 1_200 * 900) < 1_000, $"{plate.Area:N0} sq in");
    }

    /// <summary>
    /// A wall that comes CLOSE to the loose ends but does not meet them closes nothing. Where the
    /// drawing really is open, nothing here closes it.
    /// </summary>
    [Fact]
    public void AWallThatDoesNotMeetTheEndsClosesNothing()
    {
        var segments = new List<DxfSegment>
        {
            new(Slab, new DxfPoint(0, 0), new DxfPoint(1200, 0)),
            new(Slab, new DxfPoint(1200, 0), new DxfPoint(1200, 900)),
            new(Slab, new DxfPoint(1200, 900), new DxfPoint(0, 900)),
            new(Wall, new DxfPoint(-40, 880), new DxfPoint(-40, 20)),    // parallel, 40 in away
        };

        var set = StructuralPlanClassifier.Classify(
            segments, Options(), sheet: null, tags: new[] { Slab14(600, 450) });

        Assert.Empty(set.Slabs);
    }
}
