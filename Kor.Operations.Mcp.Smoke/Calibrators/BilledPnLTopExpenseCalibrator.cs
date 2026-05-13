#nullable enable
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Smoke.Audit;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class BilledPnLTopExpenseCalibrator : CalibratorBase
{
    public BilledPnLTopExpenseCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new BilledFinancialsService(Odbc, Financials);
        _ = await svc.BuildAsync(new DateTime(2024, 4, 1), new DateTime(2024, 4, 30), "CAD", false, ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Top 3 Apr-2024 CAD expense accounts",
            [
                new ExpectedToolCall(
                    "get_billed_pnl",
                    ["\"org\":\"CAD\""],
                    row => TopNAtLeast(row, 3))
            ],
            []);
    }

    private static bool TopNAtLeast(AuditRow row, int min)
    {
        var match = System.Text.RegularExpressions.Regex.Match(row.InputJson, @"""topN""\s*:\s*(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) && n >= min;
    }
}
