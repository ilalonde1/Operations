#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class ScopeKindTests
{
    [Theory]
    [InlineData("Cash Position")]
    [InlineData("Liquidity (Cash + AR)")]
    [InlineData("AR Outstanding")]
    [InlineData("AR > 60 Days")]
    [InlineData("Utilization")]
    [InlineData("WIP (Draft Invoices)")]
    public void Firmwide_Kpis_HaveFirmwideScope(string title)
    {
        var result = ExecutiveSummaryTestSupport.Build(deltek: SyntheticDeltekData.Default(revenueGenerationDetected: true));

        Assert.Equal(AppFin.ScopeKind.Firmwide, ExecutiveSummaryTestSupport.Kpi(result, title).Scope);
    }

    [Theory]
    [InlineData("WIP (Unbilled Earned)")]
    [InlineData("Backlog")]
    [InlineData("Billings To Date")]
    [InlineData("Budget Burn")]
    [InlineData("Portfolio Delivery Risk")]
    [InlineData("Projects Over Budget")]
    public void Scoped_Kpis_HaveScopedScope(string title)
    {
        var result = ExecutiveSummaryTestSupport.Build(deltek: SyntheticDeltekData.Default(revenueGenerationDetected: true));

        Assert.Equal(AppFin.ScopeKind.Scoped, ExecutiveSummaryTestSupport.Kpi(result, title).Scope);
    }

    [Theory]
    [InlineData("Revenue (Earned) (latest 1 / 3 periods)")]
    [InlineData("Billings (Invoiced) (latest 1 / 3 periods)")]
    [InlineData("AR Outstanding (Recent Months)")]
    [InlineData("Delivery Risk (Critical Count)")]
    public void Scoped_Trends_HaveScopedScope(string title)
    {
        var result = ExecutiveSummaryTestSupport.Build(deltek: SyntheticDeltekData.Default(revenueGenerationDetected: true));

        Assert.Equal(AppFin.ScopeKind.Scoped, ExecutiveSummaryTestSupport.Trend(result, title).Scope);
    }
}
