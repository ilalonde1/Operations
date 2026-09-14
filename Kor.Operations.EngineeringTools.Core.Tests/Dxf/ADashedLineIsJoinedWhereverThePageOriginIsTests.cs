#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// Audit F8 (intake step 61, 2026-09-13): the joiner measured each dash's offset along its OWN normal
/// from the page's origin, so two dashes of one line drawn half a degree apart - within the angle
/// tolerance - and fifty metres from the origin sat 436 mm apart in offset and were two lines; and
/// where the origin was decided it (the frame class of s63). Offsets are measured from the direction's
/// first segment, along its normal.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two nearly parallel dashes far from the origin join; the same dashes shifted by
/// a fractional vector join the same; two dashes a real offset apart stay two. WHAT IT DOES NOT: the
/// gap rule along the line (maxGap); a run of dashes that curves past the angle tolerance over its
/// length (each is judged against the first).
/// </remarks>
public sealed class ADashedLineIsJoinedWhereverThePageOriginIsTests
{
    private static DxfSegment Seg(double x0, double y0, double x1, double y1) => new("L", new DxfPoint(x0, y0), new DxfPoint(x1, y1));

    [Fact]
    public void TwoDashesHalfADegreeApartFiftyMetresOutAreOneLine()
    {
        // a dash along X at y = 40,000 from x 50,000 to 50,600; the next dash 10 in on, turned 0.4 degrees
        double turn = 0.4 * Math.PI / 180;
        var a = Seg(50000, 40000, 50600, 40000);
        var b = Seg(50610, 40000, 50610 + 600 * Math.Cos(turn), 40000 + 600 * Math.Sin(turn));
        var joined = DashedLineJoiner.Join([a, b], maxGap: 24, angleToleranceDegrees: 0.5, offsetTolerance: 0.15);
        Assert.Single(joined);

        // the same dashes shifted by a fractional vector: the same answer (the offset is measured between them, not from the origin)
        var shifted = DashedLineJoiner.Join([Shift(a, 5000.37, 3000.61), Shift(b, 5000.37, 3000.61)], maxGap: 24, angleToleranceDegrees: 0.5, offsetTolerance: 0.15);
        Assert.Single(shifted);

        // two dashes on two lines 2 units apart stay two
        var apart = DashedLineJoiner.Join([a, Seg(50610, 40002, 51210, 40002)], maxGap: 24, angleToleranceDegrees: 0.5, offsetTolerance: 0.15);
        Assert.Equal(2, apart.Count);
    }

    private static DxfSegment Shift(DxfSegment s, double dx, double dy) => new(s.Layer, new DxfPoint(s.Start.X + dx, s.Start.Y + dy), new DxfPoint(s.End.X + dx, s.End.Y + dy));
}
