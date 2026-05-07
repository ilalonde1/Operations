#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddExecutiveMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // Executive Summary KPIs (Command Center)
        d["Exec_CashPosition"] = new FinancialMetricDefinition
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
        };
        d["Exec_Liquidity"] = new FinancialMetricDefinition
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
        };
        d["Exec_ArOutstanding"] = new FinancialMetricDefinition
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
                "breakdown table is also firmwide (every WBS1 with open AR), so Σ rows " +
                "reconciles to the headline number regardless of the active Scope toggle.",
            Formula = "SUM(AR.InvBalanceSourceCurrency) firmwide"
        };
        d["Exec_ArOver60"] = new FinancialMetricDefinition
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
        };
        d["Exec_WipUnbilled"] = new FinancialMetricDefinition
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
        };
        d["Exec_WipFirmwide"] = new FinancialMetricDefinition
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
        };
        d["Exec_Backlog"] = new FinancialMetricDefinition
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
        };
        d["Exec_BillingsToDate"] = new FinancialMetricDefinition
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
        };
        d["Exec_ProjectsOverBudget"] = new FinancialMetricDefinition
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
        };
        d["Exec_NetMultiplier"] = new FinancialMetricDefinition
        {
            Key = "Exec_NetMultiplier",
            DisplayName = "Net Multiplier",
            Description =
                "WHAT:\n" +
                "AEC industry-standard firm-health KPI: how many dollars of net service revenue the firm generates per dollar of direct labor cost.\n\n" +
                "WHY IT MATTERS:\n" +
                "Measures pricing power and labor efficiency together. ZweigGroup benchmarks: ≥ 3.0 healthy, ≥ 3.5 strong, < 2.5 margin-compressed.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Trailing-12-month firmwide Net Service Revenue (LedgerAR billed for revenue accounts 4001/4003/4210/4220/4240, FX-converted to CAD) divided by trailing-12-month Direct Labor Cost (tkDetail.RegAmt+OvtAmt+SpecialOvtAmt for billable LaborCodes 10/20/30/40/50/60 on direct projects, excluding overhead WBS1 prefixes 99%/9[A-Z]%/[A-Z]%).",
            Formula = "NetMultiplier = SUM(-LedgerAR.Amount where TransType='IN' and revenue accounts and trailing 12mo) / SUM(tkDetail.RegAmt+OvtAmt+SpecialOvtAmt where billable code on direct project and trailing 12mo)"
        };
        d["Exec_Dso"] = new FinancialMetricDefinition
        {
            Key = "Exec_Dso",
            DisplayName = "Days Sales Outstanding",
            Description =
                "WHAT:\n" +
                "Average number of days an invoice sits in AR before being collected.\n\n" +
                "WHY IT MATTERS:\n" +
                "Velocity dimension that AR Outstanding alone cannot show. A growing DSO means collections are slowing — even when AR balance looks healthy. Industry rule of thumb for AEC: < 60 days strong, 60-90 days typical, > 90 days collection problems.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "DSO = (Firmwide AR Outstanding (CAD-equiv) / Trailing-12-month firmwide billed) × 365.",
            Formula = "DSO = (ArFirmwideOutstandingCadEquiv / NetServiceRevenue12Mo) * 365"
        };
        d["Exec_Utilization30"] = new FinancialMetricDefinition
        {
            Key = "Exec_Utilization30",
            DisplayName = "Utilization (30d)",
            Description =
                "WHAT:\n" +
                "Billable charged hours divided by total charged hours over the last 30 days.\n\n" +
                "WHY IT MATTERS:\n" +
                "Indicates how efficiently delivered effort is converting into billable work.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "From tkDetail in the last 30 days: total hours = RegHrs+OvtHrs+SpecialOvtHrs. Billable hours are rows where LaborCode NOT IN (Admin=70, NonBillable=80), excluding overhead WBS1 prefixes [A-Z]%, 9[A-Z]%, and 99%, and excluding rejected timesheet lines (LineItemApprovalStatus = 'R').",
            Formula = "SUM(hours where LaborCode NOT IN (70,80), WBS1 is not overhead, and LineItemApprovalStatus <> 'R') / SUM(all hours)"
        };
        d["Exec_Revenue3090"] = new FinancialMetricDefinition
        {
            Key = "Exec_Revenue3090",
            DisplayName = "Revenue (Earned) latest 1 / 3 periods",
            Description =
                "WHAT:\n" +
                "Earned revenue in the latest closed PRSummaryMain period and the last 3 closed periods (portfolio), shown alongside invoiced amounts and the unbilled gap.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows delivery-driven revenue pace and whether invoicing is keeping up with earned work. Period-based windows stay meaningful even when Deltek's posted-period close runs months behind real time (the typical KOR cadence).\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums PRSummaryMain.BilledFee (with legacy Revenue fallback) for the latest closed period, and the sum of the last 3 closed periods. Same per-period sums fed by RevenueLoader.\n" +
                "Also calculates Invoiced from PRSummaryMain.Billed and Unbilled Gap = Earned - Invoiced for each window.",
            Formula = "Earned_latest = latest period's SUM(BilledFee else Revenue); Earned_last3 = sum of last 3 closed periods. Same for Invoiced (PRSummaryMain.Billed). UnbilledGap = Earned - Invoiced."
        };
        d["Exec_Billed3090"] = new FinancialMetricDefinition
        {
            Key = "Exec_Billed3090",
            DisplayName = "Billings (Invoiced) latest 1 / 3 periods",
            Description =
                "WHAT:\n" +
                "Invoice billings in the latest closed PRSummaryMain period and the last 3 closed periods (portfolio), with collection exposure context.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows cash-generation pace, billing cadence, and how much of billed value remains in AR. Period-based windows stay meaningful regardless of period-close lag.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums PRSummaryMain.Billed for the latest closed period, and the sum of the last 3 closed periods.\n" +
                "Collection exposure ratio = current scoped AR Outstanding / Last-3-periods billed.",
            Formula = "Billed_latest = latest period's SUM(PRSummaryMain.Billed); Billed_last3 = sum of last 3. CollectionExposure = ArScopedOutstanding / Billed_last3"
        };
        d["Exec_ArOutstandingRecent"] = new FinancialMetricDefinition
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
        };
        d["Exec_DeliveryRiskCriticalCount"] = new FinancialMetricDefinition
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
        };
        d["Exec_CollectionExposure"] = new FinancialMetricDefinition
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
        };
        d["Exec_UnbilledGap3090"] = new FinancialMetricDefinition
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
        };
    }
}
