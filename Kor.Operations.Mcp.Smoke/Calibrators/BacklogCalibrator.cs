#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class BacklogCalibrator : CalibratorBase
{
    public BacklogCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new BacklogService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Current backlog firmwide",
            [new ExpectedToolCall("get_backlog", [])],
            [new ExpectedAnswerValue("backlog", (decimal)result.FirmwideBacklog)]);
    }
}
