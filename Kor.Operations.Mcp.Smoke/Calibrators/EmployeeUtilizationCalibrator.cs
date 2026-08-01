#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class EmployeeUtilizationCalibrator : CalibratorBase
{
    public EmployeeUtilizationCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new EmployeeAnalyticsService(Odbc);
        var weekly = await Task.Run(() => svc.LoadEmployeeWeeklyUtilizationSync(ct), ct).ConfigureAwait(false);
        var totalBillable = weekly.Sum(r => r.BillableHrs);
        var totalHours = weekly.Sum(r => r.TotalHrs);
        var firmwidePct = totalHours > 0 ? totalBillable / totalHours : 0.0;

        return new CalibratedExpectation(
            "Firmwide last-12-weeks billable utilization",
            [new ExpectedToolCall("get_employee_utilization", [])],
            [new ExpectedAnswerValue("firmwide billable pct", (decimal)firmwidePct)]);
    }
}
