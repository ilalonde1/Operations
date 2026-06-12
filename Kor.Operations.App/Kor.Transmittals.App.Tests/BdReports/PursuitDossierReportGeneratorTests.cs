#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class PursuitDossierReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-12T01:00:00Z");

    private static PursuitDossierRow Row(
        long id,
        string name,
        decimal? cost,
        bool urgent = false,
        long? archId = null,
        string? archName = null,
        long? seId = null,
        string? seName = null,
        bool seIsKor = false)
        => new(
            id, name, "BC", "Healthcare", "RFP", "Vancouver", "Owner Co",
            cost, cost is { } c ? $"${c / 1_000_000m:N0}M" : "TBD",
            urgent ? BdVerdicts.PursueUrgent : BdVerdicts.Pursue, "the angle", urgent,
            archId, archName, 5, 3, 2,
            seId, seName, seIsKor,
            null, null);

    [Fact]
    public void Build_SummarizesCoverage_ClustersArchitects_AndListsGaps()
    {
        var pursuits = new[]
        {
            Row(1, "Urgent Tower", 900_000_000m, urgent: true, archId: 7, archName: "Dikeakos"),
            Row(2, "Mid Hospital", 500_000_000m, archId: 7, archName: "Dikeakos"),
            Row(3, "Small School", 100_000_000m, archId: 8, archName: "Solo Arch"),
            Row(4, "No Edge Yet", 800_000_000m),
        };

        var doc = PursuitDossierReportGenerator.Build(pursuits, GeneratedAt);

        var labels = doc.Blocks.OfType<LabelValueBlock>()
            .GroupBy(x => x.Label).ToDictionary(g => g.Key, g => g.First().Value);
        Assert.Equal("4", labels["Active pursuits (PURSUE + PURSUE_URGENT): "]);
        Assert.Equal("1", labels["Urgent: "]);
        Assert.Equal("3 of 4 (75%)", labels["Architect edge resolved: "]);
        Assert.Equal("1", labels["Architect edge still missing (graph gaps): "]);
        Assert.Equal("$2.3B CAD", labels["Pipeline value (where estimated): "]);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        // Dikeakos has 2 pursuits -> cluster; Solo Arch has 1 -> no cluster.
        Assert.Contains(headings, h => h.StartsWith("Dikeakos — 2 pursuits", StringComparison.Ordinal));
        Assert.DoesNotContain(headings, h => h.StartsWith("Solo Arch", StringComparison.Ordinal));
        Assert.Contains("Graph gaps — pursuits with no architect edge", headings);

        var tables = doc.Blocks.OfType<TableBlock>().ToList();
        // urgent team-state, one cluster, gaps.
        Assert.Equal(3, tables.Count);
        Assert.Equal("Urgent Tower", tables[0].Rows[0][1]);
        Assert.Equal("No Edge Yet", tables[2].Rows[0][1]); // top gap by cost

        // Drill-down dossier marks the unresolved edge loudly.
        var unresolved = doc.Blocks.OfType<LabelValueBlock>()
            .Where(x => x.Label == "Architect: ").Select(x => x.Value).ToList();
        Assert.Contains("UNRESOLVED — no graph edge yet", unresolved);
    }

    [Fact]
    public void Build_DistinguishesKorSeatFromIncumbentCompetitor()
    {
        var pursuits = new[]
        {
            Row(1, "Defend Me", 300_000_000m, urgent: true, archId: 7, archName: "A1", seId: 38918, seName: "KOR Structural Ltd.", seIsKor: true),
            Row(2, "Flip Me", 200_000_000m, urgent: true, archId: 8, archName: "A2", seId: 50, seName: "Aspect Structural"),
        };

        var doc = PursuitDossierReportGenerator.Build(pursuits, GeneratedAt);

        var labels = doc.Blocks.OfType<LabelValueBlock>().ToList();
        Assert.Contains(labels, x => x.Label == "KOR already holds the structural seat: " && x.Value == "1");
        Assert.Contains(labels, x => x.Label == "Incumbent structural engineer known: " && x.Value == "1");
        Assert.Contains(labels, x => x.Label == "Structural seat: " && x.Value.Contains("already ours", StringComparison.Ordinal));
        Assert.Contains(labels, x => x.Label == "Incumbent structural engineer: " && x.Value.Contains("Aspect Structural", StringComparison.Ordinal));

        // Urgent table shows "KOR (ours)" not the raw org name for KOR's seat.
        var urgentTable = doc.Blocks.OfType<TableBlock>().First();
        Assert.Equal("KOR (ours)", urgentTable.Rows[0][4]);
        Assert.Equal("Aspect Structural", urgentTable.Rows[1][4]);
    }

    [Fact]
    public void Build_EmptyPool_StillRendersSummaryAndMethodology()
    {
        var doc = PursuitDossierReportGenerator.Build(Array.Empty<PursuitDossierRow>(), GeneratedAt);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains("Executive Summary", headings);
        Assert.Contains("Methodology", headings);
        Assert.DoesNotContain("URGENT pursuits — team state", headings);
        Assert.Empty(doc.Blocks.OfType<TableBlock>());
    }
}
