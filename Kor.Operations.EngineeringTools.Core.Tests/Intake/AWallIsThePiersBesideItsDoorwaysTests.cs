#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A doorway is a paper-coloured fill painted over a wall (intake step 14): after the wall in paint
/// order, across its thickness, at least a door wide. The wall is emitted as the piers on either side.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two knockouts splitting a wall into three piers of the right lengths; the fill's
/// fate (Doorway, read, indexed into Geometry.Doorways) and the wall's (BecameWall, first pier); a
/// fill painted BEFORE the wall, one that does not cross the thickness, a slot narrower than 18", and
/// a mask far wider across than the wall — none a doorway; a wall turned 30° with a turned knockout;
/// a doorway at a wall's end that shortens it. WHAT IT DOES NOT: a real sheet (FiveStickFilesTests
/// banks 31168's counts); a knockout that is STROKED white, which the paper-fill population excludes;
/// a doorway drawn as a gap between two separate wall fills, which needs no rule.
/// </remarks>
public sealed class AWallIsThePiersBesideItsDoorwaysTests
{
    private const double L = 328 * 25.4, T = 30 * 25.4;
    private static readonly (byte, byte, byte) White = (0xFF, 0xFF, 0xFF);

    private static RawSubpath Wall() => WallFixture.Rect(thicknessIn: 30, lengthIn: 328);   // along x, 0..L; across y, 0..T
    private static RawSubpath Knockout(double xMm, double widthMm, double y = -20, double across = T + 40)
        => FateFixture.Rect(widthMm, across, xMm, y) with { Color = White };

    [Fact]
    public void TwoKnockoutsMakeThreePiersAndAreReadAsDoorways()
    {
        var (g, fates) = WallFixture.Read([Wall(), Knockout(2700, 1000), Knockout(5300, 1000)]);
        Assert.Equal(3, g.Walls.Count);
        Assert.Equal(new[] { 2700.0, 1600.0, Math.Round(L - 6300) }, g.Walls.Select(w => Math.Round(Length(w))).ToArray());
        Assert.All(g.Walls, w => Assert.Equal(T, w.ThicknessMm, 1));
        Assert.All(g.Walls, w => Assert.Equal(4, w.Outline.Count));
        Assert.Equal(2, g.Doorways.Count);
        Assert.All(g.Doorways, d => Assert.Equal(1000, d.LengthMm, 1));
        Assert.All(g.Doorways, d => Assert.Equal(0, d.FirstPier));

        var wall = Assert.Single(fates, f => f.PathIndex == 0);
        Assert.Equal(PathReason.BecameWall, wall.Reason);
        Assert.Equal(0, wall.ObjectIndex);
        var doors = fates.Where(f => f.Reason == PathReason.Doorway).OrderBy(f => f.PathIndex).ToList();
        Assert.Equal(new[] { 1, 2 }, doors.Select(f => f.PathIndex).ToArray());
        Assert.Equal(new int?[] { 0, 1 }, doors.Select(f => f.ObjectIndex).ToArray());
        Assert.All(doors, f => Assert.Equal(Disposition.Read, f.Disposition));
        Assert.Equal(3, fates.Count);                                             // every path fated once
    }

    [Fact]
    public void AFillPaintedBeforeTheWallIsCoveredByIt()
    {
        var (g, fates) = WallFixture.Read([Knockout(2700, 1000), Wall()]);
        Assert.Single(g.Walls);
        Assert.Empty(g.Doorways);
        Assert.Equal(PathReason.PaperFill, Assert.Single(fates, f => f.PathIndex == 0).Reason);
    }

    [Theory]
    [InlineData(2700, 1000, 200, 300)]        // does not cross the thickness: a mask on one face
    [InlineData(2700, 300, -20, T + 40)]      // a slot narrower than 18"
    [InlineData(2700, 1000, -1500, 3800)]     // far wider across than the wall: a note's background
    public void AFillThatIsNotADoorwayLeavesTheWallWhole(double x, double width, double y, double across)
    {
        var (g, fates) = WallFixture.Read([Wall(), Knockout(x, width, y, across)]);
        Assert.Single(g.Walls);
        Assert.Equal(L, Length(g.Walls[0]), 1);
        Assert.Empty(g.Doorways);
        Assert.Equal(PathReason.PaperFill, Assert.Single(fates, f => f.PathIndex == 1).Reason);
    }

    [Fact]
    public void ATurnedWallSplitsAtATurnedKnockout()
    {
        var wall = Turn(Wall(), 30);
        var door = Turn(Knockout(3000, 1000), 30);
        var (g, fates) = WallFixture.Read([wall, door]);
        Assert.Equal(2, g.Walls.Count);
        Assert.Equal(new[] { 3000.0, Math.Round(L - 4000) }, g.Walls.Select(w => Math.Round(Length(w))).OrderBy(x => x).ToArray());
        Assert.Equal(PathReason.Doorway, Assert.Single(fates, f => f.PathIndex == 1).Reason);
    }

    [Fact]
    public void AKnockoutAtTheEndShortensTheWall()
    {
        var (g, _) = WallFixture.Read([Wall(), Knockout(L - 800, 2000)]);        // overhangs the end
        var pier = Assert.Single(g.Walls);
        Assert.Equal(L - 800, Length(pier), 1);
        Assert.Equal(800, Assert.Single(g.Doorways).LengthMm, 1);
    }

    [Fact]
    public void PiersNarrowerThanAPanelAreNotCarried()
    {
        // 400 at the start and 600 at the end are piers; the 200 between the two openings is not.
        var piers = GeometryFilterService.PiersBetween([(1, 400, 1400), (2, 1600, 2600)], 3200);
        Assert.Equal(new[] { (0.0, 400.0), (2600.0, 3200.0) }, piers.Select(p => (Math.Round(p.T0), Math.Round(p.T1))).ToArray());
    }

    private static double Length(WallPanel w) => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));

    private static RawSubpath Turn(RawSubpath p, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return p with { Points = p.Points.Select(q => (q.X * c - q.Y * s + 5000, q.X * s + q.Y * c + 5000)).ToList() };
    }
}
