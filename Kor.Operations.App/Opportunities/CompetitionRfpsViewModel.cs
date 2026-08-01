#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitionRfpsViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Historical-RFPs filters + result counts to AI.
    public string ProviderName => "BD Competition RFPs";
    public bool HasData => true;
    public string BuildContext()
        => $"BD Competition Historical RFPs — {Rows.Count:N0} rows shown"
           + (KorRelevantOnly ? " (KOR-markets filter ON)" : " (all markets)")
           + (string.IsNullOrWhiteSpace(Keyword) ? "" : $"; keyword: {Keyword}")
           + (SelectedYear is { } year ? $"; year: {year}" : "")
           + (string.IsNullOrWhiteSpace(SelectedProvince) ? "" : $"; province: {SelectedProvince}")
           + (string.IsNullOrWhiteSpace(SelectedStatus) ? "" : $"; status filter: {SelectedStatus}")
           + (MinEstimatedValue is { } min ? $"; min est. value: {min:C0}" : "")
           + (HasDocumentsOnly ? "; has-documents only" : "")
           + $"; status: {StatusText}";
    public string BuildLocalContext() => BuildContext();

    private readonly ICompetitionInfoQueryStore _store;
    private readonly ILogger<CompetitionRfpsViewModel> _logger;

    public CompetitionRfpsViewModel(
        ICompetitionInfoQueryStore store,
        ILogger<CompetitionRfpsViewModel> logger)
    {
        _store = store;
        _logger = logger;
        Rows = new ObservableCollection<HistoricalOpportunityListing>();
        Years = new ObservableCollection<int?>();
        Provinces = new ObservableCollection<string?>();
        Statuses = new ObservableCollection<string?>();
        ApplyCommand = new RelayCommand(_ => _ = ReloadAsync());
    }

    public ObservableCollection<HistoricalOpportunityListing> Rows { get; }
    public ObservableCollection<int?> Years { get; }
    public ObservableCollection<string?> Provinces { get; }
    public ObservableCollection<string?> Statuses { get; }

    private string? _keyword;
    public string? Keyword { get => _keyword; set { _keyword = value; OnPropertyChanged(); } }

    private int? _selectedYear;
    public int? SelectedYear { get => _selectedYear; set { _selectedYear = value; OnPropertyChanged(); } }

    private string? _selectedProvince;
    public string? SelectedProvince { get => _selectedProvince; set { _selectedProvince = value; OnPropertyChanged(); } }

    private string? _selectedStatus;
    public string? SelectedStatus { get => _selectedStatus; set { _selectedStatus = value; OnPropertyChanged(); } }

    private decimal? _minEstimatedValue;
    public decimal? MinEstimatedValue { get => _minEstimatedValue; set { _minEstimatedValue = value; OnPropertyChanged(); } }

    private bool _hasDocumentsOnly;
    public bool HasDocumentsOnly { get => _hasDocumentsOnly; set { _hasDocumentsOnly = value; OnPropertyChanged(); } }

    // Round 54: KOR-relevance toggle. Default ON so the first paint shows
    // the BC/AB/WA/OR/CA slice (~the markets KOR actually pursues). Users
    // who want the full uncurated archive untick it.
    private bool _korRelevantOnly = true;
    public bool KorRelevantOnly { get => _korRelevantOnly; set { _korRelevantOnly = value; OnPropertyChanged(); } }

    private string _statusText = "Loading";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private bool _busy;
    public bool Busy { get => _busy; set { _busy = value; OnPropertyChanged(); } }

    public ICommand ApplyCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            var facets = await _store.GetFacetsAsync(CancellationToken.None).ConfigureAwait(true);
            Years.Clear(); Years.Add(null); foreach (var y in facets.Years) Years.Add(y);
            Provinces.Clear(); Provinces.Add(null); foreach (var p in facets.Provinces) Provinces.Add(p);
            Statuses.Clear(); Statuses.Add(null); foreach (var s in facets.Statuses) Statuses.Add(s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading facets failed.");
            StatusText = "Loading filter options failed: " + ex.Message;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    public async Task ReloadAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var filter = new CompetitionInfoFilter
            {
                KeywordLike = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
                Year = SelectedYear,
                Province = SelectedProvince,
                HistoricalStatus = SelectedStatus,
                MinEstimatedValue = MinEstimatedValue,
                HasDocuments = HasDocumentsOnly ? true : (bool?)null,
                KorRelevantOnly = KorRelevantOnly ? true : (bool?)null,
                MaxRows = 15_000,
            };

            var rows = await _store.ListAsync(filter, CancellationToken.None).ConfigureAwait(true);
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            var scope = KorRelevantOnly ? "KOR markets" : "all markets";
            StatusText = $"{rows.Count:N0} rows ({scope})"
                + (rows.Count >= 15_000 ? " — capped at 15,000; narrow filters to see more" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading competition info failed.");
            StatusText = "Loading failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _exec;
        public RelayCommand(Action<object?> exec) { _exec = exec; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _exec(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
