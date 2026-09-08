#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A footing is a dashed rectangle whose size the foundation schedule declares. Measured first
/// (2026-09-08, three foundation plans): no footing outline is a closed path; chained across the DXF
/// side's dash-join gap and closed into boxes, the boxes match the schedule to the millimetre.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a dashed square of a scheduled size becomes one footing with that mark; its
/// dashes all carry BecameFooting with the footing's index and never reach another branch; a box of
/// an unscheduled size is not a footing; a solid side (one piece) does not chain. WHAT IT DOES NOT:
/// real drawings — FiveStickFilesTests banks the counts on the five sets.
/// </remarks>
public sealed class AFootingIsADashedRectangleTheScheduleSizesTests
{
    private static List<RawSubpath> DashedSquare(double x, double y, double side, int dashesPerSide)
    {
        var raw = new List<RawSubpath>();
        double dash = side / (2.0 * dashesPerSide - 1);   // dash, gap, dash … ending on a dash
        for (int k = 0; k < dashesPerSide; k++)
        {
            double a = k * 2 * dash;
            raw.Add(Line(x + a, y, x + a + dash, y));
            raw.Add(Line(x + a, y + side, x + a + dash, y + side));
            raw.Add(Line(x, y + a, x, y + a + dash));
            raw.Add(Line(x + side, y + a, x + side, y + a + dash));
        }
        return raw;
    }

    private static RawSubpath Line(double x0, double y0, double x1, double y1)
        => new([(x0, y0), (x1, y1)], false, (0, 0, 0), false, true, 0.5, false);

    private static readonly FootingScheduleReader.FootingType F2 = new("F2", 1524, 1524, 813);
    private static readonly FootingScheduleReader.FootingType F4 = new("F4", 2134, 2134, 914);

    [Fact]
    public void ADashedSquareOfAScheduledSizeIsThatFooting()
    {
        var raw = DashedSquare(10000, 20000, 1524, 5);
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4]);
        var f = Assert.Single(footings);
        Assert.Equal("F2", f.Mark);
        Assert.Equal(1524, f.LengthMm); Assert.Equal(813, f.DepthMm);
        Assert.Equal(10000 + 762, f.Centre.X, 1); Assert.Equal(20000 + 762, f.Centre.Y, 1);
        Assert.Equal(raw.Count, pieces.Count);
        Assert.All(pieces.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void AnUnscheduledSizeIsNotAFootingAndASolidSideDoesNotChain()
    {
        var (unsized, _) = FootingOutlines.Read(DashedSquare(0, 0, 1800, 5), [F2, F4]);
        Assert.Empty(unsized);
        // one piece per side: a solid rectangle drawn as four lines is not a dashed outline
        var (solid, _) = FootingOutlines.Read(DashedSquare(0, 0, 1524, 1), [F2, F4]);
        Assert.Empty(solid);
    }

    [Fact]
    public void TheClassifierRecordsEveryDashAsTheFootingsAndEmitsNothingElseFromThem()
    {
        var raw = DashedSquare(10000, 20000, 2134, 6);
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4]);
        Assert.Single(footings);
        var result = new ExtractedGeometry();
        var fates = new List<PathFate>();
        GeometryFilterService.Classify(raw, result, 3000, 200, false, 100000, 70000, annotationsOnly: false,
            furniture: SheetFurniture.Set.Empty, fates: fates, footingPieces: pieces);
        Assert.Equal(raw.Count, fates.Count);
        Assert.All(fates, f => { Assert.Equal(PathReason.BecameFooting, f.Reason); Assert.Equal(0, f.ObjectIndex); Assert.Equal(Disposition.Read, f.Disposition); });
        Assert.Empty(result.Lines); Assert.Empty(result.Slabs); Assert.Empty(result.Walls);
    }
}
