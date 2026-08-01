#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class BilledPnLCombinedCalibrator : CalibratorBase
{
    public BilledPnLCombinedCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new BilledFinancialsService(Odbc, Financials);
        var result = await svc.BuildAsync(new DateTime(2024, 4, 1), new DateTime(2024, 4, 30), null, false, ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Combined Apr-2024 expenses",
            [new ExpectedToolCall("get_billed_pnl", [])],
            [new ExpectedAnswerValue("combined expenses", Math.Abs(SumBilledLine(result, "Total Expenses")))]);
    }
}
