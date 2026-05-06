#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Financials;

internal static partial class FinancialMetricDefinitions
{
    private static void AddPortfolioMetrics(Dictionary<string, FinancialMetricDefinition> d)
    {
        // Section tooltips (UI-only): executive-grade definitions.
        d["PortfolioDeliveryHealth"] = new FinancialMetricDefinition
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
        };
        d["PortfolioRiskExposure"] = new FinancialMetricDefinition
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
        };
        d["PortfolioTrend"] = new FinancialMetricDefinition
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
        };
        d["RiskDrivers"] = new FinancialMetricDefinition
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
        };
        d["DeliveryTrend"] = new FinancialMetricDefinition
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
        };
        d["HealthIndicators"] = new FinancialMetricDefinition
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
        };
        d["BurnRisk"] = new FinancialMetricDefinition
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
        };
    }
}
