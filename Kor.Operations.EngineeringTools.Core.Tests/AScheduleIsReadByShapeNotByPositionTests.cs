#nullable enable
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Two ways a schedule reader silently reads the wrong thing, both met while building this one.
/// </summary>
/// <remarks>
/// Neither of these is about extraction. The text came out of every one of these drawings perfectly.
/// Both are about a reader assuming the shape of the table in front of it.
///
/// WHAT THIS COVERS: that a size is found wherever the schedule puts it in the row, so a practice
/// that prints MARK | STRENGTH | SIZE reads the same as one that prints MARK | SIZE | STRENGTH; and
/// that a row is attributed to the table it is under rather than the heading that happens to be
/// nearest, including the case where another schedule's heading is closer.
///
/// WHAT IT DOES NOT COVER: it does not open a PDF, so it says nothing about whether the words come
/// off a page in the right order or with the right coordinates. It does not check the reinforcing
/// text, which is still joined across cells and comes out ragged. And it cannot catch a table whose
/// mark column is not a column — a schedule laid out horizontally would defeat the grouping
/// entirely and read as nothing.
/// </remarks>
public sealed class AScheduleIsReadByShapeNotByPositionTests
{
    private const double Tol = 0.6;

    // ── the size must be found wherever the row puts it ──────────────────────

    /// <summary>
    /// 31130 prints MARK | STRENGTH | SIZE. Reading the row left to right takes the strength's "45"
    /// as a length, giving a 45 x 610 column, which is then rejected as implausible — so the row
    /// vanishes and the schedule reads as the neighbouring table instead.
    /// </summary>
    [Fact]
    public void AStrengthBeforeTheSizeIsNotMistakenForADimension()
    {
        var mm = PrintedLength.TryFindSizeMm("45 MPa 12\" x 24\" 20-30M VERTS. 15M @ 6\" TIES");

        Assert.NotNull(mm);
        Assert.Equal(304.8, mm![0], Tol);     // 12"
        Assert.Equal(609.6, mm[1], Tol);      // 24"
    }

    /// <summary>31168 prints MARK | SIZE | STRENGTH — the same row, the other way round.</summary>
    [Fact]
    public void ASizeBeforeTheStrengthReadsTheSame()
    {
        var mm = PrintedLength.TryFindSizeMm("16\" x 40\" 45 MPa 400 MPa 10M");

        Assert.NotNull(mm);
        Assert.Equal(406.4, mm![0], Tol);     // 16"
        Assert.Equal(1016.0, mm[1], Tol);     // 40"
    }

    [Fact]
    public void AMetricSizeStillReadsWhereverItSits()
    {
        var mm = PrintedLength.TryFindSizeMm("55 MPa 500 x 900 10M @ 200 TIES");

        Assert.NotNull(mm);
        Assert.Equal(new[] { 500.0, 900.0 }, mm!);
    }

    /// <summary>A number beside MPa and nothing else is not a size.</summary>
    [Fact]
    public void AStrengthAloneIsNotASize()
        => Assert.Null(PrintedLength.TryFindSizeMm("45 MPa 400 MPa"));

    // ── a row belongs to the table it is under ───────────────────────────────

    private static ColumnScheduleReader.ScheduleHeading Column(double x, double y)
        => new(x, y, IsColumn: true, "PARKADE COLUMN SCHEDULE");

    private static ColumnScheduleReader.ScheduleHeading Foundation(double x, double y)
        => new(x, y, IsColumn: false, "FOUNDATION SCHEDULE");

    /// <summary>
    /// 31168's geometry, in PDF points (y-up), taken from its FIRST row — which is the whole point.
    /// </summary>
    /// <remarks>
    /// Its column table runs from Cy 439 down to Cy 214, and the FOUNDATION heading sits at Cy 295 —
    /// INSIDE that span. So the table's lower rows have a foreign heading above them, nearer than
    /// their own, and asking about one of those rows on its own cannot be answered correctly:
    /// TC04 at Cy 214 genuinely is 81pt under the foundation heading and 512pt under its own.
    ///
    /// That is why ownership is settled once per mark column, from its topmost row, and applied to
    /// the whole column. The first row is the only one guaranteed to sit under nothing but its own
    /// heading. Deciding per row kept TC01 and dropped TC02..TC04 — half a table, which is worse
    /// than none because the total still looks like an answer.
    /// </remarks>
    [Fact]
    public void ATableIsAttributedFromItsFirstRowWhereNoForeignHeadingIntervenes()
    {
        var headings = new[] { Column(2459, 726), Foundation(2030, 295) };

        var fromTopRow = ColumnScheduleReader.OwnerOf(x: 2346, y: 439, headings, band: 622);
        Assert.NotNull(fromTopRow);
        Assert.True(fromTopRow!.Value.IsColumn, $"attributed to '{fromTopRow.Value.Title}'");

        // and the reason the top row is the one asked: a lower row of the same table is ambiguous
        var fromBottomRow = ColumnScheduleReader.OwnerOf(x: 2346, y: 214, headings, band: 622);
        Assert.False(fromBottomRow!.Value.IsColumn,
            "a lower row sits under a foreign heading; if this ever starts resolving correctly on "
            + "its own, the per-column rule is no longer what is carrying the result");
    }

    /// <summary>
    /// 31130's geometry: both headings sit on nearly the same baseline, 56 and 53 points above the
    /// row. Vertical distance alone cannot separate them; the row is under one of them.
    /// </summary>
    [Fact]
    public void TwoHeadingsOnOneBaselineAreSeparatedByWhichOneTheRowIsUnder()
    {
        var headings = new[] { Column(2150, 413), Foundation(1400, 410) };

        var owner = ColumnScheduleReader.OwnerOf(x: 1999, y: 357, headings, band: 544);

        Assert.NotNull(owner);
        Assert.True(owner!.Value.IsColumn, $"attributed to '{owner.Value.Title}'");
    }

    /// <summary>A heading BELOW the row is not its heading, whatever the distance.</summary>
    [Fact]
    public void AHeadingUnderneathOwnsNothing()
    {
        var headings = new[] { Column(2000, 100) };

        Assert.Null(ColumnScheduleReader.OwnerOf(x: 2000, y: 500, headings, band: 600));
    }

    /// <summary>A heading in a different column of the sheet owns nothing here.</summary>
    [Fact]
    public void AHeadingOutsideTheBandOwnsNothing()
    {
        var headings = new[] { Column(200, 800) };

        Assert.Null(ColumnScheduleReader.OwnerOf(x: 2400, y: 400, headings, band: 600));
    }
}
