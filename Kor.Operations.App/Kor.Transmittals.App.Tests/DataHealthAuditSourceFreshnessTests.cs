using System.Collections;
using System.Reflection;
using System.Text;
using Kor.Opportunities.Worker.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class DataHealthAuditSourceFreshnessTests
{
    [Fact]
    public void CoverageQuery_ReusesQ9SourceUniverseWithoutItsAlertThresholds()
    {
        var q9 = (string)typeof(DataHealthAuditJob).GetField("Q9SourceStalenessSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var sourceRuns = q9[..q9.IndexOf("SourceProduction AS (", StringComparison.Ordinal)].TrimEnd().TrimEnd(',');
        Assert.StartsWith(sourceRuns, DataHealthAuditJob.SourceCoverageSql);
        Assert.Contains("LEFT JOIN SourceRuns sr ON sr.Id = s.Id", DataHealthAuditJob.SourceCoverageSql);
        Assert.DoesNotContain("RunsAllTime >= 10", DataHealthAuditJob.SourceCoverageSql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Report_WithOrWithoutAlerts_PrintsDenominatorAndNamedExclusions(bool hasAlert)
    {
        var report = Render(hasAlert,
        [
            new("Polled", 20, false, true),
            new("Archive", 2, true, false),
            new("Outreach", 7, false, false),
            new("MPI", 18, false, false),
            new("Future", 12345, false, true)
        ]);

        Assert.Contains("1 of 5 enabled sources evaluated", report);
        Assert.Contains("| Archive | Historical archive", report);
        Assert.Contains("| Outreach | Manual BD outreach", report);
        Assert.Contains("| MPI | Internal MPI upserts", report);
        Assert.Contains("| Future | UNEVALUATED: unrecognized source type 12345", report);
        Assert.DoesNotContain("All enabled sources produced", report);
        Assert.Contains("## J. MPI source freshness", report);
        if (!hasAlert)
            Assert.Contains("No Q9 staleness thresholds were met", report);
    }

    [Fact]
    public void EmptyUniverse_DoesNotClaimAllSourcesProduced()
    {
        var report = Render(false, []);
        Assert.Contains("0 of 0 enabled sources evaluated", report);
        Assert.DoesNotContain("✔", report);
    }

    [Theory]
    [InlineData(21, 21, 14, "Within windows")]
    [InlineData(21, 22, 4, "DEAD-GREEN — review (fetch current, no new projects)")]
    [InlineData(92, 92, 4, "Within windows")]
    [InlineData(92, 98, 4, "DEAD-GREEN — review (fetch current, no new projects)")]
    [InlineData(92, 136, 4, "DEAD-GREEN — review (fetch current, no new projects)")]
    [InlineData(92, 26, 18, "REVIEW — fetch overdue")]
    public void ProductionAndFetchWindows_AreIndependentReviewPrompts(int window, int productionAge, int fetchAge, string expected)
    {
        Assert.Equal(expected, DataHealthAuditJob.MpiVerdict(MpiRow(window, productionAge, fetchAge)));
    }

    [Fact]
    public void NeverProduced_AndUnknownMpiFeed_AreVisible()
    {
        var empty = MpiRow(21, 0, 0) with { Rows = 0, LiveRows = 0, LastProducedUtc = null, LastSeenUtc = null, DaysSinceProduced = null, DaysSinceSeen = null };
        Assert.Equal("NEVER-PRODUCED — review", DataHealthAuditJob.MpiVerdict(empty));
        var unknown = empty with { Name = "FutureMPI", ProductionDays = null, FetchDays = null, KeyPrefix = "" };
        var sb = new StringBuilder();
        DataHealthAuditJob.AppendMpiFreshness(sb, [empty, unknown]);
        Assert.Contains("1 of 2 enabled type-18 sources evaluated", sb.ToString());
        Assert.Contains("FutureMPI", sb.ToString());
        Assert.Contains("UNEVALUATED — no source attribution/cadence policy", sb.ToString());
    }

    [Fact]
    public void AlbertaAt98Days_RendersReviewAndCoverageLimits_NotAnError()
    {
        var sb = new StringBuilder();
        DataHealthAuditJob.AppendMpiFreshness(sb, [MpiRow(92, 98, 4) with { Name = "AB_MajorProjectsInventory" }]);
        var report = sb.ToString();
        Assert.Contains("92 / 14 | DEAD-GREEN — review", report);
        Assert.Contains("Gaps are review prompts, not errors", report);
        Assert.Contains("A feed losing columns or pages while still inserting rows can pass", report);
        Assert.Contains("including retired rows", report);
    }

    private static DataHealthAuditJob.MpiFreshnessRow MpiRow(int window, int productionAge, int fetchAge)
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        return new("TestMPI", "AB", "AB-", window, 14, false, 835, 308,
            now.AddDays(-productionAge), now.AddDays(-fetchAge), productionAge, fetchAge);
    }

    // Exercise the actual full renderer, supplying empty unrelated sections.
    private static string Render(bool hasAlert, List<DataHealthAuditJob.SourceCoverageRow> coverage)
    {
        var method = typeof(DataHealthAuditJob).GetMethod("BuildMarkdown", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = method.GetParameters().Select(p => p.ParameterType.IsGenericType
            ? Activator.CreateInstance(typeof(List<>).MakeGenericType(p.ParameterType.GetGenericArguments()))
            : p.ParameterType == typeof(DateTimeOffset) ? (object)DateTimeOffset.UtcNow
            : p.ParameterType == typeof(long) ? 0L : "test").ToArray();
        var parameters = method.GetParameters();
        args[Array.FindIndex(parameters, p => p.Name == "sourceCoverage")] = coverage;
        if (hasAlert)
        {
            var stale = (IList)args[Array.FindIndex(parameters, p => p.Name == "staleSources")]!;
            stale.Add(Activator.CreateInstance(stale.GetType().GetGenericArguments()[0], "Polled", "DEAD-GREEN", "2026-01-01", "2026-10-08", 7L));
        }
        return (string)method.Invoke(null, args)!;
    }
}
