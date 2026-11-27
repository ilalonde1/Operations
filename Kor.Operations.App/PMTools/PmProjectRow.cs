#nullable enable
using System;
using System.ComponentModel;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public sealed class PmProjectRow : INotifyPropertyChanged
    {
        private int _meetingPriority;
        private string _meetingNotes = "";
        private bool _hasMeetingRow;

        public string Wbs1 { get; private set; } = "";
        public string Name { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string DraftingManager { get; private set; } = "";
        public string ConstructionType { get; private set; } = "";
        public string ProjectCategory { get; private set; } = "";
        public string DraftingType { get; private set; } = "";

        /// <summary>Spec R7 (Jim 13 Mar 17:45, 9 Nov 0:03:16): CAD or Revit, from Deltek
        /// CustDraftingType, which holds "CAD", "RV" or nothing (read 26 Nov: 22 / 23 / 16 of 61
        /// active hot-list projects). Blank when Deltek has no value.</summary>
        public string DraftingSoftware => DraftingSoftwareLabel(DraftingType);

        internal static string DraftingSoftwareLabel(string? draftingType)
        {
            var t = (draftingType ?? "").Trim();
            if (t.Equals("RV", StringComparison.OrdinalIgnoreCase) || t.Equals("Revit", StringComparison.OrdinalIgnoreCase)) return "Revit";
            if (t.Equals("CAD", StringComparison.OrdinalIgnoreCase)) return "CAD";
            return t;
        }

        public double Gfa { get; private set; }
        public double Fee { get; private set; }
        public double FeeBilled { get; private set; }
        public double UnpostedFeeBilled { get; private set; }
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;

        // ── Jim, 13 Mar (spec R21, R22, R25, R30; rule and data in
        // docs/KOR-PMTools-Redesign-Spec-2026-11-26-sources/Deltek-Fee-Split-Findings-2026-11-26.md) ──

        /// <summary>R21: fee under the template's "X" extra phases (incl. hourly extras). CAD.</summary>
        public double ExtraFee { get; private set; }

        /// <summary>R21: the contract fee without extras.</summary>
        public double OriginalFee => Fee - ExtraFee;

        /// <summary>R22: billed as a share of the ORIGINAL fee, so over 100% shows extras being billed
        /// (Jim 19:53: "Maybe we do percent of original fee").</summary>
        public double PercentBilledOfOriginal => Math.Abs(OriginalFee) > AnalyticsThresholds.RoundingDollarFloor ? FeeBilledWithUnposted / OriginalFee : 0;

        /// <summary>R25: hours at each person's charge-out rate (Jim 24:52). CAD.</summary>
        public double EarnedFee { get; private set; }

        /// <summary>R25: hours at each person's salary rate - what the job cost KOR. CAD.</summary>
        public double LaborCost { get; private set; }

        /// <summary>R25: billed / earned. At or above 1, the job is making money (Jim 27:23).</summary>
        public double BilledToEarned => EarnedFee > AnalyticsThresholds.RoundingDollarFloor ? FeeBilledWithUnposted / EarnedFee : 0;

        /// <summary>R25: billed / cost. Below 1, "you're just working for free" (Jim 57:04).</summary>
        public double BilledToCost => LaborCost > AnalyticsThresholds.RoundingDollarFloor ? FeeBilledWithUnposted / LaborCost : 0;

        /// <summary>R30: underwater = the cost of the hours is above what has been billed. A job that
        /// has not billed yet counts only once its cost passes 10% of its fee: every new job has cost
        /// before its first invoice, and flagging all of them was noise (Ian 26 Nov, 31247-01 at SD).</summary>
        public bool IsUnderwater => LaborCost > AnalyticsThresholds.RoundingDollarFloor
            && FeeBilledWithUnposted < LaborCost
            && (FeeBilledWithUnposted > AnalyticsThresholds.RoundingDollarFloor
                || (Fee > AnalyticsThresholds.RoundingDollarFloor && LaborCost > 0.10 * Fee));   // a no-fee (hourly) job waits for its first invoice

        public string BilledToEarnedText => BilledToEarned > 0 ? BilledToEarned.ToString("0.00") : "-";
        public string BilledToCostText => BilledToCost > 0 ? BilledToCost.ToString("0.00") : "-";
        public string PercentBilledOfOriginalText => OriginalFee > AnalyticsThresholds.RoundingDollarFloor ? PercentBilledOfOriginal.ToString("0%") : "-";
        public double FeeRemaining => Fee - FeeBilledWithUnposted;
        public double PercentBilled => Math.Abs(Fee) > AnalyticsThresholds.RoundingDollarFloor ? FeeBilled / Fee : 0;
        public double PercentBilledWithUnposted => Math.Abs(Fee) > AnalyticsThresholds.RoundingDollarFloor ? FeeBilledWithUnposted / Fee : 0;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > 0.004;
        public string PercentBilledText => Math.Abs(Fee) <= AnalyticsThresholds.RoundingDollarFloor ? "—" : PercentBilled.ToString("P0");
        public string PercentBilledWithUnpostedText => Math.Abs(Fee) <= AnalyticsThresholds.RoundingDollarFloor ? "—" : PercentBilledWithUnposted.ToString("P0");
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
        public string EngBudgetTooltip => $"Estimated — KOR does not budget hours in Deltek. {(BudgetPeerCount >= 3 ? $"Peer-based: median of {BudgetPeerCount} similar projects." : "Formula fallback (<3 peers found).")}";
        public string DraftBudgetTooltip => $"Estimated — KOR does not budget hours in Deltek. {(BudgetPeerCount >= 3 ? $"Peer-based: median of {BudgetPeerCount} similar projects." : "Formula fallback (<3 peers found).")}";

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

        // Round 52: meeting notes surfaced directly in the PM Groups grid via
        // a row-details editor. Synced from the meeting VM's CurrentProjects in
        // SyncMeetingPrioritiesToRows (same channel as MeetingPriority); user
        // edits write back through WorkloadMeetingPanelViewModel's debounce.
        public string MeetingNotes
        {
            get => _meetingNotes;
            set
            {
                var v = value ?? "";
                if (_meetingNotes == v) return;
                _meetingNotes = v;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MeetingNotes)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMeetingNotes)));
            }
        }

        public bool HasMeetingNotes => !string.IsNullOrWhiteSpace(_meetingNotes);

        // Round 52f (review finding 5): true only once the meeting's
        // WorkloadMeetingProjects row actually exists in CurrentProjects — set
        // exclusively by SyncMeetingPrioritiesToRows. MeetingPriority is
        // written optimistically by the ComboBox binding BEFORE the upsert
        // round-trips, so gating the notes editor on it let users type into a
        // row whose store UPDATE would hit nothing; those keystrokes were
        // dropped and then visibly wiped by the post-upsert sync.
        public bool HasMeetingRow
        {
            get => _hasMeetingRow;
            set
            {
                if (_hasMeetingRow == value) return;
                _hasMeetingRow = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMeetingRow)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static PmProjectRow FromProject(FinancialsProjectRow p, double usdToCadRate)
        {
            var engRemaining = p.EngBudget - p.EngHrs;
            var draftRemaining = p.DraftBudget - p.DraftHrs;
            var dc = p.DeliveryResult ?? DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Watch :
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
                ExtraFee = p.ExtraFee * fx,
                EarnedFee = p.EarnedFee * fx,
                LaborCost = (p.EngLaborCost + p.DraftLaborCost + p.InspLaborCost + p.DocPrepLaborCost
                             + p.GenLaborCost + p.AdminLaborCost + p.NonBillLaborCost) * fx,
                FeeBilled = p.FeeBilled * fx,
                UnpostedFeeBilled = p.UnpostedFeeBilled * fx,
                EngBudget = p.EngBudget,
                EngHrs = p.EngHrs,
                RemainingEngHours = engRemaining,
                EngPercent = p.EngBudget == 0 ? 0 : p.EngHrs / p.EngBudget,
                IsEngBudgetEstimated = true,
                DraftBudget = p.DraftBudget,
                DraftHrs = p.DraftHrs,
                RemainingDraftHours = draftRemaining,
                DraftPercent = p.DraftBudget == 0 ? 0 : p.DraftHrs / p.DraftBudget,
                IsDraftBudgetEstimated = true,
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
