#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

// The KOR-Vancouver BAR-LIST call-out grammar ("16-15M13.9", "6-C15M08.5") found on Brewery District
// (30953) and other local sets — added ALONGSIDE the metric/imperial intensity patterns. These tests pin
// both that it now reads those sets AND that it adds nothing to Rory's 31065 metric path (byte-identical).
public sealed class RebarBarListTests
{
    private static List<string> Issue(string callouts) => new()
    {
        $"S2.01.1  LEVEL 1 SLAB REINFORCING\n{callouts}  S5.03 typ.",
        "S5.03 S5.03 S5.03 typical detail sheet referenced everywhere",
    };

    [Fact]
    public void ExtractsBarListCallouts_QtyContinuousSizeLength()
    {
        var s = RebarCalloutExtractor.Extract(Issue("16-15M13.9  6-C15M08.5  16-15M13.9  C15M3.11"))
            .Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(2, s.Callouts["16-15M13.9"]);   // repeated token counted
        Assert.Equal(1, s.Callouts["6-C15M08.5"]);   // optional quantity + Continuous
        Assert.Equal(1, s.Callouts["C15M3.11"]);     // no quantity, Continuous only
    }

    [Fact]
    public void BarSizeMustBeARealCanadianBar()
    {
        // 12M / 13M are not real bars (not a multiple of 5) → a "12M34.5"-shaped dimension is NOT a call-out.
        var s = RebarCalloutExtractor.Extract(Issue("12M34.5  13M10.5  20M05.6"))
            .Single(x => x.Sheet == "S2.01.1");
        Assert.False(s.Callouts.ContainsKey("12M34.5"));
        Assert.False(s.Callouts.ContainsKey("13M10.5"));
        Assert.Equal(1, s.Callouts["20M05.6"]);      // 20M is real
    }

    [Fact]
    public void BarList_AddsNothing_ToTheMetricIntensityPath_31065Invariance()
    {
        // Rory's 31065 set: intensity call-outs "15M @ 200". The bar-list grammar requires a size GLUED to a
        // decimal length, which "15M @ 200" never produces — so the metric output is exactly as before.
        var s = RebarCalloutExtractor.Extract(Issue("15M @ 200  15M @ 200  10M @ 300"))
            .Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(2, s.Callouts["15M@200"]);
        Assert.Equal(1, s.Callouts["10M@300"]);
        Assert.Equal(2, s.Callouts.Count);            // NO extra bar-list keys leaked in
    }

    [Fact]
    public void DetectsBarListChange_IssueToIssue()
    {
        // A quantity change (16 → 18 bars) on the same bar is a remove + add of the full token.
        var r = RebarChangeService.Compare(Issue("16-15M13.9"), Issue("18-15M13.9"), "IFC", "SSI#11");
        var s = r.Sheets.Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(RebarChangeStatus.Changed, s.Status);
        Assert.Contains("+1x 18-15M13.9", s.Added);
        Assert.Contains("-1x 16-15M13.9", s.Removed);
    }

    [Fact]
    public void TotalCalloutsRead_IsZero_WhenGrammarUnrecognised_AndPositiveWhenRead()
    {
        // The "can't-read" guard signal: a set whose call-outs don't match any grammar reads 0 (the host
        // then refuses to report "no change"); a set that DOES match reads > 0.
        var blank = RebarChangeService.Compare(Issue("GENERAL NOTES ONLY"), Issue("GENERAL NOTES ONLY"));
        Assert.Equal(0, blank.TotalCalloutsRead);

        var read = RebarChangeService.Compare(Issue("16-15M13.9"), Issue("16-15M13.9  20M05.6"));
        Assert.True(read.TotalCalloutsRead > 0);
    }
}
