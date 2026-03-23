#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kor.Operations.Core;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    internal sealed class PmGroupViewModel : ObservableObject
    {
        private bool _isExpanded = true;

        public string PmName { get; }
        public int ProjectCount { get; }
        public double TotalFee { get; }
        public double TotalEngHrs { get; }
        public double TotalEngBudget { get; }
        public double TotalDraftHrs { get; }
        public double TotalDraftBudget { get; }
        public int AtRiskOrCriticalCount { get; }
        public ObservableCollection<PmProjectRow> Projects { get; }

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
            var engHrs       = 0.0;
            var engBudget    = 0.0;
            var draftHrs     = 0.0;
            var draftBudget  = 0.0;
            var atRiskCount  = 0;

            foreach (var p in projectList)
            {
                fee         += p.Fee;
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
            TotalEngHrs           = engHrs;
            TotalEngBudget        = engBudget;
            TotalDraftHrs         = draftHrs;
            TotalDraftBudget      = draftBudget;
            AtRiskOrCriticalCount = atRiskCount;
            Projects = new ObservableCollection<PmProjectRow>(projectList);
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
