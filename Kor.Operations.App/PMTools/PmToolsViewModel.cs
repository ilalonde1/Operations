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
using System.Windows.Threading;
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
        private string _myProjectsWarning = "";
        private DateTimeOffset? _lastRefreshed;
        private string _selectedPhase = "All";
        private bool _showMyProjectsOnly;
        private bool _showWatchlistOnly = true;
        private string _selectedConstructionType = "All";
        private string _currentUserName = "";
        private string _projectSearchText = "";
        private string _selectedUtilizationPm = "All";
        private string _selectedUtilizationRisk = "All";
        private string _utilizationSearchText = "";
        private int _capacityRiskViewIndex;
        private int _pmGroupSortMode = 0;
        private double _engRate = 474;
        private double _draftRate = 655;
        private double _combinedRate = 275;
        private double _targetBilling = 185;
        private static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(90);

        // Debounce timers — lazily initialised on first use (must be on UI thread)
        private DispatcherTimer? _projectSearchDebounce;
        private DispatcherTimer? _utilizationSearchDebounce;

        public int TotalProjects { get; private set; }
        public int AtRiskOrCriticalCount { get; private set; }
        public double TotalEngHoursRemaining { get; private set; }
        public double TotalDraftHoursRemaining { get; private set; }
        public double TotalFeeRemaining { get; private set; }
        public int OverEngBudgetCount { get; private set; }
        public int PortfolioCriticalCount { get; private set; }
        public int PortfolioAtRiskCount { get; private set; }
        public int PortfolioHighConfidenceCount { get; private set; }

        // BulkObservableCollection fires one Reset notification on ReplaceAll instead of N Add events
        public BulkObservableCollection<PmProjectRow>      ProjectRows       { get; } = new();
        public BulkObservableCollection<PmGroupViewModel>  PmGroups          { get; } = new();
        public BulkObservableCollection<UtilizationRow>    UtilizationRows   { get; } = new();
        public BulkObservableCollection<DraftUtilizationRow> DraftUtilizationRows { get; } = new();
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
        public string MyProjectsWarning
        {
            get => _myProjectsWarning;
            private set { _myProjectsWarning = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(MyProjectsWarningVisibility)); }
        }
        public Visibility MyProjectsWarningVisibility => string.IsNullOrWhiteSpace(_myProjectsWarning) ? Visibility.Collapsed : Visibility.Visible;
        public bool HasData => ProjectRows.Count > 0;
        public bool CanRefresh => !_isLoading;
        public bool CanExportUtilization =>
            !_isLoading &&
            !_isExporting &&
            (IsEngineeringCapacitySelected ? UtilizationRows.Count > 0 : DraftUtilizationRows.Count > 0);
        public bool CanExportPmGroups => !_isLoading && !_isExporting && PmGroups.Count > 0;

        public string LastRefreshedDisplay =>
            _lastRefreshed.HasValue ? _lastRefreshed.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "Not yet";

        public bool IsDataStale =>
            _lastRefreshed.HasValue &&
            (DateTimeOffset.Now - _lastRefreshed.Value) > StalenessThreshold;

        public string StalenessWarning =>
            IsDataStale
                ? $"Data is {(int)(DateTimeOffset.Now - _lastRefreshed!.Value).TotalHours}h old — consider refreshing"
                : "";

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
                BuildPmGroups();
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
                BuildPmGroups();
                UpdateMyProjectsWarning();
            }
        }

        public bool ShowWatchlistOnly
        {
            get => _showWatchlistOnly;
            set { if (SetField(ref _showWatchlistOnly, value)) { OnPropertyChanged(nameof(IsWatchlistOnly)); OnPropertyChanged(nameof(IsAllActive)); } }
        }

        public bool IsWatchlistOnly => _showWatchlistOnly;
        public bool IsAllActive => !_showWatchlistOnly;

        public ObservableCollection<string> ConstructionTypeOptions { get; } = new() { "All" };

        public string SelectedConstructionType
        {
            get => _selectedConstructionType;
            set
            {
                _selectedConstructionType = value ?? "All";
                OnPropertyChanged();
                ProjectView?.Refresh();
                BuildPmGroups();
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
                {
                    ProjectView.Refresh();
                    BuildPmGroups();
                }
                UpdateMyProjectsWarning();
            }
        }

        public string ProjectSearchText
        {
            get => _projectSearchText;
            set
            {
                _projectSearchText = value ?? "";
                OnPropertyChanged();
                // Debounce: wait 300 ms of no further typing before re-filtering
                if (_projectSearchDebounce == null)
                {
                    _projectSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    _projectSearchDebounce.Tick += (_, _) =>
                    {
                        _projectSearchDebounce.Stop();
                        ProjectView.Refresh();
                        BuildPmGroups();
                    };
                }
                _projectSearchDebounce.Stop();
                _projectSearchDebounce.Start();
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
                // Debounce: wait 300 ms of no further typing before re-filtering
                if (_utilizationSearchDebounce == null)
                {
                    _utilizationSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    _utilizationSearchDebounce.Tick += (_, _) =>
                    {
                        _utilizationSearchDebounce.Stop();
                        UtilizationView.Refresh();
                        DraftUtilizationView.Refresh();
                    };
                }
                _utilizationSearchDebounce.Stop();
                _utilizationSearchDebounce.Start();
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
                OnPropertyChanged(nameof(CanExportPmGroups));
            }
        }

        public bool IsEngineeringCapacitySelected => CapacityRiskViewIndex == 0;
        public bool IsDraftingCapacitySelected => CapacityRiskViewIndex == 1;
        public string CapacityRiskTitle => IsEngineeringCapacitySelected ? "Engineering Capacity Risk" : "Drafting Capacity Risk";
        public string CapacityRiskSubtitle => IsEngineeringCapacitySelected
            ? "Highlights projects consuming engineering hours faster than planned."
            : "Highlights projects consuming drafting hours faster than planned.";
        public int PmGroupSortMode
        {
            get => _pmGroupSortMode;
            set
            {
                var v = Math.Clamp(value, 0, 3);
                if (_pmGroupSortMode == v) return;
                _pmGroupSortMode = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSortFee));
                OnPropertyChanged(nameof(IsSortUnbilled));
                OnPropertyChanged(nameof(IsSortName));
                OnPropertyChanged(nameof(IsSortAtRisk));
                BuildPmGroups();
            }
        }
        public bool IsSortFee => _pmGroupSortMode == 0;
        public bool IsSortUnbilled => _pmGroupSortMode == 3;
        public bool IsSortName => _pmGroupSortMode == 1;
        public bool IsSortAtRisk => _pmGroupSortMode == 2;

        public double EngRate { get => _engRate; set { if (SetField(ref _engRate, value)) OnPropertyChanged(nameof(CombinedRate)); } }
        public double DraftRate { get => _draftRate; set { if (SetField(ref _draftRate, value)) OnPropertyChanged(nameof(CombinedRate)); } }
        public double CombinedRate => (_engRate > 0 && _draftRate > 0) ? System.Math.Round(1.0 / (1.0 / _engRate + 1.0 / _draftRate), 0) : 0;
        public double TargetBilling { get => _targetBilling; set => SetField(ref _targetBilling, value); }

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

            // ProjectView: Critical/AtRisk first, then highest fee
            ProjectView.SortDescriptions.Add(new SortDescription(nameof(PmProjectRow.ConfidenceLevel), ListSortDirection.Descending));
            ProjectView.SortDescriptions.Add(new SortDescription(nameof(PmProjectRow.Fee), ListSortDirection.Descending));

            // Utilization: worst burn-rate first
            UtilizationView.SortDescriptions.Add(new SortDescription(nameof(UtilizationRow.PercentEngUsed), ListSortDirection.Descending));
            DraftUtilizationView.SortDescriptions.Add(new SortDescription(nameof(DraftUtilizationRow.PercentDraftUsed), ListSortDirection.Descending));

            UtilizationPmOptions.Add("All");
        }

        internal void SetExporting(bool exporting)
        {
            _isExporting = exporting;
            OnPropertyChanged(nameof(CanExportUtilization));
            OnPropertyChanged(nameof(CanExportPmGroups));
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
            OnPropertyChanged(nameof(CanExportPmGroups));

            try
            {
                var snap = await _svc.GetSnapshotAsync(forceRefresh, ct, watchlistOnly: _showWatchlistOnly);

                // ReplaceAll fires one Reset notification instead of N Add events (Fix 2)
                ProjectRows.ReplaceAll(snap.Rows.Select(PmProjectRow.FromProject));
                UtilizationRows.ReplaceAll(snap.Rows.Select(UtilizationRow.FromProject));
                DraftUtilizationRows.ReplaceAll(snap.Rows.Select(DraftUtilizationRow.FromProject));

                _lastRefreshed = snap.RefreshedAt;
                RecalcKpis();
                BuildUtilizationPmOptions(snap.Rows);
                BuildConstructionTypeOptions(snap.Rows);
                BuildPmGroups();
                ProjectView.Refresh();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();

                OnPropertyChanged(nameof(LastRefreshedDisplay));
                OnPropertyChanged(nameof(IsDataStale));
                OnPropertyChanged(nameof(StalenessWarning));
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(CanExportUtilization));
                OnPropertyChanged(nameof(CanExportPmGroups));
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
                OnPropertyChanged(nameof(CanExportPmGroups));
            }
        }

        private void RecalcKpis()
        {
            // Single pass instead of 9 separate LINQ enumerations
            var atRiskOrCritical = 0;
            var overEngBudget    = 0;
            var critical         = 0;
            var atRisk           = 0;
            var highConfidence   = 0;
            var engRemaining     = 0.0;
            var draftRemaining   = 0.0;
            var feeRemaining     = 0.0;

            foreach (var r in ProjectRows)
            {
                var cl = r.ConfidenceLevel;
                if (cl == DeliveryConfidenceLevel.Critical)       { critical++;       atRiskOrCritical++; }
                else if (cl == DeliveryConfidenceLevel.AtRisk)    { atRisk++;         atRiskOrCritical++; }
                else if (cl == DeliveryConfidenceLevel.HighConfidence) highConfidence++;

                if (r.RemainingEngHours < 0) overEngBudget++;

                engRemaining   += r.RemainingEngHours;
                draftRemaining += r.RemainingDraftHours;
                feeRemaining   += r.FeeRemaining;
            }

            TotalProjects            = ProjectRows.Count;
            AtRiskOrCriticalCount    = atRiskOrCritical;
            TotalEngHoursRemaining   = engRemaining;
            TotalDraftHoursRemaining = draftRemaining;
            TotalFeeRemaining        = feeRemaining;
            OverEngBudgetCount       = overEngBudget;
            PortfolioCriticalCount   = critical;
            PortfolioAtRiskCount     = atRisk;
            PortfolioHighConfidenceCount = highConfidence;

            OnPropertyChanged(nameof(TotalProjects));
            OnPropertyChanged(nameof(AtRiskOrCriticalCount));
            OnPropertyChanged(nameof(TotalEngHoursRemaining));
            OnPropertyChanged(nameof(TotalDraftHoursRemaining));
            OnPropertyChanged(nameof(TotalFeeRemaining));
            OnPropertyChanged(nameof(OverEngBudgetCount));
            OnPropertyChanged(nameof(PortfolioCriticalCount));
            OnPropertyChanged(nameof(PortfolioAtRiskCount));
            OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
            UpdateMyProjectsWarning();
        }

        private void UpdateMyProjectsWarning()
        {
            if (!_showMyProjectsOnly || string.IsNullOrWhiteSpace(_currentUserName))
            { MyProjectsWarning = ""; return; }
            var hasMatch = ProjectRows.Any(r => string.Equals(r.Pm, _currentUserName, StringComparison.OrdinalIgnoreCase));
            MyProjectsWarning = hasMatch ? "" : $"Your display name \"{_currentUserName}\" doesn't match any PM in Deltek. \"My Projects\" may show no results.";
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

        private void BuildConstructionTypeOptions(List<FinancialsProjectRow> rows)
        {
            var keep = _selectedConstructionType;
            var types = rows
                .Select(r => (r.ConstructionType ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ConstructionTypeOptions.Clear();
            ConstructionTypeOptions.Add("All");
            foreach (var t in types)
                ConstructionTypeOptions.Add(t);

            _selectedConstructionType = ConstructionTypeOptions.Contains(keep) ? keep : "All";
            OnPropertyChanged(nameof(SelectedConstructionType));
        }

        private void BuildPmGroups()
        {
            var grouped = ProjectView.Cast<PmProjectRow>()
                .GroupBy(r => (r.Pm ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

            var ordered = _pmGroupSortMode switch
            {
                1 => grouped.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
                2 => grouped
                    .OrderByDescending(g => g.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.Critical || r.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk))
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
                3 => grouped
                    .OrderByDescending(g => g.Sum(r => r.FeeRemaining))
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
                _ => grouped
                    .OrderByDescending(g => g.Sum(r => r.Fee))
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
            };

            PmGroups.ReplaceAll(ordered.Select(g => new PmGroupViewModel(g.Key, g)));
            OnPropertyChanged(nameof(CanExportPmGroups));
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

            if (!string.IsNullOrEmpty(_selectedConstructionType) &&
                _selectedConstructionType != "All" &&
                !string.Equals(r.ConstructionType, _selectedConstructionType, StringComparison.OrdinalIgnoreCase))
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
                r.Wbs1.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                r.Pm.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
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
