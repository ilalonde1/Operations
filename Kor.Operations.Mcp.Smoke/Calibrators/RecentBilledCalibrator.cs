#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class RecentBilledCalibrator : CalibratorBase
{
    public RecentBilledCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new RecentBilledService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Latest 3-period billed total",
            [new ExpectedToolCall("get_earned_vs_invoiced", [])],
            [new ExpectedAnswerValue("billed90", (decimal)result.Billed90)]);
    }
}
