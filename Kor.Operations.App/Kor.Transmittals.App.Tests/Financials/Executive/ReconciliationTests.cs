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
    public void Billings_IncludesUnpostedFeeBilledOverlay()
    {
        // Real-world scenario: PRSummaryMain posted FeeBilled lags ~3 months at KOR.
        // UnpostedFeeBilled is the LedgerAR overlay capturing invoices already cut
        // but not yet rolled up into PRSummaryMain. The Billings KPI must include
        // that overlay so the dashboard shows real-time invoicing state, not
        // posted-only state.
        var rows = new List<AppFin.FinancialsProjectRow>
        {
            new() { Wbs1 = "P001", Name = "A", Pm = "PM", Fee = 100_000, FeeBilled = 40_000, UnpostedFeeBilled = 15_000, Org = "CAD", PercentBilled = 0.4 },
            new() { Wbs1 = "P002", Name = "B", Pm = "PM", Fee = 200_000, FeeBilled = 100_000, UnpostedFeeBilled = 25_000, Org = "CAD", PercentBilled = 0.5 }
        };
        var snap = new AppFin.FinancialsSnapshot
        {
            RefreshedAt = new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero),
            Rows = rows,
            UsdToCadRate = 1.36,
            Headline = AppFin.FinancialsHeadlineCalculator.Compute(rows, 1.36)
        };

        var result = ExecutiveSummaryTestSupport.Build(snap);
        var billings = ExecutiveSummaryTestSupport.Kpi(result, "Billings To Date");
        var backlog = ExecutiveSummaryTestSupport.Kpi(result, "Backlog");

        // Billings tile == TotalFeeBilledWithUnposted ($140k + $25k unposted = $180k)
        Assert.Equal(snap.Headline.TotalFeeBilledWithUnposted.ToString("C0", CultureInfo.CurrentCulture), billings.ValueText);
        Assert.Equal(180_000.0, snap.Headline.TotalFeeBilledWithUnposted, 6);

        // Backlog tile == Fees − WithUnposted ($300k − $180k = $120k), not posted-only ($300k − $140k = $160k)
        Assert.Equal(snap.Headline.TotalUnbilled.ToString("C0", CultureInfo.CurrentCulture), backlog.ValueText);
        Assert.Equal(120_000.0, snap.Headline.TotalUnbilled, 6);

        // Drilldown rows reconcile to headline
        var rowSumBilled = billings.BillingsRows!.Sum(r => r.FeeBilledWithUnposted);
        ExecutiveSummaryTestSupport.AssertClose(snap.Headline.TotalFeeBilledWithUnposted, rowSumBilled);
        var rowSumBacklog = backlog.BacklogRows!.Sum(r => r.Backlog);
        ExecutiveSummaryTestSupport.AssertClose(snap.Headline.TotalUnbilled, rowSumBacklog);
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
