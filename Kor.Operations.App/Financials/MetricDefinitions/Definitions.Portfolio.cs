#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddPortfolioMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // Section tooltips (UI-only): executive-grade definitions.
        d["PortfolioDeliveryHealth"] = new FinancialMetricDefinition
        {
            Key = "PortfolioDeliveryHealth",
            DisplayName = "Portfolio Delivery Health",
            Description =
                "WHAT:\n" +
                "Real-time view of delivery risk across active projects.\n\n" +
                "WHY IT MATTERS:\n" +
                "Helps leadership prioritize intervention before issues become financial losses.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Groups active projects by Delivery Risk level using current snapshot data.",
            Formula = ""
        };
        d["PortfolioRiskExposure"] = new FinancialMetricDefinition
        {
            Key = "PortfolioRiskExposure",
            DisplayName = "Portfolio Risk Exposure",
            Description =
                "WHAT:\n" +
                "Total fee currently associated with projects flagged as Critical.\n\n" +
                "WHY IT MATTERS:\n" +
                "Represents financial exposure requiring leadership attention.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums contracted fee for projects whose Delivery Risk is Critical.",
            Formula = ""
        };
        d["PortfolioTrend"] = new FinancialMetricDefinition
        {
            Key = "PortfolioTrend",
            DisplayName = "Portfolio Trend",
            Description =
                "WHAT:\n" +
                "Week-to-week view of how delivery risk is shifting across the portfolio.\n\n" +
                "WHY IT MATTERS:\n" +
                "Reveals whether pressure is systemic or improving over time.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Uses weekly snapshots to trend counts by Delivery Risk level.",
            Formula = ""
        };
        d["RiskDrivers"] = new FinancialMetricDefinition
        {
            Key = "RiskDrivers",
            DisplayName = "Risk Drivers",
            Description =
                "WHAT:\n" +
                "The main factors pushing a project toward higher delivery risk.\n\n" +
                "WHY IT MATTERS:\n" +
                "Focuses action on the specific levers that reduce exposure.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Summarizes signals from fee, billing, backlog, and hours.",
            Formula = ""
        };
        d["DeliveryTrend"] = new FinancialMetricDefinition
        {
            Key = "DeliveryTrend",
            DisplayName = "Delivery Trend",
            Description =
                "WHAT:\n" +
                "Direction of change in a project's delivery risk over recent snapshots.\n\n" +
                "WHY IT MATTERS:\n" +
                "Confirms whether corrective action is working.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Compares recent snapshot levels to show improvement or deterioration.",
            Formula = ""
        };
        d["HealthIndicators"] = new FinancialMetricDefinition
        {
            Key = "HealthIndicators",
            DisplayName = "Health Indicators",
            Description =
                "WHAT:\n" +
                "Automated flags that highlight financial and execution health.\n\n" +
                "WHY IT MATTERS:\n" +
                "Standardizes review and reduces missed risk.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Applies fee, billing, and hours signals to produce indicator flags.",
            Formula = ""
        };
        d["BurnRisk"] = new FinancialMetricDefinition
        {
            Key = "BurnRisk",
            DisplayName = "Burn Risk",
            Description =
                "WHAT:\n" +
                "Flag that engineering hours are being consumed faster than progress supports.\n\n" +
                "WHY IT MATTERS:\n" +
                "Early warning of margin compression and budget overrun.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Evaluates hours burn against billing progress and remaining budget.",
            Formula = ""
        };

        d["ProjectMultiplier"] = new FinancialMetricDefinition
        {
            Key = "ProjectMultiplier",
            DisplayName = "Project Multiplier",
            Description =
                "WHAT:\n" +
                "How many dollars of fee we billed for every dollar of staff time spent on the project.\n\n" +
                "WHY IT MATTERS:\n" +
                "The single number that tells you whether a project is making money. The first dollar pays the staff salary. The next dollar or so pays for rent, software, admin, IT, and other firm overhead. The third dollar is actual profit. So:\n" +
                "  • <2.0 (red badge): losing money — the project isn't even covering the labor cost.\n" +
                "  • 2.0–3.0 (amber badge): treading water — covers labor and most overhead, no real profit.\n" +
                "  • ≥3.0 (green badge): healthy — covers everything and leaves profit.\n" +
                "This is the per-project version of the Net Multiplier KPI on the Executive Summary; the two definitions reconcile.\n\n" +
                "PAIRS WITH:\n" +
                "Multiplier is the LABOR lens (does not subtract overhead). The Margin and Profit columns on the same row are the OVERHEAD-INCLUSIVE lens. A project with Multiplier 2.8 (labor-decent) can show a negative Margin once overhead is allocated; both readings are simultaneously true.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Fee Billed (with unposted) divided by Direct Labor Cost. Direct Labor Cost is the sum of staff time charged to the project across LaborCodes 10–60 (Engineering, Drafting, Checking, Inspection, DocPrep, General). Admin (70) and NonBillable (80) are firm overhead and excluded so the per-project number reconciles with the firm-wide Net Multiplier.\n" +
                "Shows '—' (no badge) when the project has no booked labor yet — avoids meaningless infinities on inception-stage projects.",
            Formula = "ProjectMultiplier = FeeBilledWithUnposted ÷ (EngLaborCost + DraftLaborCost + InspLaborCost + DocPrepLaborCost + GenLaborCost)"
        };

        d["ProjectMargin"] = new FinancialMetricDefinition
        {
            Key = "ProjectMargin",
            DisplayName = "Project Net Margin",
            Description =
                "WHAT:\n" +
                "Per-project NET margin percent: bottom-line profit as a fraction of FeeBilled, after subtracting direct labor, subconsultants, and allocated firm overhead.\n\n" +
                "WHY IT MATTERS:\n" +
                "Unlike Multiplier (which is labor-only and benchmarked against 3.0), this is the actual P&L margin on a project. Industry-typical AEC net margin: 10% healthy, 0-10% mediocre (the project paid its bills but produced little firm profit), <0% loss (the project did not cover its allocated share of overhead).\n\n" +
                "Read alongside Multiplier: a Multiplier of exactly the firm's overhead break-even point (1 + OverheadRate, e.g. 2.65 at a 1.65 rate) corresponds to about 0% Net Margin. Any Multiplier above that translates directly into positive Net Margin.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Profit dollars (see Project Profit) divided by FeeBilledWithUnposted. The overhead allocation is FirmOverheadRate * TotalDirectLaborCost; the firmwide rate is configurable in App.config (Financials.PnL.OverheadRate, default 1.65).",
            Formula = "ProjectNetMargin = (FeeBilledWithUnposted - TotalDirectLaborCost - SubconsultantCost - (TotalDirectLaborCost * OverheadRate)) / FeeBilledWithUnposted"
        };

        d["ProjectProfit"] = new FinancialMetricDefinition
        {
            Key = "ProjectProfit",
            DisplayName = "Project Net Profit",
            Description =
                "WHAT:\n" +
                "Per-project NET profit dollars: bottom-line dollars after subtracting direct labor, subconsultants, and allocated firm overhead from FeeBilled.\n\n" +
                "WHY IT MATTERS:\n" +
                "This is the dollar version of Project Net Margin. Negative means the project lost the firm money once its share of overhead is subtracted, even when the labor-only Multiplier looks healthy. Sum across the active portfolio approximates the firm's contribution to bottom-line profit from the active book; the residual difference vs. firmwide Net Income is timing (posted vs. unposted) and lifetime-vs-active scope.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "FeeBilledWithUnposted minus direct labor cost, minus subconsultant cost, minus allocated overhead (TotalDirectLaborCost * OverheadRate). The OverheadRate is firmwide and configurable via App.config (Financials.PnL.OverheadRate, default 1.65).",
            Formula = "ProjectNetProfit = FeeBilledWithUnposted - TotalDirectLaborCost - SubconsultantCost - (TotalDirectLaborCost * OverheadRate)"
        };
    }
}
