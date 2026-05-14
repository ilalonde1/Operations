#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class ProjectYoYTrendCalibrator : CalibratorBase
{
    public ProjectYoYTrendCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var rows = await Task.Run(() => svc.LoadProjectRowsSync(ct), ct).ConfigureAwait(false);
        var years = YearTrendService.Build(rows, firmUtilization: null);
        var mostRecent = years.FirstOrDefault();

        if (mostRecent == null)
        {
            return new CalibratedExpectation(
                "YoY trend (no rows)",
                [new ExpectedToolCall("get_project_yoy_trend", [])],
                []);
        }

        return new CalibratedExpectation(
            $"Most-recent-year total fee ({mostRecent.Year})",
            [new ExpectedToolCall("get_project_yoy_trend", [])],
            [new ExpectedAnswerValue($"{mostRecent.Year} total fee", (decimal)mostRecent.TotalFee)]);
    }
}
