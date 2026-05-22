#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitorProfileViewModel : INotifyPropertyChanged
{
    private readonly IVendorAnalyticsStore _store;
    private readonly ILogger<CompetitorProfileViewModel> _logger;

    public CompetitorProfileViewModel(IVendorAnalyticsStore store, ILogger<CompetitorProfileViewModel> logger)
    {
        _store = store;
        _logger = logger;
        ByYear = new ObservableCollection<YearBucket>();
        TopBuyers = new ObservableCollection<OrgRollup>();
        BySource = new ObservableCollection<OrgRollup>();
        RecentWins = new ObservableCollection<AwardListing>();
    }

    public ObservableCollection<YearBucket> ByYear { get; }
    public ObservableCollection<OrgRollup> TopBuyers { get; }
    public ObservableCollection<OrgRollup> BySource { get; }
    public ObservableCollection<AwardListing> RecentWins { get; }

    private string _vendorName = "";
    public string VendorName { get => _vendorName; set { _vendorName = value; OnPropertyChanged(); } }

    private decimal _lifetimeValue;
    public decimal LifetimeValue { get => _lifetimeValue; set { _lifetimeValue = value; OnPropertyChanged(); } }

    private int _lifetimeCount;
    public int LifetimeCount { get => _lifetimeCount; set { _lifetimeCount = value; OnPropertyChanged(); } }

    private decimal? _avgContractValue;
    public decimal? AvgContractValue { get => _avgContractValue; set { _avgContractValue = value; OnPropertyChanged(); } }

    private string _activeWindow = "";
    public string ActiveWindow { get => _activeWindow; set { _activeWindow = value; OnPropertyChanged(); } }

    private string _statusText = "Loading";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public async Task LoadAsync(string vendorName)
    {
        VendorName = vendorName;
        try
        {
            var p = await _store.GetCompetitorProfileAsync(vendorName, CancellationToken.None).ConfigureAwait(true);
            LifetimeValue = p.LifetimeValue;
            LifetimeCount = p.LifetimeCount;
            AvgContractValue = p.AvgContractValue;
            ActiveWindow = p.FirstWinAtUtc.HasValue && p.LastWinAtUtc.HasValue
                ? $"{p.FirstWinAtUtc.Value:yyyy-MM-dd}  {p.LastWinAtUtc.Value:yyyy-MM-dd}"
                : "";
            ByYear.Clear(); foreach (var y in p.ByYear) ByYear.Add(y);
            TopBuyers.Clear(); foreach (var b in p.TopBuyers) TopBuyers.Add(b);
            BySource.Clear(); foreach (var s in p.BySource) BySource.Add(s);
            RecentWins.Clear(); foreach (var w in p.RecentWins) RecentWins.Add(w);
            StatusText = $"{LifetimeCount:N0} contracts  {LifetimeValue:C0} lifetime";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading competitor profile failed for {Vendor}.", vendorName);
            StatusText = "Failed: " + ex.Message;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
