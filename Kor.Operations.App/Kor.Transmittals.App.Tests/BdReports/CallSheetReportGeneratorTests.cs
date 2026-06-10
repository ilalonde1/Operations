#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class CallSheetReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-09T21:00:00Z");

    private static PursuitBriefRow Row(long id, string name, string verdict, string? korAngle, decimal? cost)
        => new(
            id, name, "BC", "Healthcare", null, "RFP", "Prop", cost, null, "City", null,
            verdict, korAngle, "status", "desc", 0.8, GeneratedAt,
            BdVerdicts.IsUrgent(verdict, korAngle));

    private static readonly SectorVerdictSummary[] Summaries =
    {
        new("hospitals", "Hospitals & Healthcare", 1, 18, 68, 27, 0, 0, 41, 155, 100, 10, 4),
    };

    [Fact]
    public void Build_SplitsTopThreeFromRemainingUrgent_AndRanksByCost()
    {
        var pool = new[]
        {
            Row(1, "Urgent Small", BdVerdicts.PursueUrgent, null, 10_000_000m),
            Row(2, "Urgent Big", BdVerdicts.PursueUrgent, null, 400_000_000m),
            Row(3, "Urgent Mid", BdVerdicts.Pursue, "URGENT now", 200_000_000m),
            Row(4, "Urgent Fourth", BdVerdicts.PursueUrgent, null, 5_000_000m),
            Row(5, "Plain Pursue", BdVerdicts.Pursue, "open seat", 300_000_000m),
        }
        .OrderByDescending(x => x.IsUrgent)
        .ThenByDescending(x => x.EstimatedCostCad ?? 0)
        .ToList();

        var doc = CallSheetReportGenerator.Build(pool, Summaries, GeneratedAt);
        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();

        Assert.Contains("THE 3 most time-sensitive — call/email THIS WEEK", headings);
        Assert.Contains(headings, h => h.StartsWith("1. Urgent Big"));
        Assert.Contains(headings, h => h.StartsWith("2. Urgent Mid"));
        Assert.Contains(headings, h => h.StartsWith("3. Urgent Small"));
        Assert.Contains("PURSUE_URGENT — additional 1 items", headings);
        Assert.Contains(headings, h => h.StartsWith("Urgent Fourth"));
        Assert.Contains("PURSUE pool — top 1 of 1 open opportunities", headings);
    }

    [Fact]
    public void Build_IncludesStrategicTargets_AndLiveSectorStatusTable()
    {
        var doc = CallSheetReportGenerator.Build(Array.Empty<PursuitBriefRow>(), Summaries, GeneratedAt);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains("Strategic Relationship Targets (compounding leverage)", headings);
        Assert.Contains(headings, h => h.StartsWith("MST Development Corp"));

        var statusTable = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("Hospitals & Healthcare", statusTable.Rows[0][0]);
        Assert.Equal("114/155", statusTable.Rows[0][1]); // honed = total - noVerdict
        Assert.Equal("1", statusTable.Rows[0][2]);
    }

    [Fact]
    public void Build_EmptyPool_RendersPlaceholders()
    {
        var doc = CallSheetReportGenerator.Build(Array.Empty<PursuitBriefRow>(), Summaries, GeneratedAt);

        var paragraphs = doc.Blocks.OfType<ParagraphBlock>().Select(p => p.Text).ToList();
        Assert.Contains("No URGENT items in the current honing data.", paragraphs);
        Assert.Contains("No open PURSUE items in the current honing data.", paragraphs);
    }
}
