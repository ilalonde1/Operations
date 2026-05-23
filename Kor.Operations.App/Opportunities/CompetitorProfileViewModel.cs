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

    private string? _agentVendorOwnershipStatus;
    public string? AgentVendorOwnershipStatus { get => _agentVendorOwnershipStatus; set { _agentVendorOwnershipStatus = value; OnPropertyChanged(); } }

    private string? _agentVendorParentCompany;
    public string? AgentVendorParentCompany
    {
        get => _agentVendorParentCompany;
        set
        {
            _agentVendorParentCompany = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasParentCompany));
        }
    }

    public bool HasParentCompany => !string.IsNullOrWhiteSpace(_agentVendorParentCompany);

    public ObservableCollection<string> AgentVendorSpecialties { get; } = new();
    public ObservableCollection<VendorLeader> AgentVendorLeadership { get; } = new();
    public ObservableCollection<string> AgentVendorLocations { get; } = new();
    public ObservableCollection<string> AgentVendorCertifications { get; } = new();
    public ObservableCollection<VendorNewsItem> AgentVendorRecentNews { get; } = new();

    private string? _agentVendorLinkedInUrl;
    public string? AgentVendorLinkedInUrl
    {
        get => _agentVendorLinkedInUrl;
        set
        {
            _agentVendorLinkedInUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLinkedInUrl));
        }
    }

    public bool HasLinkedInUrl => !string.IsNullOrWhiteSpace(_agentVendorLinkedInUrl);

    public bool HasRecentNews => AgentVendorRecentNews.Count > 0;

    private int? _agentKorOverlapScore;
    public int? AgentKorOverlapScore
    {
        get => _agentKorOverlapScore;
        set
        {
            _agentKorOverlapScore = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOverlapScore));
            OnPropertyChanged(nameof(OverlapScoreText));
            OnPropertyChanged(nameof(OverlapScoreBrush));
            OnPropertyChanged(nameof(OverlapScoreLabel));
        }
    }

    public bool HasOverlapScore => _agentKorOverlapScore.HasValue;

    public string OverlapScoreText => _agentKorOverlapScore.HasValue ? $"{_agentKorOverlapScore.Value}/10" : "";

    public string OverlapScoreLabel
    {
        get
        {
            var s = _agentKorOverlapScore ?? 0;
            if (s >= 9) return "DIRECT RIVAL";
            if (s >= 7) return "DIRECT COMPETITOR";
            if (s >= 5) return "PARTIAL OVERLAP";
            if (s >= 3) return "ADJACENT";
            return "NOT COMPETING";
        }
    }

    public System.Windows.Media.Brush OverlapScoreBrush
    {
        get
        {
            var s = _agentKorOverlapScore ?? 0;
            if (s >= 9) return System.Windows.Media.Brushes.Crimson;
            if (s >= 7) return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0x6F, 0x00));
            if (s >= 5) return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xA0, 0x00));
            if (s >= 3) return System.Windows.Media.Brushes.SteelBlue;
            return System.Windows.Media.Brushes.Gray;
        }
    }

    private string? _agentContractProjectType;
    public string? AgentContractProjectType { get => _agentContractProjectType; set { _agentContractProjectType = value; OnPropertyChanged(); } }

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
              AgentVendorOwnershipStatus = p.AgentVendorOwnershipStatus;
              AgentVendorParentCompany = p.AgentVendorParentCompany;
              AgentVendorSpecialties.Clear(); foreach (var s in p.AgentVendorSpecialties) AgentVendorSpecialties.Add(s);
              AgentVendorLeadership.Clear(); foreach (var l in p.AgentVendorLeadership) AgentVendorLeadership.Add(l);
              AgentVendorLocations.Clear(); foreach (var l in p.AgentVendorLocations) AgentVendorLocations.Add(l);
              AgentVendorCertifications.Clear(); foreach (var c in p.AgentVendorCertifications) AgentVendorCertifications.Add(c);
              AgentVendorRecentNews.Clear(); foreach (var n in p.AgentVendorRecentNews) AgentVendorRecentNews.Add(n);
              OnPropertyChanged(nameof(HasRecentNews));
              AgentVendorLinkedInUrl = p.AgentVendorLinkedInUrl;
              AgentKorOverlapScore = p.AgentKorOverlapScore;
              AgentContractProjectType = p.AgentContractProjectType;
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
