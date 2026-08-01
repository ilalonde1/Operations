#nullable enable
using System.Data;
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

/// <summary>
/// Calibrates the GL P&L (posted) expected total for CAD-org, April 2024,
/// by calling GlProfitLossService directly. Sums the GrandTotal "Total
/// Expenses" row across period columns — identical projection to
/// GlPnLTool.GetGlPnLAsync's SumGrand helper, so AI numbers match by
/// construction when it picks the right tool with the right scope.
/// </summary>
internal sealed class GlPnLCadCalibrator : CalibratorBase
{
    public GlPnLCadCalibrator(SmokeServices services) : base(services) { }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new GlProfitLossService(Odbc, Financials);
        var tables = await svc.GetTablesAsync(ct).ConfigureAwait(false);
        var first = tables.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No GLTable entries matched the configured Income Statement filter; cannot calibrate get_gl_pnl.");
        var result = await svc.BuildProfitLossAsync(
            first.TableNo,
            new DateTime(2024, 4, 1),
            new DateTime(2024, 4, 30),
            "CAD",
            flipSign: true,
            forceRefresh: false,
            ct).ConfigureAwait(false);

        decimal expenses = 0m;
        foreach (DataRow row in result.Table.Rows)
        {
            if (!string.Equals(Convert.ToString(row["RowKind"]), "GrandTotal", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(Convert.ToString(row["LineItem"]), "Total Expenses", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var col in result.PeriodColumnNames)
            {
                if (row[col] is decimal d) expenses += d;
            }
        }

        return new CalibratedExpectation(
            "CAD Apr-2024 GL P&L expenses",
            [new ExpectedToolCall("get_gl_pnl", ["\"org\":\"CAD\""])],
            [new ExpectedAnswerValue("CAD Apr-2024 GL expenses", Math.Abs(expenses))]);
    }
}
