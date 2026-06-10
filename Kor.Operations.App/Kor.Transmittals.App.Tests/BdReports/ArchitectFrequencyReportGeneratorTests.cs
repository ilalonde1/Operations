#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class ArchitectFrequencyReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-09T23:00:00Z");

    private static ArchitectLeverageRow Row(long id, string name, int projects, decimal cost)
        => new(id, name, projects, cost, 5, 3, 2, new[] { $"{id}: Sample Project (BC) — $100M" });

    [Fact]
    public void Build_SplitsKorAlignedFromNewTargets()
    {
        var architects = new[]
        {
            Row(1, "Chris Dikeakos Architects Inc.", 21, 887_000_000m), // KOR-aligned (memory list)
            Row(2, "hcma Architecture + Design", 11, 455_000_000m),     // KOR-aligned, case-insensitive
            Row(3, "Johnson Fain", 7, 0m),                              // new target
        };

        var doc = ArchitectFrequencyReportGenerator.Build(architects, GeneratedAt);

        var tables = doc.Blocks.OfType<TableBlock>().ToList();
        Assert.Equal(3, tables.Count); // top-20, KOR-aligned, new targets

        var top20 = tables[0];
        Assert.Equal("KOR-aligned", top20.Rows[0][6]);
        Assert.Equal("KOR-aligned", top20.Rows[1][6]);
        Assert.Equal("New target", top20.Rows[2][6]);

        Assert.Equal(2, tables[1].Rows.Count); // aligned table
        Assert.Single(tables[2].Rows);         // new-target table
        Assert.Equal("Johnson Fain", tables[2].Rows[0][0]);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains(headings, h => h == "Chris Dikeakos Architects Inc.");
        Assert.Contains("Strategic Synthesis", headings);
    }

    [Fact]
    public void Build_EmptyRanking_DoesNotThrow()
    {
        var doc = ArchitectFrequencyReportGenerator.Build(Array.Empty<ArchitectLeverageRow>(), GeneratedAt);

        Assert.NotEmpty(doc.Blocks);
        Assert.Single(doc.Blocks.OfType<TableBlock>()); // only the (empty) top-20 table
    }
}
