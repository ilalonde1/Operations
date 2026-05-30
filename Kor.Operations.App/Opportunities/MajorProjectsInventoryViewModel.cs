#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Data.MajorProjects;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class MajorProjectsInventoryViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Major Projects Inventory (BD)";
    public const string AllFilter = "All";

    private static readonly Brush StatusNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));
    private static readonly Brush StatusError = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));

    private readonly IMajorProjectsInventoryStore _store;
    private readonly ILogger<MajorProjectsInventoryViewModel> _logger;
    private MajorProjectRow? _selected;
    private string _searchText = string.Empty;
    private string _provinceFilter = AllFilter;
    private string _stageFilter = AllFilter;
    private string _sectorFilter = AllFilter;
    private bool _indigenousOnly;
    private decimal? _minCost;
    private string _statusMessage = "Ready.";
    private Brush _statusBrush = StatusNeutral;
    private bool _isLoading;
    private IReadOnlyList<MajorProjectRow> _contextSnapshot = Array.Empty<MajorProjectRow>();

    public MajorProjectsInventoryViewModel(
        IMajorProjectsInventoryStore store,
        ILogger<MajorProjectsInventoryViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        FilteredProjectsView = CollectionViewSource.GetDefaultView(Projects);
        FilteredProjectsView.Filter = ProjectFilterPredicate;
    }

    public ObservableCollection<MajorProjectRow> Projects { get; } = new();
    public ICollectionView FilteredProjectsView { get; }
    public ObservableCollection<string> ProvinceFilterOptions { get; } = new() { AllFilter };
    public ObservableCollection<string> StageFilterOptions { get; } = new() { AllFilter };
    public ObservableCollection<string> SectorFilterOptions { get; } = new() { AllFilter };
    public ObservableCollection<string> RegionFilterOptions { get; } = new() { AllFilter };

    public MajorProjectRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelected));
                OnPropertyChanged(nameof(HasSelectedSourceUrl));
                OnPropertyChanged(nameof(HasSelectedProjectWebsite));
                OnPropertyChanged(nameof(HasSelectedProponentOrg));
                OnPropertyChanged(nameof(HasSelectedArchitectOrg));
                OnPropertyChanged(nameof(NoSelectedProponentOrg));
                OnPropertyChanged(nameof(NoSelectedArchitectOrg));
            }
        }
    }

    public bool HasSelected => Selected is not null;
    public bool HasSelectedSourceUrl => !string.IsNullOrWhiteSpace(Selected?.SourceUrl);
    public bool HasSelectedProjectWebsite => !string.IsNullOrWhiteSpace(Selected?.ProjectWebsite);
    public bool HasSelectedProponentOrg => Selected?.ProponentCanonicalOrgId is not null;
    public bool HasSelectedArchitectOrg => Selected?.ArchitectCanonicalOrgId is not null;
    public bool NoSelectedProponentOrg => Selected is not null && Selected.ProponentCanonicalOrgId is null;
    public bool NoSelectedArchitectOrg => Selected is not null && Selected.ArchitectCanonicalOrgId is null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    public string ProvinceFilter
    {
        get => _provinceFilter;
        set
        {
            if (SetField(ref _provinceFilter, string.IsNullOrWhiteSpace(value) ? AllFilter : value))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    public string StageFilter
    {
        get => _stageFilter;
        set
        {
            if (SetField(ref _stageFilter, string.IsNullOrWhiteSpace(value) ? AllFilter : value))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    public string SectorFilter
    {
        get => _sectorFilter;
        set
        {
            if (SetField(ref _sectorFilter, string.IsNullOrWhiteSpace(value) ? AllFilter : value))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    // Pre-navigation filter slots used by the cross-view filter broker
    // (BdWorkspaceWindow.NavigateToForwardPipelineWithFilter). LoadAsync
    // applies these AFTER the filter-option lists are populated and then
    // clears them so a fresh LoadAsync doesn't double-apply.
    public string? PendingProvinceFilter { get; set; }
    public string? PendingStageFilter { get; set; }
    public string? PendingSectorFilter { get; set; }

    public bool IndigenousOnly
    {
        get => _indigenousOnly;
        set
        {
            if (SetField(ref _indigenousOnly, value))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    public decimal? MinCost
    {
        get => _minCost;
        set
        {
            if (SetField(ref _minCost, value))
            {
                FilteredProjectsView.Refresh();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusBrush = StatusNeutral;
        StatusMessage = "Loading...";
        try
        {
            var options = await _store.GetFilterOptionsAsync(ct).ConfigureAwait(true);
            var rows = await _store.ListAllAsync(ct).ConfigureAwait(true);

            ReplaceOptions(ProvinceFilterOptions, options.Provinces);
            ReplaceOptions(StageFilterOptions, options.Stages);
            ReplaceOptions(SectorFilterOptions, options.Sectors);
            ReplaceOptions(RegionFilterOptions, options.Regions);

            var selectedId = Selected?.Id;
            Projects.Clear();
            foreach (var row in rows)
            {
                Projects.Add(row);
            }
            _contextSnapshot = Projects.ToArray();

            Selected = selectedId.HasValue
                ? Projects.FirstOrDefault(p => p.Id == selectedId.Value)
                : Projects.FirstOrDefault();
            FilteredProjectsView.Refresh();

            var byProvince = Projects
                .GroupBy(p => p.Province)
                .OrderBy(g => ProvinceOrder(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Count():N0} {g.Key}");
            StatusMessage = $"{Projects.Count:N0} projects - {string.Join(", ", byProvince)}";

            // Apply any filter values that were set before LoadAsync was
            // called (e.g. dashboard bar-chart drill-down). Setting the
            // filter triggers FilteredProjectsView.Refresh() via the existing
            // property setters; clear the slots so a subsequent reload via
            // the Refresh button doesn't re-stamp them.
            ApplyPendingFilters();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusBrush = StatusError;
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "Major Projects Inventory load failed.");
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyPendingFilters()
    {
        if (PendingProvinceFilter is { Length: > 0 } p)
        {
            ProvinceFilter = p;
            PendingProvinceFilter = null;
        }

        if (PendingStageFilter is { Length: > 0 } s)
        {
            StageFilter = s;
            PendingStageFilter = null;
        }

        if (PendingSectorFilter is { Length: > 0 } se)
        {
            SectorFilter = se;
            PendingSectorFilter = null;
        }
    }

    private bool ProjectFilterPredicate(object obj)
    {
        if (obj is not MajorProjectRow row)
        {
            return false;
        }

        if (!IsAll(ProvinceFilter) && !string.Equals(row.Province, ProvinceFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAll(StageFilter)
            && !string.Equals(row.ProjectStage, StageFilter, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(row.Stage, StageFilter, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(row.ProjectStatus, StageFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAll(SectorFilter) && !string.Equals(row.Sector, SectorFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IndigenousOnly && row.IndigenousInd != true)
        {
            return false;
        }

        if (MinCost.HasValue && (!row.EstimatedCostCad.HasValue || row.EstimatedCostCad.Value < MinCost.Value))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var needle = SearchText.Trim();
        return Contains(row.ProjectName, needle)
            || Contains(row.ProponentName, needle)
            || Contains(row.ArchitectName, needle)
            || Contains(row.MunicipalityName, needle);
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool IsAll(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, AllFilter, StringComparison.OrdinalIgnoreCase);

    private static int ProvinceOrder(string province)
        => province.ToUpperInvariant() switch
        {
            "BC" => 0,
            "AB" => 1,
            _ => 2,
        };

    private static void ReplaceOptions(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        target.Add(AllFilter);
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            target.Add(value);
        }
    }

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public string ProviderName => Provider;
    public bool HasData => _contextSnapshot.Count > 0;

    public string BuildContext()
    {
        // BuildContext runs on AppAiContextBuilder's worker thread. Read the
        // immutable UI-thread snapshot instead of enumerating the live view.
        var projects = _contextSnapshot;

        var sb = new StringBuilder();
        sb.AppendLine($"Total major projects tracked: {projects.Count:N0}.");

        AppendCounts(sb, "By province:", projects.GroupBy(p => p.Province));
        AppendCounts(sb, "By stage:", projects.GroupBy(p => string.IsNullOrWhiteSpace(p.StageDisplay) ? "(blank)" : p.StageDisplay));
        AppendCounts(sb, "By sector:", projects.GroupBy(p => string.IsNullOrWhiteSpace(p.Sector) ? "(blank)" : p.Sector!));

        var top = projects
            .Where(p => p.EstimatedCostCad.HasValue)
            .OrderByDescending(p => p.EstimatedCostCad!.Value)
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Top 25 by estimated cost:");
            foreach (var p in top)
            {
                sb.AppendLine($"  {p.CostDisplay} - {p.ProjectName} ({p.Province}, {p.StageDisplay}); proponent {Blank(p.ProponentName)}; architect {Blank(p.ArchitectName)}");
            }
        }

        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        var p = Selected;
        if (p is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Selected major project: {p.ProjectName}");
        sb.AppendLine($"Province: {p.Province}");
        sb.AppendLine($"Region / municipality: {Blank(p.RegionName)} / {Blank(p.MunicipalityName)}");
        sb.AppendLine($"Stage: {Blank(p.StageDisplay)}; status: {Blank(p.ProjectStatus)}");
        sb.AppendLine($"Cost: {Blank(p.CostDisplay)}");
        sb.AppendLine($"Sector: {Blank(p.Sector)}; sub-sector: {Blank(p.SubSector)}; category: {Blank(p.ProjectCategoryName)}");
        sb.AppendLine($"Proponent/developer: {Blank(p.ProponentName)} (CanonicalOrgId {p.ProponentCanonicalOrgId?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        sb.AppendLine($"Architect: {Blank(p.ArchitectName)} (CanonicalOrgId {p.ArchitectCanonicalOrgId?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        sb.AppendLine($"Timeline: {Blank(p.TimelineDisplay)}");
        sb.AppendLine($"Funding: {Blank(p.FundingDisplay)}");
        if (p.IsIndigenous)
        {
            sb.AppendLine($"Indigenous names: {Blank(p.IndigenousNames)}");
        }

        if (p.ConstructionJobs.HasValue || p.OperatingJobs.HasValue)
        {
            sb.AppendLine($"Jobs: construction {p.ConstructionJobs?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown"}, operating {p.OperatingJobs?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown"}");
        }

        if (!string.IsNullOrWhiteSpace(p.ProjectDescription))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(p.ProjectDescription);
        }

        if (!string.IsNullOrWhiteSpace(p.SourceUrl))
        {
            sb.AppendLine($"Source: {p.SourceUrl}");
        }

        if (!string.IsNullOrWhiteSpace(p.ProjectWebsite))
        {
            sb.AppendLine($"Project website: {p.ProjectWebsite}");
        }

        return sb.ToString();
    }

    private static void AppendCounts<T>(StringBuilder sb, string title, IEnumerable<IGrouping<T, MajorProjectRow>> groups)
    {
        var ordered = groups
            .Select(g =>
            {
                object? key = g.Key;
                return new { Key = key?.ToString() ?? "(blank)", Count = g.Count() };
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        sb.AppendLine(title);
        foreach (var g in ordered)
        {
            sb.AppendLine($"  {g.Key}: {g.Count:N0}");
        }
    }

    private static string Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
