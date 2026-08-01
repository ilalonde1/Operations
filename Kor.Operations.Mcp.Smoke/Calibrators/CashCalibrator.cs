#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class CashCalibrator : CalibratorBase
{
    public CashCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new CashFinancialsService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Current cash position",
            [new ExpectedToolCall("get_cash_position", [])],
            [new ExpectedAnswerValue("combined cash", (decimal)result.CombinedCadEquivalent)]);
    }
}
