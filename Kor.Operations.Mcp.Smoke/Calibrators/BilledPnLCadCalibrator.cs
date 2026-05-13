#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class BilledPnLCadCalibrator : CalibratorBase
{
    private readonly string _lineItem;
    private readonly string _label;

    public BilledPnLCadCalibrator(SmokeServices services, string lineItem, string label)
        : base(services)
    {
        _lineItem = lineItem;
        _label = label;
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new BilledFinancialsService(Odbc, Financials);
        var result = await svc.BuildAsync(new DateTime(2024, 4, 1), new DateTime(2024, 4, 30), "CAD", false, ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            _label,
            [new ExpectedToolCall("get_billed_pnl", ["\"org\":\"CAD\""])],
            [new ExpectedAnswerValue(_label, Math.Abs(SumBilledLine(result, _lineItem)))]);
    }
}
