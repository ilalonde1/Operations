#nullable enable
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Smoke.Audit;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class BilledPnLComparisonCalibrator : CalibratorBase
{
    public BilledPnLComparisonCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new BilledFinancialsService(Odbc, Financials);
        var apr = await svc.BuildAsync(new DateTime(2024, 4, 1), new DateTime(2024, 4, 30), "CAD", false, ct).ConfigureAwait(false);
        var feb = await svc.BuildAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), "CAD", false, ct).ConfigureAwait(false);
        return new CalibratedExpectation(
            "Apr-2024 vs Feb-2026 CAD expense comparison",
            [
                new ExpectedToolCall("get_billed_pnl", ["\"org\":\"CAD\""], row => ContainsPeriod(row, "2024-04")),
                new ExpectedToolCall("get_billed_pnl", ["\"org\":\"CAD\""], row => ContainsPeriod(row, "2026-02"))
            ],
            [
                new ExpectedAnswerValue("Apr 2024 CAD expenses", Math.Abs(SumBilledLine(apr, "Total Expenses"))),
                new ExpectedAnswerValue("Feb 2026 CAD expenses", Math.Abs(SumBilledLine(feb, "Total Expenses")))
            ]);
    }

    private static bool ContainsPeriod(AuditRow row, string yyyyMm)
        => row.InputJson.Contains(yyyyMm, StringComparison.Ordinal);
}
