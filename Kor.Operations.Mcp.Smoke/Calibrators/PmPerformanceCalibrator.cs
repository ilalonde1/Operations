#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class PmPerformanceCalibrator : CalibratorBase
{
    public PmPerformanceCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var rows = await Task.Run(() => svc.LoadProjectRowsSync(ct), ct).ConfigureAwait(false);
        var scored = PmPerformanceService.Build(rows);
        var top = scored.OrderByDescending(r => r.PerformanceScore).FirstOrDefault();

        var values = top != null
            ? new[] { new ExpectedAnswerValue($"{top.Pm} performance score", (decimal)top.PerformanceScore) }
            : Array.Empty<ExpectedAnswerValue>();

        return new CalibratedExpectation(
            "Top PM by Performance Score",
            [new ExpectedToolCall("get_pm_performance", [])],
            values);
    }
}
