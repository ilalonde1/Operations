#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddHistoricalMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // ── Historical Project Analytics ──────────────────────────────
        d["Hist_Wbs1"] = new FinancialMetricDefinition
        {
            Key = "Hist_Wbs1", Category = "Historical",
            DisplayName = "Project #",
            Description = "Deltek WBS1 project number."
        };
        d["Hist_Name"] = new FinancialMetricDefinition
        {
            Key = "Hist_Name", Category = "Historical",
            DisplayName = "Project Name",
            Description = "Project name from Deltek PR table."
        };
        d["Hist_PM"] = new FinancialMetricDefinition
        {
            Key = "Hist_PM", Category = "Historical",
            DisplayName = "Project Manager",
            Description = "Project manager assigned in Deltek (PR.ProjMgr → EMMain name)."
        };
        d["Hist_Phase"] = new FinancialMetricDefinition
        {
            Key = "Hist_Phase", Category = "Historical",
            DisplayName = "Phase",
            Description = "Current project phase from ProjectCustomTabFields.CustProjectPhase (SD, DD, CD, CA)."
        };
        d["Hist_Status"] = new FinancialMetricDefinition
        {
            Key = "Hist_Status", Category = "Historical",
            DisplayName = "Status",
            Description = "Deltek PR.Status — typically 'A' (Active) or 'I'/'C' (Inactive/Closed)."
        };
        d["Hist_OpenDate"] = new FinancialMetricDefinition
        {
            Key = "Hist_OpenDate", Category = "Historical",
            DisplayName = "Opened",
            Description = "Date the project was opened in Deltek (PR.OpenDate)."
        };
        d["Hist_CloseDate"] = new FinancialMetricDefinition
        {
            Key = "Hist_CloseDate", Category = "Historical",
            DisplayName = "Closed",
            Description = "Date the project was closed in Deltek (PR.CloseDate). Blank if still active."
        };
        d["Hist_Fee"] = new FinancialMetricDefinition
        {
            Key = "Hist_Fee", Category = "Historical",
            DisplayName = "Fee",
            Description = "Total project fee from Deltek PR.Fee.",
            Formula = "PR.Fee"
        };
        d["Hist_PctBilled"] = new FinancialMetricDefinition
        {
            Key = "Hist_PctBilled", Category = "Historical",
            DisplayName = "% Billed",
            Description = "Percentage of the fee that has been billed to date.",
            Formula = "SUM(PRSummaryMain.BilledFee else Revenue) / PR.Fee"
        };
        d["Hist_EngHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_EngHrs", Category = "Historical",
            DisplayName = "Eng Hrs",
            Description = "Total engineering hours charged to the project. Includes checking hours (LaborCode 10 + 30).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode IN (10, 30)"
        };
        d["Hist_DraftHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_DraftHrs", Category = "Historical",
            DisplayName = "Draft Hrs",
            Description = "Total drafting hours charged to the project (tkDetail.LaborCode = 20).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 20"
        };
        d["Hist_TotalHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_TotalHrs", Category = "Historical",
            DisplayName = "Total",
            Description = "Sum of engineering + drafting hours.",
            Formula = "Eng Hrs + Draft Hrs"
        };
        d["Hist_EngPct"] = new FinancialMetricDefinition
        {
            Key = "Hist_EngPct", Category = "Historical",
            DisplayName = "Eng %",
            Description = "Engineering hours as a percentage of total (eng + draft). The peer-based budget uses actual splits from similar projects.",
            Formula = "Eng Hrs / (Eng Hrs + Draft Hrs)"
        };
        d["Hist_DraftPct"] = new FinancialMetricDefinition
        {
            Key = "Hist_DraftPct", Category = "Historical",
            DisplayName = "Draft %",
            Description = "Drafting hours as a percentage of total (eng + draft).",
            Formula = "Draft Hrs / (Eng Hrs + Draft Hrs)"
        };
        d["Hist_FeePerHr"] = new FinancialMetricDefinition
        {
            Key = "Hist_FeePerHr", Category = "Historical",
            DisplayName = "Fee/Hr",
            Description = "Effective billing rate — fee divided by total eng + draft hours.",
            Formula = "Fee / (Eng Hrs + Draft Hrs)"
        };
        d["Hist_EstEngBgt"] = new FinancialMetricDefinition
        {
            Key = "Hist_EstEngBgt", Category = "Historical",
            DisplayName = "Est Eng Budget",
            Description =
                "Estimated engineering hours for this project, shown in purple.\n\n" +
                "Primary: Peer-based — median eng hours from similar completed projects (fee ±50%, same construction type/phase, 50+ hrs, top 8 by fee proximity).\n" +
                "Fallback: Formula — (Fee / Target) × (Combined / EngRate). Only used when fewer than 3 peers found.\n\n" +
                "Compare to actual Eng Hrs to see how accurate the estimate is. The Eng Δ column shows the difference.",
            Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / EngRate)"
        };
        d["Hist_EstDraftBgt"] = new FinancialMetricDefinition
        {
            Key = "Hist_EstDraftBgt", Category = "Historical",
            DisplayName = "Est Draft Budget",
            Description =
                "Estimated drafting hours for this project, shown in purple.\n\n" +
                "Primary: Peer-based — median draft hours from similar completed projects (fee ±50%, same construction type/phase, 50+ hrs, top 8 by fee proximity).\n" +
                "Fallback: Formula — (Fee / Target) × (Combined / DraftRate). Only used when fewer than 3 peers found.\n\n" +
                "Compare to actual Draft Hrs to see how accurate the estimate is. The Draft Δ column shows the difference.",
            Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / DraftRate)"
        };
        d["Hist_EngDelta"] = new FinancialMetricDefinition
        {
            Key = "Hist_EngDelta", Category = "Historical",
            DisplayName = "Eng Δ",
            Description =
                "Estimated eng budget minus actual eng hours.\n\n" +
                "Positive = estimate was higher than reality (conservative).\n" +
                "Negative = actual hours exceeded the estimate (under-predicted).",
            Formula = "Est Eng Budget − Eng Hrs"
        };
        d["Hist_DraftDelta"] = new FinancialMetricDefinition
        {
            Key = "Hist_DraftDelta", Category = "Historical",
            DisplayName = "Draft Δ",
            Description =
                "Estimated draft budget minus actual draft hours.\n\n" +
                "Positive = estimate was higher than reality (conservative).\n" +
                "Negative = actual hours exceeded the estimate (under-predicted).",
            Formula = "Est Draft Budget − Draft Hrs"
        };

        // ── Historical: PM Performance Summary ──
        d["Hist_PM_Pm"] = new FinancialMetricDefinition
        {
            Key = "Hist_PM_Pm", Category = "Historical",
            DisplayName = "Project Manager",
            Description = "Aggregated metrics across all visible projects for this PM. Filters (Status, Data, Search) are applied before grouping."
        };
        d["Hist_PM_ProjectCount"] = new FinancialMetricDefinition
        {
            Key = "Hist_PM_ProjectCount", Category = "Historical",
            DisplayName = "Project Count",
            Description = "Number of projects assigned to this PM (after filters)."
        };
        d["Hist_PM_TotalFee"] = new FinancialMetricDefinition
        {
            Key = "Hist_PM_TotalFee", Category = "Historical",
            DisplayName = "Total Fee",
            Description = "Sum of all project fees managed by this PM."
        };
        d["Hist_PM_FeePerHr"] = new FinancialMetricDefinition
        {
            Key = "Hist_PM_FeePerHr", Category = "Historical",
            DisplayName = "Fee/Hr (PM)",
            Description = "Total fee ÷ total production hours (eng + draft) across all this PM's projects. Higher = more efficient use of production hours relative to fee.",
            Formula = "SUM(Fee) / SUM(EngHrs + DraftHrs)"
        };

        // ── Historical: A/R Aging ──
        d["Hist_ArTotal"] = new FinancialMetricDefinition
        {
            Key = "Hist_ArTotal", Category = "Historical",
            DisplayName = "AR Outstanding",
            Description = "Total outstanding accounts receivable for this project — sum of all unpaid invoice balances where absolute balance is greater than $0.004.",
            Formula = "SUM(AR.InvBalanceSourceCurrency) WHERE ABS(balance) > 0.004"
        };
        d["Hist_ArCurrent"] = new FinancialMetricDefinition
        {
            Key = "Hist_ArCurrent", Category = "Historical",
            DisplayName = "AR Current (0-30 days)",
            Description = "AR balance for invoices due within the last 30 days.",
            Formula = "SUM(InvBalance) WHERE DATEDIFF(day, DueDate, today) <= 30"
        };
        d["Hist_Ar31To60"] = new FinancialMetricDefinition
        {
            Key = "Hist_Ar31To60", Category = "Historical",
            DisplayName = "AR 31-60 days",
            Description = "AR balance for invoices 31-60 days past due.",
            Formula = "SUM(InvBalance) WHERE DATEDIFF 31-60"
        };
        d["Hist_Ar61To90"] = new FinancialMetricDefinition
        {
            Key = "Hist_Ar61To90", Category = "Historical",
            DisplayName = "AR 61-90 days",
            Description = "AR balance for invoices 61-90 days past due. Collection risk increases significantly at this tier.",
            Formula = "SUM(InvBalance) WHERE DATEDIFF 61-90"
        };
        d["Hist_Ar90Plus"] = new FinancialMetricDefinition
        {
            Key = "Hist_Ar90Plus", Category = "Historical",
            DisplayName = "AR 90+ days",
            Description = "AR balance for invoices over 90 days past due. High collection risk — may indicate disputes or billing quality issues.",
            Formula = "SUM(InvBalance) WHERE DATEDIFF > 90"
        };

        // ── Historical: Subconsultant costs ──
        d["Hist_SubCost"] = new FinancialMetricDefinition
        {
            Key = "Hist_SubCost", Category = "Historical",
            DisplayName = "Subconsultant Cost",
            Description = "Total accounts payable amounts posted to this project from Deltek apDetail.\n\nIncludes all AP line items — typically subconsultant invoices.",
            Formula = "SUM(apDetail.Amount)"
        };
        d["Hist_SubPctOfFee"] = new FinancialMetricDefinition
        {
            Key = "Hist_SubPctOfFee", Category = "Historical",
            DisplayName = "Sub % of Fee",
            Description = "Subconsultant cost as a percentage of total project fee.\n\nHigh sub % projects need different budget assumptions — less in-house eng/draft effort per dollar of fee.",
            Formula = "SubCost / Fee"
        };

        // ── Historical: Full labor code breakdown ──
        // Hist_ChkHrs removed — Checking merged into Engineering
        d["Hist_InspHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_InspHrs", Category = "Historical",
            DisplayName = "Inspection Hours",
            Description = "Hours charged to inspection labor code (LaborCode = 40).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 40"
        };
        d["Hist_TotalInspections"] = new FinancialMetricDefinition
        {
            Key = "Hist_TotalInspections", Category = "Historical",
            DisplayName = "Total Inspections",
            Description = "Total number of site inspection visits for this project.\n\nEach time entry with LaborCode 40 counts as one inspection visit. This is a COUNT of entries, not a sum of hours.",
            Formula = "COUNT(*) WHERE LaborCode = 40"
        };
        d["Hist_LastMonthInspections"] = new FinancialMetricDefinition
        {
            Key = "Hist_LastMonthInspections", Category = "Historical",
            DisplayName = "Inspections Last Month",
            Description = "Number of inspection visits in the previous calendar month.\n\nUseful for tracking current inspection activity on active projects. Resets on the 1st of each month.",
            Formula = "COUNT(*) WHERE LaborCode = 40 AND TransDate in previous calendar month"
        };
        d["Hist_DocPrepHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_DocPrepHrs", Category = "Historical",
            DisplayName = "Doc Prep Hours",
            Description = "Hours charged to document preparation labor code (LaborCode = 50).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 50"
        };
        d["Hist_GenHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_GenHrs", Category = "Historical",
            DisplayName = "General Hours",
            Description = "Hours charged to general labor code (LaborCode = 60). Coordination, meetings, etc.",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 60"
        };
        d["Hist_AdminHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_AdminHrs", Category = "Historical",
            DisplayName = "Admin Hours",
            Description = "Hours charged to admin labor code (LaborCode = 70).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 70"
        };
        d["Hist_NonBillHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_NonBillHrs", Category = "Historical",
            DisplayName = "Non-Billable Hours",
            Description = "Hours charged to non-billable labor code (LaborCode = 80).",
            Formula = "SUM(RegHrs + OvtHrs) WHERE LaborCode = 80"
        };
        d["Hist_TotalAllHrs"] = new FinancialMetricDefinition
        {
            Key = "Hist_TotalAllHrs", Category = "Historical",
            DisplayName = "All Hours",
            Description = "Total hours across all 8 labor codes (Eng + Draft + Chk + Insp + DocPrep + Gen + Admin + NonBill).",
            Formula = "SUM(RegHrs + OvtHrs) — all labor codes"
        };
        d["Hist_BillablePct"] = new FinancialMetricDefinition
        {
            Key = "Hist_BillablePct", Category = "Historical",
            DisplayName = "Billable %",
            Description = "Percentage of total hours that are on real billable projects.\n\nNon-billable = hours logged to overhead project numbers (99XXX — General Overhead, Vacation, Sick Leave, CPD, Stat Holidays, Business Development, etc.) OR hours with Admin/Non-Billable labor codes (70, 80).\n\nTotal hours includes everything. Low billable % means more time on overhead/admin vs revenue-generating project work.",
            Formula = "SUM(hrs on billable projects with LaborCode NOT IN (70,80)) / SUM(all hrs)"
        };
    }
}
