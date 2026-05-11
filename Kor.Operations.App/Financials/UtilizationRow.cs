#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Financials
{
    public sealed class UtilizationRow
    {
        public FinancialsProjectRow Project { get; private set; } = new();
        public string Wbs1 { get; private set; } = "";
        public string ProjectName { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public double EngBudget { get; private set; }
        public double EngHours { get; private set; }
        public double RemainingEngHours { get; private set; }
        public double PercentEngUsed { get; private set; }
        public double Fee { get; private set; }
        public double PercentBilled { get; private set; }
        public double PercentBilledWithUnposted { get; private set; }
        public bool   HasUnpostedBilling { get; private set; }
        public string RiskStatus { get; private set; } = "Healthy";
        public string RiskColorName { get; private set; } = "Green";
        public string DeliveryConfidence { get; private set; } = "High Confidence";
        public string DeliveryConfidenceColorName { get; private set; } = "Green";
        public string DeliveryConfidenceSummary { get; private set; } = "";
        public string DeliveryConfidenceTooltip { get; private set; } = "";
        public DeliveryConfidenceLevel ConfidenceLevel { get; private set; } = DeliveryConfidenceLevel.HighConfidence;
        public string ConfidenceDisplay { get; private set; } = "High Confidence";
        public string ConstructionType { get; private set; } = "";
        public string ProjectCategory { get; private set; } = "";
        public string DraftingType { get; private set; } = "";

        public static UtilizationRow FromProject(FinancialsProjectRow p)
        {
            var budget = p?.EngBudget ?? 0.0;
            var hrs = p?.EngHrs ?? 0.0;
            var remaining = budget - hrs;

            var atRiskThreshold = budget > 0 ? budget * 0.15 : 0.0;
            var status = remaining < 0 ? "Over budget" : (budget > 0 && remaining < atRiskThreshold ? "At risk" : "Healthy");
            var color = status == "Over budget" ? "Red" : (status == "At risk" ? "Amber" : "Green");

            var dc = p?.DeliveryResult ?? DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Watch :
                DeliveryConfidenceLevel.HighConfidence;

            return new UtilizationRow
            {
                Project = p ?? new FinancialsProjectRow(),
                Wbs1 = (p?.Wbs1 ?? "").Trim(),
                ProjectName = (p?.Name ?? "").Trim(),
                Pm = (p?.Pm ?? "").Trim(),
                Phase = (p?.Phase ?? "").Trim(),
                EngBudget = budget,
                EngHours = hrs,
                RemainingEngHours = remaining,
                PercentEngUsed = System.Math.Abs(budget) > AnalyticsThresholds.RoundingDollarFloor ? (hrs / budget) : 0.0,
                Fee = p?.TotalFee ?? 0.0,
                PercentBilled = p?.PercentBilled ?? 0.0,
                PercentBilledWithUnposted = p?.PercentBilledWithUnposted ?? 0.0,
                HasUnpostedBilling = p?.HasUnpostedBilling ?? false,
                RiskStatus = status,
                RiskColorName = color,
                DeliveryConfidence = dc.Status,
                DeliveryConfidenceColorName = dc.ColorName,
                DeliveryConfidenceSummary = dc.Summary,
                DeliveryConfidenceTooltip = dc.Tooltip,
                ConfidenceLevel = level,
                ConfidenceDisplay = dc.Status,
                ConstructionType = (p?.ConstructionType ?? "").Trim(),
                ProjectCategory = (p?.ProjectCategory ?? "").Trim(),
                DraftingType = (p?.DraftingType ?? "").Trim(),
            };
        }
    }
}
