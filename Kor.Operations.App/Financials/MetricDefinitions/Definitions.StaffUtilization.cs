#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddStaffUtilizationMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // ── Staff Utilization ─────────────────────────────────────────────────
        d["StaffUtil_Trend"] = new FinancialMetricDefinition
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
        };
        d["StaffUtil_ThisWeek"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_ThisWeek", Category = "Staff",
            DisplayName = "This Week Hours",
            Description =
                "WHAT:\nHours logged in the 7-day rolling window ending today.\n\n" +
                "WHY IT MATTERS:\nGives an immediate read on current workload. Use alongside the 4-week and 12-week averages for a stable picture — this figure can be skewed by holidays, leave, or late timesheet entry.\n\n" +
                "HOW IT IS CALCULATED:\nSums tkDetail.RegHrs + OvtHrs where TransDate >= today - 7 days for the employee.",
            Formula = "SUM(RegHrs + OvtHrs) WHERE TransDate >= TODAY - 7"
        };
        d["StaffUtil_FourWkAvg"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_FourWkAvg", Category = "Staff",
            DisplayName = "4-Week Average (hrs/wk)",
            Description =
                "WHAT:\nAverage hours per week over the past 28 days.\n\n" +
                "WHY IT MATTERS:\nShort-term workload signal that reacts faster than the 12-week average. Useful for identifying emerging capacity pressure before it shows up in the longer rolling window.\n\n" +
                "HOW IT IS CALCULATED:\nSums tkDetail hours for the past 28 days, then divides by 4.\n" +
                "4-Wk Avg = SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 28) / 4",
            Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 28) / 4"
        };
        d["StaffUtil_TwelveWkTotal"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_TwelveWkTotal", Category = "Staff",
            DisplayName = "12-Week Total Hours",
            Description =
                "WHAT:\nAll hours logged across every project in the past 84 days (12 calendar weeks).\n\n" +
                "WHY IT MATTERS:\nThe primary workload baseline for the Staff Utilization window. Covers a long enough window to smooth out holidays, leave, and single-week spikes.\n\n" +
                "HOW IT IS CALCULATED:\nSums tkDetail.RegHrs + OvtHrs where TransDate >= today - 84 days.",
            Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 84)"
        };
        d["StaffUtil_TwelveWkAvg"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_TwelveWkAvg", Category = "Staff",
            DisplayName = "12-Week Average (hrs/wk)",
            Description =
                "WHAT:\nAverage hours per week over the past 12 calendar weeks.\n\n" +
                "WHY IT MATTERS:\nThe denominator for Utilization %. Provides a stable, seasonality-smoothed view of sustained workload.\n\n" +
                "HOW IT IS CALCULATED:\n12-Wk Total / 12.\n" +
                "Values consistently above 37.5 indicate overtime culture; consistently below may signal bench time.",
            Formula = "SUM(RegHrs + OvtHrs WHERE TransDate >= TODAY - 84) / 12"
        };
        d["StaffUtil_Overtime"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_Overtime", Category = "Staff",
            DisplayName = "Overtime Hours (12 wk)",
            Description =
                "WHAT:\nTotal overtime hours (OvtHrs) logged across all projects in the 12-week window.\n\n" +
                "WHY IT MATTERS:\nConsistently high overtime for an individual can signal under-resourcing, unrealistic deadlines, or an unsustainable workload that creates burnout risk and schedule fragility.\n\n" +
                "HOW IT IS CALCULATED:\nSums tkDetail.OvtHrs where TransDate >= today - 84 days.",
            Formula = "SUM(OvtHrs WHERE TransDate >= TODAY - 84)"
        };
        d["StaffUtil_LaborCost"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_LaborCost", Category = "Staff",
            DisplayName = "12-Wk Labor Cost",
            Description =
                "WHAT:\nFully-burdened labor cost (regular + overtime + special overtime) over the past 12 weeks for this employee.\n\n" +
                "WHY IT MATTERS:\nThe expense side of the utilization picture. Pair with billable hours and project margin to see whether this person's hours are paying for themselves at current rates.\n\n" +
                "HOW IT IS CALCULATED:\nSUM(tkDetail.RegAmt + OvtAmt + SpecialOvtAmt) where TransDate >= today - 84 days, excluding rejected timesheet lines. USA-org rows are FX-converted to CAD-equivalent at Financials.Billed.UsdToCadRate (default 1.36); the project master row's pr.Org determines the FX bucket.",
            Formula = "SUM(RegAmt + OvtAmt + SpecialOvtAmt WHERE TransDate >= TODAY - 84 AND LineItemApprovalStatus <> 'R'), FX by master PR.Org"
        };
        d["StaffUtil_OvertimeCost"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_OvertimeCost", Category = "Staff",
            DisplayName = "12-Wk Overtime Cost",
            Description =
                "WHAT:\nDollar value of overtime (OT + special OT) hours logged in the past 12 weeks.\n\n" +
                "WHY IT MATTERS:\nOvertime is more expensive per hour than regular time. Sustained overtime cost is both a margin drag and a leading indicator of burnout / under-resourcing.\n\n" +
                "HOW IT IS CALCULATED:\nSUM(tkDetail.OvtAmt + SpecialOvtAmt) where TransDate >= today - 84 days.",
            Formula = "SUM(OvtAmt + SpecialOvtAmt WHERE TransDate >= TODAY - 84)"
        };
        d["StaffUtil_CostPerBillableHr"] = new FinancialMetricDefinition
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
        };
        d["StaffUtil_BillablePct"] = new FinancialMetricDefinition
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
        };
        d["StaffUtil_Projects"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_Projects", Category = "Staff",
            DisplayName = "Project Count (12 wk)",
            Description =
                "WHAT:\nNumber of distinct projects this person logged hours to over the past 12 weeks.\n\n" +
                "WHY IT MATTERS:\nVery high project counts can indicate excessive context-switching overhead, coordination cost, and diluted focus. Very low counts (1–2) may indicate good focus or narrow specialization.\n\n" +
                "HOW IT IS CALCULATED:\nCOUNT(DISTINCT WBS1) from tkDetail where TransDate >= today - 84 days.",
            Formula = "COUNT(DISTINCT WBS1 WHERE TransDate >= TODAY - 84)"
        };
        d["StaffUtil_UtilizationPct"] = new FinancialMetricDefinition
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
        };
        d["StaffUtil_Status"] = new FinancialMetricDefinition
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
        };
        d["StaffUtil_RegHrs"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_RegHrs", Category = "Staff",
            DisplayName = "Regular Hours",
            Description =
                "WHAT:\nNon-overtime hours logged to a project or week in the 12-week window.\n\n" +
                "WHY IT MATTERS:\nThe base workload signal; distinguishes sustained effort from overtime-driven effort.\n\n" +
                "HOW IT IS CALCULATED:\nSum of tkDetail.RegHrs for the employee within the specified time window.",
            Formula = "SUM(tkDetail.RegHrs)"
        };
        d["StaffUtil_OvtHrs"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_OvtHrs", Category = "Staff",
            DisplayName = "Overtime Hours",
            Description =
                "WHAT:\nOvertime hours logged to a specific project or week in the 12-week window.\n\n" +
                "WHY IT MATTERS:\nHighlighted in amber when non-zero. Consistent project-level overtime may indicate under-resourcing or an unrealistic schedule for that engagement.\n\n" +
                "HOW IT IS CALCULATED:\nSum of tkDetail.OvtHrs for the employee and project/period.",
            Formula = "SUM(tkDetail.OvtHrs)"
        };
        d["StaffUtil_PctOfTotal"] = new FinancialMetricDefinition
        {
            Key = "StaffUtil_PctOfTotal", Category = "Staff",
            DisplayName = "% of Total Hours",
            Description =
                "WHAT:\nThis project's hours as a share of the employee's total 12-week hours.\n\n" +
                "WHY IT MATTERS:\nHighlights where a person's time is most concentrated. High percentages on a single project can indicate single-project dependency and schedule risk.\n\n" +
                "HOW IT IS CALCULATED:\n(Project Total Hours) / (Employee's 12-Wk Total Hours).",
            Formula = "ProjectTotalHrs / Employee12WkHrs"
        };
        d["StaffUtil_VsTarget"] = new FinancialMetricDefinition
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
        };
    }
}
