#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddBilledPnLMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        d["Billed_Revenue"] = new FinancialMetricDefinition
        {
            Key = "Billed_Revenue", Category = "Billed P&L",
            DisplayName = "Billed Revenue",
            Description =
                "WHAT:\n" +
                "Revenue from invoices issued to clients in the date range — the immediate source-of-truth for billed top-line, no GL posting delay.\n\n" +
                "WHY IT MATTERS:\n" +
                "Daler's reference Crystal report uses LedgerAR (not PRSummaryMain) for billed revenue because invoices land in AR the moment they're issued, while PRSummaryMain.BilledFee can lag ~3 months behind period close. For real-time cash-flow questions this is the right source.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(-LedgerAR.Amount) WHERE TransType='IN' AND LEFT(LTRIM(RTRIM(Account)),4) IN ('4001','4003','4210','4220','4240') AND TransDate BETWEEN @from AND @to.\n" +
                "Excludes account 4260 (intercompany). Account 4500 does NOT exist at KOR — don't reference it.\n" +
                "USA-org rows are FX-converted to CAD at App.config Financials.Billed.UsdToCadRate (default 1.36) before summing; CAD rows pass through unchanged.\n" +
                "Revenue is stored as negative Amount in LedgerAR (accounting convention) — the unary minus flips it.",
            Formula = "SUM(-LedgerAR.Amount) FILTER (TransType='IN', Account prefix in canonical revenue accounts, in-range, FX-bucketed at pr.Org)"
        };
        d["Billed_Expenses"] = new FinancialMetricDefinition
        {
            Key = "Billed_Expenses", Category = "Billed P&L",
            DisplayName = "Billed Expenses",
            Description =
                "WHAT:\n" +
                "Expenses for the range — pulled from the AP / EX / Misc ledger tables so we see costs that have hit the books even before GL posting catches up.\n\n" +
                "WHY IT MATTERS:\n" +
                "Pairing this with Billed Revenue gives a true-up of margin at billing-time, not at posting-time. Lets leadership ask 'are we making money on the work we just invoiced?' without waiting on month-end close.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Combined sum of LedgerAP, LedgerEX, and LedgerMisc entries in the date range, by GL account category. Same currency handling as Billed Revenue (USA-org FX-converted to CAD via UsdToCadRate, CAD rows unchanged).",
            Formula = "SUM(LedgerAP + LedgerEX + LedgerMisc) over range, FX-bucketed at pr.Org"
        };
        d["Billed_Net"] = new FinancialMetricDefinition
        {
            Key = "Billed_Net", Category = "Billed P&L",
            DisplayName = "Billed Net Income",
            Description =
                "WHAT:\n" +
                "Net income (revenue minus expenses) for the date range, on a billed/invoiced basis.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows real margin earned on the work invoiced in the period. Often diverges from the Posted GL net because invoices land in AR immediately while GL posting lags.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Billed Revenue − Billed Expenses (both as defined above).",
            Formula = "Billed_Revenue - Billed_Expenses"
        };
        d["Billed_Margin"] = new FinancialMetricDefinition
        {
            Key = "Billed_Margin", Category = "Billed P&L",
            DisplayName = "Billed Margin",
            Description =
                "WHAT:\n" +
                "Billed net income as a percent of billed revenue for the range.\n\n" +
                "WHY IT MATTERS:\n" +
                "Comparable across periods regardless of revenue scale. The signed-aware divisor guard (Math.Abs(Revenue) > RoundingDollarFloor, post-Batch 37) prevents misleading ratios when revenue is near zero.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Billed Net / Billed Revenue. Returns 0 when |Billed Revenue| <= RoundingDollarFloor.",
            Formula = "Billed_Net / Billed_Revenue (signed-aware guard)"
        };
        d["Billed_Reconciliation"] = new FinancialMetricDefinition
        {
            Key = "Billed_Reconciliation", Category = "Billed P&L",
            DisplayName = "Billed vs Posted GL Reconciliation",
            Description =
                "WHAT:\n" +
                "Delta between Billed-side revenue (LedgerAR) and Posted-side revenue (GLSummary) over the same range. Positive delta = invoices issued but not yet posted into the GL.\n\n" +
                "WHY IT MATTERS:\n" +
                "Quantifies the timing gap caused by Deltek's accounting close lag (~3 months). A large positive delta means real billed revenue isn't yet visible on the GL P&L tab — leadership needs both views to avoid under-reporting income at period boundaries.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Billed (range) = SUM from LedgerAR per Billed_Revenue.\n" +
                "Posted (range) = SUM from GLSummary on the same canonical revenue accounts, same date range, same FX bucketing.\n" +
                "Delta = Billed - Posted. Reported in CAD.",
            Formula = "Billed_Range - Posted_Range (both on canonical revenue accounts, FX-to-CAD)"
        };
    }
}
