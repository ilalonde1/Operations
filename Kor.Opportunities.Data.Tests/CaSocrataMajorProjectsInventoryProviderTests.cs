using Kor.Opportunities.Data.Ingestion.Providers;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Covers the {year} expansion of a year-stamped source URL, nothing else: not the
/// 403/404 fallback to last year's file, not the CSV mapping. A file whose NAME changes
/// shape (approvals_created_FY27_...) would escape, and so would a source that carries
/// the year as a literal again -- the migration, not this test, is what removed it.
/// </summary>
public sealed class CaSocrataMajorProjectsInventoryProviderTests
{
    private const string Template = "https://seshat.datasd.org/development_permits/approvals_created_{year}_datasd.csv";

    [Theory]
    [InlineData(Template, 2026, "https://seshat.datasd.org/development_permits/approvals_created_2026_datasd.csv")]
    [InlineData(Template, 2027, "https://seshat.datasd.org/development_permits/approvals_created_2027_datasd.csv")]
    [InlineData("https://seshat.datasd.org/development_permits/approvals_created_{YEAR}_datasd.csv", 2026,
                "https://seshat.datasd.org/development_permits/approvals_created_2026_datasd.csv")]
    [InlineData("https://example.org/no-token.csv", 2026, "https://example.org/no-token.csv")]
    public void ResolveBaseUrl_ExpandsTheYearToken(string template, int year, string expected)
        => Assert.Equal(expected, CaSocrataMajorProjectsInventoryProvider.ResolveBaseUrl(template, year));

    [Fact]
    public void ResolveBaseUrl_LeavesNoTokenBehind()
    {
        // The defect was a literal 2026 that nothing rewrote; the cure must not leave the
        // token in the request either.
        var resolved = CaSocrataMajorProjectsInventoryProvider.ResolveBaseUrl(Template, 2030);
        Assert.DoesNotContain("{year}", resolved);
        Assert.Contains("2030", resolved);
    }
}
