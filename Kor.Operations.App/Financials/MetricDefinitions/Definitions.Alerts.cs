#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddAlertMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        d["Alert_ArOver60"] = new FinancialMetricDefinition
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
        };
        d["Alert_ProjectsOverBudget"] = new FinancialMetricDefinition
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
        };
        d["Alert_BacklogDeclining"] = new FinancialMetricDefinition
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
        };
        d["Alert_BillingLaggingBurn"] = new FinancialMetricDefinition
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
        };
    }
}
