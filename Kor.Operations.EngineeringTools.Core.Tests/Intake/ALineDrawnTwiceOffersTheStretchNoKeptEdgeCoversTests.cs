#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A LINE DRAWN TWICE OFFERS THE STRETCH NO KEPT EDGE COVERS, AND A STROKE CONTINUING A KEPT EDGE IN LINE IS OFFERED
/// WHOLE (step 170 as repaired in f5a74e04, and 2026-10-26). The repair offered only the part of a stroke past a kept
/// edge it OVERLAPS; a stroke that meets a kept edge end to end overlaps nothing, read as "not along a kept edge at all",
/// and was dropped - 31168's building C level 3 (S2.40.1) lost its outline's east side, the ring open by 25 in, the floor
/// 23,199 sq ft cut back to the 11,885 of the line inside it (SlabPassTraceProbe, page 30: with the continuation offered,
/// 0 columns on no floor; without, 6).
/// WHAT THIS COVERS: the rule alone, on one kept edge - a continuation end to end, an overlap past the end, a stroke the
/// edge covers, a parallel stroke a wall's width off, and a stroke meeting the edge's end across its line.
/// WHAT IT DOES NOT: which edges are kept (the walk's loops and pieces) or the arrangement built from what is offered -
/// the real sheet is the witness for that. A same-class fault it would not catch: a continuation that starts a few
/// millimetres past the kept edge's end (beyond the join), which is a gap for the bridge, not a continuation.
/// </summary>
public sealed class ALineDrawnTwiceOffersTheStretchNoKeptEdgeCoversTests
{
    private const double Join = GeometryFilterService.SlabEdgeJoinMm;
    private static readonly (DxfPoint A, DxfPoint B)[] Kept = [(new DxfPoint(0, 0), new DxfPoint(1000, 0))];

    private static List<DxfPoint[]> Offered(double x0, double y0, double x1, double y1)
        => GeometryFilterService.StretchesNoKeptEdgeCovers(new DxfPoint(x0, y0), new DxfPoint(x1, y1), Kept, Join);

    [Fact]
    public void AStrokeContinuingAKeptEdgeEndToEndIsOfferedWhole()
    {
        var piece = Assert.Single(Offered(1000, 0, 2500, 0));
        Assert.Equal(1000, piece[0].X, 3);
        Assert.Equal(2500, piece[1].X, 3);
    }

    [Fact]
    public void AStrokeOverlappingAKeptEdgeOffersOnlyTheStretchPastIt()
    {
        var piece = Assert.Single(Offered(500, 0, 2000, 0));
        Assert.Equal(1000, piece[0].X, 3);
        Assert.Equal(2000, piece[1].X, 3);
    }

    [Fact]
    public void AStrokeTheKeptEdgeCoversOffersNothing() => Assert.Empty(Offered(200, 0, 800, 0));

    [Fact]
    public void AParallelStrokeAWallsWidthOffOffersNothing() => Assert.Empty(Offered(1000, 100, 2000, 100));

    [Fact]
    public void AStrokeMeetingTheEdgesEndAcrossItsLineOffersNothing() => Assert.Empty(Offered(1000, 0, 1000, 1500));
}
