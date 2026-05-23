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

    private string? _agentVendorProfile;
    public string? AgentVendorProfile
    {
        get => _agentVendorProfile;
        set
        {
            _agentVendorProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAgentProfile));
        }
    }

    private string? _agentCompetitionNotes;
    public string? AgentCompetitionNotes { get => _agentCompetitionNotes; set { _agentCompetitionNotes = value; OnPropertyChanged(); } }

    private bool? _agentCompetesWithKor;
    public bool? AgentCompetesWithKor { get => _agentCompetesWithKor; set { _agentCompetesWithKor = value; OnPropertyChanged(); } }

    private DateTimeOffset? _agentEnrichedAtUtc;
    public DateTimeOffset? AgentEnrichedAtUtc { get => _agentEnrichedAtUtc; set { _agentEnrichedAtUtc = value; OnPropertyChanged(); } }

    public bool HasAgentProfile => !string.IsNullOrWhiteSpace(_agentVendorProfile);

    private string? _agentVendorWebsite;
    public string? AgentVendorWebsite
    {
        get => _agentVendorWebsite;
        set
        {
            _agentVendorWebsite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVendorWebsite));
        }
    }

    public bool HasVendorWebsite => !string.IsNullOrWhiteSpace(_agentVendorWebsite);

    private string? _agentVendorHqLocation;
    public string? AgentVendorHqLocation { get => _agentVendorHqLocation; set { _agentVendorHqLocation = value; OnPropertyChanged(); } }

    private string? _agentVendorSizeBand;
    public string? AgentVendorSizeBand { get => _agentVendorSizeBand; set { _agentVendorSizeBand = value; OnPropertyChanged(); } }

    private int? _agentVendorFoundedYear;
    public int? AgentVendorFoundedYear { get => _agentVendorFoundedYear; set { _agentVendorFoundedYear = value; OnPropertyChanged(); } }

    public ObservableCollection<string> AgentVendorSpecialties { get; } = new();
    public ObservableCollection<VendorLeader> AgentVendorLeadership { get; } = new();

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
              AgentVendorProfile = p.AgentVendorProfile;
              AgentCompetitionNotes = p.AgentCompetitionNotes;
              AgentCompetesWithKor = p.AgentCompetesWithKor;
              AgentEnrichedAtUtc = p.AgentEnrichedAtUtc;
              AgentVendorWebsite = p.AgentVendorWebsite;
              AgentVendorHqLocation = p.AgentVendorHqLocation;
              AgentVendorSizeBand = p.AgentVendorSizeBand;
              AgentVendorFoundedYear = p.AgentVendorFoundedYear;
              AgentVendorSpecialties.Clear(); foreach (var s in p.AgentVendorSpecialties) AgentVendorSpecialties.Add(s);
              AgentVendorLeadership.Clear(); foreach (var l in p.AgentVendorLeadership) AgentVendorLeadership.Add(l);
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
