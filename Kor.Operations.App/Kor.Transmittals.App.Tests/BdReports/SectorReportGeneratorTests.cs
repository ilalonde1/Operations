#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class SectorReportGeneratorTests
{
    private static readonly SectorReportDefinition Definition =
        new("hospitals", "Hospitals & Healthcare", "KOR Structural — BC + AB Hospitals BD Report", "(m.Sector = N'x')");

    private static readonly SectorReportProse Prose = new(
        "Intro framing.",
        new[] { "Summary narrative." },
        new[] { new SynthesisSection("Pattern one", new[] { "Pattern paragraph." }) },
        new[] { "First action.", "Second action." });

    private static PursuitBriefRow Row(
        long id, string name, string? verdict, string? korAngle = null,
        decimal? cost = null, string province = "BC", string? proponent = "Prop")
        => new(
            id, name, province, "Healthcare", null, "RFP", proponent, cost, null,
            null, null, verdict, korAngle, "status", "desc", 0.8,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            BdVerdicts.IsUrgent(verdict, korAngle));

    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-09T20:00:00Z");

    [Fact]
    public void Build_ProducesCanonicalSectionSkeleton()
    {
        var rows = new[]
        {
            Row(1, "Urgent One", BdVerdicts.PursueUrgent, "teams forming"),
            Row(2, "Pursue One", BdVerdicts.Pursue, "open seat"),
            Row(3, "Monitor One", BdVerdicts.Monitor, "locked now"),
            Row(4, "Discover One", BdVerdicts.Discover),
            Row(5, "Dead One", BdVerdicts.Dead, "Alliance locked"),
            Row(6, "NotHoned One", null),
        };

        var doc = SectorReportGenerator.Build(Definition, Prose, rows, GeneratedAt);

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => $"{h.Level}:{h.Text}").ToList();
        Assert.Equal(new[]
        {
            "1:KOR Structural — BC + AB Hospitals BD Report",
            "2:Executive Summary",
            "2:URGENT — IMMEDIATE action required",
            "3:1. Urgent One",
            "2:PURSUE — Open opportunities (not yet urgent)",
            "2:MONITOR — Current phase locked, future phases open",
            "2:DISCOVER — Pre-procurement relationship-build",
            "2:DEAD — Locked (Alliance / P3 / captive / Delivered)",
            "2:Strategic Synthesis",
            "3:Pattern one",
            "2:Recommended next actions",
        }, headings);

        // No DUPLICATE section when the bucket is empty.
        Assert.DoesNotContain(headings, h => h.Contains("DUPLICATE"));
    }

    [Fact]
    public void Build_ExecSummaryCountsExcludeNotHoned_AndUrgencyReclassifies()
    {
        var rows = new[]
        {
            Row(1, "Urgent By Verdict", BdVerdicts.PursueUrgent),
            Row(2, "Urgent By Angle", BdVerdicts.Pursue, "URGENT — act now"),
            Row(3, "Plain Pursue", BdVerdicts.Pursue, "open"),
            Row(4, "Not Honed", null),
        };

        var doc = SectorReportGenerator.Build(Definition, Prose, rows, GeneratedAt);

        // First-wins lookup: URGENT/PURSUE entries later in the doc reuse
        // labels like "Id: ", so a strict ToDictionary would collide.
        var labels = doc.Blocks.OfType<LabelValueBlock>()
            .GroupBy(x => x.Label)
            .ToDictionary(g => g.Key, g => g.First().Value);

        Assert.Equal("3", labels["Hospitals & Healthcare projects honed: "]);
        Assert.Equal("2", labels["PURSUE_URGENT — IMMEDIATE action: "]);
        Assert.Equal("1", labels["PURSUE — open opportunities: "]);
    }

    [Fact]
    public void Build_DuplicateSectionOnlyWhenPresent_AndTablePopulated()
    {
        var rows = new[] { Row(9, "Dup One", BdVerdicts.Duplicate, "same as 4570") };

        var doc = SectorReportGenerator.Build(Definition, Prose, rows, GeneratedAt);

        var dupHeading = doc.Blocks.OfType<HeadingBlock>().SingleOrDefault(h => h.Text.StartsWith("DUPLICATE"));
        Assert.NotNull(dupHeading);

        var tables = doc.Blocks.OfType<TableBlock>().ToList();
        var dupTable = tables.Single(t => t.Headers.Contains("Why DUPLICATE"));
        Assert.Equal("9", dupTable.Rows[0][0]);
        Assert.Equal("same as 4570", dupTable.Rows[0][3]);
    }

    [Fact]
    public void Build_EmptyBuckets_RenderNoneParagraphInsteadOfEmptyTable()
    {
        var rows = new[] { Row(1, "Only Pursue", BdVerdicts.Pursue, "open") };

        var doc = SectorReportGenerator.Build(Definition, Prose, rows, GeneratedAt);

        // URGENT/MONITOR/DISCOVER/DEAD buckets are all empty: no tables, four
        // "None" placeholders.
        Assert.Empty(doc.Blocks.OfType<TableBlock>());
        Assert.Equal(4, doc.Blocks.OfType<ParagraphBlock>().Count(p => p.Text == "None in this honing pass."));
    }

    [Fact]
    public void Build_StampsGeneratedAtAndProseIntro()
    {
        var doc = SectorReportGenerator.Build(Definition, Prose, Array.Empty<PursuitBriefRow>(), GeneratedAt);

        var intro = (ItalicNoteBlock)doc.Blocks[1];
        Assert.StartsWith("Intro framing.", intro.Text);
        Assert.Contains("2026-06-09 20:00 UTC", intro.Text);
        Assert.Contains("live KorOpportunitiesDb", intro.Text);
    }

    [Fact]
    public void Build_TruncatesLongAnglesPerBuilderCaps()
    {
        var longAngle = new string('x', 700);
        var rows = new[] { Row(1, "Monitor Long", BdVerdicts.Monitor, longAngle) };

        var doc = SectorReportGenerator.Build(Definition, Prose, rows, GeneratedAt);

        var monitorTable = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(110, monitorTable.Rows[0][5].Length);
    }

    [Fact]
    public void ProseCatalog_CoversEverySectorDefinition()
    {
        foreach (var def in SectorReportDefinitionCatalog.All)
        {
            var prose = SectorReportProseCatalog.For(def.Key);
            Assert.False(string.IsNullOrWhiteSpace(prose.IntroNote));
        }
    }

    [Fact]
    public void ProseCatalog_UnknownKeyThrows()
    {
        Assert.Throws<ArgumentException>(() => SectorReportProseCatalog.For("nope"));
    }
}
