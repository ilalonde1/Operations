#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A RECTANGLE NO SCHEDULE DECLARES, LONGER THAN 24 IN AND TWICE AS LONG AS WIDE, IS A WALL PIER (intake step 99,
/// 2026-09-16). The engineers' review from the corpus: 622 of our unmatched columns over 37 sets stand on a wall
/// she modelled, 483 of the 554 with a section 24 in or longer on their long side - her ruling W1 drawn as a
/// filled rectangle the column reader took whole. The set's column schedule is the other half: a size it
/// declares is a column whatever its length (31130's 14 x 36, 31098's 12 x 24).
/// WHAT THIS COVERS: an undeclared 14 x 40 standing down to a wall panel of its length and thickness, along its
/// long axis, the column flagged and not counted; the same rectangle declared by the schedule staying a column; a
/// 24 x 24 (not longer than 24 in) and an 18 x 24 (not twice as long as wide) staying columns whatever the
/// schedule says; a tendon anchor never becoming a pier; both rows read from the options. WHAT IT DOES NOT: the
/// schedule reading itself (PlanAgreesWithItsSchedule's tests); the DXF written (the exporter skips flagged
/// columns as it skips anchors); the real sheets (the six-set gate and the yardstick). A same-class fault it
/// would not catch: a pier the set schedules as a column in one sheet's schedule and not another's.
/// </summary>
public sealed class ARectangleNoScheduleDeclaresIsAWallPierTests
{
    private static ExtractedGeometry With(params (double X, double Y, double W, double D)[] columns)
    {
        var g = new ExtractedGeometry();
        foreach (var (x, y, w, d) in columns)
        {
            g.Columns.Add((x, y)); g.ColumnSizes.Add((w, d)); g.ColumnColors.Add((0, 0, 0)); g.ColumnIsAnnotation.Add(false);
        }
        return g;
    }

    [Fact]
    public void AnUndeclaredLongRectangleIsAWallOfItsLengthAndThickness()
    {
        var g = With((10000, 20000, 355.6, 1016));            // 14 x 40, standing along Y
        int stood = WallPiers.StandDownColumns(g, sizeIsDeclared: [false]);
        Assert.Equal(1, stood);
        Assert.True(Assert.Single(g.ColumnIsWallPier));
        var wall = Assert.Single(g.Walls);
        Assert.Equal(355.6, wall.ThicknessMm, 3);
        Assert.Equal((10000, 20000 - 508), wall.Start);
        Assert.Equal((10000, 20000 + 508), wall.End);
        Assert.Single(g.WallColors); Assert.Single(g.WallIsAnnotation);
    }

    [Fact]
    public void TheSameRectangleDeclaredByTheScheduleStaysAColumn()
    {
        var g = With((10000, 20000, 355.6, 1016));
        Assert.Equal(0, WallPiers.StandDownColumns(g, sizeIsDeclared: [true]));
        Assert.False(Assert.Single(g.ColumnIsWallPier));
        Assert.Empty(g.Walls);
    }

    [Fact]
    public void ASquareAndAStockyRectangleStayColumnsWhateverTheScheduleSays()
    {
        var g = With((0, 0, 609.6, 609.6), (5000, 0, 457.2, 609.6), (10000, 0, 304.8, 762));   // 24 x 24, 18 x 24, 12 x 30
        // no schedule at all: judged by size alone - only the 12 x 30 (30 > 24 and 30 >= 2 x 12 ... 762 >= 609.6, aspect 2.5) is a pier
        Assert.Equal(1, WallPiers.StandDownColumns(g, sizeIsDeclared: null));
        Assert.Equal([false, false, true], g.ColumnIsWallPier);
    }

    [Fact]
    public void ATendonAnchorIsNeverAPierAndTheRowsAreRead()
    {
        var g = With((0, 0, 355.6, 1016));
        g.ColumnIsTendonAnchor.Add(true);
        Assert.Equal(0, WallPiers.StandDownColumns(g, sizeIsDeclared: null));

        // the rows: a firm whose piers start at 36 in leaves a 14 x 30 a column
        var h = With((0, 0, 355.6, 762));
        Assert.Equal(1, WallPiers.StandDownColumns(h, null));
        Assert.Equal(0, WallPiers.StandDownColumns(With((0, 0, 355.6, 762)), null, pierMinLongSideMm: 914.4));
        Assert.Equal(0, WallPiers.StandDownColumns(With((0, 0, 355.6, 762)), null, pierMinAspect: 3.0));
        Assert.Equal(WallPiers.DefaultPierMinLongSideMm, PdfIntakeOptions.Default.PierMinLongSideMm);
        Assert.Equal(WallPiers.DefaultPierMinAspect, PdfIntakeOptions.Default.PierMinAspect);
    }
}
