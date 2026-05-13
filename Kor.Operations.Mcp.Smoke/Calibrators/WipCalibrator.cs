#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

/// <summary>
/// Calibrates firmwide WIP (Work-In-Progress) expected total by calling
/// WipFinancialsService directly. Compares to AI answer for the get_wip
/// tool, anchoring on FirmWipUnbilled (Earned at latest posted period) —
/// the headline number get_wip surfaces as payload.firmwide.earned.
/// </summary>
internal sealed class WipCalibrator : CalibratorBase
{
    public WipCalibrator(SmokeServices services) : base(services) { }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new WipFinancialsService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Firmwide WIP earned",
            [new ExpectedToolCall("get_wip", [])],
            [new ExpectedAnswerValue("Firmwide WIP earned", (decimal)result.FirmWipUnbilled)]);
    }
}
