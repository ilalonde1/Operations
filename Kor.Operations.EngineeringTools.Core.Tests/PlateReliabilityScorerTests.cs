#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlateReliabilityScorerTests
{
    private static System.Collections.Generic.IReadOnlyList<PlanFlag> Flags(
        double fill = double.NaN, int clusters = 0,
        ThicknessSource thk = ThicknessSource.Callout, bool degenerate = false, double peer = double.NaN,
        string? level = null)
        => PlateReliabilityScorer.MeasurementFlags(fill, clusters, thk, degenerate, peer, level);

    // A clean tower floor: callout thickness, well-sealed box, few clusters, area in line with peers.
    [Fact]
    public void A_clean_plate_raises_no_measurement_flags()
    {
        var f = Flags(fill: 0.88, clusters: 5, thk: ThicknessSource.Callout, peer: 1.02);
        Assert.Empty(f);
        Assert.Equal(TakeoffConfidence.High, PlanCheck.From(f).Confidence);
    }

    // The Coronation podium: enclosed area is a small fraction of the box (open boundary) -> UNDER-count.
    [Fact]
    public void A_leaky_open_boundary_plate_is_flagged_review()
    {
        var f = Flags(fill: 0.22, clusters: 114, thk: ThicknessSource.Callout);
        Assert.Contains(f, x => x.Code == "AREA_LEAKY" && x.Severity == PlanFlagSeverity.Review);
        Assert.Contains(f, x => x.Code == "AREA_FRAGMENTED");
        Assert.Equal(TakeoffConfidence.Review, PlanCheck.From(f).Confidence);
    }

    // The mezzanine: poché grabbed a full plate for a partial mezz -> oversized vs peers.
    [Fact]
    public void An_oversized_plate_vs_peers_is_flagged()
    {
        var f = Flags(fill: 0.80, clusters: 8, peer: 3.6);
        Assert.Contains(f, x => x.Code == "AREA_LARGE_VS_PEERS" && x.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void A_too_small_plate_vs_peers_is_flagged()
    {
        var f = Flags(fill: 0.80, clusters: 8, peer: 0.17);
        Assert.Contains(f, x => x.Code == "AREA_SMALL_VS_PEERS" && x.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void Synthesis_thickness_is_review_sibling_is_info()
    {
        Assert.Contains(Flags(thk: ThicknessSource.SynthesisFallback),
            x => x.Code == "THK_SYNTH" && x.Severity == PlanFlagSeverity.Review);

        var sib = Flags(thk: ThicknessSource.SiblingReconcile);
        Assert.Contains(sib, x => x.Code == "THK_SIBLING" && x.Severity == PlanFlagSeverity.Info);
        // Info alone keeps the plate High — a reconciled sibling thickness is a note, not a doubt.
        Assert.Equal(TakeoffConfidence.High, PlanCheck.From(sib).Confidence);
    }

    [Fact]
    public void Missing_thickness_is_left_to_the_reconciler_not_duplicated_here()
    {
        Assert.DoesNotContain(Flags(thk: ThicknessSource.None), x => x.Code.StartsWith("THK"));
    }

    [Fact]
    public void Degenerate_box_substitution_is_review()
    {
        Assert.Contains(Flags(degenerate: true),
            x => x.Code == "BOX_DEGENERATE" && x.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void A_soft_boundary_is_an_info_note_not_a_doubt()
    {
        var f = Flags(fill: 0.38);
        Assert.Contains(f, x => x.Code == "AREA_SOFT" && x.Severity == PlanFlagSeverity.Info);
        Assert.Equal(TakeoffConfidence.High, PlanCheck.From(f).Confidence);
    }

    [Fact]
    public void Multiple_problems_accumulate_every_reason()
    {
        var f = Flags(fill: 0.22, clusters: 90, thk: ThicknessSource.SynthesisFallback, peer: 0.4);
        Assert.Contains(f, x => x.Code == "THK_SYNTH");
        Assert.Contains(f, x => x.Code == "AREA_LEAKY");
        Assert.Contains(f, x => x.Code == "AREA_FRAGMENTED");
        Assert.Contains(f, x => x.Code == "AREA_SMALL_VS_PEERS");
        Assert.True(f.Count >= 4);
        Assert.Equal(TakeoffConfidence.Review, PlanCheck.From(f).Confidence);
    }

    // NaN signals (not computed / no peer group) must not invent flags.
    [Fact]
    public void Unavailable_signals_do_not_flag()
    {
        Assert.Empty(Flags(fill: double.NaN, clusters: 3, peer: double.NaN));
    }

    // The L01-podium / ROOF lesson: locally-clean plates on irregular levels still need area review.
    [Theory]
    [InlineData("1")]
    [InlineData("ROOF")]
    [InlineData("P1 MEZZ")]
    [InlineData("GROUND")]
    public void Complex_geometry_levels_are_flagged_for_area_review(string level)
    {
        // Clean fill, callout thickness, no peer issue — yet the level type alone warrants review.
        var f = Flags(fill: 0.60, clusters: 6, level: level);
        Assert.Contains(f, x => x.Code == "AREA_COMPLEX_LEVEL" && x.Severity == PlanFlagSeverity.Review);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("13")]
    [InlineData("28")]
    [InlineData("P5")]   // a typical parkade level is a simple rectangle here — not complex-geometry
    public void Typical_levels_are_not_flagged_complex(string level)
        => Assert.DoesNotContain(Flags(fill: 0.60, clusters: 6, level: level), x => x.Code == "AREA_COMPLEX_LEVEL");
}
