#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class FirmHealthNetMultiplierCalibrator : CalibratorBase
{
    public FirmHealthNetMultiplierCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new FirmHealthService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "T12mo Net Multiplier",
            [new ExpectedToolCall("get_firm_health", [])],
            [new ExpectedAnswerValue("net multiplier", (decimal)result.NetMultiplier)]);
    }
}
