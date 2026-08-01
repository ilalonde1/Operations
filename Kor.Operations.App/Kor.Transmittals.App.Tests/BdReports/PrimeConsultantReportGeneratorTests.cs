#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class PrimeConsultantReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = DateTimeOffset.Parse("2026-06-10T01:00:00Z");

    private static PrimeConsultantRow Row(long id, string name, string? firm, decimal cost, bool knownToKor = false)
        => new(id, name, "BC", "Owner", $"${cost / 1_000_000m:N0}M", cost, firm, null, null, knownToKor, null, "angle", 0.8);

    [Fact]
    public void Build_SplitsIdentifiedAlignedAndUnknown_AndGroupsHeatmap()
    {
        var projects = new[]
        {
            Row(1, "Big Known", "EllisDon Design Build", 1_000_000_000m, knownToKor: true),
            Row(2, "Mid Identified", "EllisDon Design Build", 500_000_000m),
            Row(3, "Small Identified", "Hariri Pontarini", 100_000_000m),
            Row(4, "Unknown Prime", null, 800_000_000m),
        };

        var doc = PrimeConsultantReportGenerator.Build(projects, GeneratedAt);

        var labels = doc.Blocks.OfType<LabelValueBlock>()
            .GroupBy(x => x.Label).ToDictionary(g => g.Key, g => g.First().Value);
        Assert.Equal("4", labels["Projects researched: "]);
        Assert.Equal("3 (75%)", labels["Prime consultant identified: "]);
        Assert.Equal("1 (25%)", labels["Prime unknown after research: "]);
        Assert.Equal("1", labels["KOR-aligned primes (warm-intro available): "]);

        var tables = doc.Blocks.OfType<TableBlock>().ToList();
        Assert.Equal(4, tables.Count); // aligned, top-30, heatmap, unknown

        Assert.Equal("Big Known", tables[0].Rows[0][1]);                  // KOR-aligned
        Assert.Equal("EllisDon Design Build", tables[2].Rows[0][0]);      // heatmap top firm
        Assert.Equal("2", tables[2].Rows[0][1]);                          // 2 projects
        Assert.Equal("Unknown Prime", tables[3].Rows[0][1]);              // unknown table

        var headings = doc.Blocks.OfType<HeadingBlock>().Select(h => h.Text).ToList();
        Assert.Contains(headings, h => h == "1. Big Known");
        Assert.Contains("Strategic Synthesis", headings);
    }

    [Fact]
    public void Parser_ReadsPrimeConsultantContract()
    {
        const string json = """
        {
          "overallConfidence": 0.78,
          "description": "...",
          "korAngle": "the angle",
          "primeConsultant": {
            "firmName": "Station One Architects",
            "principalInCharge": "",
            "contactEmail": "x@y.com",
            "confidence": 0.55,
            "knownToKor": false,
            "korRelationshipNotes": "notes"
          }
        }
        """;

        var result = PrimeConsultantParser.Parse(json);

        Assert.Equal("Station One Architects", result.FirmName);
        Assert.Null(result.PrincipalInCharge); // empty string = missing
        Assert.Equal("x@y.com", result.ContactEmail);
        Assert.False(result.KnownToKor);
        Assert.Equal("notes", result.KorRelationshipNotes);
        Assert.Equal("the angle", result.KorAngle);
        Assert.Equal(0.55, result.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    public void Parser_MalformedOrEmpty_ReturnsEmptyNoThrow(string? json)
    {
        var result = PrimeConsultantParser.Parse(json);

        Assert.Null(result.FirmName);
        Assert.False(result.KnownToKor);
    }
}
