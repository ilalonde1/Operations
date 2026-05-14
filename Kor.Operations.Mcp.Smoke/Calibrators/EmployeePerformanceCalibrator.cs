#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class EmployeePerformanceCalibrator : CalibratorBase
{
    public EmployeePerformanceCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var projectSvc = new ProjectAnalyticsService(Odbc, Financials);
        var employeeSvc = new EmployeeAnalyticsService(Odbc);
        var projectsTask = Task.Run(() => projectSvc.LoadProjectRowsSync(ct), ct);
        var hoursTask = Task.Run(() => employeeSvc.LoadEmployeeProjectHoursSync(ct), ct);
        await Task.WhenAll(projectsTask, hoursTask).ConfigureAwait(false);

        var scored = EmployeePerformanceService.Build(
            projectsTask.Result,
            hoursTask.Result,
            Array.Empty<string>());
        var top = scored.OrderByDescending(r => r.ProductivityScore).FirstOrDefault();

        var values = top != null
            ? new[] { new ExpectedAnswerValue($"{top.EmployeeName} productivity score", (decimal)top.ProductivityScore) }
            : Array.Empty<ExpectedAnswerValue>();

        return new CalibratedExpectation(
            "Top employee by Productivity Score",
            [new ExpectedToolCall("get_employee_performance", [])],
            values);
    }
}
