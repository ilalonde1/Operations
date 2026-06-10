#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class ExecSummaryReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-09T22:00:00Z");

    private static readonly SectorVerdictSummary[] Summaries =
    {
        new("hospitals", "Hospitals & Healthcare", 1, 18, 68, 27, 0, 0, 41, 155, 100, 10, 4, 26_511_000_000m),
        new("schools", "K-12 Schools", 0, 109, 258, 144, 0, 0, 101, 612, 500, 11, 0, 19_869_000_000m),
    };

    [Fact]
    public void Build_LiveHeadlineAndHeatmap_ComputedFromInputs()
    {
        var headline = new BdExecHeadline(1942, 1425, 93_822_000_000m);
        var pool = new[]
        {
            new PursuitBriefRow(1, "Urgent One", "BC", null, null, null, null, 100m, null, "Surrey", null,
                BdVerdicts.PursueUrgent, "act now", null, null, null, GeneratedAt, true),
            new PursuitBriefRow(2, "Pursue One", "BC", null, null, null, null, 50m, null, null, null,
                BdVerdicts.Pursue, "open", null, null, null, GeneratedAt, false),
        };

        var doc = ExecSummaryReportGenerator.Build(headline, Summaries, pool, GeneratedAt);

        var labels = doc.Blocks.OfType<LabelValueBlock>()
            .GroupBy(x => x.Label).ToDictionary(g => g.Key, g => g.First().Value);
        Assert.Equal("1,942", labels["Total active MPIs (cross-sector, distinct): "]);
        Assert.Equal("1,425 (73%)", labels["Honed (deep BD intel — verified, named, action-ready): "]);
        Assert.Equal("~$93.8B CAD", labels["Total $ honed pipeline (estimated cost rollup): "]);
        Assert.Equal("1", labels["URGENT items live right now: "]);

        var heatmap = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("Hospitals & Healthcare", heatmap.Rows[0][0]);
        Assert.Equal("114", heatmap.Rows[0][2]);  // honed = 155 - 41
        Assert.Equal("74%", heatmap.Rows[0][3]);
        Assert.Equal("$26,511M", heatmap.Rows[0][4]);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains(headings, h => h.StartsWith("1. Urgent One"));
        Assert.Contains("Strategic compounding relationships", headings);
        Assert.Contains("Cross-sector strategic insights", headings);
        Assert.Contains("Methodology footnote", headings);
    }

    [Fact]
    public void Build_EmptyPipeline_DoesNotThrow()
    {
        var doc = ExecSummaryReportGenerator.Build(
            new BdExecHeadline(0, 0, 0m), Array.Empty<SectorVerdictSummary>(),
            Array.Empty<PursuitBriefRow>(), GeneratedAt);

        Assert.Contains(doc.Blocks.OfType<ParagraphBlock>(), p => p.Text == "No URGENT items in the current honing data.");
    }
}
