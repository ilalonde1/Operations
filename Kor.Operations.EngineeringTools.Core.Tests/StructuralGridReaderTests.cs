#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class StructuralGridReaderTests
{
    private const double W = 2592, H = 1728;

    // A bubble token: text centred at (cx,cy) with the given font height.
    private static VectorPageReader.TextToken T(string text, double cx, double cy, double hgt = 14)
        => new(text, cx, cy, cx - 3, cy - hgt / 2, cx + 3, cy + hgt / 2);

    private static VectorPageReader.PageContent Page(params VectorPageReader.TextToken[] words)
        => new(1, W, H, words, Array.Empty<VectorPageReader.GeomPath>());

    // Digit bubbles 'lo'..'hi' across the top edge at even spacing, and letter bubbles A.. down one side.
    private static IEnumerable<VectorPageReader.TextToken> NorthGrid()
    {
        var t = new List<VectorPageReader.TextToken>();
        for (int g = 1; g <= 8; g++) t.Add(T(g.ToString(), 400 + (g - 1) * 200, 0.95 * H));   // 1..8 across
        string[] rows = { "A", "B", "C", "D", "E", "F" };
        for (int i = 0; i < rows.Length; i++) t.Add(T(rows[i], 300, 1400 - i * 200));          // A..F down
        return t;
    }

    [Fact]
    public void Reads_a_clean_north_tower_grid()
    {
        var f = StructuralGridReader.FromPage(Page(NorthGrid().ToArray()))!;
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, f.XLabels);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, f.YLabels);
        Assert.False(f.MultiPlan);
        Assert.True(f.IsUsable);
        Assert.Equal(1400, f.XSpanPt, 1);   // 7 bays × 200pt
        Assert.Equal(1000, f.YSpanPt, 1);   // 5 bays × 200pt
    }

    [Fact]
    public void Reads_a_south_tower_grid_with_two_digit_labels()
    {
        var t = new List<VectorPageReader.TextToken>();
        foreach (var (g, i) in new[] { 9, 10, 11, 12, 13 }.Select((g, i) => (g, i)))
            t.Add(T(g.ToString(), 400 + i * 200, 0.95 * H));
        foreach (var (r, i) in new[] { "A", "B", "C", "D", "E", "F" }.Select((r, i) => (r, i)))
            t.Add(T(r, 300, 1400 - i * 200));
        var f = StructuralGridReader.FromPage(Page(t.ToArray()))!;
        Assert.Equal(new[] { "9", "10", "11", "12", "13" }, f.XLabels);
        Assert.False(f.MultiPlan);
    }

    [Fact]
    public void Flags_a_sheet_carrying_two_plans_and_reports_one_plan_span()
    {
        // Two [2,3,4,5] runs far apart (the L6-18 sheet form) → MultiPlan, span of ONE plan.
        var t = new List<VectorPageReader.TextToken>();
        for (int i = 0; i < 4; i++) t.Add(T((i + 2).ToString(), 400 + i * 200, 0.95 * H));   // left plan 2-5
        for (int i = 0; i < 4; i++) t.Add(T((i + 2).ToString(), 1600 + i * 200, 0.95 * H));  // right plan 2-5
        foreach (var (r, i) in new[] { "B", "C", "D", "E", "F" }.Select((r, i) => (r, i)))
            t.Add(T(r, 300, 1300 - i * 200));
        var f = StructuralGridReader.FromPage(Page(t.ToArray()))!;
        Assert.True(f.MultiPlan);
        Assert.Equal(new[] { "2", "3", "4", "5" }, f.XLabels);   // one plan, not both
        Assert.Equal(600, f.XSpanPt, 1);                          // 3 bays, not the whole sheet width
    }

    [Fact]
    public void Trims_an_off_interval_stray_bubble_without_flagging_multiplan()
    {
        var t = NorthGrid().ToList();
        t.Add(T("4", 2400, 0.95 * H));   // a lone stray "4" far to the right (the parkade P2 case)
        var f = StructuralGridReader.FromPage(Page(t.ToArray()))!;
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, f.XLabels);
        Assert.False(f.MultiPlan);       // a single stray is not a second plan
        Assert.Equal(1400, f.XSpanPt, 1);
    }

    [Fact]
    public void Rejects_interior_dimension_digits_by_height_and_margin()
    {
        var t = NorthGrid().ToList();
        t.Add(T("35", 1200, 0.45 * H, hgt: 6));   // an interior dimension "35", small font, mid-page
        t.Add(T("90", 800, 0.50 * H, hgt: 6));    // another interior number
        var f = StructuralGridReader.FromPage(Page(t.ToArray()))!;
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, f.XLabels);   // strays ignored
    }

    [Fact]
    public void Splits_letters_across_two_columns_for_an_L_shaped_plate()
    {
        // Parkade form: A–D at one x, E–F shifted in (different x) — still one plate, full A–F height.
        var t = new List<VectorPageReader.TextToken>();
        for (int g = 1; g <= 8; g++) t.Add(T(g.ToString(), 400 + (g - 1) * 200, 0.95 * H));
        t.Add(T("A", 2300, 1500)); t.Add(T("B", 2300, 1300)); t.Add(T("C", 2300, 1100)); t.Add(T("D", 2300, 900));
        t.Add(T("E", 1900, 700));  t.Add(T("F", 1900, 500));   // E,F shifted left (L-shape)
        var f = StructuralGridReader.FromPage(Page(t.ToArray()))!;
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, f.YLabels);
        Assert.Equal(1000, f.YSpanPt, 1);   // full A→F height across both columns
    }

    [Fact]
    public void Envelope_converts_points_to_square_feet_at_metric_scale()
    {
        var f = StructuralGridReader.FromPage(Page(NorthGrid().ToArray()))!;
        // 1400 × 1000 pt at 1:100. 1pt = 100/72 in = 0.115741 ft; pt² = 0.0133959 ft².
        Assert.Equal(1400 * 1000 * 0.0133959, f.EnvelopeSqFt(100), 0);
    }

    [Fact]
    public void Returns_null_when_no_grid_bubbles_are_present()
        => Assert.Null(StructuralGridReader.FromPage(Page(
            T("SHEAR", 1000, 800, 9), T("WALL", 1100, 800, 9), T("SCHEDULE", 1250, 800, 9))));

    [Fact]
    public void Exposes_the_absolute_plate_box_so_the_poche_can_be_cropped_from_the_grid()
    {
        // The deterministic replacement for the AI locate: digits 1..8 span x∈[400,1800], letters A..F span
        // y∈[400,1400]. The box must bound those bubbles so a downstream crop measures the right rectangle.
        var f = StructuralGridReader.FromPage(Page(NorthGrid().ToArray()))!;
        Assert.True(f.IsLocatable);
        Assert.Equal(400, f.XMinPt, 1);
        Assert.Equal(1800, f.XMaxPt, 1);
        Assert.Equal(400, f.YMinPt, 1);
        Assert.Equal(1400, f.YMaxPt, 1);
        // The box width/height agree with the spans (box is the absolute placement of the same bubbles).
        Assert.Equal(f.XSpanPt, f.XMaxPt - f.XMinPt, 1);
        Assert.Equal(f.YSpanPt, f.YMaxPt - f.YMinPt, 1);
    }

    [Fact]
    public void A_span_only_grid_is_usable_but_not_locatable()
    {
        // A grid hand-built from spans (no box) — usable for area, but the poché cannot be cropped from it.
        var f = new GridFrame(new[] { "1", "2" }, new[] { "A", "B" }, 1400, 1000, false);
        Assert.True(f.IsUsable);
        Assert.False(f.IsLocatable);
    }
}
