#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class AlertPathParityTests
{
    [Theory]
    [InlineData(100.0)]
    [InlineData(-100.0)]
    public void Ar60_AlertProjectFilter_MatchesKpiProjectFilter(double agedValue)
    {
        var arRows = new[]
        {
            new AppFin.ArProjectOutstandingRow("P-CREDIT", "Credit", "PM", agedValue, 0, 0, 0, agedValue, null)
        };
        var deltek = SyntheticDeltekData.Default(arProjectRows: arRows);

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "AR > 60 Days");
        var alert = ExecutiveSummaryTestSupport.Alert(result, "AR > 60 Days");

        Assert.Contains(kpi.ArOutstandingRows!, r => r.Wbs1 == "P-CREDIT");
        Assert.Contains(alert.ArOutstandingRows!, r => r.Wbs1 == "P-CREDIT");
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(-100.0)]
    public void Ar60_AlertInvoiceFilter_MatchesKpiInvoiceFilter(double balance)
    {
        var arRows = new[]
        {
            new AppFin.ArProjectOutstandingRow("P-CREDIT", "Credit", "PM", balance, 0, 0, 0, balance, null)
        };
        var invoices = new[]
        {
            new AppFin.ArInvoiceOutstandingRow("P-CREDIT", "Credit", "PM", "INV-9001", "C-CREDIT", "Credit Co.", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), 90, balance)
        };
        var deltek = SyntheticDeltekData.Default(arProjectRows: arRows, arInvoiceRows: invoices);

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);
        var kpi = ExecutiveSummaryTestSupport.Kpi(result, "AR > 60 Days");
        var alert = ExecutiveSummaryTestSupport.Alert(result, "AR > 60 Days");

        Assert.Contains(kpi.ArInvoiceRows!, r => r.Wbs1 == "P-CREDIT");
        Assert.Contains(alert.ArInvoiceRows!, r => r.Wbs1 == "P-CREDIT");
    }
}
