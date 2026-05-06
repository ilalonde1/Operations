#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class EmptyScopeTests
{
    [Theory]
    [InlineData("Backlog")]
    [InlineData("Billings To Date")]
    public void EmptyWatchlist_ScopedKpis_ReturnDataUnavailable(string title)
    {
        var result = ExecutiveSummaryTestSupport.Build(
            snap: SyntheticSnapshot.Empty(),
            deltek: SyntheticDeltekData.Default());

        Assert.Equal("Data unavailable", ExecutiveSummaryTestSupport.Kpi(result, title).ValueText);
    }

    [Theory]
    [InlineData("Cash Position")]
    [InlineData("AR Outstanding")]
    [InlineData("Liquidity (Cash + AR)")]
    public void EmptyWatchlist_FirmwideKpis_StillPopulated(string title)
    {
        var arRows = new[]
        {
            new AppFin.ArProjectOutstandingRow("P001", "A", "PM", 250, 250, 0, 0, 0, null)
        };
        var result = ExecutiveSummaryTestSupport.Build(
            snap: SyntheticSnapshot.Empty(),
            deltek: SyntheticDeltekData.Default(arProjectRows: arRows));

        Assert.NotEqual("Data unavailable", ExecutiveSummaryTestSupport.Kpi(result, title).ValueText);
    }

    [Theory]
    [InlineData("Revenue (Earned) (latest 1 / 3 periods)")]
    [InlineData("Billings (Invoiced) (latest 1 / 3 periods)")]
    public void EmptyWatchlist_RevenueAndBillingsTrends_ReturnDataUnavailable(string title)
    {
        var result = ExecutiveSummaryTestSupport.Build(
            snap: SyntheticSnapshot.Empty(),
            deltek: SyntheticDeltekData.Default());

        Assert.Equal("Data unavailable", ExecutiveSummaryTestSupport.Trend(result, title).ValueText);
    }
}
