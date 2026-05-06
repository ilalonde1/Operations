#nullable enable
using System;
using System.ComponentModel;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public sealed class PmProjectRow : INotifyPropertyChanged
    {
        private int _meetingPriority;

        public string Wbs1 { get; private set; } = "";
        public string Name { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string DraftingManager { get; private set; } = "";
        public string ConstructionType { get; private set; } = "";
        public string ProjectCategory { get; private set; } = "";
        public string DraftingType { get; private set; } = "";
        public double Gfa { get; private set; }
        public double Fee { get; private set; }
        public double FeeBilled { get; private set; }
        public double UnpostedFeeBilled { get; private set; }
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double FeeRemaining => Fee - FeeBilled;
        public double PercentBilled => Fee == 0 ? 0 : FeeBilled / Fee;
        public double PercentBilledWithUnposted => Fee == 0 ? 0 : FeeBilledWithUnposted / Fee;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > 0.004;
        public string PercentBilledText => Fee == 0 ? "—" : PercentBilled.ToString("P0");
        public string PercentBilledWithUnpostedText => Fee == 0 ? "—" : PercentBilledWithUnposted.ToString("P0");
        public string PercentBilledBarColor => PercentBilled switch
        {
            >= 0.95 => "#DC2626",
            >= 0.85 => "#EA580C",
            >= 0.50 => "#16A34A",
            _ => "#6B7280",
        };
        public string PercentBilledWithUnpostedBarColor => PercentBilledWithUnposted switch
        {
            >= 0.95 => "#DC2626",
            >= 0.85 => "#EA580C",
            >= 0.50 => "#16A34A",
            _ => "#6B7280",
        };
        public double PercentBilledBarValue => System.Math.Min(1.0, System.Math.Max(0.0, PercentBilled));
        public double PercentBilledWithUnpostedBarValue => System.Math.Min(1.0, System.Math.Max(0.0, PercentBilledWithUnposted));

        public string EngPercentText => EngBudget == 0 ? "—" : EngPercent.ToString("P0");
        public string EngPercentBarColor => EngPercent switch
        {
            >= 1.0 => "#DC2626",
            >= 0.85 => "#EA580C",
            >= 0.50 => "#16A34A",
            _ => "#6B7280",
        };
        public double EngPercentBarValue => System.Math.Min(1.0, System.Math.Max(0.0, EngPercent));

        public string DraftPercentText => DraftBudget == 0 ? "—" : DraftPercent.ToString("P0");
        public string DraftPercentBarColor => DraftPercent switch
        {
            >= 1.0 => "#DC2626",
            >= 0.85 => "#EA580C",
            >= 0.50 => "#16A34A",
            _ => "#6B7280",
        };
        public double DraftPercentBarValue => System.Math.Min(1.0, System.Math.Max(0.0, DraftPercent));

        public double EngBudget { get; private set; }
        public double EngHrs { get; private set; }
        public double RemainingEngHours { get; private set; }
        public double EngPercent { get; private set; }
        public bool IsEngBudgetEstimated { get; private set; }

        public double DraftBudget { get; private set; }
        public double DraftHrs { get; private set; }
        public double RemainingDraftHours { get; private set; }
        public double DraftPercent { get; private set; }
        public bool IsDraftBudgetEstimated { get; private set; }

        public bool IsEngOverBudget => RemainingEngHours < 0;
        public bool IsDraftOverBudget => RemainingDraftHours < 0;

        public string EngBudgetDisplay => IsEngBudgetEstimated ? $"{EngBudget:N1} *" : $"{EngBudget:N1}";
        public string DraftBudgetDisplay => IsDraftBudgetEstimated ? $"{DraftBudget:N1} *" : $"{DraftBudget:N1}";
        public string EngBudgetTooltip => IsEngBudgetEstimated
            ? $"Estimated — no budget in Deltek. {(BudgetPeerCount >= 3 ? $"Peer-based: median of {BudgetPeerCount} similar projects." : "Formula fallback (<3 peers found).")}"
            : "Actual budget from Deltek PRLabor.";
        public string DraftBudgetTooltip => IsDraftBudgetEstimated
            ? $"Estimated — no budget in Deltek. {(BudgetPeerCount >= 3 ? $"Peer-based: median of {BudgetPeerCount} similar projects." : "Formula fallback (<3 peers found).")}"
            : "Actual budget from Deltek PRLabor.";

        public double InspHrs { get; private set; }
        public int TotalInspections { get; private set; }
        public int LastMonthInspections { get; private set; }

        public int BudgetPeerCount { get; private set; }
        public double FeePerHours { get; private set; }
        public double BilledPerHours { get; private set; }
        public double BilledPerHoursWithUnposted { get; private set; }

        public string DeliveryRisk { get; private set; } = "High Confidence";
        public string DeliveryRiskTooltip { get; private set; } = "";
        public DeliveryConfidenceLevel ConfidenceLevel { get; private set; } = DeliveryConfidenceLevel.HighConfidence;

        public FinancialsProjectRow Source { get; private set; } = new();

        public int MeetingPriority
        {
            get => _meetingPriority;
            set
            {
                if (_meetingPriority == value) return;
                _meetingPriority = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MeetingPriority)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Backwards-compatible default — assumes rate=1.0 (CAD-only). Production callers
        // should pass the snapshot's UsdToCadRate so USA-org rows roll into CAD-equivalent
        // PM/group totals.
        public static PmProjectRow FromProject(FinancialsProjectRow p) => FromProject(p, usdToCadRate: 1.0);

        public static PmProjectRow FromProject(FinancialsProjectRow p, double usdToCadRate)
        {
            var engRemaining = p.EngBudget - p.EngHrs;
            var draftRemaining = p.DraftBudget - p.DraftHrs;
            var dc = p.DeliveryResult ?? DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                DeliveryConfidenceLevel.HighConfidence;

            // FX-convert dollar fields for USA-org rows so PmTools rollups don't mix CAD+USD.
            var fx = OrgFx.IsUsaOrg(p.Org) ? usdToCadRate : 1.0;

            return new PmProjectRow
            {
                Source = p,
                Wbs1 = (p.Wbs1 ?? "").Trim(),
                Name = (p.Name ?? "").Trim(),
                Phase = (p.Phase ?? "").Trim(),
                Pm = (p.Pm ?? "").Trim(),
                DraftingManager = (p.DraftingManager ?? "").Trim(),
                ConstructionType = (p.ConstructionType ?? "").Trim(),
                ProjectCategory = (p.ProjectCategory ?? "").Trim(),
                DraftingType = (p.DraftingType ?? "").Trim(),
                Gfa = p.Gfa,
                Fee = p.TotalFee * fx,
                FeeBilled = p.FeeBilled * fx,
                UnpostedFeeBilled = p.UnpostedFeeBilled * fx,
                EngBudget = p.EngBudget,
                EngHrs = p.EngHrs,
                RemainingEngHours = engRemaining,
                EngPercent = p.EngBudget == 0 ? 0 : p.EngHrs / p.EngBudget,
                IsEngBudgetEstimated = p.EngBudgetActual <= 0,
                DraftBudget = p.DraftBudget,
                DraftHrs = p.DraftHrs,
                RemainingDraftHours = draftRemaining,
                DraftPercent = p.DraftBudget == 0 ? 0 : p.DraftHrs / p.DraftBudget,
                IsDraftBudgetEstimated = p.DraftBudgetActual <= 0,
                BudgetPeerCount = p.BudgetPeerCount,
                InspHrs = p.InspHrs,
                TotalInspections = p.TotalInspections,
                LastMonthInspections = p.LastMonthInspections,
                FeePerHours = p.FeePerHours,
                BilledPerHours = p.BilledPerHours,
                BilledPerHoursWithUnposted = p.BilledPerHoursWithUnposted,
                DeliveryRisk = dc.Status,
                DeliveryRiskTooltip = dc.Tooltip,
                ConfidenceLevel = level,
            };
        }
    }
}
