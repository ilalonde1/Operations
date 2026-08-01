#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class EarnedVsInvoicedCalibrator : CalibratorBase
{
    public EarnedVsInvoicedCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new RecentBilledService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Earned-vs-invoiced gap latest closed period",
            [new ExpectedToolCall("get_earned_vs_invoiced", [])],
            [new ExpectedAnswerValue("latest gap", (decimal)(result.Earned30 - result.Billed30))]);
    }
}
