#nullable enable
using AppFin = Kor.Operations.Financials;

namespace Kor.Operations.Tests.Financials.Executive;

internal static class ExecutiveSummaryTestSupport
{
    public static AppFin.ExecutiveSummaryResult Build(
        AppFin.FinancialsSnapshot? snap = null,
        AppFin.ExecutiveSummaryDeltekData? deltek = null,
        AppFin.UtilizationRow[]? util = null)
        => AppFin.ExecutiveSummaryService.Build(
            snap ?? SyntheticSnapshot.WithProjects(("P001", "Project 1", 100_000, 20_000, 10, 100, "CAD")),
            trend: null,
            util,
            deltek ?? SyntheticDeltekData.Default());

    public static AppFin.ExecutiveKpi Kpi(AppFin.ExecutiveSummaryResult result, string title)
        => result.Kpis.Single(k => string.Equals(k.Title, title, StringComparison.Ordinal));

    public static AppFin.ExecutiveTrend Trend(AppFin.ExecutiveSummaryResult result, string title)
        => result.Trends.Single(t => string.Equals(t.Title, title, StringComparison.Ordinal));

    public static AppFin.ExecutiveAlert Alert(AppFin.ExecutiveSummaryResult result, string title)
        => result.Alerts.Single(a => string.Equals(a.Title, title, StringComparison.Ordinal));

    public static void AssertClose(double expected, double actual, int precision = 2)
        => Xunit.Assert.Equal(expected, actual, precision);
}
