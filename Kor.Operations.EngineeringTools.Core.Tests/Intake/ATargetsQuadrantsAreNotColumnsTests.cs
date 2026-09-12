#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A target's quadrants are not columns (intake step 49). A spot-elevation target is a circle
/// with two diagonally opposite quadrants filled: two filled squares of one size that meet at the
/// centre and nowhere else. Each is column-sized, so 31202's plans read 62 such pairs as 9" x 9"
/// columns, the DXF loop builder walked each pair through its shared corner as one figure-of-eight,
/// and the area formula put its centroid kilometres away (2026-09-12). No two columns share only a
/// corner: a pair of same-size filled shapes a width apart in x AND a depth apart in y is a symbol.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a corner-to-corner pair leaving the columns, kept as quadrants, fated
/// SymbolQuadrant and Discarded; the surviving column's fate re-pointed; a diagonal of three
/// standing (a checkerboard, step 37's case, not a target); two shapes of different sizes at a
/// corner standing; a declared-size column at a corner standing. WHAT IT DOES NOT: the target's
/// circle and text; a real sheet (31202 p16: seen in the crop, the render of the 300 dpi overlay);
/// the six-set run, which the gate measures.
/// </remarks>
public sealed class ATargetsQuadrantsAreNotColumnsTests
{
    private static RawSubpath Quadrant(double x, double y, double side = 229) => FateFixture.Rect(side, side, x, y) with { Color = (0, 0, 0) };

    private static (ExtractedGeometry, List<PathFate>) Read(params RawSubpath[] paths)
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify(paths, fates);
        return (g, fates);
    }

    [Fact]
    public void TwoFilledSquaresCornerToCornerAreASymbolNotTwoColumns()
    {
        var (g, fates) = Read(Quadrant(60000, 30000), Quadrant(60229, 30229), FateFixture.Rect(600, 800, 70000, 30000));
        var column = Assert.Single(g.Columns);
        Assert.Equal(70300, column.X, 1.0);
        Assert.Equal(2, g.SymbolQuadrants.Count);
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.SymbolQuadrant));
        Assert.All(fates.Where(f => f.Reason == PathReason.SymbolQuadrant), f => Assert.Equal(Disposition.Discarded, f.Disposition));
        Assert.Equal(0, Assert.Single(fates, f => f.Reason == PathReason.BecameColumnByShape).ObjectIndex);
    }

    [Fact]
    public void ADiagonalOfThreeIsACheckerboardAndStands()
    {
        var (g, _) = Read(Quadrant(60000, 30000), Quadrant(60229, 30229), Quadrant(60458, 30458));
        Assert.Equal(3, g.Columns.Count);
        Assert.Empty(g.SymbolQuadrants);
    }

    [Fact]
    public void TwoSizesAtACornerAreNotAPair()
    {
        var (g, _) = Read(Quadrant(60000, 30000), Quadrant(60229, 30229, 400));
        Assert.Equal(2, g.Columns.Count);
    }

    [Fact]
    public void ADeclaredColumnAtACornerIsNeverAQuadrant()
    {
        // 1800 x 400 is the fixture schedule's declared size
        var (g, fates) = Read(FateFixture.Rect(1800, 400, 60000, 30000), FateFixture.Rect(1800, 400, 61800, 30400));
        Assert.Equal(2, g.Columns.Count);
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.BecameColumnByDeclaredSize));
    }
}
