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

public sealed class BuyerProfileViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Buyer Profile state to AI.
    public string ProviderName => "BD Buyer Profile";
    public bool HasData => !string.IsNullOrWhiteSpace(_buyerName);
    public string BuildContext()
        => $"BD Buyer Profile — buyer: {BuyerName}; lifetime: {LifetimeCount:N0} contracts / {LifetimeValue:C0}"
           + (AvgContractValue is { } avg ? $"; avg contract: {avg:C0}" : "")
           + (string.IsNullOrWhiteSpace(ActiveWindow) ? "" : $"; active window: {ActiveWindow}")
           + $"; top winners shown: {TopWinners.Count:N0}; recent awards shown: {RecentAwards.Count:N0}.";
    public string BuildLocalContext() => BuildContext();

    private readonly IVendorAnalyticsStore _store;
    private readonly ILogger<BuyerProfileViewModel> _logger;

    public BuyerProfileViewModel(IVendorAnalyticsStore store, ILogger<BuyerProfileViewModel> logger)
    {
        _store = store;
        _logger = logger;
        ByYear = new ObservableCollection<YearBucket>();
        TopWinners = new ObservableCollection<OrgRollup>();
        BySource = new ObservableCollection<OrgRollup>();
        RecentAwards = new ObservableCollection<AwardListing>();
    }

    public ObservableCollection<YearBucket> ByYear { get; }
    public ObservableCollection<OrgRollup> TopWinners { get; }
    public ObservableCollection<OrgRollup> BySource { get; }
    public ObservableCollection<AwardListing> RecentAwards { get; }

    private string _buyerName = "";
    public string BuyerName { get => _buyerName; set { _buyerName = value; OnPropertyChanged(); } }

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

    public async Task LoadAsync(string buyerName)
    {
        BuyerName = buyerName;
        try
        {
            var p = await _store.GetBuyerProfileAsync(buyerName, CancellationToken.None).ConfigureAwait(true);
            LifetimeValue = p.LifetimeValue;
            LifetimeCount = p.LifetimeCount;
            AvgContractValue = p.AvgContractValue;
            ActiveWindow = p.FirstAwardAtUtc.HasValue && p.LastAwardAtUtc.HasValue
                ? $"{p.FirstAwardAtUtc.Value:yyyy-MM-dd}  {p.LastAwardAtUtc.Value:yyyy-MM-dd}"
                : "";
            ByYear.Clear(); foreach (var y in p.ByYear) ByYear.Add(y);
            TopWinners.Clear(); foreach (var w in p.TopWinners) TopWinners.Add(w);
            BySource.Clear(); foreach (var s in p.BySource) BySource.Add(s);
            RecentAwards.Clear(); foreach (var a in p.RecentAwards) RecentAwards.Add(a);
            StatusText = $"{LifetimeCount:N0} contracts  {LifetimeValue:C0} lifetime";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading buyer profile failed for {Buyer}.", buyerName);
            StatusText = "Failed: " + ex.Message;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
