#nullable enable
using System.Collections.Generic;
using Kor.Operations.App.Options;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.App.Tests.Financials;

public sealed class FinancialAsOfLabelTests
{
    [Fact]
    public void BilledSurfaceShowsAsOfLabelFromMaxBilledPeriod()
    {
        var vm = new BilledFinancialsViewModel(new FinancialsOptions());
        vm.ApplySummary(new BilledFinancialsResult(
            Periods: [202605],
            PeriodColumnNames: ["May 26"],
            Lines:
            [
                new BilledLine("Revenue", "Total Revenue", "GrandTotal", 0, 0, new Dictionary<int, decimal> { [202605] = 100m }),
                new BilledLine("Expenses", "Total Expenses", "GrandTotal", 1, 0, new Dictionary<int, decimal> { [202605] = 25m }),
                new BilledLine("Net", "Net Income", "GrandTotal", 2, 0, new Dictionary<int, decimal> { [202605] = 75m }),
            ],
            NetIncomeTrendValues: [],
            RevenueTrendValues: [],
            ExpenseTrendValues: [],
            TrendLabels: [],
            MaxBilledPeriod: 202605,
            Reconciliation: new BilledPostedReconciliation(100m, 95m, 5m, 202605)));

        Assert.Contains("Billed through May 2026", vm.AsOfLabel);
    }

    [Fact]
    public void PartnerSurfaceShowsAsOfLabelFromLastActivityPeriod()
    {
        var vm = new PartnerFinancialsViewModel(
            new BilledFinancialsService(new DeltekOdbcOptions(), new FinancialsOptions()),
            new FinancialsOptions());

        vm.ApplyRows(
        [
            new BilledFinancialsService.PartnerBilledRevenueRow(
                "1", "Partner", "30000", "Project", 202605, "CAD", 100m)
        ]);

        Assert.Contains("Billed through May 2026", vm.AsOfLabel);
    }
}
