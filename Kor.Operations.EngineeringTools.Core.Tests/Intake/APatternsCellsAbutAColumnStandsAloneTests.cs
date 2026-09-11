#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A pattern's cells abut, three and more of a size; a column stands alone (intake step 37). A
/// stippled or hatched wall arrives from Vectorworks as its fill pattern's cells — closed, filled,
/// one cell in size, shoulder to shoulder along the wall — and each is the size of a column. A
/// pattern is many of one thing: three or more shapes of one size, each edge to edge with the next,
/// are its cells, and a shape of the run's width abutting it is the run's last cell, cut short.
/// Two shapes alone are not a pattern: 31138's GC15 is one column drawn as two filled pieces.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a run of three cells end to end leaving no column; three side by side; a
/// cut-short cell of the run's width at its end going with it; TWO cells alone both standing (a
/// column in two pieces); two columns a bay apart both standing; corner-touching shapes standing;
/// a declared-size column beside a run never becoming a cell; the surviving column's fate
/// re-pointed at it and the cells kept on the geometry for the overlay. WHAT IT DOES NOT: the real
/// sheets (31170 A101: 311 → 66 columns, 245 cells; 31138 S2.14: GC15 kept, 2026-09-10) and the
/// six-set run; a rotated pattern; a pattern of two cells; the wall the cells filled.
/// </remarks>
public sealed class APatternsCellsAbutAColumnStandsAloneTests
{
    private static RawSubpath Cell(double x, double y, double w = 900, double h = 1200) => FateFixture.Rect(w, h, x, y) with { Color = (0, 0, 0) };
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);

    private static (ExtractedGeometry, List<PathFate>) Read(params RawSubpath[] paths)
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify(paths, fates);
        return (g, fates);
    }

    [Fact]
    public void ARunOfThreeCellsEndToEndIsNoColumnAtAll()
    {
        var (g, fates) = Read(Cell(60000, 30000), Cell(60000, 31200), Cell(60000, 32400));
        Assert.Empty(g.Columns);
        Assert.Equal(3, g.PatternCells.Count);
        Assert.Equal(3, fates.Count(f => f.Reason == PathReason.PatternCell));
        Assert.All(fates.Where(f => f.Reason == PathReason.PatternCell), f => Assert.Equal(Disposition.Discarded, f.Disposition));
    }

    [Fact]
    public void ThreeCellsSideBySideAreCellsToo()
    {
        var (g, _) = Read(Cell(60000, 30000), Cell(60900, 30000), Cell(61800, 30000));
        Assert.Empty(g.Columns);
    }

    [Fact]
    public void TheCutShortCellAtTheRunsEndGoesWithTheRun()
    {
        var (g, _) = Read(Cell(60000, 30000), Cell(60000, 31200), Cell(60000, 32400), Cell(60000, 33600, 900, 400));   // the last, 400 long where the wall ends
        Assert.Empty(g.Columns);
        Assert.Equal(4, g.PatternCells.Count);
    }

    [Fact]
    public void TwoShapesAloneAreAColumnInTwoPiecesNotAPattern()
    {
        // 31138's GC15 (18" x 49"): an 18 x 41 piece and an 11 x 18 piece end to end where a bearing wall crosses it
        var (g, _) = Read(FateFixture.Rect(457, 1041, 60000, 30000), FateFixture.Rect(279, 457, 60089, 31041));
        Assert.Equal(2, g.Columns.Count);
        Assert.Empty(g.PatternCells);
        // and two identical cells alone are not a pattern either
        var (two, _) = Read(Cell(60000, 30000), Cell(60000, 31200));
        Assert.Equal(2, two.Columns.Count);
    }

    [Fact]
    public void TwoColumnsABayApartBothStand()
    {
        var (g, fates) = Read(Column(60000, 30000), Column(66000, 30000), Column(72000, 30000));
        Assert.Equal(3, g.Columns.Count);
        Assert.Equal(3, fates.Count(f => f.Reason == PathReason.BecameColumnByShape));
    }

    [Fact]
    public void TouchingAtACornerIsNotAbutting()
    {
        var (g, _) = Read(Cell(60000, 30000), Cell(60900, 31200), Cell(61800, 32400));           // a diagonal of three sharing points, not edges
        Assert.Equal(3, g.Columns.Count);
    }

    [Fact]
    public void ADeclaredColumnBesideARunIsNeverACell()
    {
        // 1800 x 400 is the fixture schedule's declared size: three of them end to end are three declared columns,
        // and a declared column standing at the end of a REAL run of cells is not the run's end cell either (audit F25)
        var (g, fates) = Read(FateFixture.Rect(1800, 400, 60000, 30000), FateFixture.Rect(1800, 400, 60000, 30400), FateFixture.Rect(1800, 400, 60000, 30800),
                              Cell(70000, 30000), Cell(70000, 31200), Cell(70000, 32400), FateFixture.Rect(1800, 400, 70000, 33600));
        Assert.Equal(4, g.Columns.Count);
        Assert.Equal(4, fates.Count(f => f.Reason == PathReason.BecameColumnByDeclaredSize));
        Assert.Equal(3, g.PatternCells.Count);
    }

    [Fact]
    public void TheSurvivingColumnsFatePointsAtIt()
    {
        var (g, fates) = Read(Cell(60000, 30000), Cell(60000, 31200), Cell(60000, 32400), Column(63000, 30000));
        var column = Assert.Single(g.Columns);
        var fate = Assert.Single(fates, f => f.Reason == PathReason.BecameColumnByShape);
        Assert.Equal(0, fate.ObjectIndex);
        Assert.InRange(column.X, 63000, 63600); Assert.InRange(column.Y, 30000, 30800);      // the column at (63000, 30000), not a cell's place
    }
}
