#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public sealed class PmProjectRow
    {
        public string Wbs1 { get; private set; } = "";
        public string Name { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string DraftingManager { get; private set; } = "";
        public double Gfa { get; private set; }
        public double Fee { get; private set; }

        public double EngBudget { get; private set; }
        public double EngHrs { get; private set; }
        public double RemainingEngHours { get; private set; }
        public double EngPercent { get; private set; }

        public double DraftBudget { get; private set; }
        public double DraftHrs { get; private set; }
        public double RemainingDraftHours { get; private set; }
        public double DraftPercent { get; private set; }
        public bool IsEngOverBudget => RemainingEngHours < 0;
        public bool IsDraftOverBudget => RemainingDraftHours < 0;

        public double ChkHrs { get; private set; }
        public double InspHrs { get; private set; }

        public string DeliveryRisk { get; private set; } = "High Confidence";
        public string DeliveryRiskTooltip { get; private set; } = "";
        public DeliveryConfidenceLevel ConfidenceLevel { get; private set; } = DeliveryConfidenceLevel.HighConfidence;

        public FinancialsProjectRow Source { get; private set; } = new();

        public static PmProjectRow FromProject(FinancialsProjectRow p)
        {
            var engRemaining = p.EngBudget - p.EngHrs;
            var draftRemaining = p.DraftBudget - p.DraftHrs;
            var dc = DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                DeliveryConfidenceLevel.HighConfidence;

            return new PmProjectRow
            {
                Source = p,
                Wbs1 = (p.Wbs1 ?? "").Trim(),
                Name = (p.Name ?? "").Trim(),
                Phase = (p.Phase ?? "").Trim(),
                Pm = (p.Pm ?? "").Trim(),
                DraftingManager = (p.DraftingManager ?? "").Trim(),
                Gfa = p.Gfa,
                Fee = p.Fee,
                EngBudget = p.EngBudget,
                EngHrs = p.EngHrs,
                RemainingEngHours = engRemaining,
                EngPercent = p.EngBudget == 0 ? 0 : p.EngHrs / p.EngBudget,
                DraftBudget = p.DraftBudget,
                DraftHrs = p.DraftHrs,
                RemainingDraftHours = draftRemaining,
                DraftPercent = p.DraftBudget == 0 ? 0 : p.DraftHrs / p.DraftBudget,
                ChkHrs = p.ChkHrs,
                InspHrs = p.InspHrs,
                DeliveryRisk = dc.Status,
                DeliveryRiskTooltip = dc.Tooltip,
                ConfidenceLevel = level,
            };
        }
    }
}
