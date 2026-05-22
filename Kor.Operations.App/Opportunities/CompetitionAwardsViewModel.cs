#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitionAwardsViewModel : INotifyPropertyChanged
{
    private readonly IAwardQueryStore _store;
    private readonly ILogger<CompetitionAwardsViewModel> _logger;

    public CompetitionAwardsViewModel(IAwardQueryStore store, ILogger<CompetitionAwardsViewModel> logger)
    {
        _store = store;
        _logger = logger;
        Rows = new ObservableCollection<AwardListing>();
        Years = new ObservableCollection<int?>();
        Sources = new ObservableCollection<string?>();
        ApplyCommand = new RelayCommand(_ => _ = ReloadAsync());
    }

    public ObservableCollection<AwardListing> Rows { get; }
    public ObservableCollection<int?> Years { get; }
    public ObservableCollection<string?> Sources { get; }

    private string? _keyword;
    public string? Keyword { get => _keyword; set { _keyword = value; OnPropertyChanged(); } }

    private string? _vendor;
    public string? Vendor { get => _vendor; set { _vendor = value; OnPropertyChanged(); } }

    private int? _selectedYear;
    public int? SelectedYear { get => _selectedYear; set { _selectedYear = value; OnPropertyChanged(); } }

    private string? _selectedSource;
    public string? SelectedSource { get => _selectedSource; set { _selectedSource = value; OnPropertyChanged(); } }

    private decimal? _minContractValue;
    public decimal? MinContractValue { get => _minContractValue; set { _minContractValue = value; OnPropertyChanged(); } }

    private bool _competesWithKorOnly;
    public bool CompetesWithKorOnly { get => _competesWithKorOnly; set { _competesWithKorOnly = value; OnPropertyChanged(); } }

    private string _statusText = "Loading";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private bool _busy;
    public bool Busy { get => _busy; set { _busy = value; OnPropertyChanged(); } }

    public ICommand ApplyCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            var f = await _store.GetFacetsAsync(CancellationToken.None).ConfigureAwait(true);
            Years.Clear(); Years.Add(null); foreach (var y in f.Years) Years.Add(y);
            Sources.Clear(); Sources.Add(null); foreach (var s in f.SourceNames) Sources.Add(s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading award facets failed.");
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
            var filter = new AwardQueryFilter
            {
                KeywordLike = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
                VendorLike = string.IsNullOrWhiteSpace(Vendor) ? null : Vendor,
                Year = SelectedYear,
                SourceName = SelectedSource,
                MinContractValue = MinContractValue,
                MaxRows = 5000,
                CompetesWithKorOnly = CompetesWithKorOnly ? true : (bool?)null,
            };
            var rows = await _store.ListAsync(filter, CancellationToken.None).ConfigureAwait(true);
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            StatusText = $"{rows.Count:N0} awards"
                + (rows.Count == 5000 ? " (capped at 5,000  narrow filters to see more)" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading awards failed.");
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
