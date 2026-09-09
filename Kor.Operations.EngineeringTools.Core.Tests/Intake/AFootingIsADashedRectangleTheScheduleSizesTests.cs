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

    /// <summary>One full dashed side at x, and a stub at each of its ends running +x — the outline of a footing something stands on.</summary>
    private static List<RawSubpath> InterruptedSquare(double x, double y, double side, int dashesPerSide, double stub)
    {
        var raw = new List<RawSubpath>();
        double dash = side / (2.0 * dashesPerSide - 1);
        for (int k = 0; k < dashesPerSide; k++) { double a = k * 2 * dash; raw.Add(Line(x, y + a, x, y + a + dash)); }
        raw.Add(Line(x, y, x + stub, y));
        raw.Add(Line(x, y + side, x + stub, y + side));
        return raw;
    }

    [Fact]
    public void AFullSideAndTwoStubsPlaceTheFootingItsScheduledDepthFromTheSide()
    {
        var raw = InterruptedSquare(10000, 20000, 1524, 5, 400);
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4]);
        var f = Assert.Single(footings);
        Assert.Equal("F2", f.Mark);
        Assert.Equal(10000 + 762, f.Centre.X, 1); Assert.Equal(20000 + 762, f.Centre.Y, 1);
        Assert.Equal(raw.Count, pieces.Count);
        Assert.False(f.LabelledOnThePlan);
    }

    [Fact]
    public void AFullSideWithAStubAtOneEndOnlyIsNotAFootingNorAreStubsPointingApart()
    {
        var oneStub = InterruptedSquare(10000, 20000, 1524, 5, 400);
        oneStub.RemoveAt(oneStub.Count - 1);
        Assert.Empty(FootingOutlines.Read(oneStub, [F2, F4]).Footings);
        var apart = InterruptedSquare(10000, 20000, 1524, 5, 400);
        apart[^1] = Line(10000, 20000 + 1524, 10000 - 400, 20000 + 1524);   // top stub runs −x, bottom +x
        Assert.Empty(FootingOutlines.Read(apart, [F2, F4]).Footings);
    }

    [Fact]
    public void PiecesEitherSideOfATwelveMillimetreBoundaryAreOneSide()
    {
        // every other dash of the left side sits 10 mm off the others' line (77,000 / 77,010 — the
        // 31065 p15 case was 77,208 / 77,220); a fixed 12 mm bucket split them 3 and 2, one side does not
        var raw = DashedSquare(77000, 20000, 2134, 5);
        int n = 0;
        for (int i = 0; i < raw.Count; i++)
        {
            var s = raw[i];
            bool leftSide = s.Points.All(p => Math.Abs(p.X - 77000) < 1) && s.Points[0].Y != s.Points[1].Y;
            if (leftSide && n++ % 2 == 1) raw[i] = s with { Points = s.Points.Select(p => (p.X + 10, p.Y)).ToList() };
        }
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4]);
        Assert.Equal("F4", Assert.Single(footings).Mark);
        Assert.Equal(raw.Count, pieces.Count);
    }

    [Fact]
    public void ALabelInOrJustBeneathTheBoxNamesItAndOneElsewhereOrOfAnotherMarkDoesNot()
    {
        var raw = DashedSquare(10000, 20000, 1524, 5);
        var inside = FootingOutlines.Read(raw, [F2, F4], labels: [new FootingOutlines.MarkLabel("F2", 10700, 20700)]);
        Assert.True(Assert.Single(inside.Footings).LabelledOnThePlan);
        // 31130 labels 254–461 mm beneath the outline, beside the column
        var beneath = FootingOutlines.Read(raw, [F2, F4], labels: [new FootingOutlines.MarkLabel("F2", 10700, 20000 - 400)]);
        Assert.True(Assert.Single(beneath.Footings).LabelledOnThePlan);
        var elsewhere = FootingOutlines.Read(raw, [F2, F4],
            labels: [new FootingOutlines.MarkLabel("F2", 30000, 30000), new FootingOutlines.MarkLabel("F4", 10700, 20700)]);
        Assert.False(Assert.Single(elsewhere.Footings).LabelledOnThePlan);
    }

    [Fact]
    public void TheClassifierRecordsEveryDashAsTheFootingsAndEmitsNothingElseFromThem()
    {
        var raw = DashedSquare(10000, 20000, 2134, 6);
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4], labels: [new FootingOutlines.MarkLabel("F4", 11000, 21000)]);
        Assert.Single(footings);
        var result = new ExtractedGeometry();
        result.Footings.AddRange(footings);
        var fates = new List<PathFate>();
        GeometryFilterService.Classify(raw, result, 3000, 200, false, 100000, 70000, annotationsOnly: false,
            furniture: SheetFurniture.Set.Empty, fates: fates, footingPieces: pieces);
        Assert.Equal(raw.Count, fates.Count);
        Assert.All(fates, f => { Assert.Equal(PathReason.BecameFooting, f.Reason); Assert.Equal(0, f.ObjectIndex); Assert.Equal(Disposition.Read, f.Disposition); });
        Assert.Empty(result.Lines); Assert.Empty(result.Slabs); Assert.Empty(result.Walls); Assert.Empty(result.Columns);
    }

    /// <summary>A dashed box of a scheduled size that no label names is emitted, flagged, and its pieces are unaccounted, not read (audit F2).</summary>
    [Fact]
    public void ABoxNoLabelNamesIsEmittedFlaggedAndItsPiecesAreUnaccounted()
    {
        var raw = DashedSquare(10000, 20000, 2134, 6);
        var (footings, pieces) = FootingOutlines.Read(raw, [F2, F4]);
        var f = Assert.Single(footings);
        Assert.False(f.LabelledOnThePlan);
        var result = new ExtractedGeometry();
        result.Footings.AddRange(footings);
        var fates = new List<PathFate>();
        GeometryFilterService.Classify(raw, result, 3000, 200, false, 100000, 70000, annotationsOnly: false,
            furniture: SheetFurniture.Set.Empty, fates: fates, footingPieces: pieces);
        Assert.Equal(raw.Count, fates.Count);
        Assert.All(fates, ft => { Assert.Equal(PathReason.FootingBoxNoLabel, ft.Reason); Assert.Equal(0, ft.ObjectIndex); Assert.Equal(Disposition.Unaccounted, ft.Disposition); });
        Assert.Empty(result.Lines); Assert.Empty(result.Slabs); Assert.Empty(result.Walls);
    }
}
