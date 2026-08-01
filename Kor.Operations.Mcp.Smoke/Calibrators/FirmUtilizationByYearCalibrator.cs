#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class FirmUtilizationByYearCalibrator : CalibratorBase
{
    public FirmUtilizationByYearCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new FirmAnalyticsService(Odbc);
        var stats = await Task.Run(() => svc.LoadFirmUtilizationSync(ct), ct).ConfigureAwait(false);
        var mostRecent = stats.ByYear
            .OrderByDescending(kvp => kvp.Key)
            .FirstOrDefault();

        if (mostRecent.Value.Total <= 0)
        {
            return new CalibratedExpectation(
                "Firm utilization by year (no data)",
                [new ExpectedToolCall("get_firm_utilization_by_year", [])],
                []);
        }

        var pct = mostRecent.Value.Billable / mostRecent.Value.Total;
        return new CalibratedExpectation(
            $"Most-recent-year firm billable pct ({mostRecent.Key})",
            [new ExpectedToolCall("get_firm_utilization_by_year", [])],
            [new ExpectedAnswerValue($"{mostRecent.Key} billablePct", (decimal)pct)]);
    }
}
