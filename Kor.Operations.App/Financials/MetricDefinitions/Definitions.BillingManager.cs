#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddBillingManagerMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        d["BM_Label"] = new FinancialMetricDefinition
        {
            Key = "BM_Label",
            DisplayName = "Partner / Project",
            Description =
                "WHAT:\n" +
                "Rows are grouped by partner. Partner rows can be expanded to show the projects that contributed invoiced revenue in the selected period.\n\n" +
                "WHY IT MATTERS:\n" +
                "Matches Daler Singh's Financial Performance deck by showing which partners and projects drove billings.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "LedgerAR invoiced revenue is grouped by PR.Principal, with project rows grouped by LedgerAR.WBS1 and PR.Name.",
            Formula = "GROUP BY PR.Principal, LedgerAR.WBS1, PR.Name"
        };
        d["BM_Info"] = new FinancialMetricDefinition
        {
            Key = "BM_Info",
            DisplayName = "Info",
            Description =
                "WHAT:\n" +
                "Partner rows show project count and active-month coverage for the selected year. Project rows are the expandable detail under each partner.\n\n" +
                "WHY IT MATTERS:\n" +
                "Separates partner-level production from the project-level invoices that make up the total.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Project count is the count of WBS1 groups under the partner. Active months count selected-year months with non-zero LedgerAR invoiced revenue.",
            Formula = "COUNT(DISTINCT WBS1); COUNT(months where billed <> 0)"
        };
        d["BM_YoyDelta"] = new FinancialMetricDefinition
        {
            Key = "BM_YoyDelta",
            DisplayName = "Year-over-Year Delta (YoY)",
            Description =
                "WHAT:\n" +
                "Change in billings between the selected year and the prior year.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows whether each partner's invoiced revenue is up or down against the prior year.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "YoY = selected-year LedgerAR invoiced revenue minus prior-year LedgerAR invoiced revenue, using the active currency view.",
            Formula = "YoY = SUM(CY LedgerAR invoiced revenue) - SUM(LY LedgerAR invoiced revenue)"
        };
        d["BM_12MoTotal"] = new FinancialMetricDefinition
        {
            Key = "BM_12MoTotal",
            DisplayName = "Selected-Year Total Billings",
            Description =
                "WHAT:\n" +
                "Total invoiced revenue for the selected calendar year.\n\n" +
                "WHY IT MATTERS:\n" +
                "This is the primary partner-production number from Daler's deck.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums LedgerAR.Amount with sign flipped for TransType='IN' and revenue account prefixes 4001, 4003, 4210, 4220, and 4240, grouped by PR.Principal. The combined grid converts USA rows using the selected year's USD/CAD rate.",
            Formula = "SUM(-LedgerAR.Amount) WHERE TransType='IN' AND LEFT(Account,4) IN (4001,4003,4210,4220,4240)"
        };
        d["BM_Trend"] = new FinancialMetricDefinition
        {
            Key = "BM_Trend",
            DisplayName = "Billing Trend (Sparkline)",
            Description =
                "WHAT:\n" +
                "A mini line chart of monthly invoiced revenue across the selected year.\n\n" +
                "WHY IT MATTERS:\n" +
                "Reveals billing cadence and late-year/monthly concentration at a glance.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Plots monthly LedgerAR invoiced revenue for the active currency view. Line color follows the trend direction.",
            Formula = "Monthly SUM(-LedgerAR.Amount) for selected year"
        };
        d["BM_TrendArrow"] = new FinancialMetricDefinition
        {
            Key = "BM_TrendArrow",
            DisplayName = "Trend Direction",
            Description =
                "WHAT:\n" +
                "Compact trend direction based on recent selected-year billings.\n\n" +
                "WHY IT MATTERS:\n" +
                "Provides a quick signal for whether invoiced revenue is rising, flat, or falling.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Compares the average of the latest 3 active months to the prior 3 months in the selected year.",
            Formula = "AVG(last 3 active months) compared with AVG(prior 3 months)"
        };
        d["BM_LastMo"] = new FinancialMetricDefinition
        {
            Key = "BM_LastMo",
            DisplayName = "Latest Active Month Billings",
            Description =
                "WHAT:\n" +
                "Invoiced revenue in the latest selected-year month that has billing activity.\n\n" +
                "WHY IT MATTERS:\n" +
                "Avoids showing future/current empty periods as the latest signal.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums LedgerAR invoiced revenue for the latest selected-year period with non-zero billings.",
            Formula = "SUM(-LedgerAR.Amount) WHERE Period = latest active selected-year period"
        };
        d["BM_2MoAgo"] = new FinancialMetricDefinition
        {
            Key = "BM_2MoAgo",
            DisplayName = "Prior Active Month Billings",
            Description =
                "WHAT:\n" +
                "Invoiced revenue in the selected-year month immediately before the latest active month.\n\n" +
                "WHY IT MATTERS:\n" +
                "Gives a direct month-over-month comparison against the latest billing month.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Sums LedgerAR invoiced revenue for the period immediately before the latest active selected-year period.",
            Formula = "SUM(-LedgerAR.Amount) WHERE Period = prior active selected-year period"
        };
        d["BM_Streak"] = new FinancialMetricDefinition
        {
            Key = "BM_Streak",
            DisplayName = "Billing Streak",
            Description =
                "WHAT:\n" +
                "Consecutive selected-year months with non-zero invoiced revenue, counting backward from the latest active month.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows sustained billing cadence by partner or project.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Counts consecutive selected-year periods where LedgerAR invoiced revenue is non-zero.",
            Formula = "COUNT consecutive periods FROM latest active period WHERE billed <> 0"
        };
        d["BM_12MoChart"] = new FinancialMetricDefinition
        {
            Key = "BM_12MoChart",
            DisplayName = "Billings by Partner Chart",
            Description =
                "WHAT:\n" +
                "Horizontal bar chart comparing selected-year invoiced revenue by partner.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows relative partner contribution to firmwide billings.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Bar width = partner selected-year billings divided by the highest partner total.",
            Formula = "Bar width = partner total / MAX(partner total)"
        };
        d["BM_YoyChart"] = new FinancialMetricDefinition
        {
            Key = "BM_YoyChart",
            DisplayName = "Year-over-Year Growth Chart",
            Description =
                "WHAT:\n" +
                "Diverging bar chart showing each partner's selected-year change versus the prior year.\n\n" +
                "WHY IT MATTERS:\n" +
                "Makes partner-level growth and decline visible without sorting the grid.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Bar width = absolute YoY delta divided by the largest absolute partner delta.",
            Formula = "Bar width = |YoY| / MAX(|YoY|)"
        };
        d["BM_60MoChart"] = new FinancialMetricDefinition
        {
            Key = "BM_60MoChart",
            DisplayName = "60-Month Billing History by Partner",
            Description =
                "WHAT:\n" +
                "Stacked monthly billings for the five-year window ending in the selected year.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows seasonality, long-term trend, and partner mix over time.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Each segment is LedgerAR invoiced revenue grouped by PR.Principal for one accounting period, shown in CAD-equivalent for the combined view.",
            Formula = "Monthly SUM(-LedgerAR.Amount) by PR.Principal"
        };
        d["BM_CombinedGrid"] = new FinancialMetricDefinition
        {
            Key = "BM_CombinedGrid",
            DisplayName = "CAD+USD Combined Grid",
            Description =
                "WHAT:\n" +
                "Partner billings with CAD source rows plus USA source rows converted to CAD-equivalent.\n\n" +
                "WHY IT MATTERS:\n" +
                "Matches the deck's consolidated firmwide view while preserving source-currency auditability in the separate tabs.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "CAD rows are unconverted. USA rows are multiplied by the selected year's USD/CAD rate from Financials.Billed.UsdToCadRateByYear, falling back to Financials.Billed.UsdToCadRate when absent.",
            Formula = "CAD + (USA * year USD/CAD rate)"
        };
        d["BM_CadGrid"] = new FinancialMetricDefinition
        {
            Key = "BM_CadGrid",
            DisplayName = "CAD Only Grid",
            Description =
                "WHAT:\n" +
                "Partner billings for rows bucketed as CAD.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows the Canadian operating entity without FX conversion.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Includes rows where PR.Org is not USA and sums LedgerAR invoiced revenue in source currency.",
            Formula = "SUM(-LedgerAR.Amount) WHERE OrgBucket = CAD"
        };
        d["BM_UsdGrid"] = new FinancialMetricDefinition
        {
            Key = "BM_UsdGrid",
            DisplayName = "USD Only Grid",
            Description =
                "WHAT:\n" +
                "Partner billings for rows bucketed as USA.\n\n" +
                "WHY IT MATTERS:\n" +
                "Shows the USA source-currency billings before CAD conversion.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Includes rows where PR.Org is USA and sums LedgerAR invoiced revenue in USD source currency.",
            Formula = "SUM(-LedgerAR.Amount) WHERE OrgBucket = USA"
        };
        d["BM_YearSelector"] = new FinancialMetricDefinition
        {
            Key = "BM_YearSelector",
            DisplayName = "Year Selector",
            Description =
                "WHAT:\n" +
                "Selects the reporting year for the Partner Financials grids and YoY page.\n\n" +
                "WHY IT MATTERS:\n" +
                "Daler's deck is year-based, not trailing-12 based.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "The selected year loads LedgerAR periods year*100+1 through year*100+12. The YoY page compares those months to the same months in the prior year.",
            Formula = "Period BETWEEN YYYY01 AND YYYY12"
        };
        d["BM_FxProvisional"] = new FinancialMetricDefinition
        {
            Key = "BM_FxProvisional",
            DisplayName = "FX Provisional Badge",
            Description =
                "WHAT:\n" +
                "Warning that the selected year's USD/CAD rate is provisional or a fallback.\n\n" +
                "WHY IT MATTERS:\n" +
                "A provisional CAD-equivalent total should not be read as reconciled to Daler's final deck.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Shown when OrgFx.ResolveUsdToCadRate returns IsProvisional=true for the selected year.",
            Formula = "IsProvisional == true"
        };
    }
}
