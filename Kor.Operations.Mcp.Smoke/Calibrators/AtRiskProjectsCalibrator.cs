#nullable enable
using Kor.Operations.Financials;
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class AtRiskProjectsCalibrator : CalibratorBase
{
    public AtRiskProjectsCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var rows = await Task.Run(() => svc.LoadProjectRowsSync(ct), ct).ConfigureAwait(false);
        var threshold = AnalyticsThresholds.OverBudgetFactor;
        var atRiskCount = rows.Count(r => r.EstEngBudget > 0 && r.EngHrs > r.EstEngBudget * threshold);

        return new CalibratedExpectation(
            "At-risk project count",
            [new ExpectedToolCall("get_at_risk_projects", [])],
            [new ExpectedAnswerValue("at-risk project count", atRiskCount)]);
    }
}
