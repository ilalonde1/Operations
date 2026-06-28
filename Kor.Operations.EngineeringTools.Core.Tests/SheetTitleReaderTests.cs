#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SheetTitleReaderTests
{
    private const double W = 3024, H = 2160;

    // A word centred at normalized (fx across, fy up-from-bottom) with font height h (pts).
    private static TT Word(string text, double fx, double fy, double h)
    {
        double cx = fx * W, cy = fy * H;
        return new TT(text, cx, cy, cx - 20, cy - h / 2, cx + 20, cy + h / 2);
    }

    private static PC Page(params TT[] words) => new(1, W, H, words, new List<VectorPageReader.GeomPath>());

    // ── Pure title-line parse ────────────────────────────────────────────────
    [Theory]
    [InlineData("LEVEL P7 PLAN NORTH", "P7", "NORTH")]
    [InlineData("LEVEL P4 PLAN - SOUTH", "P4", "SOUTH")]
    [InlineData("LEVEL P1 MEZZ. PLAN", "P1 MEZZ", null)]
    [InlineData("LEVEL P1 MEZZANINE PLAN NORTH", "P1 MEZZ", "NORTH")]
    [InlineData("LEVEL 1 PLAN", "1", null)]
    [InlineData("LEVEL 13 PLAN", "13", null)]
    [InlineData("2ND FLOOR PLAN", "2", null)]
    [InlineData("ROOF PLAN", "ROOF", null)]
    public void ParseTitleLine_reads_level_and_zone(string line, string lvl, string? zone)
    {
        var p = SheetTitleReader.ParseTitleLine(line);
        Assert.NotNull(p);
        Assert.Equal(lvl, p!.Value.Level);
        Assert.Equal(zone, p.Value.Zone);
    }

    [Theory]
    [InlineData("SHEAR WALL SCHEDULE")]
    [InlineData("PARKADE COLUMN SCHEDULE")]
    [InlineData("")]
    public void ParseTitleLine_returns_null_for_non_titles(string line)
        => Assert.Null(SheetTitleReader.ParseTitleLine(line));

    // ── Read the title from the title-block region of a page ──────────────────
    [Fact]
    public void FromPage_reads_parkade_title_and_zone_from_the_title_block()
    {
        // Title band bottom-right at h=13.6; sheet number "S2.02-N" in the corner; a note elsewhere.
        var page = Page(
            Word("LEVEL", 0.92, 0.10, 13.6), Word("P7", 0.93, 0.10, 13.6),
            Word("PLAN", 0.94, 0.10, 13.6), Word("NORTH", 0.96, 0.10, 13.6),
            Word("S2.02-N", 0.94, 0.04, 28.0),
            Word("SEE", 0.30, 0.50, 9.0), Word("PLAN", 0.34, 0.50, 9.0)); // a note, must be ignored
        var t = SheetTitleReader.FromPage(page);
        Assert.NotNull(t);
        Assert.Equal("P7", t!.Level);
        Assert.Equal("NORTH", t.Zone);
        Assert.Equal("P7|NORTH", t.Key);
    }

    [Fact]
    public void FromPage_takes_the_half_from_the_sheet_number_when_the_title_text_omits_it()
    {
        // Mezzanine title carries no NORTH/SOUTH word, but the sheet number "S2.09.1-S" does.
        var page = Page(
            Word("LEVEL", 0.91, 0.10, 13.6), Word("P1", 0.92, 0.10, 13.6),
            Word("MEZZ.", 0.93, 0.10, 13.6), Word("PLAN", 0.95, 0.10, 13.6),
            Word("S2.09.1-S", 0.94, 0.04, 28.0));
        var t = SheetTitleReader.FromPage(page);
        Assert.Equal("P1 MEZZ", t!.Level);
        Assert.Equal("SOUTH", t.Zone);
    }

    [Fact]
    public void FromPage_ignores_body_cross_references_to_other_levels()
    {
        // The page's own title is "LEVEL 1 PLAN" (corner); a body note references another level's plan.
        var page = Page(
            Word("LEVEL", 0.92, 0.10, 13.6), Word("1", 0.93, 0.10, 13.6), Word("PLAN", 0.94, 0.10, 13.6),
            Word("SEE", 0.28, 0.55, 9.0), Word("LEVEL", 0.31, 0.55, 9.0),
            Word("P4", 0.34, 0.55, 9.0), Word("PLAN", 0.36, 0.55, 9.0), Word("NORTH", 0.39, 0.55, 9.0));
        var t = SheetTitleReader.FromPage(page);
        Assert.Equal("1", t!.Level);
        Assert.Null(t.Zone);
    }

    [Fact]
    public void FromPage_returns_null_when_there_is_no_title_block_plan()
    {
        var page = Page(Word("SHEAR", 0.5, 0.5, 12.0), Word("WALL", 0.55, 0.5, 12.0), Word("SCHEDULE", 0.6, 0.5, 12.0));
        Assert.Null(SheetTitleReader.FromPage(page));
        Assert.Null(SheetTitleReader.FromPage(null));
    }

    [Fact]
    public void Distinct_zones_of_the_same_level_have_distinct_keys_but_a_redrawn_half_matches()
    {
        var north = SheetTitleReader.FromPage(Page(
            Word("LEVEL", 0.92, 0.10, 13.6), Word("P5", 0.93, 0.10, 13.6), Word("PLAN", 0.94, 0.10, 13.6),
            Word("S2.03-N", 0.94, 0.04, 28.0)))!;
        var south = SheetTitleReader.FromPage(Page(
            Word("LEVEL", 0.92, 0.10, 13.6), Word("P5", 0.93, 0.10, 13.6), Word("PLAN", 0.94, 0.10, 13.6),
            Word("S2.03-S", 0.94, 0.04, 28.0)))!;
        // A different sub-sheet (reinforcing) of the same half: different sheet number, same (level,zone).
        var northReinf = SheetTitleReader.FromPage(Page(
            Word("LEVEL", 0.92, 0.10, 13.6), Word("P5", 0.93, 0.10, 13.6), Word("PLAN", 0.94, 0.10, 13.6),
            Word("S2.04-N", 0.94, 0.04, 28.0)))!;
        Assert.NotEqual(north.Key, south.Key);
        Assert.Equal(north.Key, northReinf.Key);
    }
}
