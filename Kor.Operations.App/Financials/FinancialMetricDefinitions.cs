#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;

namespace Kor.Operations.Financials
{
    public sealed class FinancialMetricDefinition
    {
        public string Key { get; init; } = "";
        public string Category { get; init; } = "Financial";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public string Formula { get; init; } = "";
    }

    internal static class FinancialMetricDefinitions
    {
        internal static readonly Dictionary<string, FinancialMetricDefinition> Definitions =
            NormalizeDefinitions(new(StringComparer.OrdinalIgnoreCase)
            {
                ["TotalFees"] = new FinancialMetricDefinition
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
                },
                ["TotalFeeBilled"] = new FinancialMetricDefinition
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
                },
                ["TotalUnbilled"] = new FinancialMetricDefinition
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
                },
                ["PercentFeeUnbilled"] = new FinancialMetricDefinition
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
                },
                ["HoursSpent"] = new FinancialMetricDefinition
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
                },
                ["HoursBudgeted"] = new FinancialMetricDefinition
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
                },
                ["HoursRemaining"] = new FinancialMetricDefinition
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
                },
                ["PercentHoursSpent"] = new FinancialMetricDefinition
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
                },
                ["AvgFeePerFt2"] = new FinancialMetricDefinition
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
                },
                ["TeamDaysRemaining"] = new FinancialMetricDefinition
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
                },
                ["Backlog"] = new FinancialMetricDefinition
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
                },

                // ── Revenue Forecast section metrics ──
                ["Forecast_Trailing12"] = new FinancialMetricDefinition
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
                },
                ["Forecast_Trailing3"] = new FinancialMetricDefinition
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
                },
                ["Forecast_Backlog"] = new FinancialMetricDefinition
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
                },
                ["Forecast_MonthsOfRunway"] = new FinancialMetricDefinition
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
                },
                ["Forecast_Next3"] = new FinancialMetricDefinition
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
                },
                ["Forecast_Next12"] = new FinancialMetricDefinition
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
                },
                ["PercentBilled"] = new FinancialMetricDefinition
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
                },
                ["HealthyProjects"] = new FinancialMetricDefinition
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
                },
                ["WatchProjects"] = new FinancialMetricDefinition
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
                },
                ["CriticalProjects"] = new FinancialMetricDefinition
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
                },
                ["DeliveryConfidence"] = new FinancialMetricDefinition
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
                },


                // Executive Summary KPIs (Command Center)
                ["Exec_CashPosition"] = new FinancialMetricDefinition
                {
                    Key = "Exec_CashPosition",
                    DisplayName = "Cash Position",
                    Description =
                        "WHAT:\n" +
                        "Estimated cash across bank accounts shown in Deltek CFGBanks.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Fast signal for liquidity and near-term operating flexibility.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Maps CFGBanks accounts to GLSummary and sums balances by company (CAD/USA/BCC) for the latest available period.",
                    Formula = "CFGBanks -> GLSummary (latest period); sum balances by company"
                },
                ["Exec_Liquidity"] = new FinancialMetricDefinition
                {
                    Key = "Exec_Liquidity",
                    DisplayName = "Liquidity (Cash + AR)",
                    Description =
                        "WHAT:\n" +
                        "Bank cash balance plus firmwide outstanding AR — total near-cash assets.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Cash alone understates the firm's liquidity. AR is real money clients owe " +
                        "and typically settles within 30-60 days. The sum is what's available to " +
                        "fund operations once collections come in.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Cash Position (CAD-equivalent of all bank GL balances) + firmwide AR " +
                        "(SUM of AR.InvBalanceSourceCurrency for all open invoices, no scope filter).",
                    Formula = "Cash (CAD-equiv) + Σ AR.InvBalanceSourceCurrency"
                },
                ["Exec_ArOutstanding"] = new FinancialMetricDefinition
                {
                    Key = "Exec_ArOutstanding",
                    DisplayName = "AR Outstanding",
                    Description =
                        "WHAT:\n" +
                        "Firmwide open invoice balance — money clients have been billed but " +
                        "haven't paid yet.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Represents cash that has been invoiced but not yet collected. Combined " +
                        "with Cash Position gives the true liquidity picture.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums AR.InvBalanceSourceCurrency across all open invoices firmwide. The " +
                        "breakdown table is filtered to projects in the current scope (Watchlist " +
                        "or All Active) for relevance, but the headline number is firmwide.",
                    Formula = "SUM(AR.InvBalanceSourceCurrency) firmwide"
                },
                ["Exec_ArOver60"] = new FinancialMetricDefinition
                {
                    Key = "Exec_ArOver60",
                    DisplayName = "AR > 60 Days",
                    Description =
                        "WHAT:\n" +
                        "Portion of open AR that is more than 60 days past due.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Aging AR is a collection risk and can signal disputes or billing quality issues.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums open AR where DueDate (or InvoiceDate if DueDate is null) is older than today minus 60 days.",
                    Formula = "SUM(InvBalance) WHERE COALESCE(DueDate,InvoiceDate) <= Today-60"
                },
                                                ["Exec_WipUnbilled"] = new FinancialMetricDefinition
                {
                    Key = "Exec_WipUnbilled",
                    DisplayName = "WIP (Unbilled Earned)",
                    Description =
                        "WHAT:\n" +
                        "Earned revenue not yet invoiced (contract asset) for the watchlist portfolio.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows delivery progress that has not been converted into invoices yet, and highlights overbilling risk.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "As of the latest closed period, we compute a balance and show three numbers: earned (asset), overbilled (liability), and net = earned - overbilled.\n" +
                        "If PRSummaryMain.Unbilled is populated, Unbilled is used directly. If Unbilled is empty in this environment, we use a proxy balance where Diff = Revenue - Billed (by period), accumulated through the as-of period.\n" +
                        "The Executive Summary card also shows a firmwide proxy breakdown for context.",
                    Formula = "Earned=SUM(max(Diff,0)); Overbilled=SUM(max(-Diff,0)); Net=Earned-Overbilled (Diff=Unbilled or Revenue-Billed)"
                },
["Exec_WipFirmwide"] = new FinancialMetricDefinition
                {
                    Key = "Exec_WipFirmwide",
                    DisplayName = "WIP (Firmwide Proxy)",
                    Description =
                        "WHAT:\n" +
                        "Firmwide proxy view of earned-unbilled vs overbilled (contract asset/liability) as of the latest closed period.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Provides context so a watchlist that nets to $0 is not misleading.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Across all projects in PRSummaryMain through the latest closed period: Diff = Revenue - Billed. Earned = SUM(max(Diff,0)); Overbilled = SUM(max(-Diff,0)); Net = SUM(Diff).",
                    Formula = "Firmwide cumulative: Earned=SUM(max(Rev-Billed,0)); Overbilled=SUM(max(Billed-Rev,0)); Net=SUM(Rev-Billed)"
                },["Exec_WipPreInvoice"] = new FinancialMetricDefinition
                {
                    Key = "Exec_WipPreInvoice",
                    DisplayName = "WIP (Draft Invoices)",
                    Description =
                        "WHAT:\n" +
                        "Draft invoices in progress that are not yet posted as invoices.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Represents near-term billing pipeline (what could become invoices soon).\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums ARPreInvoiceDetail.Amount - PaidAmount for pre-invoices that are not cancelled and not applied to an invoice.",
                    Formula = "SUM(ARPreInvoiceDetail.Amount - PaidAmount) for open pre-invoices"
                },
                ["Exec_Backlog"] = new FinancialMetricDefinition
                {
                    Key = "Exec_Backlog",
                    DisplayName = "Backlog",
                    Description =
                        "WHAT:\n" +
                        "Remaining contracted fee not yet billed for the watchlist portfolio.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Indicates future billing runway and remaining revenue on active work.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "For each watchlist project, Backlog = Total Fees - Total Fee Billed; then sums across projects.",
                    Formula = "Portfolio Backlog = SUM(TotalFees - TotalFeeBilled) over watchlist projects"
                },
                ["Exec_BillingsToDate"] = new FinancialMetricDefinition
                {
                    Key = "Exec_BillingsToDate",
                    DisplayName = "Billings To Date",
                    Description =
                        "WHAT:\n" +
                        "Total fee billed to date across watchlist projects.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows invoicing progress and realized billings against the portfolio fee base.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums Total Fee Billed across watchlist projects (no period filter in this KPI card).",
                    Formula = "SUM(TotalFeeBilled) over watchlist projects"
                },
                ["Exec_ProjectsOverBudget"] = new FinancialMetricDefinition
                {
                    Key = "Exec_ProjectsOverBudget",
                    DisplayName = "Projects Over Budget",
                    Description =
                        "WHAT:\n" +
                        "Count of watchlist projects where engineering hours consumed are above the engineering budget.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Flags immediate delivery overrun and margin pressure that needs PM action.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "For each project, Remaining Engineering Hours = Engineering Budget - Engineering Hours Spent.\n" +
                        "A project is marked Over Budget when Remaining Engineering Hours is below zero.\n" +
                        "The KPI value is the count of those projects.",
                    Formula = "RemainingEngHours = EngBudget - EngHours; OverBudget when RemainingEngHours < 0; KPI = COUNT(OverBudget projects)"
                },
                ["Exec_BudgetBurn"] = new FinancialMetricDefinition
                {
                    Key = "Exec_BudgetBurn",
                    DisplayName = "Budget Burn",
                    Description =
                        "WHAT:\n" +
                        "Portion of planned engineering hours already used.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Quickly signals delivery burn against plan across the selected portfolio.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "At the portfolio level, divides total engineering hours spent by total engineering hours budgeted.\n" +
                        "The detail grid shows project-level burn and remaining engineering hours to identify outliers.",
                    Formula =
                        "-- Portfolio KPI (watchlist scope)\n" +
                        "SELECT\n" +
                        "  SUM(EngHrs)    AS HoursSpent,\n" +
                        "  SUM(EngBudget) AS HoursBudget,\n" +
                        "  CASE WHEN SUM(EngBudget)=0 THEN 0 ELSE SUM(EngHrs)/SUM(EngBudget) END AS BudgetBurnPct\n" +
                        "FROM PortfolioUtilizationSnapshot\n" +
                        "WHERE WBS1 IN (Watchlist);\n\n" +
                        "-- Detail grid\n" +
                        "SELECT WBS1, ProjectName, PM, EngHrs, EngBudget,\n" +
                        "       CASE WHEN EngBudget=0 THEN 0 ELSE EngHrs/EngBudget END AS BurnPct,\n" +
                        "       (EngBudget-EngHrs) AS RemainingHours\n" +
                        "FROM PortfolioUtilizationSnapshot\n" +
                        "WHERE WBS1 IN (Watchlist)\n" +
                        "ORDER BY BurnPct DESC"
                },
                ["Exec_Utilization30"] = new FinancialMetricDefinition
                {
                    Key = "Exec_Utilization30",
                    DisplayName = "Utilization (30d)",
                    Description =
                        "WHAT:\n" +
                        "Billable charged hours divided by total charged hours over the last 30 days.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Indicates how efficiently delivered effort is converting into billable work.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "From tkDetail in the last 30 days: total hours = RegHrs+OvtHrs+SpecialOvtHrs; billable hours are rows with BillExt > 0.",
                    Formula = "SUM(hours where BillExt>0) / SUM(all hours)"
                },
                ["Exec_Revenue3090"] = new FinancialMetricDefinition
                {
                    Key = "Exec_Revenue3090",
                    DisplayName = "Revenue (Earned) 30/90",
                    Description =
                        "WHAT:\n" +
                        "Earned revenue in the last 30 and 90 days (portfolio), shown alongside invoiced amounts and the unbilled gap.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows delivery-driven revenue pace and whether invoicing is keeping up with earned work.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums PRSummaryMain.BilledFee (with legacy Revenue fallback) for periods whose period-end date falls within the last 30/90 days.\n" +
                        "Also calculates Invoiced from PRSummaryMain.Billed and Unbilled Gap = Earned - Invoiced for each window.",
                    Formula = "Earned30/90 = SUM(PRSummaryMain.BilledFee else Revenue); Invoiced30/90 = SUM(PRSummaryMain.Billed); UnbilledGap30/90 = Earned30/90 - Invoiced30/90"
                },
                ["Exec_Billed3090"] = new FinancialMetricDefinition
                {
                    Key = "Exec_Billed3090",
                    DisplayName = "Billings (Invoiced) 30/90",
                    Description =
                        "WHAT:\n" +
                        "Invoice billings in the last 30 and 90 days (portfolio), with collection exposure context.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows cash-generation pace, billing cadence, and how much of billed value remains in AR.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums PRSummaryMain.Billed for periods whose period-end date falls within the last 30/90 days.\n" +
                        "Collection exposure ratio = current AR Outstanding / 90-day billed.",
                    Formula = "Billed30/90 = SUM(PRSummaryMain.Billed); CollectionExposure = AROutstanding / Billed90"
                },
                ["Exec_ArOutstandingRecent"] = new FinancialMetricDefinition
                {
                    Key = "Exec_ArOutstandingRecent",
                    DisplayName = "AR Outstanding (Recent Months)",
                    Description =
                        "WHAT:\n" +
                        "Period-end AR outstanding trend over recent months for watchlist projects.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows whether unpaid invoiced balances are improving or accumulating.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "For each closed period, sums PRSummaryMain.AR across watchlist WBS1 and trends the recent series.",
                    Formula = "ARSeries(period) = SUM(PRSummaryMain.AR) by period (watchlist)"
                },
                ["Exec_DeliveryRiskCriticalCount"] = new FinancialMetricDefinition
                {
                    Key = "Exec_DeliveryRiskCriticalCount",
                    DisplayName = "Delivery Risk (Critical Count)",
                    Description =
                        "WHAT:\n" +
                        "Count of projects currently classified as Critical delivery risk, trended over time.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Tracks concentration of immediate delivery risk that may require intervention.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses stored weekly portfolio snapshots and trends the Critical count by snapshot date.",
                    Formula = "CriticalSeries(week) = COUNT(projects where DeliveryRisk = Critical)"
                },
                ["Exec_CollectionExposure"] = new FinancialMetricDefinition
                {
                    Key = "Exec_CollectionExposure",
                    DisplayName = "Collection Exposure (AR / 90-day Billed)",
                    Description =
                        "WHAT:\n" +
                        "Ratio of current AR Outstanding to billings invoiced in the last 90 days.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "High values indicate cash collection is lagging recent invoice production.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Divides current AR Outstanding by Billed90 (last 90-day PRSummaryMain.Billed sum).",
                    Formula = "CollectionExposure = AROutstanding / Billed90"
                },
                ["Exec_UnbilledGap3090"] = new FinancialMetricDefinition
                {
                    Key = "Exec_UnbilledGap3090",
                    DisplayName = "Unbilled Gap (30/90)",
                    Description =
                        "WHAT:\n" +
                        "Difference between earned revenue and invoiced billings over the last 30 and 90 days.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Positive gap means delivered value not yet invoiced; negative gap means billings outpaced recognized earned value in the window.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "For each window: Unbilled Gap = Revenue - Billed.",
                    Formula = "UnbilledGap30/90 = SUM(PRSummaryMain.BilledFee else Revenue) - SUM(PRSummaryMain.Billed)"
                },
                ["Alert_ArOver60"] = new FinancialMetricDefinition
                {
                    Key = "Alert_ArOver60",
                    DisplayName = "Alert: AR > 60 Days",
                    Description =
                        "WHAT:\n" +
                        "Alert when open AR more than 60 days old exists.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Signals elevated collection risk and potential dispute/billing-cycle issues.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Checks whether portfolio AR aged 61-90 plus 90+ is greater than zero.",
                    Formula = "Trigger when SUM(AR where age>60 days) > 0"
                },
                ["Alert_ProjectsOverBudget"] = new FinancialMetricDefinition
                {
                    Key = "Alert_ProjectsOverBudget",
                    DisplayName = "Alert: Projects Over Budget",
                    Description =
                        "WHAT:\n" +
                        "Alert when one or more projects have negative remaining engineering hours.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Flags immediate delivery budget overrun risk.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Triggers when any project has Remaining Engineering Hours = EngBudget - EngHrs below zero.",
                    Formula = "Trigger when COUNT(projects with EngBudget-EngHrs<0) > 0"
                },
                ["Alert_BacklogDeclining"] = new FinancialMetricDefinition
                {
                    Key = "Alert_BacklogDeclining",
                    DisplayName = "Alert: Backlog Declining",
                    Description =
                        "WHAT:\n" +
                        "Placeholder alert about backlog trend deterioration.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Would indicate diminishing future billing runway.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Not currently sourced because historical backlog trend is not loaded in this module.",
                    Formula = "Not currently calculated (historical backlog series unavailable)"
                },
                ["Alert_BillingLaggingBurn"] = new FinancialMetricDefinition
                {
                    Key = "Alert_BillingLaggingBurn",
                    DisplayName = "Alert: Billing Lagging Burn",
                    Description =
                        "WHAT:\n" +
                        "Alert when delivery burn materially outpaces fee billed percentage.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows potential margin and cash timing risk from delayed billing relative to effort consumed.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "At portfolio level, triggers when (PercentHoursSpent - PercentFeeBilled) >= 15 percentage points and burn >= 60%.",
                    Formula = "Trigger when (BurnPct - BilledPct) >= 0.15 AND BurnPct >= 0.60"
                },
                // Section tooltips (UI-only): executive-grade definitions.
                ["PortfolioDeliveryHealth"] = new FinancialMetricDefinition
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
                },
                ["PortfolioRiskExposure"] = new FinancialMetricDefinition
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
                },
                ["PortfolioTrend"] = new FinancialMetricDefinition
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
                },
                ["RiskDrivers"] = new FinancialMetricDefinition
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
                },
                ["DeliveryTrend"] = new FinancialMetricDefinition
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
                },
                ["HealthIndicators"] = new FinancialMetricDefinition
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
                },
                ["BurnRisk"] = new FinancialMetricDefinition
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
                }
                ,

                // GL P&L (read-only): definitions for the executive report window.
                ["GlPnL_DateRange"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_DateRange",
                    DisplayName = "P&L Date Range",
                    Description =
                        "WHAT:\n" +
                        "The accounting period range used to build the P&L columns.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Sets which months and executive rollups (current, YTD, trailing) are shown.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Converts the selected dates to Deltek periods (YYYYMM) and includes any periods found in GLSummary within that range.",
                    Formula = ""
                },
                ["GlPnL_Table"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_Table",
                    DisplayName = "P&L Table",
                    Description =
                        "WHAT:\n" +
                        "The Deltek GL income statement definition used for grouping lines.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Different GL tables can produce different section layouts and account groupings.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses GLTable + GLParentHeading/GLParentDetail + GLGroupDetail to determine which accounts roll into each line.",
                    Formula = ""
                },
                ["GlPnL_Org"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_Org",
                    DisplayName = "Org Filter",
                    Description =
                        "WHAT:\n" +
                        "Optional organization (Org) filter applied to the GL P&L.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Lets leadership view a specific legal entity or org rollup (e.g., CAD, USA).\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Filters GLSummary rows to the selected Org before line aggregation.",
                    Formula = ""
                },
                ["GlPnL_FlipSign"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_FlipSign",
                    DisplayName = "Flip Sign",
                    Description =
                        "WHAT:\n" +
                        "Toggles sign conventions on the report.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Some GL setups store income as negative (credits) and expenses as positive (debits). Flipping can make the report easier to read.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Multiplies all aggregated GLSummary amounts by -1 before displaying them.",
                    Formula = ""
                },
                ["GlPnL_HideZeros"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_HideZeros",
                    DisplayName = "Hide Zeros",
                    Description =
                        "WHAT:\n" +
                        "Hides lines that are zero across all displayed periods.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Reduces noise so executives can focus on meaningful drivers.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Suppresses rows that have 0 across all period columns.",
                    Formula = ""
                },
                ["GlPnL_RevenuePeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_RevenuePeriod",
                    DisplayName = "Revenue (Range)",
                    Description =
                        "WHAT:\n" +
                        "Total revenue summed across every month in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Top-line view that matches what the From/To pickers show in the grid.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums the Total Revenue Summary row across all displayed period columns.",
                    Formula = ""
                },
                ["GlPnL_ExpensesPeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_ExpensesPeriod",
                    DisplayName = "Expenses (Range)",
                    Description =
                        "WHAT:\n" +
                        "Total expenses summed across every month in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Cost view that matches what the From/To pickers show in the grid.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums the Total Expenses Summary row across all displayed period columns.",
                    Formula = ""
                },
                ["GlPnL_NetIncomePeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_NetIncomePeriod",
                    DisplayName = "Net Income (Range)",
                    Description =
                        "WHAT:\n" +
                        "Net income summed across every month in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Single-number view of profitability over the displayed window.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums the Net Income Summary row across all displayed period columns. " +
                        "For Posted GL the sign depends on Flip Sign; the default sign convention " +
                        "is configured so revenue is positive and expenses are negative, giving a " +
                        "signed Net Income directly.",
                    Formula = ""
                },
                ["GlPnL_NetMarginPeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_NetMarginPeriod",
                    DisplayName = "Net Margin (Range)",
                    Description =
                        "WHAT:\n" +
                        "Net income as a percentage of revenue over the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Normalizes profitability so it is comparable across time and orgs.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Net Margin = Net Income (range) / |Revenue (range)|. The denominator uses " +
                        "magnitude to avoid sign confusion when revenue is stored with an accounting sign.",
                    Formula = ""
                },
                ["GlPnL_NetIncomeTrend"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_NetIncomeTrend",
                    DisplayName = "Net Income Trend",
                    Description =
                        "WHAT:\n" +
                        "Net income across the last 12 available accounting periods.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows momentum and volatility at a glance.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses the Summary Net Income row across the last 12 periods.",
                    Formula = ""
                },
                ["GlPnL_RevVsExpTrend"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_RevVsExpTrend",
                    DisplayName = "Revenue vs Expenses Trend",
                    Description =
                        "WHAT:\n" +
                        "Stacked view of revenue and expenses across the last 12 periods.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Makes margin compression and cost expansion visually obvious.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses magnitudes of Total Revenue and Total Expenses from the Summary rows.",
                    Formula = ""
                },
                ["GlPnL_LineItem"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_LineItem",
                    DisplayName = "P&L Line Item",
                    Description =
                        "WHAT:\n" +
                        "A reporting line that rolls up one or more GL accounts.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows which cost or income drivers are moving the business.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Account ranges are defined in GLGroupDetail for the selected GL table.",
                    Formula = ""
                },
                ["GlPnL_PeriodColumn"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_PeriodColumn",
                    DisplayName = "Accounting Period",
                    Description =
                        "WHAT:\n" +
                        "A single accounting period column from Deltek (YYYYMM).\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Period-by-period view helps pinpoint when changes occurred.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Aggregates GLSummary.Amount for the period into each P&L line group.",
                    Formula = ""
                },
                ["GlPnL_Current"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_Current",
                    DisplayName = "Current",
                    Description =
                        "WHAT:\n" +
                        "The value for the most recent period in the selected range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Acts as the headline month for the report.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses the last period column in the selected range.",
                    Formula = ""
                },
                ["GlPnL_Prior"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_Prior",
                    DisplayName = "Prior",
                    Description =
                        "WHAT:\n" +
                        "The value for the period immediately before Current.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Provides a quick baseline for month-over-month change.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses the second-to-last period column in the selected range.",
                    Formula = ""
                },
                ["GlPnL_MoM"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_MoM",
                    DisplayName = "MoM Change",
                    Description =
                        "WHAT:\n" +
                        "Change from Prior to Current.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Highlights the immediate drivers moving the latest month.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "MoM = Current - Prior.",
                    Formula = ""
                },
                ["GlPnL_MoMPct"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_MoMPct",
                    DisplayName = "MoM %",
                    Description =
                        "WHAT:\n" +
                        "Month-over-month change expressed as a percentage of Prior.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Normalizes changes so small and large lines are comparable.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "MoM % = (Current - Prior) / |Prior|.",
                    Formula = ""
                },
                ["GlPnL_YTD"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_YTD",
                    DisplayName = "Year-to-Date (YTD)",
                    Description =
                        "WHAT:\n" +
                        "Sum of periods from the start of the fiscal year through Current.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Used for leadership reporting and progress against annual expectations.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums period columns from fiscal-year start through Current (configured start month).",
                    Formula = ""
                },
                ["GlPnL_TTM"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_TTM",
                    DisplayName = "Trailing 12 Months (TTM)",
                    Description =
                        "WHAT:\n" +
                        "Sum of the most recent 12 accounting periods.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Smooths seasonality and provides a stable view of run-rate performance.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums the last 12 period columns available in the selected range.",
                    Formula = ""
                },
                ["GlPnL_PctOfRevenue"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_PctOfRevenue",
                    DisplayName = "% of Revenue",
                    Description =
                        "WHAT:\n" +
                        "Each line expressed as a percentage of Total Revenue for the Current period.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Makes cost structure and overhead intensity immediately obvious.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Pct of Revenue = Current / |Total Revenue (Current)|.",
                    Formula = ""
                },
                ["GlPnL_Export"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_Export",
                    DisplayName = "Export to Excel",
                    Description =
                        "WHAT:\n" +
                        "Exports the current GL P&L view to an Excel workbook.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Supports executive distribution, offline review, and retaining consistent formatting.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Writes the same table shown on-screen (respecting Hide Zeros) to a formatted .xlsx file.",
                    Formula = ""
                },
                ["PmTools_ActiveProjects"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_ActiveProjects", Category = "PM",
                    DisplayName = "Active Projects",
                    Description =
                        "WHAT:\nTotal number of active projects currently tracked in the PM Tools dashboard.\n\n" +
                        "WHY IT MATTERS:\nProvides a quick read on portfolio size and overall workload volume.\n\n" +
                        "HOW IT IS CALCULATED:\nCounts all projects returned from the Deltek watchlist query."
                },
                ["PmTools_AtRiskCritical"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_AtRiskCritical", Category = "PM",
                    DisplayName = "At Risk / Critical",
                    Description =
                        "WHAT:\nCount of projects whose delivery confidence is At Risk or Critical.\n\n" +
                        "WHY IT MATTERS:\nHighlights projects that need immediate PM attention to avoid schedule or budget overruns.\n\n" +
                        "HOW IT IS CALCULATED:\nCounts projects where hours spent as a share of budget outpaces fee billed as a share of contract, or where fee billed already exceeds contracted fee."
                },
                ["PmTools_EngHoursRemaining"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_EngHoursRemaining", Category = "PM",
                    DisplayName = "Eng Hours Remaining (Portfolio)",
                    Description =
                        "WHAT:\nSum of remaining engineering hours across all active projects.\n\n" +
                        "WHY IT MATTERS:\nShows total available engineering capacity before budgets are exhausted across the portfolio.\n\n" +
                        "HOW IT IS CALCULATED:\nFor each project: Engineering Budget - Engineering Hours Spent. Negative values indicate over-budget projects. Summed across all projects."
                },
                ["PmTools_DraftHoursRemaining"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftHoursRemaining", Category = "PM",
                    DisplayName = "Draft Hours Remaining (Portfolio)",
                    Description =
                        "WHAT:\nSum of remaining drafting hours across all active projects.\n\n" +
                        "WHY IT MATTERS:\nShows total available drafting capacity before budgets are exhausted across the portfolio.\n\n" +
                        "HOW IT IS CALCULATED:\nFor each project: Drafting Budget - Drafting Hours Spent. Negative values indicate over-budget projects. Summed across all projects."
                },
                ["PmTools_OverEngBudget"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_OverEngBudget", Category = "PM",
                    DisplayName = "Over Eng Budget",
                    Description =
                        "WHAT:\nCount of projects where engineering hours spent exceed the engineering hour budget.\n\n" +
                        "WHY IT MATTERS:\nFlags projects already past their engineering budget, requiring scope review or reallocation.\n\n" +
                        "HOW IT IS CALCULATED:\nCounts projects where Remaining Engineering Hours < 0."
                },
                ["PmTools_FeeRemaining"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_FeeRemaining", Category = "PM",
                    DisplayName = "Fee Remaining",
                    Description =
                        "WHAT:\nTotal unbilled fee across all watchlist projects.\n\n" +
                        "WHY IT MATTERS:\nShows the portfolio backlog, meaning work already under contract but not yet billed.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of (Contract Fee - Fee Billed) for every active watchlist project.",
                    Formula = "SUM(Fee - FeeBilled)"
                },
                ["PmTools_EngBudget"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_EngBudget", Category = "PM",
                    DisplayName = "Engineering Budget (hrs)",
                    Description =
                        "Engineering hours budgeted for this project.\n\n" +
                        "Black text = actual budget from Deltek (PRLabor.EstimateHrs).\n" +
                        "Purple italic with * = estimated — no budget exists in Deltek.\n\n" +
                        "HOW ESTIMATES ARE CALCULATED:\n" +
                        "Primary: Peer-based — finds similar completed projects (fee ±50%, same construction type/phase, 50+ hrs) and uses their median eng hours.\n" +
                        "Fallback: Formula — (Fee / Target) × (Combined / EngRate). Only used when fewer than 3 peers found.\n\n" +
                        "The peer-based approach is much more accurate because it accounts for construction type and project complexity, not just fee.",
                    Formula = "Actual: PRLabor | Primary: Peer median | Fallback: (Fee / Target) × (Combined / EngRate)"
                },
                ["PmTools_EngHrs"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_EngHrs", Category = "PM",
                    DisplayName = "Engineering Hours Spent",
                    Description =
                        "WHAT:\nEngineering hours charged to this project to date.\n\n" +
                        "WHY IT MATTERS:\nTracks actual engineering effort consumed versus budget.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of all labor hours posted to engineering labor codes in Deltek for this project."
                },
                ["PmTools_EngPercent"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_EngPercent", Category = "PM",
                    DisplayName = "Engineering % Used",
                    Description =
                        "WHAT:\nShare of the engineering hour budget consumed so far.\n\n" +
                        "WHY IT MATTERS:\nWhen compared to % fee billed, reveals whether engineering effort is outpacing billing progress.\n\n" +
                        "HOW IT IS CALCULATED:\nEngineering Hours Spent / Engineering Budget."
                },
                ["PmTools_EngRemaining"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_EngRemaining", Category = "PM",
                    DisplayName = "Remaining Engineering Hours",
                    Description =
                        "WHAT:\nEngineering hours still available before the budget is exhausted.\n\n" +
                        "WHY IT MATTERS:\nA negative value means the project is already over its engineering budget. Values below 15% of budget trigger an At Risk flag.\n\n" +
                        "HOW IT IS CALCULATED:\nEngineering Budget - Engineering Hours Spent."
                },
                ["PmTools_DraftBudget"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftBudget", Category = "PM",
                    DisplayName = "Drafting Budget (hrs)",
                    Description =
                        "Drafting hours budgeted for this project.\n\n" +
                        "Black text = actual budget from Deltek (PRLabor.EstimateHrs).\n" +
                        "Purple italic with * = estimated — no budget exists in Deltek.\n\n" +
                        "HOW ESTIMATES ARE CALCULATED:\n" +
                        "Primary: Peer-based — finds similar completed projects (fee ±50%, same construction type/phase, 50+ hrs) and uses their median draft hours.\n" +
                        "Fallback: Formula — (Fee / Target) × (Combined / DraftRate). Only used when fewer than 3 peers found.\n\n" +
                        "The peer-based approach is much more accurate because it accounts for construction type and project complexity, not just fee.",
                    Formula = "Actual: PRLabor | Primary: Peer median | Fallback: (Fee / Target) × (Combined / DraftRate)"
                },
                ["PmTools_DraftHrs"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftHrs", Category = "PM",
                    DisplayName = "Drafting Hours Spent",
                    Description =
                        "WHAT:\nDrafting hours charged to this project to date.\n\n" +
                        "WHY IT MATTERS:\nTracks actual drafting effort consumed versus budget.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of all labor hours posted to drafting labor codes in Deltek for this project."
                },
                ["PmTools_DraftPercent"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftPercent", Category = "PM",
                    DisplayName = "Drafting % Used",
                    Description =
                        "WHAT:\nShare of the drafting hour budget consumed so far.\n\n" +
                        "WHY IT MATTERS:\nHighlights drafting-heavy projects that may exhaust production capacity before completion.\n\n" +
                        "HOW IT IS CALCULATED:\nDrafting Hours Spent / Drafting Budget."
                },
                ["PmTools_DraftRemaining"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftRemaining", Category = "PM",
                    DisplayName = "Remaining Drafting Hours",
                    Description =
                        "WHAT:\nDrafting hours still available before the budget is exhausted.\n\n" +
                        "WHY IT MATTERS:\nA negative value means the project is already over its drafting budget. Values below 15% of budget trigger an At Risk flag.\n\n" +
                        "HOW IT IS CALCULATED:\nDrafting Budget - Drafting Hours Spent."
                },
                // PmTools_ChkHrs removed — Checking merged into Engineering
                ["PmTools_InspHrs"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_InspHrs", Category = "PM",
                    DisplayName = "Inspection Hours",
                    Description =
                        "WHAT:\nHours charged to site inspection labor codes on this project.\n\n" +
                        "WHY IT MATTERS:\nInspection hours indicate Construction Administration workload and can signal scope creep if unusually high.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of hours posted to inspection labor codes in Deltek."
                },
                ["PmTools_DeliveryRisk"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DeliveryRisk", Category = "PM",
                    DisplayName = "Delivery Risk",
                    Description =
                        "WHAT:\nA four-level rating summarising how well a project's effort consumption aligns with its billing progress.\n\n" +
                        "WHY IT MATTERS:\nProvides an at-a-glance signal for PMs to identify which projects are drifting toward overrun before it becomes a financial problem.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Critical  fee billed exceeds contracted fee, OR hours spent exceed budgeted hours.\n" +
                        "At Risk  hours-spent % exceeds fee-billed % by more than 15 percentage points.\n" +
                        "Watch  remaining engineering hours are below 15% of budget.\n" +
                        "High Confidence  none of the above conditions apply."
                },
                ["PmTools_CapacityRisk"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_CapacityRisk", Category = "PM",
                    DisplayName = "Capacity Risk",
                    Description =
                        "WHAT:\nA ranked view of projects by how much of their engineering or drafting budget has been consumed.\n\n" +
                        "WHY IT MATTERS:\nHelps resource managers spot which projects are drawing down team capacity fastest, enabling proactive reallocation before budgets are exhausted.\n\n" +
                        "HOW IT IS CALCULATED:\nProjects are sorted by remaining hours (ascending). Risk status: Over budget = remaining < 0; At risk = remaining < 15% of budget; Healthy = otherwise."
                },

                ["PmTools_Fee"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_Fee", Category = "PM",
                    DisplayName = "Fee",
                    Description =
                        "WHAT:\nThe total project fee — fixed contract amount plus any hourly/T&M extras revenue.\n\n" +
                        "WHY IT MATTERS:\nThis is the number all budget, billing, and profitability metrics are measured against. It reflects the true project value, not just the original contract.\n\n" +
                        "HOW IT IS CALCULATED:\nFixed fee from Deltek (PR.Fee) plus revenue from any hourly extras elements."
                },
                ["PmTools_PercentBilled"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_PercentBilled", Category = "PM",
                    DisplayName = "% Fee Billed",
                    Description =
                        "WHAT:\nHow much of the total project fee has been invoiced to the client.\n\n" +
                        "WHY IT MATTERS:\nCompare this to % Hours Spent. If you've used most of the hours but billed a small share of the fee, the project is burning faster than it's earning.\n\n" +
                        "HOW IT IS CALCULATED:\nAmount billed to date divided by total fee (fixed + hourly).\n\n" +
                        "NOTE:\nWhen the cell is italic, the displayed percentage includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. The bar color still reflects the unposted-inclusive % billed; once posting catches up the italic clears and the value matches the posted-only formula above.",
                    Formula = "Fee Billed / Total Fee"
                },
                ["PmTools_Unbilled"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_Unbilled", Category = "PM",
                    DisplayName = "Unbilled Fee",
                    Description =
                        "WHAT:\nThe dollar amount of the contracted fee that has not yet been invoiced.\n\n" +
                        "WHY IT MATTERS:\nRepresents the remaining revenue opportunity on this project. A negative value means the project has been billed beyond its contracted fee.\n\n" +
                        "HOW IT IS CALCULATED:\nFee − Fee Billed.",
                    Formula = "Fee - Fee Billed"
                },
                ["PmTools_FeePerHours"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_FeePerHours", Category = "PM",
                    DisplayName = "Fee / Hours",
                    Description =
                        "WHAT:\nThe contracted fee divided by total billable hours charged (engineering, drafting, checking, and inspection).\n\n" +
                        "WHY IT MATTERS:\nShows the effective hourly rate implied by the project's fee and effort to date. A declining value over time signals scope creep or underestimated effort. Compare against the firm's target billing rate.\n\n" +
                        "HOW IT IS CALCULATED:\nFee ÷ (Eng Hrs + Draft Hrs + Chk Hrs + Insp Hrs). Returns $0 if no billable hours have been logged.",
                    Formula = "Fee / (Eng Hrs + Draft Hrs + Chk Hrs + Insp Hrs)"
                },
                ["PmTools_BilledPerHours"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_BilledPerHours", Category = "PM",
                    DisplayName = "Billed / Hours",
                    Description =
                        "WHAT:\nThe total fee billed to date divided by ALL hours charged to the project, including non-billable time.\n\n" +
                        "WHY IT MATTERS:\nReveals the true revenue per hour of effort — including overhead hours that Fee/Hrs ignores. A large gap between Fee/Hrs and Billed/Hrs exposes hidden overhead or non-billable time on the project.\n\n" +
                        "HOW IT IS CALCULATED:\nFee Billed ÷ (Eng + Draft + Chk + Insp + DocPrep + Gen + Admin + NonBill hours). Returns $0 if no hours have been logged.\n\n" +
                        "NOTE:\nWhen the cell is italic + amber, the numerator includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. Once posting catches up the styling clears and the value matches the posted-only formula above.",
                    Formula = "Fee Billed / (all 8 labor code hours)"
                },
                ["PmTools_DraftingMgr"] = new FinancialMetricDefinition
                {
                    Key = "PmTools_DraftingMgr", Category = "PM",
                    DisplayName = "Drafting Manager",
                    Description =
                        "WHAT:\nThe drafting manager assigned to this project in Deltek Vantagepoint.\n\n" +
                        "WHY IT MATTERS:\nIdentifies who is responsible for managing production resources on this project.\n\n" +
                        "HOW IT IS CALCULATED:\nFrom ProjectCustomTabFields.CustDraftingManager joined to EMMain for the display name."
                },

                // ── Staff Utilization ─────────────────────────────────────────────────
                ["StaffUtil_Trend"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_Trend", Category = "Staff",
                    DisplayName = "Workload Trend",
                    Description =
                        "WHAT:\nDirection of change in an individual's recent workload relative to their rolling 12-week average.\n\n" +
                        "WHY IT MATTERS:\nHelps resource managers spot who is ramping up (↑) and may become a bottleneck, or easing off (↓) and may have capacity to absorb new work.\n\n" +
                        "HOW IT IS CALCULATED:\nCompares the 4-week average to the 12-week average.\n" +
                        "↑ = 4-wk avg > 12-wk avg × 1.10 (more than 10% above baseline)\n" +
                        "↓ = 4-wk avg < 12-wk avg × 0.90 (more than 10% below baseline)\n" +
                        "→ = within ±10% of the 12-wk baseline (stable)"
                },
                ["StaffUtil_ThisWeek"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_ThisWeek", Category = "Staff",
                    DisplayName = "This Week Hours",
                    Description =
                        "WHAT:\nHours logged in the 7-day rolling window ending today.\n\n" +
                        "WHY IT MATTERS:\nGives an immediate read on current workload. Use alongside the 4-week and 12-week averages for a stable picture — this figure can be skewed by holidays, leave, or late timesheet entry.\n\n" +
                        "HOW IT IS CALCULATED:\nSums tkDetail.RegHrs + OvtHrs where TransDate >= today - 7 days for the employee.",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE TransDate >= TODAY - 7"
                },
                ["StaffUtil_FourWkAvg"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_FourWkAvg", Category = "Staff",
                    DisplayName = "4-Week Average (hrs/wk)",
                    Description =
                        "WHAT:\nAverage hours per week over the past 28 days.\n\n" +
                        "WHY IT MATTERS:\nShort-term workload signal that reacts faster than the 12-week average. Useful for identifying emerging capacity pressure before it shows up in the longer rolling window.\n\n" +
                        "HOW IT IS CALCULATED:\nSums tkDetail hours for the past 28 days, then divides by 4.\n" +
                        "4-Wk Avg = SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 28) / 4",
                    Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 28) / 4"
                },
                ["StaffUtil_TwelveWkTotal"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_TwelveWkTotal", Category = "Staff",
                    DisplayName = "12-Week Total Hours",
                    Description =
                        "WHAT:\nAll hours logged across every project in the past 84 days (12 calendar weeks).\n\n" +
                        "WHY IT MATTERS:\nThe primary workload baseline for the Staff Utilization window. Covers a long enough window to smooth out holidays, leave, and single-week spikes.\n\n" +
                        "HOW IT IS CALCULATED:\nSums tkDetail.RegHrs + OvtHrs where TransDate >= today - 84 days.",
                    Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 84)"
                },
                ["StaffUtil_TwelveWkAvg"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_TwelveWkAvg", Category = "Staff",
                    DisplayName = "12-Week Average (hrs/wk)",
                    Description =
                        "WHAT:\nAverage hours per week over the past 12 calendar weeks.\n\n" +
                        "WHY IT MATTERS:\nThe denominator for Utilization %. Provides a stable, seasonality-smoothed view of sustained workload.\n\n" +
                        "HOW IT IS CALCULATED:\n12-Wk Total / 12.\n" +
                        "Values consistently above 37.5 indicate overtime culture; consistently below may signal bench time.",
                    Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 84) / 12"
                },
                ["StaffUtil_Overtime"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_Overtime", Category = "Staff",
                    DisplayName = "Overtime Hours (12 wk)",
                    Description =
                        "WHAT:\nTotal overtime hours (OvtHrs) logged across all projects in the 12-week window.\n\n" +
                        "WHY IT MATTERS:\nConsistently high overtime for an individual can signal under-resourcing, unrealistic deadlines, or an unsustainable workload that creates burnout risk and schedule fragility.\n\n" +
                        "HOW IT IS CALCULATED:\nSums tkDetail.OvtHrs where TransDate >= today - 84 days.",
                    Formula = "SUM(OvtHrs WHERE TransDate >= TODAY - 84)"
                },
                ["StaffUtil_LaborCost"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_LaborCost", Category = "Staff",
                    DisplayName = "12-Wk Labor Cost",
                    Description =
                        "WHAT:\nFully-burdened labor cost (regular + overtime + special overtime) over the past 12 weeks for this employee.\n\n" +
                        "WHY IT MATTERS:\nThe expense side of the utilization picture. Pair with billable hours and project margin to see whether this person's hours are paying for themselves at current rates.\n\n" +
                        "HOW IT IS CALCULATED:\nSUM(tkDetail.RegAmt + OvtAmt + SpecialOvtAmt) where TransDate >= today - 84 days. Cost is computed by Deltek using each employee's EMCompany.ProvCostRate.",
                    Formula = "SUM(RegAmt + OvtAmt + SpecialOvtAmt WHERE TransDate >= TODAY - 84)"
                },
                ["StaffUtil_OvertimeCost"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_OvertimeCost", Category = "Staff",
                    DisplayName = "12-Wk Overtime Cost",
                    Description =
                        "WHAT:\nDollar value of overtime (OT + special OT) hours logged in the past 12 weeks.\n\n" +
                        "WHY IT MATTERS:\nOvertime is more expensive per hour than regular time. Sustained overtime cost is both a margin drag and a leading indicator of burnout / under-resourcing.\n\n" +
                        "HOW IT IS CALCULATED:\nSUM(tkDetail.OvtAmt + SpecialOvtAmt) where TransDate >= today - 84 days.",
                    Formula = "SUM(OvtAmt + SpecialOvtAmt WHERE TransDate >= TODAY - 84)"
                },
                ["StaffUtil_CostPerBillableHr"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_CostPerBillableHr", Category = "Staff",
                    DisplayName = "Cost per Billable Hour",
                    Description =
                        "WHAT:\nFully-loaded labor cost per billable hour for this employee over the past 12 weeks.\n\n" +
                        "WHY IT MATTERS:\nThe minimum hourly billing rate this person needs to break even on their fully-loaded cost. If this number is close to or above the project rate they bill at, that work is unprofitable. The metric absorbs admin / non-billable / leave time as overhead spread across the billable hours, so it answers \"what does an actual billable hour from this person cost the firm?\"\n\n" +
                        "HOW IT IS CALCULATED:\n12-Wk Labor Cost / 12-Wk Billable Hours.\n" +
                        "Billable hours = tkDetail hours where LaborCode NOT IN (70 Admin, 80 Non-Billable) and WBS1 is a real project (not starting with letter / 9-letter / 99-).\n" +
                        "Returns 0 when billable hours = 0 (e.g., 12 weeks of pure admin/leave).",
                    Formula = "(SUM RegAmt + OvtAmt + SpecialOvtAmt) / (SUM billable RegHrs + OvtHrs + SpecialOvtHrs)"
                },
                ["StaffUtil_BillablePct"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_BillablePct", Category = "Staff",
                    DisplayName = "Billable %",
                    Description =
                        "WHAT:\nPercentage of 12-week hours posted to billable labor codes.\n\n" +
                        "WHY IT MATTERS:\nA high billable rate means most of this person's time is generating revenue. Low rates can indicate significant administrative overhead, leave, business development, or non-billable project phases.\n\n" +
                        "HOW IT IS CALCULATED:\nBillable hours = tkDetail hours where LaborCode NOT IN (70 Admin, 80 Non-Billable).\n" +
                        "Billable % = Billable Hours / Total 12-Wk Hours.\n\n" +
                        "Bands: ≥ 85% = Good (green)  |  65–84% = Fair (amber)  |  < 65% = High overhead (red).",
                    Formula = "SUM(hours where LaborCode NOT IN (70,80)) / SUM(RegHrs + OvtHrs)"
                },
                ["StaffUtil_Projects"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_Projects", Category = "Staff",
                    DisplayName = "Project Count (12 wk)",
                    Description =
                        "WHAT:\nNumber of distinct projects this person logged hours to over the past 12 weeks.\n\n" +
                        "WHY IT MATTERS:\nVery high project counts can indicate excessive context-switching overhead, coordination cost, and diluted focus. Very low counts (1–2) may indicate good focus or narrow specialization.\n\n" +
                        "HOW IT IS CALCULATED:\nCOUNT(DISTINCT WBS1) from tkDetail where TransDate >= today - 84 days.",
                    Formula = "COUNT(DISTINCT WBS1 WHERE TransDate >= TODAY - 84)"
                },
                ["StaffUtil_UtilizationPct"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_UtilizationPct", Category = "Staff",
                    DisplayName = "Utilization %",
                    Description =
                        "WHAT:\nHow fully a staff member's time is being used relative to the 37.5 hr/week full-time standard.\n\n" +
                        "WHY IT MATTERS:\nThe primary capacity gauge in the Staff Utilization window. Values above 100% indicate sustained overtime. Values below 60% may signal bench time, leave, or material non-billable work.\n\n" +
                        "HOW IT IS CALCULATED:\n12-Wk Avg (hrs/wk) / 37.5.\n\n" +
                        "Status bands:\n" +
                        "High ≥ 90%     — fully loaded or working overtime\n" +
                        "Normal 60–89% — healthy billable workload\n" +
                        "Low < 60%      — bandwidth may be available",
                    Formula = "(SUM(RegHrs + OvtHrs) / 12) / 37.5"
                },
                ["StaffUtil_Status"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_Status", Category = "Staff",
                    DisplayName = "Utilization Status",
                    Description =
                        "WHAT:\nA three-band label summarising an individual's utilization vs. the 37.5 hr/week target.\n\n" +
                        "WHY IT MATTERS:\nAllows quick scanning to identify over-capacity and under-capacity staff across the team.\n\n" +
                        "HOW IT IS CALCULATED:\nDerived from Utilization %.\n" +
                        "High ≥ 90%     — fully loaded; overtime risk\n" +
                        "Normal 60–89% — on-target workload\n" +
                        "Low < 60%      — available capacity"
                },
                ["StaffUtil_RegHrs"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_RegHrs", Category = "Staff",
                    DisplayName = "Regular Hours",
                    Description =
                        "WHAT:\nNon-overtime hours logged to a project or week in the 12-week window.\n\n" +
                        "WHY IT MATTERS:\nThe base workload signal; distinguishes sustained effort from overtime-driven effort.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of tkDetail.RegHrs for the employee within the specified time window.",
                    Formula = "SUM(tkDetail.RegHrs)"
                },
                ["StaffUtil_OvtHrs"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_OvtHrs", Category = "Staff",
                    DisplayName = "Overtime Hours",
                    Description =
                        "WHAT:\nOvertime hours logged to a specific project or week in the 12-week window.\n\n" +
                        "WHY IT MATTERS:\nHighlighted in amber when non-zero. Consistent project-level overtime may indicate under-resourcing or an unrealistic schedule for that engagement.\n\n" +
                        "HOW IT IS CALCULATED:\nSum of tkDetail.OvtHrs for the employee and project/period.",
                    Formula = "SUM(tkDetail.OvtHrs)"
                },
                ["StaffUtil_PctOfTotal"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_PctOfTotal", Category = "Staff",
                    DisplayName = "% of Total Hours",
                    Description =
                        "WHAT:\nThis project's hours as a share of the employee's total 12-week hours.\n\n" +
                        "WHY IT MATTERS:\nHighlights where a person's time is most concentrated. High percentages on a single project can indicate single-project dependency and schedule risk.\n\n" +
                        "HOW IT IS CALCULATED:\n(Project Total Hours) / (Employee's 12-Wk Total Hours).",
                    Formula = "ProjectTotalHrs / Employee12WkHrs"
                },
                ["StaffUtil_VsTarget"] = new FinancialMetricDefinition
                {
                    Key = "StaffUtil_VsTarget", Category = "Staff",
                    DisplayName = "vs 37.5 Target",
                    Description =
                        "WHAT:\nDifference (in hours) between a week's total logged hours and the 37.5 hr/week standard.\n\n" +
                        "WHY IT MATTERS:\nPositive values mean the person worked more than target that week (overtime pressure). Large negative values may indicate leave, holiday weeks, or a bench period.\n\n" +
                        "HOW IT IS CALCULATED:\nWeek Total Hours - 37.5.\n\n" +
                        "Colour bands:\n" +
                        "+5 or more (amber) — sustained overtime, over-target\n" +
                        "-5 or less (red)   — notably under target; possible leave or idle time\n" +
                        "Within ±5 (grey)   — on target",
                    Formula = "WeekTotalHrs - 37.5"
                },

                // ── Billing Manager Report ────────────────────────────────────────────
                ["BM_Label"] = new FinancialMetricDefinition
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
                },
                ["BM_Info"] = new FinancialMetricDefinition
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
                },
                ["BM_YoyDelta"] = new FinancialMetricDefinition
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
                },
                ["BM_12MoTotal"] = new FinancialMetricDefinition
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
                },
                ["BM_RemFee"] = new FinancialMetricDefinition
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
                },
                ["BM_PctFee"] = new FinancialMetricDefinition
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
                },
                ["BM_Trend"] = new FinancialMetricDefinition
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
                },
                ["BM_TrendArrow"] = new FinancialMetricDefinition
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
                },
                ["BM_LastMo"] = new FinancialMetricDefinition
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
                },
                ["BM_2MoAgo"] = new FinancialMetricDefinition
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
                },
                ["BM_Streak"] = new FinancialMetricDefinition
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
                },
                ["BM_AvgMo"] = new FinancialMetricDefinition
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
                },
                ["BM_12MoChart"] = new FinancialMetricDefinition
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
                },
                ["BM_YoyChart"] = new FinancialMetricDefinition
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
                },
                ["BM_60MoChart"] = new FinancialMetricDefinition
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
                },

                // ── Historical Project Analytics ──────────────────────────────
                ["Hist_Wbs1"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Wbs1", Category = "Historical",
                    DisplayName = "Project #",
                    Description = "Deltek WBS1 project number."
                },
                ["Hist_Name"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Name", Category = "Historical",
                    DisplayName = "Project Name",
                    Description = "Project name from Deltek PR table."
                },
                ["Hist_PM"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PM", Category = "Historical",
                    DisplayName = "Project Manager",
                    Description = "Project manager assigned in Deltek (PR.ProjMgr → EMMain name)."
                },
                ["Hist_Phase"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Phase", Category = "Historical",
                    DisplayName = "Phase",
                    Description = "Current project phase from ProjectCustomTabFields.CustProjectPhase (SD, DD, CD, CA)."
                },
                ["Hist_Status"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Status", Category = "Historical",
                    DisplayName = "Status",
                    Description = "Deltek PR.Status — typically 'A' (Active) or 'I'/'C' (Inactive/Closed)."
                },
                ["Hist_OpenDate"] = new FinancialMetricDefinition
                {
                    Key = "Hist_OpenDate", Category = "Historical",
                    DisplayName = "Opened",
                    Description = "Date the project was opened in Deltek (PR.OpenDate)."
                },
                ["Hist_CloseDate"] = new FinancialMetricDefinition
                {
                    Key = "Hist_CloseDate", Category = "Historical",
                    DisplayName = "Closed",
                    Description = "Date the project was closed in Deltek (PR.CloseDate). Blank if still active."
                },
                ["Hist_Fee"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Fee", Category = "Historical",
                    DisplayName = "Fee",
                    Description = "Total project fee from Deltek PR.Fee.",
                    Formula = "PR.Fee"
                },
                ["Hist_PctBilled"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PctBilled", Category = "Historical",
                    DisplayName = "% Billed",
                    Description = "Percentage of the fee that has been billed to date.",
                    Formula = "SUM(PRSummaryMain.BilledFee else Revenue) / PR.Fee"
                },
                ["Hist_EngHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_EngHrs", Category = "Historical",
                    DisplayName = "Eng Hrs",
                    Description = "Total engineering hours charged to the project. Includes checking hours (LaborCode 10 + 30).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode IN (10, 30)"
                },
                ["Hist_DraftHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_DraftHrs", Category = "Historical",
                    DisplayName = "Draft Hrs",
                    Description = "Total drafting hours charged to the project (tkDetail.LaborCode = 20).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 20"
                },
                ["Hist_TotalHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_TotalHrs", Category = "Historical",
                    DisplayName = "Total",
                    Description = "Sum of engineering + drafting hours.",
                    Formula = "Eng Hrs + Draft Hrs"
                },
                ["Hist_EngPct"] = new FinancialMetricDefinition
                {
                    Key = "Hist_EngPct", Category = "Historical",
                    DisplayName = "Eng %",
                    Description = "Engineering hours as a percentage of total (eng + draft). The peer-based budget uses actual splits from similar projects.",
                    Formula = "Eng Hrs / (Eng Hrs + Draft Hrs)"
                },
                ["Hist_DraftPct"] = new FinancialMetricDefinition
                {
                    Key = "Hist_DraftPct", Category = "Historical",
                    DisplayName = "Draft %",
                    Description = "Drafting hours as a percentage of total (eng + draft).",
                    Formula = "Draft Hrs / (Eng Hrs + Draft Hrs)"
                },
                ["Hist_FeePerHr"] = new FinancialMetricDefinition
                {
                    Key = "Hist_FeePerHr", Category = "Historical",
                    DisplayName = "Fee/Hr",
                    Description = "Effective billing rate — fee divided by total eng + draft hours.",
                    Formula = "Fee / (Eng Hrs + Draft Hrs)"
                },
                ["Hist_EstEngBgt"] = new FinancialMetricDefinition
                {
                    Key = "Hist_EstEngBgt", Category = "Historical",
                    DisplayName = "Est Eng Budget",
                    Description =
                        "Estimated engineering hours for this project, shown in purple.\n\n" +
                        "Primary: Peer-based — median eng hours from similar completed projects (fee ±50%, same construction type/phase, 50+ hrs, top 8 by fee proximity).\n" +
                        "Fallback: Formula — (Fee / Target) × (Combined / EngRate). Only used when fewer than 3 peers found.\n\n" +
                        "Compare to actual Eng Hrs to see how accurate the estimate is. The Eng Δ column shows the difference.",
                    Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / EngRate)"
                },
                ["Hist_EstDraftBgt"] = new FinancialMetricDefinition
                {
                    Key = "Hist_EstDraftBgt", Category = "Historical",
                    DisplayName = "Est Draft Budget",
                    Description =
                        "Estimated drafting hours for this project, shown in purple.\n\n" +
                        "Primary: Peer-based — median draft hours from similar completed projects (fee ±50%, same construction type/phase, 50+ hrs, top 8 by fee proximity).\n" +
                        "Fallback: Formula — (Fee / Target) × (Combined / DraftRate). Only used when fewer than 3 peers found.\n\n" +
                        "Compare to actual Draft Hrs to see how accurate the estimate is. The Draft Δ column shows the difference.",
                    Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / DraftRate)"
                },
                ["Hist_EngDelta"] = new FinancialMetricDefinition
                {
                    Key = "Hist_EngDelta", Category = "Historical",
                    DisplayName = "Eng Δ",
                    Description =
                        "Estimated eng budget minus actual eng hours.\n\n" +
                        "Positive = estimate was higher than reality (conservative).\n" +
                        "Negative = actual hours exceeded the estimate (under-predicted).",
                    Formula = "Est Eng Budget − Eng Hrs"
                },
                ["Hist_DraftDelta"] = new FinancialMetricDefinition
                {
                    Key = "Hist_DraftDelta", Category = "Historical",
                    DisplayName = "Draft Δ",
                    Description =
                        "Estimated draft budget minus actual draft hours.\n\n" +
                        "Positive = estimate was higher than reality (conservative).\n" +
                        "Negative = actual hours exceeded the estimate (under-predicted).",
                    Formula = "Est Draft Budget − Draft Hrs"
                },

                // ── Historical: PM Performance Summary ──
                ["Hist_PM_Pm"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PM_Pm", Category = "Historical",
                    DisplayName = "Project Manager",
                    Description = "Aggregated metrics across all visible projects for this PM. Filters (Status, Data, Search) are applied before grouping."
                },
                ["Hist_PM_ProjectCount"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PM_ProjectCount", Category = "Historical",
                    DisplayName = "Project Count",
                    Description = "Number of projects assigned to this PM (after filters)."
                },
                ["Hist_PM_TotalFee"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PM_TotalFee", Category = "Historical",
                    DisplayName = "Total Fee",
                    Description = "Sum of all project fees managed by this PM."
                },
                ["Hist_PM_FeePerHr"] = new FinancialMetricDefinition
                {
                    Key = "Hist_PM_FeePerHr", Category = "Historical",
                    DisplayName = "Fee/Hr (PM)",
                    Description = "Total fee ÷ total production hours (eng + draft) across all this PM's projects. Higher = more efficient use of production hours relative to fee.",
                    Formula = "SUM(Fee) / SUM(EngHrs + DraftHrs)"
                },

                // ── Historical: A/R Aging ──
                ["Hist_ArTotal"] = new FinancialMetricDefinition
                {
                    Key = "Hist_ArTotal", Category = "Historical",
                    DisplayName = "AR Outstanding",
                    Description = "Total outstanding accounts receivable for this project — sum of all unpaid invoice balances.",
                    Formula = "SUM(AR.InvBalanceSourceCurrency) WHERE balance <> 0"
                },
                ["Hist_ArCurrent"] = new FinancialMetricDefinition
                {
                    Key = "Hist_ArCurrent", Category = "Historical",
                    DisplayName = "AR Current (0-30 days)",
                    Description = "AR balance for invoices due within the last 30 days.",
                    Formula = "SUM(InvBalance) WHERE DATEDIFF(day, DueDate, today) <= 30"
                },
                ["Hist_Ar31To60"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Ar31To60", Category = "Historical",
                    DisplayName = "AR 31-60 days",
                    Description = "AR balance for invoices 31-60 days past due.",
                    Formula = "SUM(InvBalance) WHERE DATEDIFF 31-60"
                },
                ["Hist_Ar61To90"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Ar61To90", Category = "Historical",
                    DisplayName = "AR 61-90 days",
                    Description = "AR balance for invoices 61-90 days past due. Collection risk increases significantly at this tier.",
                    Formula = "SUM(InvBalance) WHERE DATEDIFF 61-90"
                },
                ["Hist_Ar90Plus"] = new FinancialMetricDefinition
                {
                    Key = "Hist_Ar90Plus", Category = "Historical",
                    DisplayName = "AR 90+ days",
                    Description = "AR balance for invoices over 90 days past due. High collection risk — may indicate disputes or billing quality issues.",
                    Formula = "SUM(InvBalance) WHERE DATEDIFF > 90"
                },

                // ── Historical: Subconsultant costs ──
                ["Hist_SubCost"] = new FinancialMetricDefinition
                {
                    Key = "Hist_SubCost", Category = "Historical",
                    DisplayName = "Subconsultant Cost",
                    Description = "Total accounts payable amounts posted to this project from Deltek apDetail.\n\nIncludes all AP line items — typically subconsultant invoices.",
                    Formula = "SUM(apDetail.Amount)"
                },
                ["Hist_SubPctOfFee"] = new FinancialMetricDefinition
                {
                    Key = "Hist_SubPctOfFee", Category = "Historical",
                    DisplayName = "Sub % of Fee",
                    Description = "Subconsultant cost as a percentage of total project fee.\n\nHigh sub % projects need different budget assumptions — less in-house eng/draft effort per dollar of fee.",
                    Formula = "SubCost / Fee"
                },

                // ── Historical: Full labor code breakdown ──
                // Hist_ChkHrs removed — Checking merged into Engineering
                ["Hist_InspHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_InspHrs", Category = "Historical",
                    DisplayName = "Inspection Hours",
                    Description = "Hours charged to inspection labor code (LaborCode = 40).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 40"
                },
                ["Hist_TotalInspections"] = new FinancialMetricDefinition
                {
                    Key = "Hist_TotalInspections", Category = "Historical",
                    DisplayName = "Total Inspections",
                    Description = "Total number of site inspection visits for this project.\n\nEach time entry with LaborCode 40 counts as one inspection visit. This is a COUNT of entries, not a sum of hours.",
                    Formula = "COUNT(*) WHERE LaborCode = 40"
                },
                ["Hist_LastMonthInspections"] = new FinancialMetricDefinition
                {
                    Key = "Hist_LastMonthInspections", Category = "Historical",
                    DisplayName = "Inspections Last Month",
                    Description = "Number of inspection visits in the previous calendar month.\n\nUseful for tracking current inspection activity on active projects. Resets on the 1st of each month.",
                    Formula = "COUNT(*) WHERE LaborCode = 40 AND TransDate in previous calendar month"
                },
                ["Hist_DocPrepHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_DocPrepHrs", Category = "Historical",
                    DisplayName = "Doc Prep Hours",
                    Description = "Hours charged to document preparation labor code (LaborCode = 50).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 50"
                },
                ["Hist_GenHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_GenHrs", Category = "Historical",
                    DisplayName = "General Hours",
                    Description = "Hours charged to general labor code (LaborCode = 60). Coordination, meetings, etc.",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 60"
                },
                ["Hist_AdminHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_AdminHrs", Category = "Historical",
                    DisplayName = "Admin Hours",
                    Description = "Hours charged to admin labor code (LaborCode = 70).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 70"
                },
                ["Hist_NonBillHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_NonBillHrs", Category = "Historical",
                    DisplayName = "Non-Billable Hours",
                    Description = "Hours charged to non-billable labor code (LaborCode = 80).",
                    Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 80"
                },
                ["Hist_TotalAllHrs"] = new FinancialMetricDefinition
                {
                    Key = "Hist_TotalAllHrs", Category = "Historical",
                    DisplayName = "All Hours",
                    Description = "Total hours across all 8 labor codes (Eng + Draft + Chk + Insp + DocPrep + Gen + Admin + NonBill).",
                    Formula = "SUM(RegHrs + OvtHrs) — all labor codes"
                },
                ["Hist_BillablePct"] = new FinancialMetricDefinition
                {
                    Key = "Hist_BillablePct", Category = "Historical",
                    DisplayName = "Billable %",
                    Description = "Percentage of total hours that are on real billable projects.\n\nNon-billable = hours logged to overhead project numbers (99XXX — General Overhead, Vacation, Sick Leave, CPD, Stat Holidays, Business Development, etc.) OR hours with Admin/Non-Billable labor codes (70, 80).\n\nTotal hours includes everything. Low billable % means more time on overhead/admin vs revenue-generating project work.",
                    Formula = "SUM(hrs on billable projects with LaborCode NOT IN (70,80)) / SUM(all hrs)"
                }
            });

        private static Dictionary<string, FinancialMetricDefinition> NormalizeDefinitions(
            Dictionary<string, FinancialMetricDefinition> source)
        {
            var normalized = new Dictionary<string, FinancialMetricDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in source)
            {
                var def = kv.Value ?? new FinancialMetricDefinition { Key = kv.Key, DisplayName = kv.Key };
                normalized[kv.Key] = new FinancialMetricDefinition
                {
                    Key = string.IsNullOrWhiteSpace(def.Key) ? kv.Key : def.Key,
                    Category = string.IsNullOrWhiteSpace(def.Category) ? "Financial" : def.Category,
                    DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? kv.Key : def.DisplayName,
                    Description = def.Description ?? string.Empty,
                    Formula = EnsureFormula(def.Description, def.Formula)
                };
            }
            return normalized;
        }

        private static string EnsureFormula(string? description, string? formula)
        {
            if (!string.IsNullOrWhiteSpace(formula))
                return formula.Trim();

            var how = ExtractHowCalculated(description);
            if (!string.IsNullOrWhiteSpace(how))
                return how;

            return "Calculation: see business definition.";
        }

        private static string ExtractHowCalculated(string? description)
        {
            var text = (description ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;

            const string marker = "HOW IT IS CALCULATED:";
            var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;

            var after = text.Substring(start + marker.Length).Trim();
            if (after.Length == 0) return string.Empty;

            var nextSection = after.IndexOf("\n\n", StringComparison.Ordinal);
            if (nextSection >= 0)
                after = after.Substring(0, nextSection).Trim();

            return after;
        }

        internal static string? TryGetTooltipText(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!Definitions.TryGetValue(key.Trim(), out var def) || def == null) return null;
            if (string.IsNullOrWhiteSpace(def.Description) && string.IsNullOrWhiteSpace(def.Formula)) return null;

            var desc = (def.Description ?? string.Empty).Trim();
            var formula = (def.Formula ?? string.Empty).Trim();
            if (desc.Length == 0 && formula.Length == 0) return null;
            if (formula.Length == 0) return desc.Length == 0 ? null : desc;
            if (desc.Length == 0) return $"Formula: {formula}";
            return $"{desc}\nFormula: {formula}";
        }
    }
}
