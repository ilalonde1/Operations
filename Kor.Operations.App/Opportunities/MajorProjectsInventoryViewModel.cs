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

public enum PipelineFunnel
{
    None = 0,
    OpenSeats,
    BidWindow,
    Radar,
}

public sealed class MajorProjectsInventoryViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Major Projects Inventory (BD)";
    public const string AllFilter = "All";

    private static readonly Brush StatusNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));
    private static readonly Brush StatusError = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));

    private readonly IMajorProjectsInventoryStore _store;
    private readonly IPursuitLifecycleStore _lifecycleStore;
    private readonly ILogger<MajorProjectsInventoryViewModel> _logger;
    private MajorProjectRow? _selected;
    private string _searchText = string.Empty;
    private string _provinceFilter = AllFilter;
    private string _stageFilter = AllFilter;
    private string _sectorFilter = AllFilter;
    private string _lifecycleFilter = LifecycleActive;
    private PipelineFunnel _funnelFilter = PipelineFunnel.None;
    private bool _indigenousOnly;
    private decimal? _minCost;
    private string _statusMessage = "Ready.";
    private Brush _statusBrush = StatusNeutral;
    private bool _isLoading;
    private IReadOnlyList<MajorProjectRow> _contextSnapshot = Array.Empty<MajorProjectRow>();

    public MajorProjectsInventoryViewModel(
        IMajorProjectsInventoryStore store,
        IPursuitLifecycleStore lifecycleStore,
        ILogger<MajorProjectsInventoryViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lifecycleStore = lifecycleStore ?? throw new ArgumentNullException(nameof(lifecycleStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        FilteredProjectsView = CollectionViewSource.GetDefaultView(Projects);
        FilteredProjectsView.Filter = ProjectFilterPredicate;
    }

    // Lifecycle view chips (migration 284). Active (default) hides removed
    // rows; owned rows stay visible with their owner badge. Removed shows the
    // "not for us" set with who/when/why — nothing is ever deleted.
    public const string LifecycleActive = "Active";
    public const string LifecycleOwned = "Owned";
    public const string LifecycleRemoved = "Removed";

    public IReadOnlyList<string> LifecycleFilterOptions { get; } =
        new[] { LifecycleActive, LifecycleOwned, LifecycleRemoved, AllFilter };

    public string LifecycleFilter
    {
        get => _lifecycleFilter;
        set
        {
            if (SetField(ref _lifecycleFilter, string.IsNullOrWhiteSpace(value) ? LifecycleActive : value))
            {
                FilteredProjectsView.Refresh();
            }
        }
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
                NotifyLifecycleChanged();
            }
        }
    }

    private void NotifyLifecycleChanged()
    {
        OnPropertyChanged(nameof(CanOwnSelected));
        OnPropertyChanged(nameof(CanDismissSelected));
        OnPropertyChanged(nameof(CanReleaseSelected));
        OnPropertyChanged(nameof(CanRestoreSelected));
        OnPropertyChanged(nameof(SelectedLifecycleDisplay));
        OnPropertyChanged(nameof(HasSelectedLifecycleNote));
    }

    // Lifecycle affordances for the selected row (migration 284).
    public bool CanOwnSelected => Selected is { OwnerStaffId: null, DismissedAtUtc: null };
    public bool CanDismissSelected => Selected is { DismissedAtUtc: null };
    public bool CanReleaseSelected => Selected is { OwnerStaffId: not null };
    public bool CanRestoreSelected => Selected is { DismissedAtUtc: not null };
    public bool HasSelectedLifecycleNote => !string.IsNullOrWhiteSpace(SelectedLifecycleDisplay);

    /// <summary>Owner / removed badge for the detail pane, e.g.
    /// "Owned by ian since Jul 13" or "Removed by jim Jul 12 — wrong region".</summary>
    public string SelectedLifecycleDisplay
    {
        get
        {
            if (Selected is null) return string.Empty;
            if (Selected.DismissedAtUtc is { } d)
            {
                var by = ShortUpn(Selected.DismissedBy);
                var reason = string.IsNullOrWhiteSpace(Selected.DismissedReason) ? "" : $" — {Selected.DismissedReason}";
                return $"Removed by {by} {d.ToLocalTime():MMM d}{reason}";
            }
            if (Selected.OwnerStaffId is { } o)
            {
                var since = Selected.OwnedAtUtc?.ToLocalTime().ToString("MMM d") ?? "?";
                return $"Owned by {ShortUpn(o)} since {since}";
            }
            return string.Empty;
        }
    }

    private static string ShortUpn(string? upn)
    {
        if (string.IsNullOrWhiteSpace(upn)) return "?";
        var at = upn.IndexOf('@');
        return at > 0 ? upn[..at] : upn;
    }

    /// <summary>Own the selected play: it leaves the shared boards + weekly
    /// sheet and enters the owner's digest. Auto-released after 14 days unless
    /// converted to a pursuit (the digest warns from day 10).</summary>
    public Task<bool> OwnSelectedAsync(string staffUpn, CancellationToken ct)
        => ApplyLifecycleAsync(r => _lifecycleStore.OwnProjectAsync(r.Id, staffUpn, ct),
            r => r with { OwnerStaffId = staffUpn, OwnedAtUtc = DateTimeOffset.UtcNow },
            "Owned — it's yours now. Convert it to a pursuit within 14 days or it returns to the pool.");

    public Task<bool> ReleaseSelectedAsync(string staffUpn, CancellationToken ct)
        => ApplyLifecycleAsync(r => _lifecycleStore.ReleaseProjectAsync(r.Id, staffUpn, ct),
            r => r with { OwnerStaffId = null, OwnedAtUtc = null },
            "Released back to the shared pool.");

    public Task<bool> DismissSelectedAsync(string staffUpn, string reason, CancellationToken ct)
        => ApplyLifecycleAsync(r => _lifecycleStore.DismissProjectAsync(r.Id, staffUpn, reason, ct),
            r => r with { DismissedAtUtc = DateTimeOffset.UtcNow, DismissedBy = staffUpn, DismissedReason = reason, OwnerStaffId = null, OwnedAtUtc = null },
            "Removed — visible under the Removed view, restorable any time.");

    public Task<bool> RestoreSelectedAsync(string staffUpn, CancellationToken ct)
        => ApplyLifecycleAsync(r => _lifecycleStore.RestoreProjectAsync(r.Id, staffUpn, ct),
            r => r with { DismissedAtUtc = null, DismissedBy = null, DismissedReason = null },
            "Restored to the actionable pool.");

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusBrush = isError ? StatusError : StatusNeutral;
    }

    private async Task<bool> ApplyLifecycleAsync(
        Func<MajorProjectRow, Task<LifecycleOutcome>> action,
        Func<MajorProjectRow, MajorProjectRow> localUpdate,
        string successMessage)
    {
        var row = Selected;
        if (row is null) return false;

        try
        {
            var outcome = await action(row).ConfigureAwait(true);
            if (outcome != LifecycleOutcome.Applied)
            {
                SetStatus("That row changed under you (someone else acted first) — refresh to see its current state.", isError: true);
                return false;
            }

            var updated = localUpdate(row);
            var index = Projects.IndexOf(row);
            if (index >= 0)
            {
                Projects[index] = updated;
            }
            Selected = updated;
            FilteredProjectsView.Refresh();
            SetStatus(successMessage, isError: false);
            NotifyLifecycleChanged();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MPI lifecycle action failed for {Id}", row.Id);
            SetStatus($"Action failed: {ex.Message}", isError: true);
            return false;
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

    public PipelineFunnel FunnelFilter
    {
        get => _funnelFilter;
        set
        {
            if (SetField(ref _funnelFilter, value))
            {
                OnPropertyChanged(nameof(FunnelFilterDisplay));
                OnPropertyChanged(nameof(HasFunnelFilter));
                FilteredProjectsView.Refresh();
            }
        }
    }

    public bool HasFunnelFilter => _funnelFilter != PipelineFunnel.None;

    public string FunnelFilterDisplay => _funnelFilter switch
    {
        PipelineFunnel.OpenSeats => "Open Seats",
        PipelineFunnel.BidWindow => "In Bid Window",
        PipelineFunnel.Radar => "Radar",
        _ => string.Empty,
    };

    // Pre-navigation filter slots used by the cross-view filter broker
    // (BdWorkspaceWindow.NavigateToForwardPipelineWithFilter). LoadAsync
    // applies these AFTER the filter-option lists are populated and then
    // clears them so a fresh LoadAsync doesn't double-apply.
    public string? PendingProvinceFilter { get; set; }
    public string? PendingStageFilter { get; set; }
    public string? PendingSectorFilter { get; set; }
    public PipelineFunnel? PendingFunnelFilter { get; set; }

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

        if (PendingFunnelFilter is { } f)
        {
            FunnelFilter = f;
            PendingFunnelFilter = null;
        }
    }

    public void ClearFunnelFilter() => FunnelFilter = PipelineFunnel.None;

    private bool ProjectFilterPredicate(object obj)
    {
        if (obj is not MajorProjectRow row)
        {
            return false;
        }

        // Lifecycle view (migration 284). Active hides removed rows; owned rows
        // stay visible in Active (they're still real pipeline, just claimed).
        var lifecycleMatch = _lifecycleFilter switch
        {
            LifecycleOwned => row.OwnerStaffId is not null,
            LifecycleRemoved => row.DismissedAtUtc is not null,
            AllFilter => true,
            _ => row.DismissedAtUtc is null,
        };
        if (!lifecycleMatch)
        {
            return false;
        }

        if (_funnelFilter != PipelineFunnel.None)
        {
            var match = _funnelFilter switch
            {
                PipelineFunnel.OpenSeats => IsOpenSeat(row),
                PipelineFunnel.BidWindow => IsInBidWindow(row),
                PipelineFunnel.Radar => IsInRadar(row),
                _ => true,
            };
            if (!match)
            {
                return false;
            }
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
            || Contains(row.StructuralEngineerName, needle)
            || Contains(row.GeneralContractorName, needle)
            || Contains(row.KorPipelineTag, needle)
            || Contains(row.ScheduleNotes, needle)
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

    private static readonly string[] RadarSectorKeywords =
    {
        "school", "hospital", "health", "recreation", "civic",
        "cultural", "universit", "college", "library", "communit",
        "education", "housing", "institution", "care", "fire", "police",
    };

    private static readonly string[] RadarSectorExactMatches =
    {
        "Civic", "Tourism / Recreation", "Government", "Mixed-use",
    };

    private static readonly string[] BidWindowStageKeywords =
    {
        "procure", "RFP", "RFQ", "tender", "design",
    };

    private static bool IsInRadar(MajorProjectRow row)
    {
        var sector = row.Sector;
        if (string.IsNullOrWhiteSpace(sector)) return false;
        foreach (var kw in RadarSectorKeywords)
        {
            if (sector.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        foreach (var exact in RadarSectorExactMatches)
        {
            if (string.Equals(sector, exact, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsInBidWindow(MajorProjectRow row)
    {
        if (!IsInRadar(row)) return false;
        // The funnel SQL OR's across ProjectStage/Stage/ProjectStatus
        // implicitly via Stage; here we mirror by checking all three
        // string columns the row model exposes.
        foreach (var src in new[] { row.ProjectStage, row.Stage, row.ProjectStatus })
        {
            if (string.IsNullOrWhiteSpace(src)) continue;
            foreach (var kw in BidWindowStageKeywords)
            {
                if (src.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    private static bool IsOpenSeat(MajorProjectRow row)
    {
        if (!IsInRadar(row)) return false;
        return string.Equals(row.SeatStatus, "likely-open", StringComparison.OrdinalIgnoreCase);
    }

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
                sb.AppendLine($"  {p.CostDisplay} - {p.ProjectName} ({p.Province}, {p.StageDisplay}); proponent {Blank(p.ProponentName)}; architect {Blank(p.ArchitectName)}; structural {Blank(p.StructuralEngineerName)}; GC {Blank(p.GeneralContractorName)}");
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
        sb.AppendLine($"Structural engineer: {Blank(p.StructuralEngineerName)} (CanonicalOrgId {p.StructuralEngineerCanonicalOrgId?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        sb.AppendLine($"General contractor: {Blank(p.GeneralContractorName)} (CanonicalOrgId {p.GeneralContractorCanonicalOrgId?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        sb.AppendLine($"KOR pipeline tag: {Blank(p.KorPipelineTag)}");
        sb.AppendLine($"Schedule notes: {Blank(p.ScheduleNotes)}");
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
