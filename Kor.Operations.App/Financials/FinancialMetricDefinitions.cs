#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.Financials
{
    public sealed class FinancialMetricDefinition
    {
        public string Key { get; init; } = "";
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
                        "Total contracted fee across the projects currently shown.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows the revenue base currently under management.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Adds each project's contracted fee in the current view.",
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
                        "Adds billed-to-date amounts for each project in view.",
                    Formula = ""
                },
                ["TotalUnbilled"] = new FinancialMetricDefinition
                {
                    Key = "TotalUnbilled",
                    DisplayName = "Total Unbilled",
                    Description =
                        "WHAT:\n" +
                        "Total remaining contracted fee not yet billed across the projects shown.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Highlights near-term billing runway and potential revenue risk.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "For each project, subtracts billed-to-date from contracted fee and sums the remainder.",
                    Formula = ""
                },
                ["PercentFeeUnbilled"] = new FinancialMetricDefinition
                {
                    Key = "PercentFeeUnbilled",
                    DisplayName = "% Fee Unbilled",
                    Description =
                        "WHAT:\n" +
                        "Share of contracted fee that has not yet been billed.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Helps leadership gauge billing progress versus the total fee base.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Compares total unbilled fee to total contracted fee for the current view.",
                    Formula = ""
                },
                ["HoursSpent"] = new FinancialMetricDefinition
                {
                    Key = "HoursSpent",
                    DisplayName = "Hours Spent",
                    Description =
                        "WHAT:\n" +
                        "Total engineering time charged to the projects shown.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Indicates delivery effort already consumed against budgets and fees.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums charged hours across the included disciplines for all projects in view.",
                    Formula = ""
                },
                ["HoursBudgeted"] = new FinancialMetricDefinition
                {
                    Key = "HoursBudgeted",
                    DisplayName = "Hours Budgeted",
                    Description =
                        "WHAT:\n" +
                        "Total planned engineering hours for the projects shown.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Sets the baseline for whether delivery is tracking to plan.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Adds the planned engineering and drafting budgets used by this dashboard.",
                    Formula = ""
                },
                ["HoursRemaining"] = new FinancialMetricDefinition
                {
                    Key = "HoursRemaining",
                    DisplayName = "Hours Remaining",
                    Description =
                        "WHAT:\n" +
                        "Remaining planned engineering hours before projects reach their budget.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Shows how much delivery capacity is left before overrun risk increases.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Subtracts hours spent from hours budgeted for the current view.",
                    Formula = ""
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
                    Formula = "PercentHoursSpent = SUM(EngHrs) / SUM(EngBudget)"
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
                        "Converts remaining hours into team-days using standard day and team assumptions.",
                    Formula = ""
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
                ["PercentBilled"] = new FinancialMetricDefinition
                {
                    Key = "PercentBilled",
                    DisplayName = "% Billed",
                    Description =
                        "WHAT:\n" +
                        "Portion of contracted fee billed to date.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Helps confirm billing progress keeps pace with delivery progress.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Compares billed-to-date to contracted fee.",
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
                ["Exec_ArOutstanding"] = new FinancialMetricDefinition
                {
                    Key = "Exec_ArOutstanding",
                    DisplayName = "AR Outstanding",
                    Description =
                        "WHAT:\n" +
                        "Open invoice balance for the watchlist portfolio.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Represents cash that has been invoiced but not yet collected.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Sums AR.InvBalanceSourceCurrency for invoices tied to watchlist WBS1.",
                    Formula = "SUM(AR.InvBalanceSourceCurrency)"
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
                        "Sums PRSummaryMain.Revenue for periods whose period-end date falls within the last 30/90 days.\n" +
                        "Also calculates Invoiced from PRSummaryMain.Billed and Unbilled Gap = Earned - Invoiced for each window.",
                    Formula = "Earned30/90 = SUM(PRSummaryMain.Revenue); Invoiced30/90 = SUM(PRSummaryMain.Billed); UnbilledGap30/90 = Earned30/90 - Invoiced30/90"
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
                    Formula = "UnbilledGap30/90 = SUM(PRSummaryMain.Revenue) - SUM(PRSummaryMain.Billed)"
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
                    DisplayName = "Revenue (Period)",
                    Description =
                        "WHAT:\n" +
                        "Total revenue for the most recent period in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Quick indicator of top-line performance in the latest month.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses the P&L table's income/revenue sections and rolls them into the Summary row.",
                    Formula = ""
                },
                ["GlPnL_ExpensesPeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_ExpensesPeriod",
                    DisplayName = "Expenses (Period)",
                    Description =
                        "WHAT:\n" +
                        "Total expenses for the most recent period in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Highlights cost pressure and overhead intensity in the latest month.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Uses the P&L table's expense sections and rolls them into the Summary row.",
                    Formula = ""
                },
                ["GlPnL_NetIncomePeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_NetIncomePeriod",
                    DisplayName = "Net Income (Period)",
                    Description =
                        "WHAT:\n" +
                        "Net income for the most recent period in the selected date range.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Single-number view of profitability for the latest month.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Net Income = Total Revenue + Total Expenses (per the selected GL table sign convention).",
                    Formula = ""
                },
                ["GlPnL_NetMarginPeriod"] = new FinancialMetricDefinition
                {
                    Key = "GlPnL_NetMarginPeriod",
                    DisplayName = "Net Margin (Period)",
                    Description =
                        "WHAT:\n" +
                        "Net income as a percentage of revenue for the most recent period.\n\n" +
                        "WHY IT MATTERS:\n" +
                        "Normalizes profitability so it is comparable across time and orgs.\n\n" +
                        "HOW IT IS CALCULATED:\n" +
                        "Net Margin = Net Income / Revenue (Revenue uses magnitude to avoid sign confusion).",
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
