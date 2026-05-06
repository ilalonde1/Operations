#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddBillingManagerMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // ── Billing Manager Report ────────────────────────────────────────────
        d["BM_Label"] = new FinancialMetricDefinition
        {
            Key = "BM_Label",
            DisplayName = "Billing Manager / Project",
            Description =
                "WHAT:\n" +
                "Rows are grouped by Billing Manager. Manager rows (bold, blue strip) can be expanded with the ▶ arrow to show individual projects assigned to that manager.\n\n" +
                "WHY IT MATTERS:\n" +
                "Reveals which managers and projects are driving billings and where billing activity is concentrated.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Projects are assigned via the Billing Manager field in Deltek Vantagepoint. Projects with no manager appear under 'Unassigned'.",
            Formula = ""
        };
        d["BM_Info"] = new FinancialMetricDefinition
        {
            Key = "BM_Info",
            DisplayName = "Info (Phase / Principal)",
            Description =
                "WHAT:\n" +
                "For project rows: the project's phase and billing principal. For manager rows: a summary of active project count.\n\n" +
                "WHY IT MATTERS:\n" +
                "Provides project-stage and ownership context without requiring a drill-down.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Phase and principal are read directly from the Deltek project record. Active count is derived from projects with billing activity in the last 12 closed periods.",
            Formula = ""
        };
        d["BM_YoyDelta"] = new FinancialMetricDefinition
        {
            Key = "BM_YoyDelta",
            DisplayName = "Year-over-Year Delta (YoY Δ)",
            Description =
                "WHAT:\n" +
                "Change in total billings between the most recent 12 closed periods and the 12 periods before that.\n\n" +
                "WHY IT MATTERS:\n" +
                "Quickly shows whether a manager or project is growing, flat, or declining in billing output year-over-year. Green = growth; red = decline.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "YoY Δ = (Last 12 periods billed) − (Prior 12 periods billed).",
            Formula = "YoY Δ = SUM(Billed, last 12 periods) − SUM(Billed, prior 12 periods)"
        };
        d["BM_12MoTotal"] = new FinancialMetricDefinition
        {
            Key = "BM_12MoTotal",
            DisplayName = "12-Month Total Billings",
            Description =
                "WHAT:\n" +
                "Total fee billed across the last 12 closed accounting periods.\n\n" +
                "WHY IT MATTERS:\n" +
                "The primary revenue production metric for the billing manager. Drives the billing bar chart and YoY comparison.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums PRSummaryMain.Billed for the 12 most recent closed periods, grouped by Billing Manager and project.",
            Formula = "SUM(PRSummaryMain.Billed) for the last 12 closed periods"
        };
        d["BM_RemFee"] = new FinancialMetricDefinition
        {
            Key = "BM_RemFee",
            DisplayName = "Remaining Fee",
            Description =
                "WHAT:\n" +
                "Total contracted fee not yet billed across the manager's active projects.\n\n" +
                "WHY IT MATTERS:\n" +
                "Represents future billing runway. Negative values (shown in red) mean projects have billed beyond their contracted fee — an over-billing flag.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Remaining Fee = Contracted Fee − Total Fee Billed to Date, summed across all projects under the manager.",
            Formula = "Remaining Fee = SUM(Fee − FeeBilled)"
        };
        d["BM_PctFee"] = new FinancialMetricDefinition
        {
            Key = "BM_PctFee",
            DisplayName = "% Fee Billed",
            Description =
                "WHAT:\n" +
                "Portion of the total contracted fee that has been billed to date.\n\n" +
                "WHY IT MATTERS:\n" +
                "Measures billing progress relative to the contract. Values approaching or exceeding 100% flag over-billing risk.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "% Fee Billed = Total Fee Billed ÷ Contracted Fee. Colour: green ≥ 75%, amber 50–74%, red < 50% or > 100%.",
            Formula = "% Fee Billed = SUM(FeeBilled) / SUM(Fee)"
        };
        d["BM_Trend"] = new FinancialMetricDefinition
        {
            Key = "BM_Trend",
            DisplayName = "Billing Trend (Sparkline)",
            Description =
                "WHAT:\n" +
                "A mini line chart of monthly billings across the last 12 closed periods.\n\n" +
                "WHY IT MATTERS:\n" +
                "Reveals billing momentum at a glance — whether billings are accelerating, decelerating, or erratic across the year.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Plots PRSummaryMain.Billed by period for the last 12 closed periods. Line color matches the trend direction arrow.",
            Formula = ""
        };
        d["BM_TrendArrow"] = new FinancialMetricDefinition
        {
            Key = "BM_TrendArrow",
            DisplayName = "Trend Direction",
            Description =
                "WHAT:\n" +
                "Arrow glyph summarising the direction of billing momentum across the last 12 months.\n\n" +
                "WHY IT MATTERS:\n" +
                "Single-character signal for whether billing output is trending up, flat, or down. Read alongside the sparkline for confirmation.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Compares the average of the last 3 periods to the average of the prior 3 periods within the 12-month window.\n" +
                "↑ green  = recent 3-period avg is > 5% above prior 3-period avg\n" +
                "↓ red    = recent avg is > 5% below prior avg\n" +
                "→ grey   = within ±5% (stable)",
            Formula = ""
        };
        d["BM_LastMo"] = new FinancialMetricDefinition
        {
            Key = "BM_LastMo",
            DisplayName = "Last Closed Period Billings",
            Description =
                "WHAT:\n" +
                "Fee billed in the most recently closed accounting period.\n\n" +
                "WHY IT MATTERS:\n" +
                "The freshest billing signal. Useful for spotting managers or projects that went quiet in the latest period.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "PRSummaryMain.Billed for the single most recent closed period.",
            Formula = "PRSummaryMain.Billed WHERE Period = MAX(closed periods)"
        };
        d["BM_2MoAgo"] = new FinancialMetricDefinition
        {
            Key = "BM_2MoAgo",
            DisplayName = "Two Periods Ago Billings",
            Description =
                "WHAT:\n" +
                "Fee billed in the accounting period immediately before the most recent closed period.\n\n" +
                "WHY IT MATTERS:\n" +
                "Provides a quick month-over-month comparison so you can see whether the latest period is higher or lower than the one before it.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "PRSummaryMain.Billed for the second-most-recent closed period.",
            Formula = "PRSummaryMain.Billed WHERE Period = MAX(closed periods) − 1"
        };
        d["BM_Streak"] = new FinancialMetricDefinition
        {
            Key = "BM_Streak",
            DisplayName = "Active Billing Streak",
            Description =
                "WHAT:\n" +
                "Number of consecutive closed periods (counting back from the most recent) in which this manager or project recorded any billings.\n\n" +
                "WHY IT MATTERS:\n" +
                "A long streak signals sustained, uninterrupted billing cadence. A streak of 0 or 1 means the manager or project recently went quiet — a potential billing gap to investigate.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Starting from the most recent closed period, counts consecutive periods with Billed > 0 and stops at the first period with zero billings.",
            Formula = "COUNT consecutive periods FROM latest WHERE Billed > 0"
        };
        d["BM_AvgMo"] = new FinancialMetricDefinition
        {
            Key = "BM_AvgMo",
            DisplayName = "Average Monthly Billings",
            Description =
                "WHAT:\n" +
                "Average fee billed per month across all periods in which this manager or project recorded any billing activity.\n\n" +
                "WHY IT MATTERS:\n" +
                "A stable run-rate baseline unaffected by billing gaps. Useful for comparing recent period billings against the long-term average to spot acceleration or slowdown.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Total billings across all active periods ÷ count of periods with Billed > 0.",
            Formula = "SUM(Billed) / COUNT(periods WHERE Billed > 0)"
        };
        d["BM_12MoChart"] = new FinancialMetricDefinition
        {
            Key = "BM_12MoChart",
            DisplayName = "12-Month Billings by Manager (Chart)",
            Description =
                "WHAT:\n" +
                "Horizontal bar chart comparing the last 12 months of total billings across all billing managers.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows relative billing contribution by manager at a glance. Bar length is proportional to each manager's share of the 12-month total.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Bar width = manager's 12-month billings ÷ highest manager total × 265 px. Colors match the 60-month chart legend for consistency.",
            Formula = ""
        };
        d["BM_YoyChart"] = new FinancialMetricDefinition
        {
            Key = "BM_YoyChart",
            DisplayName = "Year-over-Year Growth (Chart)",
            Description =
                "WHAT:\n" +
                "Diverging bar chart showing each manager's year-over-year billing change. Green bars extend right for growth; red bars extend left for decline.\n\n" +
                "WHY IT MATTERS:\n" +
                "Makes YoY performance comparison across managers immediate and visual, with clear directional signal.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "YoY Δ = (Last 12 periods) − (Prior 12 periods). Bar width = |YoY Δ| ÷ largest absolute delta across all managers × 85 px.",
            Formula = "Bar width = |YoY Δ| / MAX(|all YoY Δ|) × 85 px"
        };
        d["BM_60MoChart"] = new FinancialMetricDefinition
        {
            Key = "BM_60MoChart",
            DisplayName = "60-Month Billing History by Manager",
            Description =
                "WHAT:\n" +
                "Stacked bar chart of the last 60 closed accounting periods of firmwide billings, color-coded by billing manager contribution.\n\n" +
                "WHY IT MATTERS:\n" +
                "Reveals billing seasonality, long-term growth trends, and which managers drive production in each period. Hover any bar segment to see the manager name and billing amount for that period.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Each bar = one accounting period. Bar height = period total ÷ highest single-period total × 80 px. Each color segment = one manager's contribution. Managers with < $0.004k in a period are omitted from that bar.",
            Formula = "Bar height = period total / MAX(all period totals) × 80 px"
        };
    }
}
