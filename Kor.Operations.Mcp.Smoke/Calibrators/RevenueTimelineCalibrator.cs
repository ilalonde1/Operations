#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class RevenueTimelineCalibrator : CalibratorBase
{
    public RevenueTimelineCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var byWbs = await Task.Run(() => svc.LoadRevenueTimelineSync(ct), ct).ConfigureAwait(false);

        var firmwide = byWbs
            .SelectMany(kvp => kvp.Value)
            .GroupBy(p => p.Period, StringComparer.Ordinal)
            .Select(g => new { Period = g.Key, Revenue = g.Sum(p => p.Revenue) })
            .OrderByDescending(p => p.Period, StringComparer.Ordinal)
            .ToList();

        var mostRecent = firmwide.FirstOrDefault();
        if (mostRecent == null)
        {
            return new CalibratedExpectation(
                "Revenue timeline (no data)",
                [new ExpectedToolCall("get_revenue_timeline", [])],
                []);
        }

        return new CalibratedExpectation(
            $"Most-recent-period firmwide revenue ({mostRecent.Period})",
            [new ExpectedToolCall("get_revenue_timeline", [])],
            [new ExpectedAnswerValue($"{mostRecent.Period} firmwide revenue", (decimal)mostRecent.Revenue)]);
    }
}
