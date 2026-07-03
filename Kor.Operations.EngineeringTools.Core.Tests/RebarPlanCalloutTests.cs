#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The CSA metric PLAN call-out grammar ("24-20M6400 @ 250") + fake-bold dedupe + weight deltas.
/// Test inputs are synthetic, written from the CSA call-out convention — deliberately NOT lifted
/// from the 31065 validation set, so passing here is independent of the answer key.
/// </summary>
public sealed class RebarPlanCalloutTests
{
    // ── grammar ──────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("24-20M6400 @ 250 EXTRA TOP", "24-20M6400@250")]
    [InlineData("3-25M7200@450", "3-25M7200@450")]           // glued @; qualifier-free
    [InlineData("C10M900 @ 400", "C10M900@400")]             // continuous, no count
    [InlineData("45M12000 @ 300", "45M12000@300")]           // no count, big bar at stock length
    [InlineData("7-30M2350 EACH WAY BOTTOM", "7-30M2350")]   // footing bar: no spacing at all
    public void Plan_callouts_parse_to_canonical_keys(string text, string key)
    {
        var m = RebarPlanCallout.TextRe.Match(text);
        Assert.True(m.Success);
        var p = RebarPlanCallout.FromGroups(m);
        Assert.NotNull(p);
        Assert.Equal(key, p!.Value.Key);
    }

    [Theory]
    [InlineData("15M @ 200")]          // intensity form — no glued length; not a plan call-out
    [InlineData("16-15M13.9")]         // bar-list form — feet-inch dot
    [InlineData("12M3400 @ 200")]      // 12M is not a CSA bar
    [InlineData("2190mm CLR.")]        // lowercase dimension text
    [InlineData("20M250 @ 300")]       // 250 mm "length" — below any real bar length
    [InlineData("35M25000 @ 300")]     // 25 m — beyond any bar/coupled run
    public void Non_plan_text_is_rejected(string text)
    {
        var m = RebarPlanCallout.TextRe.Match(text);
        if (m.Success) Assert.Null(RebarPlanCallout.FromGroups(m));
    }

    [Fact]
    public void Implausible_spacing_drops_out_of_the_key_but_keeps_the_callout()
    {
        // "@ 16" after a bar run is a detail reference, not a spacing — the bar identity survives.
        var m = RebarPlanCallout.TextRe.Match("6-20M4800 @ 16");
        var p = RebarPlanCallout.FromGroups(m);
        Assert.Equal("6-20M4800", p!.Value.Key);
    }

    // ── weights ──────────────────────────────────────────────────────────────
    [Fact]
    public void Plan_callout_weight_is_count_times_length_times_csa_mass()
    {
        // 24 bars × 6.4 m × 2.355 kg/m (20M) = 361.7 kg = 797.4 lb.
        var p = RebarPlanCallout.ParseKey("24-20M6400@250");
        Assert.NotNull(p);
        Assert.Equal(797.4, RebarPlanCallout.WeightLb(p!.Value)!.Value, 0);
    }

    [Fact]
    public void Continuous_callout_without_count_is_unweighable_not_guessed()
    {
        var p = RebarPlanCallout.ParseKey("C10M900@400");
        Assert.NotNull(p);
        Assert.Null(RebarPlanCallout.WeightLb(p!.Value));
    }

    [Fact]
    public void KeyWeightLb_routes_plan_barlist_and_intensity_keys_correctly()
    {
        Assert.NotNull(RebarBarListWeigher.KeyWeightLb("24-20M6400@250"));   // plan (mm)
        Assert.NotNull(RebarBarListWeigher.KeyWeightLb("16-15M13.9"));       // bar-list (ft-in)
        Assert.Null(RebarBarListWeigher.KeyWeightLb("15M@200"));             // intensity — no qty/length
        Assert.Null(RebarBarListWeigher.KeyWeightLb("C10M900@400"));         // continuous — no qty
    }

    // ── extractor + change service end to end (page text) ───────────────────
    private static IReadOnlyList<string> Pages(params string[] pages) => pages;

    [Fact]
    public void Change_service_weighs_a_plan_callout_quantity_change()
    {
        // One sheet; the 18-bar run becomes a 12-bar run: Δ = −6 × 5.5 m × 1.570 kg/m = −51.8 kg = −114 lb.
        var before = Pages("S9.01 TYPICAL PLAN  18-15M5500 @ 200 EXTRA BOT.");
        var after  = Pages("S9.01 TYPICAL PLAN  12-15M5500 @ 200 EXTRA BOT.");
        var r = RebarChangeService.Compare(before, after);

        var sheet = Assert.Single(r.Sheets, s => s.Status == RebarChangeStatus.Changed);
        Assert.Contains(sheet.Added, s => s.Contains("12-15M5500@200"));
        Assert.Contains(sheet.Removed, s => s.Contains("18-15M5500@200"));
        Assert.Equal(-114.0, sheet.NetWeightLb, 0);
        Assert.Equal(-114.0, r.NetWeightLb, 0);
        Assert.Equal(0, r.UnweighedChanges);
    }

    [Fact]
    public void Unweighable_changes_are_counted_never_silently_weighted()
    {
        var before = Pages("S9.02 PLAN  C15M1000 @ 300");
        var after  = Pages("S9.02 PLAN  C15M1000 @ 250");
        var r = RebarChangeService.Compare(before, after);
        Assert.Equal(0.0, r.NetWeightLb, 2);
        Assert.Equal(2, r.UnweighedChanges);   // one removed key + one added key, neither weighable
    }

    // ── fake-bold dedupe ─────────────────────────────────────────────────────
    [Fact]
    public void Double_drawn_words_collapse_but_distinct_occurrences_survive()
    {
        var words = new[]
        {
            FakeWord("10M", 100.0, 500.0),
            FakeWord("10M", 100.4, 500.3),   // bold double-draw: sub-point offset -> dropped
            FakeWord("10M", 100.0, 480.0),   // a real second occurrence a text-height away -> kept
            FakeWord("@", 112.0, 500.0),
        };
        var kept = PdfWordDedupe.Filter(words);
        Assert.Equal(3, kept.Count);
        Assert.Equal(2, kept.Count(w => w.Text == "10M"));
    }

    // Minimal PdfPig Word for the dedupe test: letters carrying only the box and text.
    private static UglyToad.PdfPig.Content.Word FakeWord(string text, double x, double y)
    {
        var font = new UglyToad.PdfPig.PdfFonts.FontDetails("Test", false, 400, false);
        var letters = new List<UglyToad.PdfPig.Content.Letter>();
        double cx = x;
        foreach (char ch in text)
        {
            var rect = new UglyToad.PdfPig.Core.PdfRectangle(cx, y, cx + 5, y + 5);
            letters.Add(new UglyToad.PdfPig.Content.Letter(
                ch.ToString(), rect,
                new UglyToad.PdfPig.Core.PdfPoint(cx, y), new UglyToad.PdfPig.Core.PdfPoint(cx + 5, y),
                5, 1, font, UglyToad.PdfPig.Core.TextRenderingMode.Fill,
                UglyToad.PdfPig.Graphics.Colors.GrayColor.Black,
                UglyToad.PdfPig.Graphics.Colors.GrayColor.Black, 5, 0));
            cx += 5;
        }
        return new UglyToad.PdfPig.Content.Word(letters);
    }
}
