#nullable enable
using System.Globalization;
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class ReconciliationTests
{
    [Fact]
    public void Backlog_HeadlineEqualsSumOfRows()
    {
        var snap = SyntheticSnapshot.WithProjects(
            ("P001", "A", 100_000, 40_000, 10, 100, "CAD"),
            ("P002", "B", 80_000, 90_000, 20, 100, "CAD"),
            ("P003", "C", 50_000, 10_000, 30, 100, "CAD"),
            ("P004", "D", 200_000, 150_000, 40, 100, "CAD"),
            ("P005", "E", 25_000, 5_000, 50, 100, "CAD"));

        var result = ExecutiveSummaryTestSupport.Build(snap);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "Backlog");
        var rowSum = kpi.BacklogRows!.Sum(r => r.Backlog);

        Assert.Equal(snap.Headline.TotalUnbilled.ToString("C0", CultureInfo.CurrentCulture), kpi.ValueText);
        ExecutiveSummaryTestSupport.AssertClose(snap.Headline.TotalUnbilled, rowSum);
    }

    [Fact]
    public void BillingsToDate_HeadlineEqualsSumOfRows()
    {
        var snap = SyntheticSnapshot.WithProjects(
            ("P001", "A", 100_000, 40_000, 10, 100, "CAD"),
            ("P002", "B", 80_000, -5_000, 20, 100, "CAD"),
            ("P003", "C", 50_000, 10_000, 30, 100, "CAD"),
            ("P004", "D", 200_000, 150_000, 40, 100, "CAD"),
            ("P005", "E", 25_000, 5_000, 50, 100, "CAD"));

        var result = ExecutiveSummaryTestSupport.Build(snap);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "Billings To Date");
        var rowSum = kpi.BillingsRows!.Sum(r => r.FeeBilled);

        Assert.Equal(snap.Headline.TotalFeeBilled.ToString("C0", CultureInfo.CurrentCulture), kpi.ValueText);
        ExecutiveSummaryTestSupport.AssertClose(snap.Headline.TotalFeeBilled, rowSum);
    }

    [Fact]
    public void BudgetBurn_HeadlineEqualsRecomputedRatio()
    {
        var snap = SyntheticSnapshot.WithProjects(
            ("P001", "A", 100_000, 40_000, 40, 100, "CAD"),
            ("P002", "B", 80_000, 40_000, 60, 100, "CAD"),
            ("P003", "C", 50_000, 10_000, 20, 50, "CAD"));

        var result = ExecutiveSummaryTestSupport.Build(snap);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "Budget Burn");
        var rows = kpi.BudgetBurnRows!;
        var expected = rows.Sum(r => r.EngHours) / rows.Sum(r => r.EngBudget);

        Assert.Equal(expected.ToString("P1", CultureInfo.CurrentCulture), kpi.ValueText);
        ExecutiveSummaryTestSupport.AssertClose(expected, rows.Sum(r => r.EngHours) / rows.Sum(r => r.EngBudget), precision: 6);
    }

    [Fact]
    public void ArOutstanding_HeadlineEqualsArProjectRowsSum()
    {
        var arRows = new[]
        {
            new AppFin.ArProjectOutstandingRow("P001", "A", "PM", 100, 70, 20, 10, 0, null),
            new AppFin.ArProjectOutstandingRow("P002", "B", "PM", -25, -25, 0, 0, 0, null),
            new AppFin.ArProjectOutstandingRow("P003", "C", "PM", 50, 0, 0, 20, 30, null)
        };
        var deltek = SyntheticDeltekData.Default(arProjectRows: arRows);

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "AR Outstanding");
        var rowSum = kpi.ArOutstandingRows!.Sum(r => r.Total);

        Assert.Equal(deltek.ArFirmwideOutstanding.ToString("C0", CultureInfo.CurrentCulture), kpi.ValueText);
        ExecutiveSummaryTestSupport.AssertClose(deltek.ArFirmwideOutstanding, rowSum);
    }

    [Fact]
    public void Cash_HeadlineEqualsSumOfAccounts()
    {
        const double cashFx = 1.36;
        var deltek = SyntheticDeltekData.Default(cashCad: 100_000, cashUsa: 50_000, cashBcc: 10_000, cashFx: cashFx);

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "Cash Position");
        var rowSum = kpi.CashAccountRows!.Sum(r => r.Currency == "USA" ? r.Balance * cashFx : r.Balance);

        Assert.Equal(deltek.CashCombinedCadEquivalent.ToString("C0", CultureInfo.CurrentCulture), kpi.ValueText);
        ExecutiveSummaryTestSupport.AssertClose(deltek.CashCombinedCadEquivalent, rowSum);
    }
}
