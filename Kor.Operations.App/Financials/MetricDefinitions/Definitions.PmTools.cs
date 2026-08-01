#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddPmToolsMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        d["PmTools_ActiveProjects"] = new FinancialMetricDefinition
        {
            Key = "PmTools_ActiveProjects", Category = "PM",
            DisplayName = "Active Projects",
            Description =
                "WHAT:\nTotal number of active projects currently tracked in the PM Tools dashboard.\n\n" +
                "WHY IT MATTERS:\nProvides a quick read on portfolio size and overall workload volume.\n\n" +
                "HOW IT IS CALCULATED:\nCounts all projects returned from the Deltek watchlist query."
        };
        d["PmTools_AtRiskCritical"] = new FinancialMetricDefinition
        {
            Key = "PmTools_AtRiskCritical", Category = "PM",
            DisplayName = "At Risk / Critical",
            Description =
                "WHAT:\nCount of projects whose delivery confidence is At Risk or Critical.\n\n" +
                "WHY IT MATTERS:\nHighlights projects that need immediate PM attention to avoid schedule or budget overruns.\n\n" +
                "HOW IT IS CALCULATED:\nCounts projects where hours spent as a share of budget outpaces fee billed as a share of contract, or where fee billed already exceeds contracted fee."
        };
        d["PmTools_EngHoursRemaining"] = new FinancialMetricDefinition
        {
            Key = "PmTools_EngHoursRemaining", Category = "PM",
            DisplayName = "Eng Hours Remaining (Portfolio)",
            Description =
                "WHAT:\nSum of remaining engineering hours across all active projects.\n\n" +
                "WHY IT MATTERS:\nShows total available engineering capacity before budgets are exhausted across the portfolio.\n\n" +
                "HOW IT IS CALCULATED:\nFor each project: Engineering Budget - Engineering Hours Spent. Negative values indicate over-budget projects. Summed across all projects."
        };
        d["PmTools_DraftHoursRemaining"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftHoursRemaining", Category = "PM",
            DisplayName = "Draft Hours Remaining (Portfolio)",
            Description =
                "WHAT:\nSum of remaining drafting hours across all active projects.\n\n" +
                "WHY IT MATTERS:\nShows total available drafting capacity before budgets are exhausted across the portfolio.\n\n" +
                "HOW IT IS CALCULATED:\nFor each project: Drafting Budget - Drafting Hours Spent. Negative values indicate over-budget projects. Summed across all projects."
        };
        d["PmTools_OverEngBudget"] = new FinancialMetricDefinition
        {
            Key = "PmTools_OverEngBudget", Category = "PM",
            DisplayName = "Over Eng Budget",
            Description =
                "WHAT:\nCount of projects where engineering hours spent exceed the engineering hour budget.\n\n" +
                "WHY IT MATTERS:\nFlags projects already past their engineering budget, requiring scope review or reallocation.\n\n" +
                "HOW IT IS CALCULATED:\nCounts projects where Remaining Engineering Hours < 0."
        };
        d["PmTools_FeeRemaining"] = new FinancialMetricDefinition
        {
            Key = "PmTools_FeeRemaining", Category = "PM",
            DisplayName = "Fee Remaining",
            Description =
                "WHAT:\nTotal unbilled fee across all watchlist projects.\n\n" +
                "WHY IT MATTERS:\nShows the portfolio backlog, meaning work already under contract but not yet billed.\n\n" +
                "HOW IT IS CALCULATED:\nSum of (Contract Fee - Fee Billed) for every active watchlist project.",
            Formula = "SUM(Fee - FeeBilled)"
        };
        d["PmTools_EngBudget"] = new FinancialMetricDefinition
        {
            Key = "PmTools_EngBudget", Category = "PM",
            DisplayName = "Engineering Budget (hrs)",
            Description =
                "Engineering hours budgeted for this project.\n\n" +
                "Purple italic with * = estimated — KOR does not budget hours in Deltek.\n\n" +
                "HOW ESTIMATES ARE CALCULATED:\n" +
                "Primary: Peer-based — finds similar completed projects (fee ±50%, same construction type/phase, 50+ hrs) and uses their median eng hours.\n" +
                "Fallback: Formula — (Fee / Target) × (Combined / EngRate). Only used when fewer than 3 peers found.\n\n" +
                "The peer-based approach is much more accurate because it accounts for construction type and project complexity, not just fee.",
            Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / EngRate)"
        };
        d["PmTools_EngHrs"] = new FinancialMetricDefinition
        {
            Key = "PmTools_EngHrs", Category = "PM",
            DisplayName = "Engineering Hours Spent",
            Description =
                "WHAT:\nEngineering hours charged to this project to date.\n\n" +
                "WHY IT MATTERS:\nTracks actual engineering effort consumed versus budget.\n\n" +
                "HOW IT IS CALCULATED:\nSum of all labor hours posted to engineering labor codes in Deltek for this project."
        };
        d["PmTools_EngPercent"] = new FinancialMetricDefinition
        {
            Key = "PmTools_EngPercent", Category = "PM",
            DisplayName = "Engineering % Used",
            Description =
                "WHAT:\nShare of the engineering hour budget consumed so far.\n\n" +
                "WHY IT MATTERS:\nWhen compared to % fee billed, reveals whether engineering effort is outpacing billing progress.\n\n" +
                "HOW IT IS CALCULATED:\nEngineering Hours Spent / Engineering Budget."
        };
        d["PmTools_EngRemaining"] = new FinancialMetricDefinition
        {
            Key = "PmTools_EngRemaining", Category = "PM",
            DisplayName = "Remaining Engineering Hours",
            Description =
                "WHAT:\nEngineering hours still available before the budget is exhausted.\n\n" +
                "WHY IT MATTERS:\nA negative value means the project is already over its engineering budget. Values below 15% of budget trigger an At Risk flag.\n\n" +
                "HOW IT IS CALCULATED:\nEngineering Budget - Engineering Hours Spent."
        };
        d["PmTools_DraftBudget"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftBudget", Category = "PM",
            DisplayName = "Drafting Budget (hrs)",
            Description =
                "Drafting hours budgeted for this project.\n\n" +
                "Purple italic with * = estimated — KOR does not budget hours in Deltek.\n\n" +
                "HOW ESTIMATES ARE CALCULATED:\n" +
                "Primary: Peer-based — finds similar completed projects (fee ±50%, same construction type/phase, 50+ hrs) and uses their median draft hours.\n" +
                "Fallback: Formula — (Fee / Target) × (Combined / DraftRate). Only used when fewer than 3 peers found.\n\n" +
                "The peer-based approach is much more accurate because it accounts for construction type and project complexity, not just fee.",
            Formula = "Primary: Peer median | Fallback: (Fee / Target) × (Combined / DraftRate)"
        };
        d["PmTools_DraftHrs"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftHrs", Category = "PM",
            DisplayName = "Drafting Hours Spent",
            Description =
                "WHAT:\nDrafting hours charged to this project to date.\n\n" +
                "WHY IT MATTERS:\nTracks actual drafting effort consumed versus budget.\n\n" +
                "HOW IT IS CALCULATED:\nSum of all labor hours posted to drafting labor codes in Deltek for this project."
        };
        d["PmTools_DraftPercent"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftPercent", Category = "PM",
            DisplayName = "Drafting % Used",
            Description =
                "WHAT:\nShare of the drafting hour budget consumed so far.\n\n" +
                "WHY IT MATTERS:\nHighlights drafting-heavy projects that may exhaust production capacity before completion.\n\n" +
                "HOW IT IS CALCULATED:\nDrafting Hours Spent / Drafting Budget."
        };
        d["PmTools_DraftRemaining"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftRemaining", Category = "PM",
            DisplayName = "Remaining Drafting Hours",
            Description =
                "WHAT:\nDrafting hours still available before the budget is exhausted.\n\n" +
                "WHY IT MATTERS:\nA negative value means the project is already over its drafting budget. Values below 15% of budget trigger an At Risk flag.\n\n" +
                "HOW IT IS CALCULATED:\nDrafting Budget - Drafting Hours Spent."
        };
        // PmTools_ChkHrs removed — Checking merged into Engineering
        d["PmTools_InspHrs"] = new FinancialMetricDefinition
        {
            Key = "PmTools_InspHrs", Category = "PM",
            DisplayName = "Inspection Hours",
            Description =
                "WHAT:\nHours charged to site inspection labor codes on this project.\n\n" +
                "WHY IT MATTERS:\nInspection hours indicate Construction Administration workload and can signal scope creep if unusually high.\n\n" +
                "HOW IT IS CALCULATED:\nSum of hours posted to inspection labor codes in Deltek."
        };
        d["PmTools_DeliveryRisk"] = new FinancialMetricDefinition
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
        };
        d["PmTools_CapacityRisk"] = new FinancialMetricDefinition
        {
            Key = "PmTools_CapacityRisk", Category = "PM",
            DisplayName = "Capacity Risk",
            Description =
                "WHAT:\nA ranked view of projects by how much of their engineering or drafting budget has been consumed.\n\n" +
                "WHY IT MATTERS:\nHelps resource managers spot which projects are drawing down team capacity fastest, enabling proactive reallocation before budgets are exhausted.\n\n" +
                "HOW IT IS CALCULATED:\nProjects are sorted by remaining hours (ascending). Risk status: Over budget = remaining < 0; At risk = remaining < 15% of budget; Healthy = otherwise."
        };

        d["PmTools_Fee"] = new FinancialMetricDefinition
        {
            Key = "PmTools_Fee", Category = "PM",
            DisplayName = "Fee",
            Description =
                "WHAT:\nThe total project fee — fixed contract amount plus any hourly/T&M extras revenue.\n\n" +
                "WHY IT MATTERS:\nThis is the number all budget, billing, and profitability metrics are measured against. It reflects the true project value, not just the original contract.\n\n" +
                "HOW IT IS CALCULATED:\nFixed fee from Deltek (PR.Fee) plus revenue from any hourly extras elements."
        };
        d["PmTools_PercentBilled"] = new FinancialMetricDefinition
        {
            Key = "PmTools_PercentBilled", Category = "PM",
            DisplayName = "% Fee Billed",
            Description =
                "WHAT:\nHow much of the total project fee has been invoiced to the client.\n\n" +
                "WHY IT MATTERS:\nCompare this to % Hours Spent. If you've used most of the hours but billed a small share of the fee, the project is burning faster than it's earning.\n\n" +
                "HOW IT IS CALCULATED:\nAmount billed to date divided by total fee (fixed + hourly).\n\n" +
                "NOTE:\nWhen the cell is italic, the displayed percentage includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. The bar color still reflects the unposted-inclusive % billed; once posting catches up the italic clears and the value matches the posted-only formula above.",
            Formula = "Fee Billed / Total Fee"
        };
        d["PmTools_Unbilled"] = new FinancialMetricDefinition
        {
            Key = "PmTools_Unbilled", Category = "PM",
            DisplayName = "Unbilled Fee",
            Description =
                "WHAT:\nThe dollar amount of the contracted fee that has not yet been invoiced.\n\n" +
                "WHY IT MATTERS:\nRepresents the remaining revenue opportunity on this project. A negative value means the project has been billed beyond its contracted fee.\n\n" +
                "HOW IT IS CALCULATED:\nFee − Fee Billed.",
            Formula = "Fee - Fee Billed"
        };
        d["PmTools_FeePerHours"] = new FinancialMetricDefinition
        {
            Key = "PmTools_FeePerHours", Category = "PM",
            DisplayName = "Fee / Hours",
            Description =
                "WHAT:\nThe contracted fee divided by total billable hours charged (engineering, drafting, checking, and inspection).\n\n" +
                "WHY IT MATTERS:\nShows the effective hourly rate implied by the project's fee and effort to date. A declining value over time signals scope creep or underestimated effort. Compare against the firm's target billing rate.\n\n" +
                "HOW IT IS CALCULATED:\nFee ÷ (Eng Hrs + Draft Hrs + Chk Hrs + Insp Hrs). Returns $0 if no billable hours have been logged.",
            Formula = "Fee / (Eng Hrs + Draft Hrs + Chk Hrs + Insp Hrs)"
        };
        d["PmTools_BilledPerHours"] = new FinancialMetricDefinition
        {
            Key = "PmTools_BilledPerHours", Category = "PM",
            DisplayName = "Billed / Hours",
            Description =
                "WHAT:\nThe total fee billed to date divided by ALL hours charged to the project, including non-billable time.\n\n" +
                "WHY IT MATTERS:\nReveals the true revenue per hour of effort — including overhead hours that Fee/Hrs ignores. A large gap between Fee/Hrs and Billed/Hrs exposes hidden overhead or non-billable time on the project.\n\n" +
                "HOW IT IS CALCULATED:\nFee Billed ÷ (Eng + Draft + Chk + Insp + DocPrep + Gen + Admin + NonBill hours). Returns $0 if no hours have been logged.\n\n" +
                "NOTE:\nWhen the cell is italic + amber, the numerator includes invoices issued in Deltek AR but not yet posted to PRSummaryMain. Once posting catches up the styling clears and the value matches the posted-only formula above.",
            Formula = "Fee Billed / (all 8 labor code hours)"
        };
        d["PmTools_DraftingMgr"] = new FinancialMetricDefinition
        {
            Key = "PmTools_DraftingMgr", Category = "PM",
            DisplayName = "Drafting Manager",
            Description =
                "WHAT:\nThe drafting manager assigned to this project in Deltek Vantagepoint.\n\n" +
                "WHY IT MATTERS:\nIdentifies who is responsible for managing production resources on this project.\n\n" +
                "HOW IT IS CALCULATED:\nFrom ProjectCustomTabFields.CustDraftingManager joined to EMMain for the display name."
        };
    }
}
