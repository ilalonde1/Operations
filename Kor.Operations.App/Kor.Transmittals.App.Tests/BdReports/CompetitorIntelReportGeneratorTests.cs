#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class CompetitorIntelReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-09T23:30:00Z");

    [Fact]
    public void Build_RendersFootprintTableAndDrillDown()
    {
        var competitors = new[]
        {
            new CompetitorFootprintRow(1, "Aspect Structural Engineers", 37, 6, 18, 2,
                new[] { "10: Sample Tower (BC) — $200M" }),
            new CompetitorFootprintRow(2, "RJC Engineers", 22, 11, 14, 0, Array.Empty<string>()),
        };

        var doc = CompetitorIntelReportGenerator.Build(competitors, GeneratedAt);

        var footprint = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("Aspect Structural Engineers", footprint.Rows[0][0]);
        Assert.Equal("61", footprint.Rows[0][4]); // 37+6+18
        Assert.Equal("2", footprint.Rows[0][5]);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains("Top 5 rivals — drill-down", headings);
        Assert.Contains("Aspect Structural Engineers", headings);
        Assert.Contains("Strategic synthesis", headings);

        // Unlinked rival gets the no-links placeholder, not an empty section.
        var paragraphs = doc.Blocks.OfType<ParagraphBlock>().Select(p => p.Text).ToList();
        Assert.Contains(paragraphs, p => p.Contains("No structurally-linked projects"));
        Assert.Contains(paragraphs, p => p.Contains("10: Sample Tower"));
    }

    [Fact]
    public void Build_EmptyList_DoesNotThrow()
    {
        var doc = CompetitorIntelReportGenerator.Build(Array.Empty<CompetitorFootprintRow>(), GeneratedAt);

        Assert.NotEmpty(doc.Blocks);
    }
}
