using System.Text;
using Kor.Opportunities.Worker.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

/// <summary>
/// §K exists because the same measurement already lived in
/// tools/BdSourceCoverage/coverage_gap.py, hardcoded its held set as a python literal,
/// went stale, and was then re-derived from scratch a month later. These tests pin the
/// two properties that failure turned on: the held set is DERIVED, and an absent roster
/// asserts nothing rather than implying full coverage.
/// </summary>
public sealed class DataHealthAuditFootprintCoverageTests
{
    private static string Render(params DataHealthAuditJob.FootprintCoverageRow[] rows)
    {
        var sb = new StringBuilder();
        DataHealthAuditJob.AppendFootprintCoverage(sb, rows);
        return sb.ToString();
    }

    [Fact]
    public void HeldIsDerivedFromLiveSources_NotStoredAndNotHardcoded()
    {
        var sql = DataHealthAuditJob.FootprintCoverageSql;

        // The join to live sources is the whole point.
        Assert.Contains("opportunities.OpportunitySources", sql);
        Assert.Contains("s.IsEnabled = 1", sql);
        Assert.Contains("FootprintMunicipalities", sql);

        // Tender feeds are not coverage: BidsTenders_PortMoody existing is the illusion
        // this section was built to dispel.
        Assert.Contains("s.SourceType IN (2, 20, 21, 22)", sql);

        // No held set may be baked into the query.
        Assert.DoesNotContain("HAVE_APPLICATIONS", sql);
    }

    [Fact]
    public void CountsOnlyMunicipalitiesWithAMatchingSource()
    {
        var report = Render(
            new("Metro Vancouver", "Coquitlam", 148625, "Coquitlam_DevelopmentApplications"),
            new("Metro Vancouver", "Burnaby", 249125, null),
            new("Metro Vancouver", "Richmond", 209937, null));

        Assert.Contains("**1 of 3** in-footprint municipalities have an early-signal feed", report);
        // Only the two unheld populations are counted as uncovered.
        Assert.Contains("459,062 of 607,687 people live where we hold no application feed", report);
    }

    [Fact]
    public void RegionsAreRankedByUncoveredPopulation_AndGapsAreNamed()
    {
        var report = Render(
            new("Vancouver Island", "Victoria", 91867, "Victoria_DevelopmentApplications"),
            new("Vancouver Island", "Duncan", 5047, null),
            new("Metro Vancouver", "Burnaby", 249125, null),
            new("Metro Vancouver", "Richmond", 209937, null));

        var metro = report.IndexOf("| Metro Vancouver |", StringComparison.Ordinal);
        var island = report.IndexOf("| Vancouver Island |", StringComparison.Ordinal);
        Assert.True(metro > 0 && island > 0, "both regions render");
        Assert.True(metro < island, "the region with the most uncovered people comes first");

        Assert.Contains("| Burnaby | Metro Vancouver | 249,125 |", report);
        Assert.Contains("Largest gaps (3 of 3 unheld)", report);
    }

    [Fact]
    public void AnAbsentRosterAssertsNothing_AndNeverImpliesFullCoverage()
    {
        var report = Render();

        Assert.Contains("migration 333 has not run", report);
        Assert.Contains("This section asserts nothing", report);
        // The §I failure this whole change was about: a green claim on unread evidence.
        Assert.DoesNotContain("in-footprint municipalities have an early-signal feed", report);
        Assert.DoesNotContain("0 of 0", report);
    }

    [Fact]
    public void StatesWhatItDoesNotCover_IncludingASameClassFaultItWouldMiss()
    {
        var report = Render(new DataHealthAuditJob.FootprintCoverageRow(
            "Metro Vancouver", "Coquitlam", 148625, "Coquitlam_DevelopmentApplications"));

        // Configuration is not production -- a held source can be silent.
        Assert.Contains("does NOT mean the source is producing", report);
        Assert.Contains("counts configuration, not production", report);
        // The named same-class miss, per CLAUDE.md rule 11.
        Assert.Contains("zoning-overlay layer", report);
        Assert.Contains("ingests cleanly as nonsense and counts as held", report);
        // And the roster's own scope.
        Assert.Contains("56 of BC's 160 municipalities", report);
    }
}
