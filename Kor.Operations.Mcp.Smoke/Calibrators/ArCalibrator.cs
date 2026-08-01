#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class ArCalibrator : CalibratorBase
{
    public ArCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ArFinancialsService(Odbc, Financials);
        var result = await svc.LoadAsync(ct).ConfigureAwait(false);
        var over90 = result.ProjectRows.Sum(r => r.Aged90Plus);
        return new CalibratedExpectation(
            "AR over 90 days",
            [new ExpectedToolCall("get_ar", [])],
            [new ExpectedAnswerValue("AR 90+", (decimal)over90)]);
    }
}
