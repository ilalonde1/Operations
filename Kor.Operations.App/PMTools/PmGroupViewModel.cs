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
            var projectList = (projects ?? Array.Empty<PmProjectRow>())
                .OrderByDescending(p => GetPhaseOrder(p.Phase))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PmName = (pmName ?? string.Empty).Trim();
            ProjectCount = projectList.Count;
            TotalFee = projectList.Sum(p => p.Fee);
            TotalEngHrs = projectList.Sum(p => p.EngHrs);
            TotalEngBudget = projectList.Sum(p => p.EngBudget);
            TotalDraftHrs = projectList.Sum(p => p.DraftHrs);
            TotalDraftBudget = projectList.Sum(p => p.DraftBudget);
            AtRiskOrCriticalCount = projectList.Count(p =>
                p.ConfidenceLevel == DeliveryConfidenceLevel.Critical ||
                p.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk);
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
