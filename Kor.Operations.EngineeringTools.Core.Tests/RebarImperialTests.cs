#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class RebarImperialTests
{
    // Page 1 carries the sheet's own number + the call-outs; page 2 is a cross-referenced detail
    // so the own-sheet (rarest token) rule resolves to S2.01.1.
    private static List<string> Issue(string callouts) => new()
    {
        $"S2.01.1  LEVEL 1 SLAB REINFORCING\n{callouts}  S5.03 typ.",
        "S5.03 S5.03 S5.03 typical detail sheet referenced everywhere",
    };

    [Fact]
    public void ExtractsImperialCallouts()
    {
        var sheets = RebarCalloutExtractor.Extract(Issue("#5 @ 12  #5 @ 12  #4 @ 16"), UnitSystem.Imperial);
        var s = sheets.Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(2, s.Callouts["#5@12"]);
        Assert.Equal(1, s.Callouts["#4@16"]);
    }

    [Fact]
    public void ImperialInchBoundsFilterDetailNumbers()
    {
        // 2" (< 3) and 60" (> 48) are out of plausible bar spacing → dropped.
        var s = RebarCalloutExtractor.Extract(Issue("#5 @ 2  #5 @ 60  #6 @ 18"), UnitSystem.Imperial)
            .Single(x => x.Sheet == "S2.01.1");
        Assert.False(s.Callouts.ContainsKey("#5@2"));
        Assert.False(s.Callouts.ContainsKey("#5@60"));
        Assert.Equal(1, s.Callouts["#6@18"]);
    }

    [Fact]
    public void ImperialSpacingHasRightBoundary_NoTruncation()
    {
        // "#5 @ 120" must NOT truncate to a false "#5@12"; 120 is out of bar-spacing range.
        var s = RebarCalloutExtractor.Extract(Issue("#5 @ 120  #6 @ 18"), UnitSystem.Imperial)
            .Single(x => x.Sheet == "S2.01.1");
        Assert.False(s.Callouts.ContainsKey("#5@12"));
        Assert.Equal(1, s.Callouts["#6@18"]);
    }

    [Fact]
    public void MetricPathIgnoresImperialNotation_AndViceVersa()
    {
        // Metric (default) extractor sees no `#`-bars.
        var metricOnImperial = RebarCalloutExtractor.Extract(Issue("#5 @ 12"), UnitSystem.Metric)
            .Single(x => x.Sheet == "S2.01.1");
        Assert.Empty(metricOnImperial.Callouts);

        // Imperial extractor sees no metric `##M@` call-outs.
        var imperialOnMetric = RebarCalloutExtractor.Extract(Issue("15M @ 200"), UnitSystem.Imperial)
            .Single(x => x.Sheet == "S2.01.1");
        Assert.Empty(imperialOnMetric.Callouts);
    }

    [Fact]
    public void MetricExtractionUnchangedByGating()
    {
        // The default (metric) path still reads `15M@200` exactly as before.
        var s = RebarCalloutExtractor.Extract(Issue("15M @ 200  15M @ 200"))
            .Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(2, s.Callouts["15M@200"]);
    }

    [Fact]
    public void DetectsImperialChangeIssueToIssue()
    {
        var r = RebarChangeService.Compare(
            Issue("#5 @ 12"), Issue("#5 @ 10"), "IFT", "IFC", UnitSystem.Imperial);
        var s = r.Sheets.Single(x => x.Sheet == "S2.01.1");
        Assert.Equal(RebarChangeStatus.Changed, s.Status);
        Assert.Contains("+1x #5@10", s.Added);
        Assert.Contains("-1x #5@12", s.Removed);
    }
}
