#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A wall is what its clip lets through (intake step 14). Revit's export draws a core face as one
/// fill the length of the face and clips it to its piers with the path drawn just before it; the
/// fill shows only through the clip, so the wall is those pieces.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a clip of three pieces on the wall, drawn immediately before it, giving three
/// piers of the pieces' lengths, the clip's fate (ClipOfWall, read, first pier) and the wall's
/// (BecameWall, first pier); a clip that is not immediately before the wall, and one with a piece
/// off the wall — the wall whole and the clip NoInk; a clip and a doorway together; a clip whose
/// pieces are all narrower than a panel, leaving the wall whole. WHAT IT DOES NOT: a real sheet
/// (FiveStickFilesTests banks 31168's counts, and p22's core faces are the case); a clip that ends
/// (Q) before the wall is painted, which PdfPig does not expose — the pieces-on-the-wall test is
/// the guard, not the graphics state; a clip that is a curve.
/// </remarks>
public sealed class AWallIsWhatItsClipLetsThroughTests
{
    private const double L = 328 * 25.4, T = 30 * 25.4;

    private static RawSubpath Wall(int ordinal) => WallFixture.Rect(thicknessIn: 30, lengthIn: 328) with { PathOrdinal = ordinal };
    private static RawSubpath Clip(double xMm, double lengthMm, int ordinal, double y = 0, double across = T)
        => FateFixture.Rect(lengthMm, across, xMm, y) with { IsFilled = false, IsStroked = false, IsClipping = true, PathOrdinal = ordinal };

    [Fact]
    public void AClipOfThreePiecesDrawnBeforeTheWallMakesThreePiers()
    {
        var (g, fates) = WallFixture.Read([Clip(0, 1870, 4), Clip(3100, 2995, 4), Clip(7287, 1044, 4), Wall(5)]);
        Assert.Equal(new[] { 1870.0, 2995.0, 1044.0 }, g.Walls.Select(w => Math.Round(Length(w))).ToArray());
        Assert.All(g.Walls, w => Assert.Equal(T, w.ThicknessMm, 1));
        Assert.Empty(g.Doorways);
        var clips = fates.Where(f => f.Reason == PathReason.ClipOfWall).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, clips.Select(f => f.PathIndex).ToArray());
        Assert.All(clips, f => { Assert.Equal(Disposition.Read, f.Disposition); Assert.Equal(0, f.ObjectIndex); });
        var wall = Assert.Single(fates, f => f.PathIndex == 3);
        Assert.Equal(PathReason.BecameWall, wall.Reason);
        Assert.Equal(0, wall.ObjectIndex);
        Assert.Equal(4, fates.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, fates.Select(f => f.PathIndex).ToArray());       // path order kept
    }

    [Fact]
    public void AClipNotDrawnImmediatelyBeforeTheWallIsNotItsClip()
    {
        var (g, fates) = WallFixture.Read([Clip(0, 1870, 4), Wall(9)]);
        Assert.Equal(L, Length(Assert.Single(g.Walls)), 1);
        Assert.Equal(PathReason.NoInk, Assert.Single(fates, f => f.PathIndex == 0).Reason);
    }

    [Fact]
    public void AClipWithAPieceOffTheWallLeavesTheWallWhole()
    {
        // the second piece sits 3 m away: the clip belongs to something else, and the graphics
        // state that ended it is invisible to the reader — so the wall is taken as drawn
        var (g, fates) = WallFixture.Read([Clip(0, 1870, 4), Clip(0, 1870, 4, y: 3000), Wall(5)]);
        Assert.Equal(L, Length(Assert.Single(g.Walls)), 1);
        Assert.All(fates.Where(f => f.PathIndex < 2), f => Assert.Equal(PathReason.NoInk, f.Reason));
    }

    [Fact]
    public void AClipAndADoorwayTogether()
    {
        var door = FateFixture.Rect(1000, T + 40, 500, -20) with { Color = (0xFF, 0xFF, 0xFF), PathOrdinal = 6 };
        var (g, fates) = WallFixture.Read([Clip(0, 4000, 4), Clip(5000, 3000, 4), Wall(5), door]);
        // the first piece 0..4000 less the door 500..1500 -> 0..500 and 1500..4000; the second whole
        Assert.Equal(new[] { 500.0, 2500.0, 3000.0 }, g.Walls.Select(w => Math.Round(Length(w))).ToArray());
        Assert.Single(g.Doorways);
        Assert.Equal(PathReason.Doorway, Assert.Single(fates, f => f.PathIndex == 3).Reason);
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.ClipOfWall));
    }

    [Fact]
    public void PiecesNarrowerThanAPanelLeaveTheWallWhole()
    {
        var (g, fates) = WallFixture.Read([Clip(0, 200, 4), Clip(4000, 250, 4), Wall(5)]);
        Assert.Equal(L, Length(Assert.Single(g.Walls)), 1);
        Assert.All(fates.Where(f => f.PathIndex < 2), f => Assert.Equal(PathReason.NoInk, f.Reason));
    }

    private static double Length(WallPanel w) => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
}
