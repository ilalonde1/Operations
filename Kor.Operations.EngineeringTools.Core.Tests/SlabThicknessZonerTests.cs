#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using static Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SlabThicknessZonerTests
{
    private static TextToken Word(string text, double cx, double cy, double w = 18, double h = 12)
        => new(text, cx, cy, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);

    // A «N" SLAB» callout = an inch-marked number immediately left of SLAB on one baseline.
    private static IEnumerable<TextToken> Callout(string num, string slab, double y, double x)
    {
        yield return Word(num, x, y);
        yield return Word(slab, x + 40, y);
    }

    private static PageContent Page(params TextToken[] words)
        => new(1, 1000, 800, words, System.Array.Empty<GeomPath>());

    [Fact]
    public void Reads_callouts_with_positions_and_values()
    {
        var page = Page(Callout("8\"", "SLAB", 600, 100)
            .Concat(Callout("12\"", "SLAB", 400, 300)).ToArray());
        var callouts = SlabThicknessZoner.ReadCallouts(page);

        Assert.Equal(2, callouts.Count);
        Assert.Contains(callouts, c => c.ValueIn == 8);
        Assert.Contains(callouts, c => c.ValueIn == 12);
        // Anchor sits at the SLAB word's centre (just right of its number).
        var eight = callouts.Single(c => c.ValueIn == 8);
        Assert.Equal(600, eight.Cy);
        Assert.True(eight.Cx > 100);
    }

    [Fact]
    public void Skips_slab_on_grade_and_column_callouts_like_the_reader_does()
    {
        // "UNREINFORCED SLAB" — a word, not a number, sits immediately left of SLAB.
        var page = Page(
            Word("4\"", 100, 600), Word("UNREINFORCED", 150, 600), Word("SLAB", 220, 600));
        Assert.Empty(SlabThicknessZoner.ReadCallouts(page));
    }

    [Fact]
    public void Qualifies_a_genuine_two_zone_tower_floor()
    {
        // 8"x7 + 12"x5 — the real p48 tower split: 12" has real support, so it zones.
        var callouts = Enumerable.Repeat(new SlabThicknessZoner.Callout(0, 0, 8), 7)
            .Concat(Enumerable.Repeat(new SlabThicknessZoner.Callout(0, 0, 12), 5)).ToList();
        var qual = SlabThicknessZoner.QualifyingValues(callouts, 8);
        Assert.Equal(new[] { 8, 12 }, qual.OrderBy(v => v));
    }

    [Fact]
    public void Does_not_zone_a_uniform_parkade_slab_with_a_single_stray()
    {
        // 10"x2 + 8"x1 — the 8" is a lone stray; the floor stays a single 10" zone (no Voronoi carve).
        var callouts = new List<SlabThicknessZoner.Callout>
        {
            new(0, 0, 10), new(0, 0, 10), new(0, 0, 8),
        };
        Assert.Equal(new[] { 10 }, SlabThicknessZoner.QualifyingValues(callouts, 10));
    }

    [Fact]
    public void Excludes_thickening_bands_from_the_field_average()
    {
        // 24" slab bands are localized add-ons, never part of the field thickness, even if repeated.
        var callouts = new List<SlabThicknessZoner.Callout>
        {
            new(0, 0, 10), new(0, 0, 10), new(0, 0, 10), new(0, 0, 24), new(0, 0, 24),
        };
        Assert.Equal(new[] { 10 }, SlabThicknessZoner.QualifyingValues(callouts, 10));
    }

    [Fact]
    public void Effective_thickness_is_area_weighted()
    {
        var zonePx = new Dictionary<int, long> { [8] = 5800, [12] = 4200 };
        // (8*5800 + 12*4200) / 10000 = 9.68
        Assert.Equal(9.68, SlabThicknessZoner.EffectiveThicknessIn(zonePx, 8), 2);
    }

    [Fact]
    public void Effective_thickness_falls_back_to_modal_when_not_split()
    {
        Assert.Equal(10, SlabThicknessZoner.EffectiveThicknessIn(new Dictionary<int, long>(), 10));
        Assert.Equal(10, SlabThicknessZoner.EffectiveThicknessIn(null, 10));
        // A single measured zone is exact — its own value, not the (possibly stale) modal.
        Assert.Equal(12, SlabThicknessZoner.EffectiveThicknessIn(new Dictionary<int, long> { [12] = 9000 }, 8));
    }
}
