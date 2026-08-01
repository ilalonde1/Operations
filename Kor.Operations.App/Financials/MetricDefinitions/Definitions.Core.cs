#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddCoreMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        d["TotalFees"] = new FinancialMetricDefinition
        {
            Key = "TotalFees",
            DisplayName = "Total Fees",
            Description =
                "WHAT:\n" +
                "Total fee across all projects in the current view. Includes both fixed contract fees and any hourly/T&M extras revenue.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows the full revenue base currently under management — not just the original contracts, but all billable work.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "For each project: Fixed Fee (from Deltek contract) + Hourly Extras Revenue (from time-and-materials billing). Summed across all visible projects.\n\n" +
                "CURRENCY:\n" +
                "USA-org rows are FX-converted to CAD-equivalent at the rate in App.config (Financials.Billed.UsdToCadRate, default 1.36) before summing. CAD rows roll up unchanged.",
            Formula = ""
        };
        d["TotalFeeBilled"] = new FinancialMetricDefinition
        {
            Key = "TotalFeeBilled",
            DisplayName = "Total Fee Billed",
            Description =
                "WHAT:\n" +
                "Total fee billed to date across the projects currently shown.\n\n" +
                "WHY IT MATTERS:\n" +
                "Indicates how much revenue has been invoiced so far.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Adds billed-to-date amounts for each project in view.\n\n" +
                "CURRENCY:\n" +
                "USA-org rows are FX-converted to CAD-equivalent at the rate in App.config (Financials.Billed.UsdToCadRate, default 1.36) before summing. CAD rows roll up unchanged.\n\n" +
                "NOTE:\n" +
                "When the value is italic + amber, it includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. Once posting catches up the styling clears and the value matches the posted-only figure above.",
            Formula = ""
        };
        d["TotalUnbilled"] = new FinancialMetricDefinition
        {
            Key = "TotalUnbilled",
            DisplayName = "Total Unbilled",
            Description =
                "WHAT:\n" +
                "Total fee not yet billed across the projects shown.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows how much revenue is still available to bill — your billing runway.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "For each project: Total Fee (fixed + hourly) minus amount billed to date. Summed across all visible projects.\n\n" +
                "CURRENCY:\n" +
                "USA-org rows are FX-converted to CAD-equivalent at the rate in App.config (Financials.Billed.UsdToCadRate, default 1.36) before summing. CAD rows roll up unchanged.",
            Formula = ""
        };
        d["PercentFeeUnbilled"] = new FinancialMetricDefinition
        {
            Key = "PercentFeeUnbilled",
            DisplayName = "% Fee Unbilled",
            Description =
                "WHAT:\n" +
                "What percentage of the total fee has NOT been billed yet.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows billing progress at a glance. A high percentage means lots of work has been done but not yet invoiced.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Total Unbilled divided by Total Fees. Both include fixed contract fees and hourly extras revenue.",
            Formula = ""
        };
        d["HoursSpent"] = new FinancialMetricDefinition
        {
            Key = "HoursSpent",
            DisplayName = "Hours Spent",
            Description =
                "WHAT:\n" +
                "Production hours (engineering + drafting) charged to the projects shown.\n\n" +
                "WHY IT MATTERS:\n" +
                "Indicates delivery effort consumed against eng+draft budgets. Compares apples-to-apples with Hours Budgeted.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(Eng Hours + Draft Hours) across all projects in view. Other codes (insp, admin, etc.) are tracked separately in the Discipline Breakdown.",
            Formula = "SUM(EngHrs + DraftHrs)"
        };
        d["HoursBudgeted"] = new FinancialMetricDefinition
        {
            Key = "HoursBudgeted",
            DisplayName = "Hours Budgeted",
            Description =
                "WHAT:\n" +
                "Estimated total engineering + drafting hours the projects should take.\n\n" +
                "WHY IT MATTERS:\n" +
                "The target to compare actual hours against. If hours spent exceeds this, the project is over budget.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Uses Deltek budget if entered, otherwise estimates from peer projects with similar fees, or a formula based on total fee and billing rates.",
            Formula = ""
        };
        d["HoursRemaining"] = new FinancialMetricDefinition
        {
            Key = "HoursRemaining",
            DisplayName = "Hours Remaining",
            Description =
                "WHAT:\n" +
                "Remaining engineering + drafting hours before projects reach their budget.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows how much production capacity is left before overrun risk increases. When this hits zero, the project is over budget.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Hours Budgeted minus Hours Spent (both engineering and drafting combined).",
            Formula = "Hours Budgeted - Hours Spent"
        };
        d["PercentHoursSpent"] = new FinancialMetricDefinition
        {
            Key = "PercentHoursSpent",
            DisplayName = "% Hours Spent",
            Description =
                "WHAT:\n" +
                "Portion of planned engineering hours already used.\n\n" +
                "WHY IT MATTERS:\n" +
                "Quickly signals delivery burn against plan across the selected work.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Compares hours spent to hours budgeted for the current view.",
            Formula = "PercentHoursSpent = TotalHoursSpent / (EngBudget + DraftBudget)"
        };
        d["AvgFeePerFt2"] = new FinancialMetricDefinition
        {
            Key = "AvgFeePerFt2",
            DisplayName = "Average Fee per Square Foot",
            Description =
                "WHAT:\n" +
                "Average contracted fee per square foot for projects with GFA entered.\n\n" +
                "WHY IT MATTERS:\n" +
                "Supports benchmarking and sanity checks across similar projects.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Divides total fee by total GFA for projects where GFA is available.",
            Formula = ""
        };
        d["TeamDaysRemaining"] = new FinancialMetricDefinition
        {
            Key = "TeamDaysRemaining",
            DisplayName = "Team-Days Remaining",
            Description =
                "WHAT:\n" +
                "Estimated team-days of budget remaining across the projects shown.\n\n" +
                "WHY IT MATTERS:\n" +
                "Translates remaining hours into an intuitive capacity signal for staffing decisions.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "remaining_hours ÷ 7.5 hrs/day ÷ 35 staff = team-days of capacity.\n" +
                "7.5 = standard KOR working hours per day. 35 = approximate production headcount.\n" +
                "Update TeamSize in AnalyticsThresholds.cs if headcount changes.",
            Formula = "hoursRemaining / 7.5 / 35"
        };
        d["Backlog"] = new FinancialMetricDefinition
        {
            Key = "Backlog",
            DisplayName = "Backlog",
            Description =
                "WHAT:\n" +
                "Remaining contracted fee not yet billed for the project or selection.\n\n" +
                "WHY IT MATTERS:\n" +
                "Indicates future billing runway and remaining revenue on the work.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Subtracts billed-to-date from contracted fee.",
            Formula = ""
        };

        // ── Revenue Forecast section metrics ──
        d["Forecast_Trailing12"] = new FinancialMetricDefinition
        {
            Key = "Forecast_Trailing12", Category = "Forecast",
            DisplayName = "Trailing 12 Months Fee Billed",
            Description =
                "WHAT:\n" +
                "Total firm-wide fee billed across the most recent 12 complete months.\n\n" +
                "WHY IT MATTERS:\n" +
                "The most stable measure of firm scale. Smooths out monthly variation. Compare to Forecast Next 12 Months to see if existing backlog can sustain run rate.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(BilledFee) FROM PRSummaryMain (with legacy Revenue fallback) across the trailing 12 complete months. The current in-progress month is excluded to avoid a partial-month dip.",
            Formula = "SUM(PRSummaryMain.BilledFee else Revenue) for trailing 12 complete months"
        };
        d["Forecast_Trailing3"] = new FinancialMetricDefinition
        {
            Key = "Forecast_Trailing3", Category = "Forecast",
            DisplayName = "Trailing 3 Months Fee Billed",
            Description =
                "WHAT:\n" +
                "Fee billed across the most recent 3 complete months — the firm's current operating pace.\n\n" +
                "WHY IT MATTERS:\n" +
                "Compare to (Trailing 12 ÷ 4) to detect acceleration or slowdown. If 3-month is meaningfully below the 12-month run rate, the trend is negative.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(BilledFee) FROM PRSummaryMain (with legacy Revenue fallback) across the trailing 3 complete months.",
            Formula = "SUM(PRSummaryMain.BilledFee else Revenue) for trailing 3 complete months"
        };
        d["Forecast_Backlog"] = new FinancialMetricDefinition
        {
            Key = "Forecast_Backlog", Category = "Forecast",
            DisplayName = "Current Backlog",
            Description =
                "WHAT:\n" +
                "Total unbilled fee across all currently active projects — work the firm has won but not yet invoiced.\n\n" +
                "WHY IT MATTERS:\n" +
                "Indicates how much fee is locked in from existing contracts — a sales-pipeline-health signal. The 12-month forecast is NOT capped by backlog (it assumes steady-state new wins); see \"Months of Runway\" for the no-new-wins runout.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "SUM(Fee − Fee Billed) across active projects (PR.Status = 'A'). Negative balances are clamped to zero.",
            Formula = "SUM(MAX(0, Fee - FeeBilled)) for active projects"
        };
        d["Forecast_MonthsOfRunway"] = new FinancialMetricDefinition
        {
            Key = "Forecast_MonthsOfRunway", Category = "Forecast",
            DisplayName = "Months of Runway",
            Description =
                "WHAT:\n" +
                "How many months we can keep billing at the current pace before the active project pipeline runs out.\n\n" +
                "WHY IT MATTERS:\n" +
                "The single most actionable BD metric. Below 6 months: BD effort needs to ramp aggressively. Above 12 months: pipeline is well-stocked. Assumes no new project wins.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Current Backlog ÷ Baseline pace. Baseline = (3 × trailing-6mo median + 1 × trailing-12mo median) / 4 — robust to lumpy BilledFee invoicing.",
            Formula = "Backlog / Baseline pace"
        };
        d["Forecast_Next3"] = new FinancialMetricDefinition
        {
            Key = "Forecast_Next3", Category = "Forecast",
            DisplayName = "Forecast — Next 3 Months",
            Description =
                "WHAT:\n" +
                "Projected revenue for the next 3 calendar months.\n\n" +
                "WHY IT MATTERS:\n" +
                "Short-horizon cash visibility for billing-cycle planning and CFO conversations. The most reliable forecast window since seasonality and trend signals are strongest in the near term.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sum of monthly forecasts. Each month: (baseline + slope × i) × seasonal_index. Assumes the firm continues to win new work at historical pace — NOT capped by backlog.\n\n" +
                "  • Baseline = (3 × trailing-6mo median + 1 × trailing-12mo median) / 4 (median is robust to lumpy BilledFee invoicing)\n" +
                "  • Slope = Theil-Sen median slope on the trailing 12 months, clamped to ±15% of baseline\n" +
                "  • Seasonal index = each calendar month's median ÷ overall median, damped 50% toward 1.0",
            Formula = "SUM(monthly_forecast) for months 1-3"
        };
        d["Forecast_Next12"] = new FinancialMetricDefinition
        {
            Key = "Forecast_Next12", Category = "Forecast",
            DisplayName = "Forecast — Next 12 Months",
            Description =
                "WHAT:\n" +
                "Projected revenue across the next 12 calendar months.\n\n" +
                "WHY IT MATTERS:\n" +
                "Annual revenue projection. Assumes the firm continues to win new work at historical pace (steady-state going-concern view). Use \"Months of Runway\" alongside this for the no-new-wins downside scenario.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sum of all 12 forecast months. Same per-month formula as Forecast Next 3, applied across a 12-month horizon. NOT capped by backlog.",
            Formula = "SUM(monthly_forecast) for months 1-12"
        };
        d["PercentBilled"] = new FinancialMetricDefinition
        {
            Key = "PercentBilled",
            DisplayName = "% Billed",
            Description =
                "WHAT:\n" +
                "How much of the total project fee has been billed so far.\n\n" +
                "WHY IT MATTERS:\n" +
                "Compare this to % Hours Spent. If you've used 80% of hours but only billed 50%, there's a problem.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Amount billed to date divided by total fee (fixed contract + hourly extras).\n\n" +
                "NOTE:\n" +
                "When the cell is italic + amber, the displayed percentage includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. Once posting catches up the styling clears and the value matches the posted-only formula above.",
            Formula = ""
        };
        d["HealthyProjects"] = new FinancialMetricDefinition
        {
            Key = "HealthyProjects",
            DisplayName = "Healthy Projects",
            Description =
                "WHAT:\n" +
                "Number of projects currently assessed as High Confidence.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows how much of the portfolio is financially stable right now.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Counts projects classified as High Confidence by Delivery Risk.",
            Formula = ""
        };
        d["WatchProjects"] = new FinancialMetricDefinition
        {
            Key = "WatchProjects",
            DisplayName = "Watch Projects",
            Description =
                "WHAT:\n" +
                "Number of projects currently assessed as Watch.\n\n" +
                "WHY IT MATTERS:\n" +
                "Identifies the workload needing attention before it becomes critical.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Counts projects classified as Watch by Delivery Risk.",
            Formula = ""
        };
        d["CriticalProjects"] = new FinancialMetricDefinition
        {
            Key = "CriticalProjects",
            DisplayName = "Critical Projects",
            Description =
                "WHAT:\n" +
                "Number of projects currently assessed as Critical.\n\n" +
                "WHY IT MATTERS:\n" +
                "Quantifies immediate delivery and financial risk requiring intervention.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Counts projects classified as Critical by Delivery Risk.",
            Formula = ""
        };
        d["DeliveryConfidence"] = new FinancialMetricDefinition
        {
            Key = "DeliveryConfidence",
            DisplayName = "Delivery Risk",
            Description =
                "WHAT:\n" +
                "Predicts whether a project is likely to finish within its planned fee and engineering hours.\n\n" +
                "WHY IT MATTERS:\n" +
                "Gives leadership early warning before budget or profitability problems occur.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Evaluates billing progress, hours consumed, and remaining budget to determine financial stability.",
            Formula = ""
        };
    }
}
