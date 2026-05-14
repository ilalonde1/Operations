#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class DmPerformanceCalibrator : CalibratorBase
{
    public DmPerformanceCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var rows = await Task.Run(() => svc.LoadProjectRowsSync(ct), ct).ConfigureAwait(false);
        var scored = DmPerformanceService.Build(rows);
        var top = scored.OrderByDescending(r => r.PerformanceScore).FirstOrDefault();

        var values = top != null
            ? new[] { new ExpectedAnswerValue($"{top.Pm} performance score", (decimal)top.PerformanceScore) }
            : Array.Empty<ExpectedAnswerValue>();

        return new CalibratedExpectation(
            "Top DM by Performance Score",
            [new ExpectedToolCall("get_dm_performance", [])],
            values);
    }
}
