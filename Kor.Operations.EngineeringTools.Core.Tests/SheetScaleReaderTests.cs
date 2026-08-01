using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SheetScaleReaderTests
{
    private const double W = 2592, H = 1728;   // 31065 sheet size in points

    // A word centred at normalized (fx across, fy up-from-bottom) with font height h (pts).
    private static TT Word(string text, double fx, double fy, double h = 7)
    {
        double cx = fx * W, cy = fy * H;
        return new TT(text, cx, cy, cx - 20, cy - h / 2, cx + 20, cy + h / 2);
    }

    private static PC Page(params TT[] words) => new(1, W, H, words, new List<VectorPageReader.GeomPath>());

    [Fact]
    public void Reads_metric_ratio_from_title_block()
    {
        // The 31065 layout: "SCALE:" label with "1 : 100" spread over sibling tokens on the same baseline.
        var note = SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.91, 0.16), Word("1", 0.925, 0.16), Word(":", 0.932, 0.16), Word("100", 0.94, 0.16)));
        Assert.NotNull(note);
        Assert.Equal(PlanGeometry.MetresPerPixel("1:100", 110), PlanGeometry.MetresPerPixel(note, 110));
    }

    [Fact]
    public void Reads_value_embedded_in_the_label_token()
    {
        var note = SheetScaleReader.FromPage(Page(Word("SCALE: 1:50", 0.91, 0.16)));
        Assert.NotNull(note);
        Assert.Equal(PlanGeometry.MetresPerPixel("1:50", 110), PlanGeometry.MetresPerPixel(note, 110));
    }

    [Fact]
    public void Reads_imperial_architectural_note()
    {
        var note = SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.90, 0.16), Word("1/8\"", 0.915, 0.16), Word("=", 0.925, 0.16), Word("1'-0\"", 0.935, 0.16)));
        Assert.NotNull(note);
        Assert.Equal(PlanGeometry.MetresPerPixel("1/8\"=1'-0\"", 110), PlanGeometry.MetresPerPixel(note, 110));
    }

    [Fact]
    public void As_noted_yields_null()
    {
        Assert.Null(SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.91, 0.16), Word("AS", 0.925, 0.16), Word("NOTED", 0.94, 0.16))));
    }

    [Fact]
    public void Viewport_caption_outside_title_block_is_ignored()
    {
        // A stair-detail caption mid-sheet must not be mistaken for the sheet scale.
        Assert.Null(SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.55, 0.40), Word("1", 0.565, 0.40), Word(":", 0.572, 0.40), Word("50", 0.58, 0.40))));
    }

    [Fact]
    public void Duplicated_identical_fields_still_read()
    {
        // 31065 p24 carries the SCALE: label twice (overprinted); same value → not ambiguous.
        var note = SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.91, 0.16), Word("SCALE:", 0.91, 0.16), Word("1", 0.925, 0.16), Word(":", 0.932, 0.16), Word("100", 0.94, 0.16)));
        Assert.NotNull(note);
    }

    [Fact]
    public void Conflicting_stated_scales_yield_null()
    {
        Assert.Null(SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.91, 0.30), Word("1:100", 0.93, 0.30),
            Word("SCALE:", 0.91, 0.16), Word("1:50", 0.93, 0.16))));
    }

    [Fact]
    public void No_scale_field_yields_null()
    {
        Assert.Null(SheetScaleReader.FromPage(Page(Word("LEVEL", 0.91, 0.30), Word("1", 0.93, 0.30))));
    }

    [Fact]
    public void Detail_caption_high_in_right_strip_is_ignored()
    {
        // A parseable "SCALE: 1:20" under a detail drawn in the right-hand details column must not
        // become the sheet scale — the metadata field lives in the bottom corner.
        Assert.Null(SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.91, 0.55), Word("1", 0.925, 0.55), Word(":", 0.932, 0.55), Word("20", 0.94, 0.55))));
    }

    [Fact]
    public void As_noted_with_spliced_neighbour_ratio_yields_null()
    {
        // "SCALE: AS NOTED" with a ratio-shaped token from a neighbouring field on the same baseline:
        // the candidate opens with words, so it is not a scale — fall back flagged, never guess.
        Assert.Null(SheetScaleReader.FromPage(Page(
            Word("SCALE:", 0.90, 0.16), Word("AS", 0.915, 0.16), Word("NOTED", 0.93, 0.16), Word("1:1000", 0.96, 0.16))));
    }
}
