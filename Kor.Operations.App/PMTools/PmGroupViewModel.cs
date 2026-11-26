#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Core;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    internal sealed class PmGroupViewModel : ObservableObject
    {
        private bool _isExpanded = false;

        public string PmName { get; }
        public int ProjectCount { get; }
        public double TotalFee { get; }
        public double TotalUnbilled { get; }
        public double TotalEngHrs { get; }
        public double TotalEngBudget { get; }
        public double TotalDraftHrs { get; }
        public double TotalDraftBudget { get; }
        public int AtRiskOrCriticalCount { get; }
        public IReadOnlyList<PmProjectRow> Projects { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (!SetField(ref _isExpanded, value)) return;
                OnPropertyChanged(nameof(ExpandIcon));
            }
        }

        public string ExpandIcon => IsExpanded ? "▼" : "▶";

        public PmGroupViewModel(string pmName, IEnumerable<PmProjectRow> projects)
        {
            PmName = (pmName ?? string.Empty).Trim();

            // Sort once, then accumulate all aggregates in the same pass (was 8-9 passes)
            var projectList = (projects ?? Array.Empty<PmProjectRow>())
                .OrderByDescending(p => GetPhaseOrder(p.Phase))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fee          = 0.0;
            var unbilled     = 0.0;
            var engHrs       = 0.0;
            var engBudget    = 0.0;
            var draftHrs     = 0.0;
            var draftBudget  = 0.0;
            var atRiskCount  = 0;

            foreach (var p in projectList)
            {
                fee         += p.Fee;
                unbilled    += p.FeeRemaining;
                engHrs      += p.EngHrs;
                engBudget   += p.EngBudget;
                draftHrs    += p.DraftHrs;
                draftBudget += p.DraftBudget;
                if (p.ConfidenceLevel == DeliveryConfidenceLevel.Critical ||
                    p.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk)
                    atRiskCount++;
            }

            ProjectCount          = projectList.Count;
            TotalFee              = fee;
            TotalUnbilled         = unbilled;
            TotalEngHrs           = engHrs;
            TotalEngBudget        = engBudget;
            TotalDraftHrs         = draftHrs;
            TotalDraftBudget      = draftBudget;
            AtRiskOrCriticalCount = atRiskCount;
            Projects = projectList;
            RecomputePrioritised();
        }

        // ── Spec R9 (Jim 9 Nov 0:05:21-0:06:31): totals over prioritised (P1-P5) jobs only,
        // "because I don't want all the noise people are putting on here". Recomputed in place
        // when meeting priorities change (WorkloadMeetingWindow.SyncMeetingPrioritiesToRows),
        // so the group need not be rebuilt and the list does not jump during the meeting.
        public int PrioritisedCount { get; private set; }
        public double PrioritisedFee { get; private set; }
        public double PrioritisedUnbilled { get; private set; }

        public string PrioritisedSummary => PrioritisedCount == 0
            ? "No prioritised jobs"
            : $"Prioritised: {PrioritisedCount} · Fee {PrioritisedFee:C0} · Unbilled {PrioritisedUnbilled:C0}";

        public void RecomputePrioritised()
        {
            var prioritised = Projects.Where(PmToolsViewModel.IsPrioritised).ToList();
            PrioritisedCount = prioritised.Count;
            PrioritisedFee = prioritised.Sum(p => p.Fee);
            PrioritisedUnbilled = prioritised.Sum(p => p.FeeRemaining);
            OnPropertyChanged(nameof(PrioritisedCount));
            OnPropertyChanged(nameof(PrioritisedFee));
            OnPropertyChanged(nameof(PrioritisedUnbilled));
            OnPropertyChanged(nameof(PrioritisedSummary));
        }

        private static int GetPhaseOrder(string? phase)
        {
            var value = (phase ?? string.Empty).Trim();
            if (value.IndexOf("CA", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (value.IndexOf("CD", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (value.IndexOf("DD", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (value.IndexOf("SD", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            return 0;
        }
    }
}
