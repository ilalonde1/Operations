#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class ScheduleGridReaderTests
{
    // A word token centred at (x,y) with a small symmetric box. PDF coords: y increases UP, so a
    // higher y is a higher level on the sheet.
    private static TT W(string text, double x, double y) => new(text, x, y, x - 5, y - 5, x + 5, y + 5);

    private static PC Page(params TT[] words) => new(1, 1000, 1000, words, new List<VectorPageReader.GeomPath>());

    // A minimal three-level schedule: level axis at x=100 (L3 over L2 over L1), one mark W1 at x=200,
    // and two thickness cells for W1 — 12" at L3 and 18" at L1.
    private static PC SampleSchedule() => Page(
        // level ladder: "LEVEL" + number to its right, top (high y) to bottom
        W("LEVEL", 100, 300), W("3", 130, 300),
        W("LEVEL", 100, 250), W("2", 130, 250),
        W("LEVEL", 100, 200), W("1", 130, 200),
        // wall mark header row
        W("W1", 200, 360),
        // thickness cells: an inch token immediately left of a WALL token, same baseline
        W("12\"", 195, 300), W("WALL", 215, 300),
        W("18\"", 195, 200), W("WALL", 215, 200));

    [Fact]
    public void ReadLevelLadder_RecoversOrderedUniqueLevels_TopToBottom()
    {
        var ladder = ScheduleGridReader.ReadLevelLadder(SampleSchedule());
        Assert.Equal(new[] { "L3", "L2", "L1" }, ladder.Select(r => r.Normalized).ToArray());
    }

    [Fact]
    public void ReadThicknessCells_ReadsValueAndLevel_IgnoringNonInchTokens()
    {
        var cells = ScheduleGridReader.ReadThicknessCells(SampleSchedule());
        Assert.Equal(2, cells.Count);
        Assert.Contains(cells, c => c.ThicknessIn == 12 && c.Level == "L3");
        Assert.Contains(cells, c => c.ThicknessIn == 18 && c.Level == "L1");
    }

    [Fact]
    public void ReadWallBands_BindsMarkAndFillsDownToNextChange()
    {
        var bands = ScheduleGridReader.ReadWallBands(SampleSchedule());

        // W1 is 12" from L3 down to L2 (the row above the next change), then 18" at L1.
        Assert.Contains(bands, b => b.Mark == "W1" && b.LevelTop == "L3" && b.LevelBottom == "L2" && b.ThicknessIn == 12);
        Assert.Contains(bands, b => b.Mark == "W1" && b.LevelTop == "L1" && b.LevelBottom == "L1" && b.ThicknessIn == 18);
        Assert.All(bands, b => Assert.Equal("W1", b.Mark));
    }

    [Fact]
    public void RebarNoteTokenLeftOfWall_DoesNotBecomeThickness()
    {
        // An "@" from a rebar note sits a line BELOW the header; it must not be read as the thickness.
        var page = Page(
            W("LEVEL", 100, 300), W("3", 130, 300),
            W("W1", 200, 360),
            W("@", 210, 290),            // rebar note, 10pt below the WALL baseline
            W("30\"", 195, 300), W("WALL", 215, 300));
        var cells = ScheduleGridReader.ReadThicknessCells(page);
        Assert.Single(cells);
        Assert.Equal(30, cells[0].ThicknessIn);
    }

    [Fact]
    public void EmptyPage_YieldsNoLadderOrBands_NoThrow()
    {
        var empty = Page();
        Assert.Empty(ScheduleGridReader.ReadLevelLadder(empty));
        Assert.Empty(ScheduleGridReader.ReadWallBands(empty));
    }
}
