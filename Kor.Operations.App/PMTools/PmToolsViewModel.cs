#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Kor.Operations.Core;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    internal sealed class PmToolsViewModel : ObservableObject
    {
        private readonly FinancialsService _svc = new();
        private bool _isLoading;
        private bool _isExporting;
        private string _errorMessage = "";
        private DateTimeOffset? _lastRefreshed;
        private string _selectedPhase = "All";
        private bool _showMyProjectsOnly;
        private string _currentUserName = "";
        private string _projectSearchText = "";
        private string _selectedUtilizationPm = "All";
        private string _selectedUtilizationRisk = "All";
        private string _utilizationSearchText = "";
        private int _capacityRiskViewIndex;

        public int TotalProjects { get; private set; }
        public int AtRiskOrCriticalCount { get; private set; }
        public double TotalEngHoursRemaining { get; private set; }
        public double TotalDraftHoursRemaining { get; private set; }
        public int OverEngBudgetCount { get; private set; }
        public int PortfolioCriticalCount { get; private set; }
        public int PortfolioAtRiskCount { get; private set; }
        public int PortfolioHighConfidenceCount { get; private set; }

        public ObservableCollection<PmProjectRow> ProjectRows { get; } = new();
        public ObservableCollection<UtilizationRow> UtilizationRows { get; } = new();
        public ObservableCollection<DraftUtilizationRow> DraftUtilizationRows { get; } = new();
        public ObservableCollection<string> UtilizationPmOptions { get; } = new();
        public ObservableCollection<string> UtilizationRiskOptions { get; } = new() { "All", "Over budget", "At risk", "Healthy" };

        public ICollectionView ProjectView { get; }
        public ICollectionView UtilizationView { get; }
        public ICollectionView DraftUtilizationView { get; }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                _errorMessage = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ErrorVisibility));
            }
        }

        public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        public bool HasData => ProjectRows.Count > 0;
        public bool CanRefresh => !_isLoading;
        public bool CanExportUtilization =>
            !_isLoading &&
            !_isExporting &&
            (IsEngineeringCapacitySelected ? UtilizationRows.Count > 0 : DraftUtilizationRows.Count > 0);

        public string LastRefreshedDisplay =>
            _lastRefreshed.HasValue ? _lastRefreshed.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "Not yet";

        public string StatusHint => _isLoading ? "Loading..." : "";

        public string SelectedPhase
        {
            get => _selectedPhase;
            set
            {
                _selectedPhase = value ?? "All";
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPhaseAll));
                OnPropertyChanged(nameof(IsPhaseSD));
                OnPropertyChanged(nameof(IsPhasDD));
                OnPropertyChanged(nameof(IsPhaseCD));
                OnPropertyChanged(nameof(IsPhaseCA));
                ProjectView.Refresh();
            }
        }

        public bool ShowMyProjectsOnly
        {
            get => _showMyProjectsOnly;
            set
            {
                _showMyProjectsOnly = value;
                OnPropertyChanged();
                ProjectView.Refresh();
            }
        }

        public string CurrentUserName
        {
            get => _currentUserName;
            set
            {
                _currentUserName = value ?? "";
                OnPropertyChanged();
                if (_showMyProjectsOnly)
                    ProjectView.Refresh();
            }
        }

        public string ProjectSearchText
        {
            get => _projectSearchText;
            set
            {
                _projectSearchText = value ?? "";
                OnPropertyChanged();
                ProjectView.Refresh();
            }
        }

        public string SelectedUtilizationPm
        {
            get => _selectedUtilizationPm;
            set
            {
                _selectedUtilizationPm = value ?? "All";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public string SelectedUtilizationRisk
        {
            get => _selectedUtilizationRisk;
            set
            {
                _selectedUtilizationRisk = value ?? "All";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public string UtilizationSearchText
        {
            get => _utilizationSearchText;
            set
            {
                _utilizationSearchText = value ?? "";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public int CapacityRiskViewIndex
        {
            get => _capacityRiskViewIndex;
            set
            {
                var v = Math.Clamp(value, 0, 1);
                if (_capacityRiskViewIndex == v)
                    return;

                _capacityRiskViewIndex = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEngineeringCapacitySelected));
                OnPropertyChanged(nameof(IsDraftingCapacitySelected));
                OnPropertyChanged(nameof(CapacityRiskTitle));
                OnPropertyChanged(nameof(CapacityRiskSubtitle));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
        }

        public bool IsEngineeringCapacitySelected => CapacityRiskViewIndex == 0;
        public bool IsDraftingCapacitySelected => CapacityRiskViewIndex == 1;
        public string CapacityRiskTitle => IsEngineeringCapacitySelected ? "Engineering Capacity Risk" : "Drafting Capacity Risk";
        public string CapacityRiskSubtitle => IsEngineeringCapacitySelected
            ? "Highlights projects consuming engineering hours faster than planned."
            : "Highlights projects consuming drafting hours faster than planned.";

        public bool IsPhaseAll => SelectedPhase == "All";
        public bool IsPhaseSD => SelectedPhase == "SD";
        public bool IsPhasDD => SelectedPhase == "DD";
        public bool IsPhaseCD => SelectedPhase == "CD";
        public bool IsPhaseCA => SelectedPhase == "CA";

        public PmToolsViewModel()
        {
            ProjectView = CollectionViewSource.GetDefaultView(ProjectRows);
            ProjectView.Filter = ProjectFilter;

            UtilizationView = CollectionViewSource.GetDefaultView(UtilizationRows);
            UtilizationView.Filter = UtilizationFilter;

            DraftUtilizationView = CollectionViewSource.GetDefaultView(DraftUtilizationRows);
            DraftUtilizationView.Filter = DraftUtilizationFilter;

            UtilizationPmOptions.Add("All");
        }

        internal void SetExporting(bool exporting)
        {
            _isExporting = exporting;
            OnPropertyChanged(nameof(CanExportUtilization));
        }

        public async Task RefreshAsync(bool forceRefresh, CancellationToken ct)
        {
            if (_isLoading)
                return;

            _isLoading = true;
            ErrorMessage = "";
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(StatusHint));
            OnPropertyChanged(nameof(CanExportUtilization));

            try
            {
                var snap = await _svc.GetSnapshotAsync(forceRefresh, ct);

                ProjectRows.Clear();
                UtilizationRows.Clear();
                DraftUtilizationRows.Clear();

                foreach (var r in snap.Rows)
                {
                    ProjectRows.Add(PmProjectRow.FromProject(r));
                    UtilizationRows.Add(UtilizationRow.FromProject(r));
                    DraftUtilizationRows.Add(DraftUtilizationRow.FromProject(r));
                }

                _lastRefreshed = snap.RefreshedAt;
                RecalcKpis();
                BuildUtilizationPmOptions(snap.Rows);
                ProjectView.Refresh();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();

                OnPropertyChanged(nameof(LastRefreshedDisplay));
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Unable to load data from Deltek. Try Refresh.\n\n{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(StatusHint));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
        }

        private void RecalcKpis()
        {
            TotalProjects = ProjectRows.Count;
            AtRiskOrCriticalCount = ProjectRows.Count(r =>
                r.ConfidenceLevel == DeliveryConfidenceLevel.Critical ||
                r.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk);
            TotalEngHoursRemaining = ProjectRows.Sum(r => r.RemainingEngHours);
            TotalDraftHoursRemaining = ProjectRows.Sum(r => r.RemainingDraftHours);
            OverEngBudgetCount = ProjectRows.Count(r => r.RemainingEngHours < 0);
            PortfolioCriticalCount = ProjectRows.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.Critical);
            PortfolioAtRiskCount = ProjectRows.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk);
            PortfolioHighConfidenceCount = ProjectRows.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.HighConfidence);

            OnPropertyChanged(nameof(TotalProjects));
            OnPropertyChanged(nameof(AtRiskOrCriticalCount));
            OnPropertyChanged(nameof(TotalEngHoursRemaining));
            OnPropertyChanged(nameof(TotalDraftHoursRemaining));
            OnPropertyChanged(nameof(OverEngBudgetCount));
            OnPropertyChanged(nameof(PortfolioCriticalCount));
            OnPropertyChanged(nameof(PortfolioAtRiskCount));
            OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
        }

        private void BuildUtilizationPmOptions(List<FinancialsProjectRow> rows)
        {
            var keep = SelectedUtilizationPm;
            var pms = rows
                .Select(r => (r.Pm ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UtilizationPmOptions.Clear();
            UtilizationPmOptions.Add("All");
            foreach (var pm in pms)
                UtilizationPmOptions.Add(pm);

            SelectedUtilizationPm = UtilizationPmOptions.Contains(keep) ? keep : "All";
        }

        private bool ProjectFilter(object obj)
        {
            if (obj is not PmProjectRow r)
                return false;

            if (!string.IsNullOrEmpty(_selectedPhase) &&
                _selectedPhase != "All" &&
                r.Phase.IndexOf(_selectedPhase, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (_showMyProjectsOnly &&
                !string.IsNullOrEmpty(_currentUserName) &&
                !string.Equals(r.Pm, _currentUserName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var q = (_projectSearchText ?? "").Trim();
            if (q.Length > 0 &&
                r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                r.Wbs1.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        private bool UtilizationFilter(object obj)
        {
            if (obj is not UtilizationRow r)
                return false;

            return MatchesUtilizationFilters(r.Pm, r.RiskStatus, r.ProjectName, r.Wbs1);
        }

        private bool DraftUtilizationFilter(object obj)
        {
            if (obj is not DraftUtilizationRow r)
                return false;

            return MatchesUtilizationFilters(r.Pm, r.RiskStatus, r.ProjectName, r.Wbs1);
        }

        private bool MatchesUtilizationFilters(string pmValue, string riskValue, string projectName, string wbs1)
        {
            if (!string.IsNullOrEmpty(_selectedUtilizationPm) &&
                _selectedUtilizationPm != "All" &&
                !string.Equals(pmValue, _selectedUtilizationPm, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_selectedUtilizationRisk) &&
                _selectedUtilizationRisk != "All" &&
                !string.Equals(riskValue, _selectedUtilizationRisk, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var q = (_utilizationSearchText ?? "").Trim();
            if (q.Length > 0 &&
                (projectName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                (wbs1 ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }
    }
}
