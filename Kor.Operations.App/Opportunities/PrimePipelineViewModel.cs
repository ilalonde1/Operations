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

public sealed class PrimePipelineViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Prime Pipeline (BD)";
    public const string AllFilter = "All";

    private static readonly Brush StatusNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));
    private static readonly Brush StatusError = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));

    private readonly IPrimePipelineStore _store;
    private readonly ILogger<PrimePipelineViewModel> _logger;
    private PrimePipelineRow? _selected;
    private string _searchText = string.Empty;
    private string _pipelineTypeFilter = AllFilter;
    private string _provinceFilter = AllFilter;
    private string _statusMessage = "Ready.";
    private string _headerCountLabel = "0 of 0 prime jobs";
    private Brush _statusBrush = StatusNeutral;
    private bool _isLoading;
    private IReadOnlyList<PrimePipelineRow> _contextSnapshot = Array.Empty<PrimePipelineRow>();

    public PrimePipelineViewModel(
        IPrimePipelineStore store,
        ILogger<PrimePipelineViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        FilteredPrimePipelineView = CollectionViewSource.GetDefaultView(Jobs);
        FilteredPrimePipelineView.Filter = FilterPredicate;
    }

    public ObservableCollection<PrimePipelineRow> Jobs { get; } = new();
    public ICollectionView FilteredPrimePipelineView { get; }
    public ObservableCollection<string> PipelineTypeFilterOptions { get; } = new() { AllFilter, "Open RFP", "Pipeline Project" };
    public ObservableCollection<string> ProvinceFilterOptions { get; } = new() { AllFilter };

    public PrimePipelineRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelected));
                OnPropertyChanged(nameof(HasSelectedSourceUrl));
            }
        }
    }

    public bool HasSelected => Selected is not null;
    public bool HasSelectedSourceUrl => !string.IsNullOrWhiteSpace(Selected?.SourceUrl);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                RefreshFilters();
            }
        }
    }

    public string PipelineTypeFilter
    {
        get => _pipelineTypeFilter;
        set
        {
            if (SetField(ref _pipelineTypeFilter, string.IsNullOrWhiteSpace(value) ? AllFilter : value))
            {
                RefreshFilters();
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
                RefreshFilters();
            }
        }
    }

    public string HeaderCountLabel
    {
        get => _headerCountLabel;
        private set => SetField(ref _headerCountLabel, value);
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
            var rows = await _store.GetAllAsync(ct).ConfigureAwait(true);
            Jobs.Clear();
            foreach (var row in rows)
            {
                Jobs.Add(row);
            }

            _contextSnapshot = Jobs.ToArray();
            ReplaceProvinces(rows);

            Selected = Jobs.FirstOrDefault();
            RefreshFilters();
            StatusMessage = $"{Jobs.Count:N0} prime jobs loaded.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusBrush = StatusError;
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "Prime Pipeline load failed.");
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not PrimePipelineRow row)
        {
            return false;
        }

        if (!IsAll(PipelineTypeFilter) && !string.Equals(row.PipelineType, PipelineTypeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAll(ProvinceFilter) && !string.Equals(row.Province, ProvinceFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var needle = SearchText.Trim();
        return Contains(row.ProjectName, needle)
            || Contains(row.BuyerOrOwner, needle)
            || Contains(row.Sector, needle)
            || Contains(row.ArchitectName, needle);
    }

    private void RefreshFilters()
    {
        FilteredPrimePipelineView.Refresh();
        UpdateHeaderCount();
    }

    private void UpdateHeaderCount()
    {
        var filtered = FilteredPrimePipelineView.Cast<PrimePipelineRow>().Count();
        HeaderCountLabel = $"{filtered:N0} of {Jobs.Count:N0} prime jobs";
    }

    private void ReplaceProvinces(IReadOnlyList<PrimePipelineRow> rows)
    {
        ProvinceFilterOptions.Clear();
        ProvinceFilterOptions.Add(AllFilter);
        foreach (var province in rows
            .Select(r => r.Province)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            ProvinceFilterOptions.Add(province!);
        }
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrWhiteSpace(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool IsAll(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, AllFilter, StringComparison.OrdinalIgnoreCase);

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public string ProviderName => Provider;
    public bool HasData => _contextSnapshot.Count > 0;

    public string BuildContext()
    {
        var jobs = _contextSnapshot;
        var sb = new StringBuilder();
        sb.AppendLine($"Total prime pipeline jobs tracked: {jobs.Count:N0}.");
        AppendCounts(sb, "By pipeline type:", jobs.GroupBy(j => Blank(j.PipelineType)));
        AppendCounts(sb, "By province:", jobs.GroupBy(j => Blank(j.Province)));
        AppendCounts(sb, "By sector:", jobs.GroupBy(j => Blank(j.Sector)));

        var top = jobs
            .Where(j => j.EstimatedValueCad.HasValue)
            .OrderByDescending(j => j.EstimatedValueCad!.Value)
            .ThenBy(j => j.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Top 25 by estimated value:");
            foreach (var row in top)
            {
                sb.AppendLine($"  {row.CostDisplay} - {row.ProjectName} ({Blank(row.PipelineType)}, {Blank(row.Province)}); owner {Blank(row.BuyerOrOwner)}; architect {Blank(row.ArchitectName)}");
            }
        }

        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        var row = Selected;
        if (row is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Selected prime pipeline job: {row.ProjectName}");
        sb.AppendLine($"Type: {row.PipelineType}; source ref: {row.SourceRef}");
        sb.AppendLine($"Buyer/owner: {Blank(row.BuyerOrOwner)}");
        sb.AppendLine($"Sector: {Blank(row.Sector)}; stage: {Blank(row.Stage)}");
        sb.AppendLine($"Estimated value: {Blank(row.CostDisplay)}");
        sb.AppendLine($"Location: {Blank(row.LocationDisplay)}");
        sb.AppendLine($"Architect: {Blank(row.ArchitectName)}");
        if (!string.IsNullOrWhiteSpace(row.SourceUrl))
        {
            sb.AppendLine($"Source: {row.SourceUrl}");
        }

        return sb.ToString();
    }

    private static void AppendCounts<T>(StringBuilder sb, string title, IEnumerable<IGrouping<T, PrimePipelineRow>> groups)
    {
        var list = groups
            .Select(g => new { Name = g.Key?.ToString() ?? "(blank)", Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.AppendLine(title);
        foreach (var item in list)
        {
            sb.AppendLine($"  {item.Name}: {item.Count:N0}");
        }
    }

    private static string Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();
}
