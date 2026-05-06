#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class ThresholdParityTests
{
    [Fact]
    public void BacklogDrilldown_IncludesNegativeBacklog()
    {
        var snap = SyntheticSnapshot.WithProjects(("P-CREDIT", "Credit", 100, 200, 10, 100, "CAD"));

        var result = ExecutiveSummaryTestSupport.Build(snap);

        Assert.Contains(ExecutiveSummaryTestSupport.Kpi(result, "Backlog").BacklogRows!, r => r.Wbs1 == "P-CREDIT" && r.Backlog < 0);
    }

    [Fact]
    public void BillingsDrilldown_IncludesNegativeFeeBilled()
    {
        var snap = SyntheticSnapshot.WithProjects(("P-CREDIT", "Credit", 100, -100, 10, 100, "CAD"));

        var result = ExecutiveSummaryTestSupport.Build(snap);

        Assert.Contains(ExecutiveSummaryTestSupport.Kpi(result, "Billings To Date").BillingsRows!, r => r.Wbs1 == "P-CREDIT" && r.FeeBilled < 0);
    }

    [Fact]
    public void Ar60KpiDrilldown_IncludesNegativeAgedBalance()
    {
        var deltek = SyntheticDeltekData.Default(arProjectRows: new[]
        {
            new AppFin.ArProjectOutstandingRow("P-CREDIT", "Credit", "PM", -100, 0, 0, 0, -100, null)
        });

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);

        Assert.Contains(ExecutiveSummaryTestSupport.Kpi(result, "AR > 60 Days").ArOutstandingRows!, r => r.Wbs1 == "P-CREDIT");
    }

    [Fact]
    public void Ar60AlertDrilldown_IncludesNegativeAgedBalance()
    {
        var deltek = SyntheticDeltekData.Default(arProjectRows: new[]
        {
            new AppFin.ArProjectOutstandingRow("P-CREDIT", "Credit", "PM", -100, 0, 0, 0, -100, null)
        });

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);

        Assert.Contains(ExecutiveSummaryTestSupport.Alert(result, "AR > 60 Days").ArOutstandingRows!, r => r.Wbs1 == "P-CREDIT");
    }
}
