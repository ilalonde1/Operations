#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A filled band thicker than the thickest wall and many times longer than it is thick is a band,
/// not a slab and not a wall (intake step 21). It stays unaccounted, named, for the ledger.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: 31168's 66" x 1,631" band reading as Band and not a slab; a 60" wall still a
/// wall; a band twice the thickest wall and more reading as a slab again (a floor is wider than a
/// bay); a short thick fill (aspect under 10) still a slab; a turned band; a paper-coloured band
/// (a mask) unchanged. WHAT IT DOES NOT: what the band IS on the drawing — a property-line zone,
/// a shotcrete wall's family — which the ledger leaves to a person; a band drawn as lines.
/// </remarks>
public sealed class AFilledBandThickerThanAnyWallIsNotASlabTests
{
    private static RawSubpath Band(double thicknessIn, double lengthIn, double x = 10000, double y = 10000)
        => FateFixture.Rect(lengthIn * 25.4, thicknessIn * 25.4, x, y);

    [Fact]
    public void ABandThickerThanTheThickestWallIsNotASlab()
    {
        var (g, fates) = WallFixture.Read([Band(66, 1631)]);
        Assert.Empty(g.Slabs);
        Assert.Empty(g.Walls);
        var fate = Assert.Single(fates);
        Assert.Equal(PathReason.Band, fate.Reason);
        Assert.Equal(Disposition.Unaccounted, fate.Disposition);
    }

    [Fact]
    public void TheThickestWallIsStillAWall()
    {
        var (g, _) = WallFixture.Read([Band(60, 1631)]);
        Assert.Single(g.Walls);
        Assert.Empty(g.Slabs);
    }

    [Fact]
    public void WiderThanTwiceTheThickestWallIsAFloorAgain()
    {
        var (g, fates) = WallFixture.Read([Band(121, 1631)]);
        Assert.Single(g.Slabs);
        Assert.Equal(PathReason.BecameSlab, Assert.Single(fates).Reason);
    }

    [Fact]
    public void AShortThickFillIsStillASlab()
    {
        var (g, _) = WallFixture.Read([Band(66, 400)]);
        Assert.Single(g.Slabs);
    }

    [Fact]
    public void ATurnedBandIsABand()
    {
        double c = Math.Cos(Math.PI / 5), s = Math.Sin(Math.PI / 5), t = 66 * 25.4, l = 1631 * 25.4;
        (double, double) P(double u, double v) => (10000 + u * c - v * s, 10000 + u * s + v * c);
        var band = FateFixture.Rect(l, t) with { Points = [P(0, 0), P(l, 0), P(l, t), P(0, t)] };
        var (_, fates) = WallFixture.Read([band]);
        Assert.Equal(PathReason.Band, Assert.Single(fates).Reason);
    }

    [Fact]
    public void APaperColouredBandIsAMaskNotABand()
    {
        var (_, fates) = WallFixture.Read([Band(66, 1631) with { Color = (0xF0, 0xF0, 0xF0) }]);
        Assert.Equal(PathReason.PaperFill, Assert.Single(fates).Reason);
    }
}
