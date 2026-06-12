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

        // Headline numbers lead as the KPI summary strip (2026-06-12 UX pass).
        var kpis = Assert.Single(doc.Blocks.OfType<KpiStripBlock>()).Items
            .ToDictionary(k => k.Label, k => k);
        Assert.Equal("4", kpis["Active pursuits"].Value);
        Assert.Equal("1", kpis["Urgent"].Value);
        Assert.Equal(ChipTone.Urgent, kpis["Urgent"].Tone);
        Assert.Equal("$2.3B", kpis["Pipeline (CAD)"].Value);
        Assert.Equal("75%", kpis["Architect edge"].Value);
        Assert.Equal("1", kpis["Graph gaps"].Value);
        Assert.Equal(ChipTone.Caution, kpis["Graph gaps"].Tone);

        var labels = doc.Blocks.OfType<LabelValueBlock>()
            .GroupBy(x => x.Label).ToDictionary(g => g.Key, g => g.First().Value);
        Assert.Equal("3 of 4 (75%)", labels["Architect edge resolved: "]);

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

        // kor:// drill-down targets: dossier H3 -> project, cluster H3 -> org,
        // urgent-table pursuit cell -> project; unresolved edges mint no link.
        Assert.Equal("kor://mpi/1", doc.Blocks.OfType<HeadingBlock>().First(h => h.Text == "1. Urgent Tower").Link);
        Assert.Equal("kor://org/7", doc.Blocks.OfType<HeadingBlock>().First(h => h.Text.StartsWith("Dikeakos", StringComparison.Ordinal)).Link);
        Assert.Equal("kor://mpi/1", tables[0].CellLinks![0][1]);
        var noEdgeArchitect = doc.Blocks.OfType<LabelValueBlock>()
            .First(x => x.Label == "Architect: " && x.Value.StartsWith("UNRESOLVED", StringComparison.Ordinal));
        Assert.Null(noEdgeArchitect.Link);
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

        // Empty pool still leads with a (zeroed) KPI strip; pct shows a dash.
        var kpis = Assert.Single(doc.Blocks.OfType<KpiStripBlock>()).Items;
        Assert.Equal("0", kpis.Single(k => k.Label == "Active pursuits").Value);
        Assert.Equal("—", kpis.Single(k => k.Label == "Architect edge").Value);
    }

    [Fact]
    public void Build_DossierChips_CarryVerdictAndSeatState()
    {
        var pursuits = new[]
        {
            Row(1, "Kor Seated", 300_000_000m, urgent: true, archId: 7, archName: "A1", seId: 1, seName: "KOR", seIsKor: true),
            Row(2, "No Edge", 200_000_000m),
        };

        var doc = PursuitDossierReportGenerator.Build(pursuits, GeneratedAt);

        var chipRows = doc.Blocks.OfType<ChipRowBlock>().ToList();
        Assert.Equal(2, chipRows.Count); // one per drill-down dossier

        var korSeated = chipRows[0].Chips;
        Assert.Contains(korSeated, c => c.Text == BdVerdicts.PursueUrgent && c.Tone == ChipTone.Urgent);
        Assert.Contains(korSeated, c => c.Text == "KOR SEAT" && c.Tone == ChipTone.Positive);

        var noEdge = chipRows[1].Chips;
        Assert.Contains(noEdge, c => c.Text == BdVerdicts.Pursue && c.Tone == ChipTone.Positive);
        Assert.Contains(noEdge, c => c.Text == "NO ARCHITECT EDGE" && c.Tone == ChipTone.Neutral);
    }
}
