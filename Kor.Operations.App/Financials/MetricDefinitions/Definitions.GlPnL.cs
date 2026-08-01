#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddGlPnLMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // GL P&L (read-only): definitions for the executive report window.
        d["GlPnL_DateRange"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_Table"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_Org"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_FlipSign"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_HideZeros"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_RevenuePeriod"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_ExpensesPeriod"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_NetIncomePeriod"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_NetMarginPeriod"] = new FinancialMetricDefinition
        {
            Key = "GlPnL_NetMarginPeriod",
            DisplayName = "Net Margin (Range)",
            Description =
                "WHAT:\n" +
                "Net income as a percentage of revenue over the selected date range.\n\n" +
                "WHY IT MATTERS:\n" +
                "Normalizes profitability so it is comparable across time and orgs.\n\n" +
                "HOW IT IS CALCULATED:\n" +
                "Net Margin = Net Income (range) / Revenue (range). The calculation uses signed revenue so the margin sign follows the displayed P&L convention.",
            Formula = "Net Income (range) / Revenue (range)"
        };
        d["GlPnL_NetIncomeTrend"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_RevVsExpTrend"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_LineItem"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_PeriodColumn"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_Current"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_Prior"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_MoM"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_MoMPct"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_YTD"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_TTM"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_PctOfRevenue"] = new FinancialMetricDefinition
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
        };
        d["GlPnL_Export"] = new FinancialMetricDefinition
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
        };
    }
}
