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
                "HOW IT IS CALCULATED:\n" +
                "Fee Billed (with unposted) divided by Direct Labor Cost. Direct Labor Cost is the sum of staff time charged to the project across LaborCodes 10–60 (Engineering, Drafting, Checking, Inspection, DocPrep, General). Admin (70) and NonBillable (80) are firm overhead and excluded so the per-project number reconciles with the firm-wide Net Multiplier.\n" +
                "Shows '—' (no badge) when the project has no booked labor yet — avoids meaningless infinities on inception-stage projects.",
            Formula = "ProjectMultiplier = FeeBilledWithUnposted ÷ (EngLaborCost + DraftLaborCost + InspLaborCost + DocPrepLaborCost + GenLaborCost)"
        };

        d["ProjectMargin"] = new FinancialMetricDefinition
        {
            Key = "ProjectMargin",
            DisplayName = "Project Margin",
            Description =
                "WHAT:\n" +
                "The percentage of every billed dollar left over after paying direct project costs (labor + subs).\n\n" +
                "WHY IT MATTERS:\n" +
                "Lets you compare projects of different sizes fairly. A 50% margin on a $20K project and a 50% margin on a $200K project are equally healthy in efficiency terms (the dollar amount is what differs — see Project Profit for that lens). Thresholds:\n" +
                "  • <35% (red badge): thin — direct costs are eating most of the fee.\n" +
                "  • 35–50% (amber badge): typical for stretched fixed-fee work.\n" +
                "  • ≥50% (green badge): healthy direct margin.\n" +
                "This is a DIRECT-cost margin — does NOT include firm overhead allocation. A project showing 40% margin here looks 'amber' but, after the firm's overhead is allocated, may be closer to break-even.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "(Fee Billed with unposted − Direct Labor Cost − Subconsultant Cost) ÷ Fee Billed with unposted.\n" +
                "Shows '—' when the project has no billing yet.",
            Formula = "ProjectMargin = (FeeBilledWithUnposted − TotalDirectLaborCost − SubconsultantCost) ÷ FeeBilledWithUnposted"
        };

        d["ProjectProfit"] = new FinancialMetricDefinition
        {
            Key = "ProjectProfit",
            DisplayName = "Project Profit (Direct)",
            Description =
                "WHAT:\n" +
                "The absolute dollar amount left over after direct project costs.\n\n" +
                "WHY IT MATTERS:\n" +
                "Margin % tells you how efficient a project is. Profit dollars tell you how much the project actually contributes. A 70% margin on a $5K project is $3.5K — barely moves the needle. A 35% margin on a $200K project is $70K — that's where the year is made. Sort the column descending to see which projects are doing the heavy lifting; sort ascending and look for negatives to find projects you're paying to do.\n" +
                "Negative number = you billed less than your direct costs. Either the fee is wrong, the budget went sideways, or hours are being mis-charged.\n" +
                "Like Margin, this is a DIRECT-cost figure — no overhead allocation.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Fee Billed with unposted − Direct Labor Cost − Subconsultant Cost. Same numerator as Margin, just expressed in dollars instead of a ratio.",
            Formula = "ProjectProfit = FeeBilledWithUnposted − TotalDirectLaborCost − SubconsultantCost"
        };
    }
}
