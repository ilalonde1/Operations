#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A schedule's rows are bounded by the border the schedule draws, not by a band around its heading.
/// </summary>
/// <remarks>
/// The class of fault, in one sentence: a reader that bounds a table by a distance from its heading
/// lets any token sharing a y-bucket inside that distance become the leftmost "mark" of a row whose
/// cells are then read from a different table, or from the second line of a wrapped cell. Both
/// shapes shipped: 31130's footings read 48 cy under the marks 15M and 20M out of the shear wall
/// schedule beside the foundation one, and 31065 p14 read 8-35M and BOT. as column marks. Every
/// earlier fix moved the distance (260pt, nearest heading, ±18% of the page) and was right on some
/// sheets and wrong on others.
///
/// WHAT THIS COVERS: that a neighbouring table's cell cannot join a row; that a wrapped cell's
/// second line stays in its row; that the bottom is found through side verticals drawn per row;
/// that a separator stopping short of the border still separates; that a mark wrapped over two
/// lines is one mark; that a rule under a title with no table under it is not a border and the band
/// still reads; that pieces of one rule merge and a rule at nearly the same y does not swallow them;
/// and that every row says which extent read it.
///
/// WHAT IT DOES NOT COVER: it does not open a PDF, so it says nothing about how PdfPig delivers a
/// table's linework — that is what tools/Measure-StickFileSchedules.ps1 measures on the five stick
/// files. It does not cover a table with no rules at all, which the band reads as before. And a
/// fault it would NOT catch: two tables that share a border side by side with no vertical between
/// them — their rows would read as one table's, correctly bounded and wrongly attributed.
/// </remarks>
public sealed class ATableIsBoundedByItsOwnBorderTests
{
    private const double W = 3024, H = 2160;

    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 8, y - 3, x + 8, y + 3);

    private static GP HRule(double x0, double x1, double y) => new(
        new List<(double X, double Y)> { (x0, y), (x1, y) },
        IsClosed: false, IsFilled: false, IsStroked: true,
        MinX: Math.Min(x0, x1), MinY: y, MaxX: Math.Max(x0, x1), MaxY: y);

    private static GP VRule(double x, double y0, double y1) => new(
        new List<(double X, double Y)> { (x, y0), (x, y1) },
        IsClosed: false, IsFilled: false, IsStroked: true,
        MinX: x, MinY: Math.Min(y0, y1), MaxX: x, MaxY: Math.Max(y0, y1));

    /// <summary>A ruled table: outer box, row separators, column rules. y-up, so top &gt; bottom.</summary>
    private static IEnumerable<GP> Table(double x0, double x1, double top, double bottom, double[] rowYs, double[] colXs)
    {
        yield return HRule(x0, x1, top);
        yield return HRule(x0, x1, bottom);
        foreach (double y in rowYs) yield return HRule(x0, x1, y);
        yield return VRule(x0, bottom, top);
        yield return VRule(x1, bottom, top);
        foreach (double x in colXs) yield return VRule(x, bottom, top);
    }

    private static PC Page(IEnumerable<TT> words, IEnumerable<GP> paths) => new(1, W, H, words.ToList(), paths.ToList());

    /// <summary>A footing size cell as 31130 prints it, tokens 12pt apart from x.</summary>
    private static IEnumerable<TT> ImperialFootingSize(double x, double y, string ft, string deep)
    {
        string[] toks = [ft + "'", "-", "0\"", "x", ft + "'", "-", "0\"", "x", deep + "\"", "DEEP"];
        for (int i = 0; i < toks.Length; i++) yield return Tok(toks[i], x + 12 * i, y);
    }

    // ── a neighbouring table's cell cannot join a row ────────────────────────────────────────────

    /// <summary>
    /// 31130 p11, reduced. The FOUNDATION SCHEDULE is bordered at x 1665–2061; the SHEAR WALL
    /// SCHEDULE sits at 1278–1602 to its left, inside the ±544pt band the footing heading casts.
    /// SWA's reinforcing cell is two lines with the mark centred between them, and one line shares
    /// F1's baseline, so "15M" is the leftmost token on it. The band route read the row as mark
    /// 15M, size 4'-0" x 4'-0" x 26", and dropped F1 and F2.
    /// </summary>
    [Fact]
    public void ANeighbouringTablesWrappedCellIsNotAMark()
    {
        var words = new List<TT>
        {
            Tok("FOUNDATION", 1720, 700), Tok("SCHEDULE", 1850, 700),
            Tok("TYPE", 1700, 680), Tok("SIZE", 1820, 680), Tok("REINFORCING", 1980, 680),
            Tok("F1", 1700, 659), Tok("7-20M03.6", 1930, 659), Tok("EACH", 1965, 659), Tok("WAY", 1995, 659),
            Tok("F2", 1700, 637), Tok("11-20M04.6", 1930, 637), Tok("EACH", 1965, 637), Tok("WAY", 1995, 637),

            Tok("SHEAR", 1300, 720), Tok("WALL", 1360, 720), Tok("SCHEDULE", 1440, 720),
            Tok("MARK", 1300, 700), Tok("THK", 1365, 700), Tok("STR", 1435, 700), Tok("REINFORCING", 1540, 700),
            Tok("SWA", 1300, 665), Tok("12\"", 1365, 665), Tok("35", 1425, 665), Tok("MPa", 1450, 665),
            Tok("15M", 1500, 675), Tok("@", 1520, 675), Tok("14\"", 1540, 675), Tok("VERT.", 1570, 675),
            Tok("15M", 1500, 659), Tok("@", 1520, 659), Tok("14\"", 1540, 659), Tok("HORIZ.", 1570, 659),
        };
        words.AddRange(ImperialFootingSize(1760, 659, "4", "26"));
        words.AddRange(ImperialFootingSize(1760, 637, "5", "32"));

        var paths = Table(1665, 2061, 690, 626, [670, 648], [1740, 1900])
            .Concat(Table(1278, 1602, 710, 640, [690], [1330, 1400, 1470]));

        var (types, _) = FootingScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "F1", "F2" }, types.Select(t => t.Mark).ToArray());
        Assert.All(types, t => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, t.Route));
        Assert.Equal(4 * 12 * 25.4, types[0].LengthMm, 0.6);
        Assert.Equal(26 * 25.4, types[0].DepthMm, 0.6);
    }

    // ── a wrapped cell's second line stays in its row ───────────────────────────────────────────

    /// <summary>
    /// 31065 p14, reduced. PC4's REINFORCING cell is "8-25M VERT'S." over "10M @ 150 TIES" with the
    /// mark and size centred between the lines. Each line was its own y-bucket, "8-25M" was the
    /// leftmost token in its bucket, and it became a mark whose cells held PC4's size. Placed
    /// first, it was also the anchor, so the real marks under it were dropped for being in a
    /// different column.
    /// </summary>
    [Fact]
    public void AWrappedCellsSecondLineStaysInItsRow()
    {
        var words = new List<TT>
        {
            Tok("PARKADE", 1500, 700), Tok("COLUMN", 1580, 700), Tok("SCHEDULE", 1670, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680), Tok("STRENGTH", 1680, 680), Tok("REINFORCING", 1830, 680),

            Tok("PC4", 1520, 650), Tok("300", 1590, 650), Tok("x", 1610, 650), Tok("600", 1630, 650), Tok("35", 1680, 650), Tok("MPa", 1705, 650),
            Tok("8-25M", 1800, 655), Tok("VERT'S.", 1840, 655),
            Tok("10M", 1800, 645), Tok("@", 1825, 645), Tok("150", 1845, 645), Tok("TIES", 1875, 645),

            Tok("PC5", 1520, 622), Tok("700", 1590, 622), Tok("x", 1610, 622), Tok("700", 1630, 622), Tok("55", 1680, 622), Tok("MPa", 1705, 622),
            Tok("12-35M", 1800, 622), Tok("VERT'S.", 1840, 622),
            Tok("PC6", 1520, 594), Tok("500", 1590, 594), Tok("x", 1610, 594), Tok("900", 1630, 594), Tok("55", 1680, 594), Tok("MPa", 1705, 594),
        };
        var paths = Table(1495, 1990, 690, 580, [668, 636, 608], [1560, 1660, 1760]);

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "PC4", "PC5", "PC6" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, r.Route));
        var pc4 = rows.Single(r => r.Mark == "PC4");
        Assert.Equal(300, pc4.WidthMm, 0.6);
        Assert.Equal(600, pc4.DepthMm, 0.6);
        // both lines of the wrapped cell are in the row's text: the verticals from the line above
        // the mark and the ties from the line below it
        Assert.StartsWith("8-25M VERT", pc4.Reinforcing);
        Assert.Equal("10M @ 150 TIES", pc4.Ties);
    }

    // ── the bottom is chained through verticals drawn per row ───────────────────────────────────

    /// <summary>
    /// A CAD export draws a table's sides as a piece per row. Taking only the verticals that meet the
    /// top rule bounded 31130's FOUNDATION table above its last row when it was first measured, and
    /// a box that is right but short loses exactly the rows nobody looks at.
    /// </summary>
    [Fact]
    public void TheBottomIsChainedThroughSideVerticalsDrawnPerRow()
    {
        double[] edges = [690, 670, 648, 626, 604, 582];
        var words = new List<TT> { Tok("FOUNDATION", 1720, 700), Tok("SCHEDULE", 1850, 700), Tok("TYPE", 1700, 680), Tok("SIZE", 1820, 680) };
        string[] marks = ["F1", "F2", "F3", "F4"];
        for (int i = 0; i < marks.Length; i++)
        {
            double y = (edges[i + 1] + edges[i + 2]) / 2;
            words.Add(Tok(marks[i], 1700, y));
            words.AddRange(ImperialFootingSize(1760, y, (4 + i).ToString(), "26"));
        }

        var paths = new List<GP>();
        foreach (double y in edges) paths.Add(HRule(1665, 2061, y));
        for (int i = 1; i < edges.Length; i++)
        {
            // each row's own side pieces, meeting the row above only
            paths.Add(VRule(1665, edges[i], edges[i - 1]));
            paths.Add(VRule(2061, edges[i], edges[i - 1]));
            paths.Add(VRule(1740, edges[i], edges[i - 1]));
        }

        var (types, _) = FootingScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(marks, types.Select(t => t.Mark).ToArray());
        Assert.All(types, t => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, t.Route));
    }

    // ── a separator stopping short of the border still separates ────────────────────────────────

    /// <summary>31138 p9 draws the rule between PC4 and PC5 43pt short of the table's left edge.</summary>
    [Fact]
    public void ASeparatorStoppingShortOfTheBorderStillSeparatesRows()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("PC4", 1520, 655), Tok("18\"", 1590, 655), Tok("x", 1610, 655), Tok("42\"", 1630, 655), Tok("65", 1680, 655), Tok("MPa", 1705, 655),
            Tok("PC5", 1520, 625), Tok("22\"", 1590, 625), Tok("x", 1610, 625), Tok("36\"", 1630, 625), Tok("65", 1680, 625), Tok("MPa", 1705, 625),
        };
        var paths = Table(1495, 1990, 690, 610, [668], [1570, 1660]).ToList();
        paths.Add(HRule(1495 + 43, 1990, 640));   // the PC4/PC5 separator, short of the left edge

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "PC4", "PC5" }, rows.Select(r => r.Mark).ToArray());
        Assert.Equal(22 * 25.4, rows.Single(r => r.Mark == "PC5").WidthMm, 0.6);
    }

    /// <summary>
    /// And when the separator is missing across the whole width, the mark cell holds two marks on
    /// two lines, and the row is split between them rather than the second mark being lost.
    /// </summary>
    [Fact]
    public void TwoMarksInOneCellAreTwoRows()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("PC4", 1520, 655), Tok("18\"", 1590, 655), Tok("x", 1610, 655), Tok("42\"", 1630, 655),
            Tok("PC5", 1520, 625), Tok("22\"", 1590, 625), Tok("x", 1610, 625), Tok("36\"", 1630, 625),
        };
        var paths = Table(1495, 1990, 690, 610, [668], [1570, 1660]);

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "PC4", "PC5" }, rows.Select(r => r.Mark).ToArray());
        Assert.Equal(18 * 25.4, rows.Single(r => r.Mark == "PC4").WidthMm, 0.6);
        Assert.Equal(22 * 25.4, rows.Single(r => r.Mark == "PC5").WidthMm, 0.6);
    }

    // ── a mark wrapped over two lines is one mark ───────────────────────────────────────────────

    /// <summary>31168 S2.02 wraps GC11-C as "GC11-" over "C" in a narrow mark column.</summary>
    [Fact]
    public void AMarkWrappedOverTwoLinesIsOneMark()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("GC11-", 1520, 660), Tok("C", 1520, 650),
            Tok("24\"", 1590, 655), Tok("x", 1610, 655), Tok("24\"", 1630, 655), Tok("65", 1680, 655), Tok("MPa", 1705, 655),
            Tok("PC01", 1520, 625), Tok("14\"", 1590, 625), Tok("x", 1610, 625), Tok("36\"", 1630, 625), Tok("65", 1680, 625), Tok("MPa", 1705, 625),
        };
        var paths = Table(1495, 1990, 690, 610, [668, 640], [1570, 1660]);

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "GC11-C", "PC01" }, rows.Select(r => r.Mark).ToArray());
    }

    // ── a band no column rule crosses is a note, not a row ──────────────────────────────────────

    /// <summary>
    /// 31130 and 31065 box their NOTES under the last row of a schedule, inside the border, as one
    /// full-width cell. Its first token was read as a mark — "3." with a thickness of 48" — by the
    /// flat shear-wall reader, whose rows state one length and so accept the weaker filter.
    /// </summary>
    [Fact]
    public void ABandNoColumnRuleCrossesIsANoteNotARow()
    {
        var words = new List<TT>
        {
            Tok("SHEAR", 1300, 720), Tok("WALL", 1360, 720), Tok("SCHEDULE", 1440, 720),
            Tok("MARK", 1300, 700), Tok("THK", 1365, 700), Tok("STR", 1435, 700),
            Tok("SWA", 1300, 675), Tok("12\"", 1365, 675), Tok("35", 1425, 675), Tok("MPa", 1450, 675),
            Tok("SWB", 1300, 655), Tok("16\"", 1365, 655), Tok("45", 1425, 655), Tok("MPa", 1450, 655),
            Tok("3.", 1300, 630), Tok("LAP", 1330, 630), Tok("BARS", 1365, 630), Tok("48\"", 1400, 630), Tok("MIN.", 1430, 630),
        };
        // column rules run through the data rows only; the notes band under them is one merged cell
        var paths = new List<GP>
        {
            HRule(1278, 1602, 710), HRule(1278, 1602, 690), HRule(1278, 1602, 665), HRule(1278, 1602, 645), HRule(1278, 1602, 615),
            VRule(1278, 615, 710), VRule(1602, 615, 710),
            VRule(1330, 645, 710), VRule(1400, 645, 710),
        };

        var rows = MarkRowScheduleReader.ReadSchedule(Page(words, paths), MarkRowScheduleReader.ShearWallDefaults());

        Assert.Equal(new[] { "SWA", "SWB" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, r.Route));
    }

    /// <summary>
    /// And within a cell, tokens on one line stay on one line however their centres drift: bucketing
    /// Cy to 4pt put the x of "16\" x 48\"" on another line, the size never parsed, and PC4 was lost.
    /// </summary>
    [Fact]
    public void GlyphsOnOneLineReadAsOneLineWhateverTheirSubPointDrift()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("PC4", 1520, 655), Tok("16\"", 1590, 654.1), Tok("x", 1610, 655.9), Tok("48\"", 1630, 654.2),
        };
        var paths = Table(1495, 1990, 690, 640, [668], [1570, 1660]);

        var row = Assert.Single(ColumnScheduleReader.ReadSchedule(Page(words, paths)));

        Assert.Equal("PC4", row.Mark);
        Assert.Equal(16 * 25.4, row.WidthMm, 0.6);
        Assert.Equal(48 * 25.4, row.DepthMm, 0.6);
    }

    // ── on a ruled sheet, a heading with no border is a sentence ────────────────────────────────

    /// <summary>
    /// 31065 p15's column notes read "4. IF NOTED IN THE COLUMN SCHEDULE", which the heading finder
    /// takes for a heading. Its band read the strip footings SF1 and SF2 below it as columns of
    /// 550 x 300, and they surfaced as unplaced column marks in the sheet's self-check. On a sheet
    /// whose real table draws a border, a target heading with none under it is not a table.
    /// </summary>
    [Fact]
    public void OnARuledSheetAHeadingWithNoBorderIsASentenceNotATable()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("PC1", 1520, 655), Tok("18\"", 1590, 655), Tok("x", 1610, 655), Tok("42\"", 1630, 655), Tok("65", 1680, 655), Tok("MPa", 1705, 655),

            // the notes, a hundred points below the table, with the footing schedule's rows under them
            Tok("4.", 1500, 500), Tok("IF", 1520, 500), Tok("NOTED", 1545, 500), Tok("IN", 1580, 500), Tok("THE", 1600, 500), Tok("COLUMN", 1630, 500), Tok("SCHEDULE", 1690, 500),
            Tok("SF1", 1520, 450), Tok("550", 1590, 450), Tok("x", 1610, 450), Tok("300", 1630, 450), Tok("DEEP", 1660, 450),
            Tok("SF2", 1520, 430), Tok("900", 1590, 430), Tok("x", 1610, 430), Tok("600", 1630, 430), Tok("DEEP", 1660, 430),
        };
        var paths = Table(1495, 1990, 690, 640, [668], [1570, 1660]);

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "PC1" }, rows.Select(r => r.Mark).ToArray());
    }

    // ── a rule under a title with no table under it is not a border ─────────────────────────────

    /// <summary>
    /// 31202 titles its column schedules and underlines them; the table below is drawn cell by cell
    /// and has no mark column. An underline is not a table, so the band reads as it did before and
    /// says so, rather than a zero-height box reading nothing.
    /// </summary>
    [Fact]
    public void ATitleUnderlineIsNotATableAndTheBandStillReads()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("PC1", 1520, 655), Tok("18\"", 1590, 655), Tok("x", 1610, 655), Tok("42\"", 1630, 655), Tok("65", 1680, 655), Tok("MPa", 1705, 655),
            Tok("PC2", 1520, 625), Tok("22\"", 1590, 625), Tok("x", 1610, 625), Tok("36\"", 1630, 625), Tok("65", 1680, 625), Tok("MPa", 1705, 625),
        };
        var paths = new[] { HRule(1500, 1700, 692) };

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "PC1", "PC2" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleColumn, r.Route));
    }

    // ── pieces of one rule merge; a rule at nearly the same y does not swallow them ─────────────

    /// <summary>
    /// A CAD export draws one table line as a piece per cell. The first merge compared each piece
    /// only with the previous merged rule and let a piece be absorbed by a rule at nearly the same
    /// y whose span it never touched, and the PC6/PC7 separator on 31130 p12 vanished.
    /// </summary>
    [Fact]
    public void CollinearPiecesAreOneRuleAndANeighbourAtTheSameYDoesNotSwallowThem()
    {
        var pieces = new[]
        {
            new ScheduleTableBorder.Rule(2444.4, 2606.6, 1902.84),   // another table, same y within 0.6
            new ScheduleTableBorder.Rule(2239.7, 2384.0, 1903.20),
            new ScheduleTableBorder.Rule(2152.0, 2239.7, 1903.20),
            new ScheduleTableBorder.Rule(2088.5, 2152.0, 1903.20),
            new ScheduleTableBorder.Rule(2033.6, 2088.5, 1903.20),
            new ScheduleTableBorder.Rule(1984.8, 2033.6, 1903.20),
        };

        var merged = ScheduleTableBorder.Merge(pieces).OrderBy(r => r.Lo).ToList();

        Assert.Equal(2, merged.Count);
        Assert.Equal(1984.8, merged[0].Lo, 0.01);
        Assert.Equal(2384.0, merged[0].Hi, 0.01);
        Assert.Equal(2444.4, merged[1].Lo, 0.01);
    }

    /// <summary>A rule drawn as a closed thin rectangle yields its long edges, and both merge to one.</summary>
    [Fact]
    public void AThinFilledRectangleIsARule()
    {
        var page = Page([], [new GP(
            new List<(double X, double Y)> { (100, 500), (400, 500), (400, 500.4), (100, 500.4) },
            IsClosed: true, IsFilled: true, IsStroked: false, MinX: 100, MinY: 500, MaxX: 400, MaxY: 500.4)]);

        var rules = ScheduleTableBorder.RulesOn(page);

        var rule = Assert.Single(rules.Horizontal);
        Assert.Equal(100, rule.Lo, 0.01);
        Assert.Equal(400, rule.Hi, 0.01);
        Assert.Empty(rules.Vertical);
    }

    // ── every reach around a table is measured in the title's own text height ──────────────────

    /// <summary>
    /// The distance from a title to its table is a line or two of text on any sheet, so the reach
    /// is a multiple of the title's height and not a number of points — and not a rule key either:
    /// a row in KorStandards stating "45 points" would be a limit fitted to one sheet, which is
    /// the class of fault the border exists to end. A title four times taller than KOR's, with its
    /// table proportionally further below, reads the same.
    /// </summary>
    [Fact]
    public void TheReachToATableScalesWithItsTitleAndIsNotARuleKey()
    {
        Assert.DoesNotContain(MarkRowScheduleReader.FootingDefaults().SettingKeys, k => k.Contains("border", StringComparison.Ordinal));

        // a title of the given height, with the table's top rule `lines` title-heights below its bottom
        static PC Big(double titleHeight, double lines)
        {
            double titleBottom = 700 - titleHeight / 2;
            double top = titleBottom - lines * titleHeight;
            var words = new List<TT>
            {
                new("COLUMN", 1520, 700, 1500, titleBottom, 1540, titleBottom + titleHeight),
                new("SCHEDULE", 1600, 700, 1560, titleBottom, 1640, titleBottom + titleHeight),
                Tok("MARK", 1520, top - 10), Tok("SIZE", 1600, top - 10),
                Tok("PC1", 1520, top - 35), Tok("18\"", 1590, top - 35), Tok("x", 1610, top - 35), Tok("42\"", 1630, top - 35),
            };
            return Page(words, Table(1495, 1990, top, top - 50, [top - 22], [1570, 1660]));
        }

        // a 6pt title with its table three lines below: found
        var small = ColumnScheduleReader.ReadSchedule(Big(6, 3));
        Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, Assert.Single(small).Route);

        // a 24pt title with its table three of ITS lines below — 72pt, far past any point constant: found
        var big = ColumnScheduleReader.ReadSchedule(Big(24, 3));
        Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, Assert.Single(big).Route);

        // and a 6pt title with a table six lines below it is not that table's title: the band reads
        var far = ColumnScheduleReader.ReadSchedule(Big(6, 6));
        Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleColumn, Assert.Single(far).Route);
    }

    // ── a title's underline is not its table's top rule ────────────────────────────────────────

    /// <summary>
    /// 31202 underlines every schedule title inside the table's own title row. The underline is a
    /// rule under the title spanning its left edge, so it was taken for the top, no vertical came
    /// within reach of it, and the table was not found. A rule that begins and ends within the
    /// title's own extent draws under the words; a table's rule extends beyond them.
    /// </summary>
    [Fact]
    public void ATitleUnderlineIsSteppedOverToTheTableBeneathIt()
    {
        // a 12pt title, as the real ones are, inside a boxed title row 27pt tall
        var words = new List<TT>
        {
            new("COLUMN", 1620, 700, 1595, 694, 1645, 706), new("SCHEDULE", 1700, 700, 1670, 694, 1730, 706),
            Tok("TYPE", 1520, 675), Tok("MARK", 1520, 665), Tok("COLUMN", 1600, 675), Tok("SIZE", 1600, 665),
            Tok("1", 1520, 640), Tok("14\"", 1590, 640), Tok("x", 1610, 640), Tok("48\"", 1630, 640), Tok("7.0", 1680, 640), Tok("ksi", 1705, 640),
            Tok("2", 1520, 615), Tok("14\"", 1590, 615), Tok("x", 1610, 615), Tok("24\"", 1630, 615), Tok("7.0", 1680, 615), Tok("ksi", 1705, 615),
        };
        var paths = Table(1495, 1990, 685, 600, [655, 628], [1570, 1660]).ToList();
        paths.Add(HRule(1595, 1730, 692));    // the underline: exactly the title's extent
        paths.Add(VRule(1495, 600, 712));     // the boxed title row's sides run above the top rule
        paths.Add(VRule(1990, 600, 712));
        paths.Add(HRule(1495, 1990, 712));

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "1", "2" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleBorder, r.Route));
        Assert.Equal(48 * 25.4, rows[0].DepthMm, 0.6);
        // and a strength printed in ksi is returned in MPa, the unit read off the sheet
        Assert.Equal(7.0 * 6.894757, rows[0].StrengthMPa!.Value, 0.01);
    }

    // ── the last word before SCHEDULE is what is scheduled ─────────────────────────────────────

    /// <summary>
    /// A SHEAR WALL ZONE SCHEDULE schedules zones and a COLUMN STIRRUP SCHEDULE schedules stirrups;
    /// "contains the word" read the first as walls (ZA..ZD, with a rebar grade for a strength) and
    /// the second as columns. A parenthetical abbreviation after the head is stepped over.
    /// </summary>
    [Theory]
    [InlineData("PARKADE COLUMN", "COLUMN", true)]
    [InlineData("COLUMN", "COLUMN", true)]
    [InlineData("STEEL BEAM (SB) & COLUMN (SC)", "COLUMN", true)]
    [InlineData("LEVEL 5 COLUMN STIRRUP", "COLUMN", false)]
    [InlineData("SHEAR WALL ZONE", "WALL", false)]
    [InlineData("SHEAR WALL ZONE", "SHEAR WALL", false)]
    [InlineData("SHEAR WALL", "SHEAR WALL", true)]
    [InlineData("SHEAR WALL", "WALL", true)]
    [InlineData("DRYWALL", "WALL", false)]
    [InlineData("FOUNDATION", "FOUNDATION", true)]
    [InlineData("RAFT SLAB REINFORCING", "FOUNDATION", false)]
    [InlineData("4. IF NOTED IN THE COLUMN", "COLUMN", true)]
    public void TheHeadNounOfTheTitleDecidesWhatIsScheduled(string before, string heading, bool expected)
    {
        var words = before.Split(' ');
        Assert.Equal(expected, MarkRowScheduleReader.IsHeadedBy(words, heading));
    }

    /// <summary>The zone schedule's rows do not reach the flat-wall reader any more.</summary>
    [Fact]
    public void AZoneScheduleIsNotAWallSchedule()
    {
        var words = new List<TT>
        {
            Tok("SHEAR", 1280, 720), Tok("WALL", 1330, 720), Tok("ZONE", 1380, 720), Tok("SCHEDULE", 1450, 720),
            Tok("MARK", 1300, 700), Tok("LENGTH", 1365, 700), Tok("VERTS", 1435, 700),
            Tok("ZA", 1300, 675), Tok("16\"", 1365, 675), Tok("400", 1425, 675), Tok("MPa", 1450, 675),
        };
        var paths = Table(1278, 1602, 710, 660, [690], [1330, 1400]);

        Assert.Empty(MarkRowScheduleReader.ReadSchedule(Page(words, paths), MarkRowScheduleReader.ShearWallDefaults()));
    }

    // ── a size that VARIES is a mark, not a parse failure ──────────────────────────────────────

    /// <summary>31168's C03-B prints "&lt;varies&gt; x &lt;varies&gt;": the size is on the plan.</summary>
    [Fact]
    public void ASizeThatVariesIsAMarkWhoseSizeIsOnThePlan()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
            Tok("C03-B", 1520, 655), Tok("<varies>", 1590, 655), Tok("x", 1620, 655), Tok("<varies>", 1650, 655), Tok("45", 1700, 655), Tok("MPa", 1725, 655),
            Tok("C04-A", 1520, 625), Tok("18\"", 1590, 625), Tok("x", 1610, 625), Tok("36\"", 1630, 625), Tok("45", 1700, 625), Tok("MPa", 1725, 625),
        };
        var paths = Table(1495, 1990, 690, 610, [668, 640], [1570, 1680]);

        var rows = ColumnScheduleReader.ReadSchedule(Page(words, paths));

        Assert.Equal(new[] { "C03-B", "C04-A" }, rows.Select(r => r.Mark).ToArray());
        var varies = rows.Single(r => r.Mark == "C03-B");
        Assert.True(varies.SizeVaries);
        Assert.Equal(45, varies.StrengthMPa);
        Assert.False(rows.Single(r => r.Mark == "C04-A").SizeVaries);
    }

    /// <summary>A fractional inch is a length: 28 1/2" x 36" is PL2 on 31138, not 2" x 36".</summary>
    [Theory]
    [InlineData("28 1/2\" x 36\"", 28.5, 36)]
    [InlineData("1/2\" x 36\"", 0.5, 36)]
    [InlineData("4' - 6 1/2\" x 4' - 0\"", 54.5, 48)]
    [InlineData("12-35M VERTS 28 1/2\" x 36\" 45 MPa", 28.5, 36)]
    public void AFractionalInchReadsAsALength(string text, double aIn, double bIn)
    {
        var mm = PrintedLength.TryFindSizeMm(text);

        Assert.NotNull(mm);
        Assert.Equal(aIn * 25.4, mm![0], 0.1);
        Assert.Equal(bIn * 25.4, mm[1], 0.1);
    }
}
