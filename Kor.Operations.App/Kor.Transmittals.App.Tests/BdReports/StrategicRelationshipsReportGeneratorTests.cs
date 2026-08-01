#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class StrategicRelationshipsReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-10T00:00:00Z");

    [Fact]
    public void Build_RendersPerTargetSections_AndLiveSummaryTable()
    {
        var def = new StrategicTargetDefinition(
            "Graham Design Builders LP", "Design-Build GC", "Why text",
            new[] { "Alex Trifunov — Pre-Construction Manager" },
            "Graham", "Angle text",
            new[] { "Month 1: email" });

        var targets = new List<(StrategicTargetDefinition, IReadOnlyList<string>)>
        {
            (def, new[] { "100: Hospital (BC) — $1B" }),
            (def with { Name = "No Match Target" }, Array.Empty<string>()),
        };

        var doc = StrategicRelationshipsReportGenerator.Build(targets, GeneratedAt);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains("Graham Design Builders LP", headings);
        Assert.Contains("No Match Target", headings);
        Assert.Contains("Summary — 2 strategic targets", headings);

        var paragraphs = doc.Blocks.OfType<ParagraphBlock>().Select(p => p.Text).ToList();
        Assert.Contains("  - Alex Trifunov — Pre-Construction Manager", paragraphs);
        Assert.Contains("  - 100: Hospital (BC) — $1B", paragraphs);
        Assert.Contains(paragraphs, p => p.Contains("No direct keyword match"));

        var summary = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("1", summary.Rows[0][2]);
        Assert.Equal("0", summary.Rows[1][2]);
    }

    [Fact]
    public void Catalog_HasTenTargets_WithCompleteFields()
    {
        Assert.Equal(10, StrategicTargetCatalog.All.Count);
        foreach (var t in StrategicTargetCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Type));
            Assert.False(string.IsNullOrWhiteSpace(t.Why));
            Assert.False(string.IsNullOrWhiteSpace(t.LikePattern));
            Assert.False(string.IsNullOrWhiteSpace(t.Angle));
            Assert.NotEmpty(t.Contacts);
            Assert.NotEmpty(t.Timeline);
        }
    }
}
