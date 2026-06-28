#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlateReliabilityScorerTests
{
    // A clean tower floor: callout thickness, well-sealed box, few clusters, area in line with peers.
    [Fact]
    public void A_clean_plate_is_high_confidence_with_no_flags()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.88, clusterCount: 5, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: false, peerAreaRatio: 1.02);
        Assert.Equal(Confidence.High, r.Level);
        Assert.Empty(r.Flags);
        Assert.False(r.NeedsReview);
    }

    // The Coronation podium: enclosed area is a small fraction of the box (open boundary) -> UNDER-count.
    [Fact]
    public void A_leaky_open_boundary_plate_is_low_confidence()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.30, clusterCount: 114, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: false, peerAreaRatio: double.NaN);
        Assert.Equal(Confidence.Low, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "AREA_LEAKY");
        Assert.Contains(r.Flags, f => f.Code == "AREA_FRAGMENTED");
        Assert.True(r.NeedsReview);
    }

    // The mezzanine: poché grabbed a full plate for a partial mezz -> oversized vs peers.
    [Fact]
    public void An_oversized_plate_vs_peers_is_flagged()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.80, clusterCount: 8, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: false, peerAreaRatio: 3.6);
        Assert.Equal(Confidence.Medium, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "AREA_LARGE_VS_PEERS");
    }

    [Fact]
    public void A_too_small_plate_vs_peers_is_low_confidence()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.80, clusterCount: 8, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: false, peerAreaRatio: 0.17);
        Assert.Equal(Confidence.Low, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "AREA_SMALL_VS_PEERS");
    }

    [Fact]
    public void Missing_thickness_is_low_and_priceless()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.85, clusterCount: 4, thickness: ThicknessSource.None,
            degenerateBoxSubstituted: false, peerAreaRatio: 1.0);
        Assert.Equal(Confidence.Low, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "THK_NONE");
    }

    [Fact]
    public void Synthesis_thickness_and_sibling_reconcile_are_medium()
    {
        var synth = PlateReliabilityScorer.Assess(0.85, 4, ThicknessSource.SynthesisFallback, false, 1.0);
        Assert.Equal(Confidence.Medium, synth.Level);
        Assert.Contains(synth.Flags, f => f.Code == "THK_SYNTH");

        var sib = PlateReliabilityScorer.Assess(0.85, 4, ThicknessSource.SiblingReconcile, false, 1.0);
        Assert.Equal(Confidence.Medium, sib.Level);
        Assert.Contains(sib.Flags, f => f.Code == "THK_SIBLING");
    }

    [Fact]
    public void Degenerate_box_substitution_is_low()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: double.NaN, clusterCount: 1, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: true, peerAreaRatio: 1.0);
        Assert.Equal(Confidence.Low, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "BOX_DEGENERATE");
    }

    // The worst case takes the lowest level and accumulates every reason (nothing is hidden).
    [Fact]
    public void Multiple_problems_take_the_lowest_level_and_keep_all_reasons()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: 0.30, clusterCount: 90, thickness: ThicknessSource.SynthesisFallback,
            degenerateBoxSubstituted: false, peerAreaRatio: 0.4);
        Assert.Equal(Confidence.Low, r.Level);
        Assert.Contains(r.Flags, f => f.Code == "THK_SYNTH");
        Assert.Contains(r.Flags, f => f.Code == "AREA_LEAKY");
        Assert.Contains(r.Flags, f => f.Code == "AREA_FRAGMENTED");
        Assert.Contains(r.Flags, f => f.Code == "AREA_SMALL_VS_PEERS");
        Assert.True(r.Flags.Count >= 4);
    }

    // NaN signals (not computed / no peer group) must not invent flags.
    [Fact]
    public void Unavailable_signals_do_not_flag()
    {
        var r = PlateReliabilityScorer.Assess(
            fillRatio: double.NaN, clusterCount: 3, thickness: ThicknessSource.Callout,
            degenerateBoxSubstituted: false, peerAreaRatio: double.NaN);
        Assert.Equal(Confidence.High, r.Level);
        Assert.Empty(r.Flags);
    }
}
