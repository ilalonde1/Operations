#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class UtilizationCalibrator : CalibratorBase
{
    public UtilizationCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new UtilizationService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Firmwide 30-day billable utilization",
            [new ExpectedToolCall("get_utilization", [])],
            [new ExpectedAnswerValue("utilization", (decimal)result.Pct)]);
    }
}
