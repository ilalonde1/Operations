#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// The BD dashboard, after the 2026-10-13 clarity pass: the funnel's three numbers,
/// what is closing, what is new, and the priority-action queue. Two reads instead of
/// six. The pipeline charts and tiles live on the Pipeline window
/// (<see cref="PipelineCharts"/>), open seats on Relationships, competitor watch on
/// Competition.
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // T5.001 audit fix (2026-05-30): every BD VM exposes itself to the AI
    // context builder so the assistant can answer questions grounded in
    // what's actually on screen.
    public string ProviderName => "BD Dashboard";
    public bool HasData => LatestRfps.Count > 0 || Deadlines.Count > 0 || PriorityActions.Count > 0;
    public string BuildContext()
        => $"BD Dashboard — funnel: {OpenSeatCountDisplay} reachable now, {BidWindowCountDisplay} in bidding, {RadarCountDisplay} watching; "
         + $"{Deadlines.Count} RFPs closing in the next 30 days; {LatestRfps.Count} newest RFPs listed; {PriorityActionsCount} open priority actions.";
    public string BuildLocalContext() => BuildContext();

    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    private readonly IOpportunityStore _opportunities;
    private readonly IBdDashboardStore _bdDashboard;
    private readonly IntelReadService _intelRead;
    private readonly IntelPersistenceService _intelPersistence;
    private string _radarCountDisplay = "0";
    private string _bidWindowCountDisplay = "0";
    private string _openSeatCountDisplay = "0";
    private string _statusMessage = "Ready.";

    private readonly Microsoft.Extensions.Logging.ILogger<DashboardViewModel>? _logger;
    private readonly IBdDirectorLedgerStore? _ledger;
    private int _askLoadVersion;
    private string? _askActor;

    public DashboardViewModel(
        IOpportunityStore opportunities,
        IBdDashboardStore bdDashboard,
        IntelReadService intelRead,
        IntelPersistenceService intelPersistence,
        Microsoft.Extensions.Logging.ILogger<DashboardViewModel>? logger = null,
        IBdDirectorLedgerStore? ledger = null)
    {
        _opportunities = opportunities ?? throw new ArgumentNullException(nameof(opportunities));
        _bdDashboard = bdDashboard ?? throw new ArgumentNullException(nameof(bdDashboard));
        _intelRead = intelRead ?? throw new ArgumentNullException(nameof(intelRead));
        _intelPersistence = intelPersistence ?? throw new ArgumentNullException(nameof(intelPersistence));
        _logger = logger;
        _ledger = ledger;
    }

    public sealed record BdAskRow(long Id, string Ask, string? WhyNow, int Minutes, DateTimeOffset DueUtc, string AskedBy);
    public ObservableCollection<BdAskRow> AskedOfMe { get; } = new();
    public bool HasAsks => AskedOfMe.Count > 0;

    public async Task LoadAsksAsync(string actor, CancellationToken ct)
    {
        var version = ++_askLoadVersion;
        if (!string.Equals(_askActor, actor, StringComparison.OrdinalIgnoreCase) || _ledger is null)
        {
            AskedOfMe.Clear();
            OnPropertyChanged(nameof(HasAsks));
        }
        _askActor = actor;
        if (_ledger is null) return;
        try
        {
            var rows = await _ledger.ListOpenAsksForAsync(actor, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (version != _askLoadVersion) return;
            Replace(AskedOfMe, rows.Select(a => new BdAskRow(a.Id, a.Ask, a.WhyNow, a.Minutes, a.DueUtc, a.AskedBy)));
            OnPropertyChanged(nameof(HasAsks));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (version != _askLoadVersion) return;
            _logger?.LogWarning(ex, "Dashboard asks could not be read.");
            StatusMessage = "Could not read asks. Reopen the dashboard to try again.";
        }
    }

    public async Task CloseAskAsync(long id, string status, string? note, string actor)
    {
        if (_ledger is null) return;
        await _ledger.CloseAskAsync(id, status, note, actor, CancellationToken.None).ConfigureAwait(true);
        ++_askLoadVersion;
        var row = AskedOfMe.FirstOrDefault(a => a.Id == id);
        if (row is not null) AskedOfMe.Remove(row);
        OnPropertyChanged(nameof(HasAsks));
    }

    // R82: BD Dashboard Priority Actions queue. Top-25 open IntelAction rows
    // ranked High→Low confidence then most-recently-refreshed. Filterable by
    // Province / ActionType / MinConfidence. Surfaces actionable intel without
    // hunting through individual dossiers.
    public ObservableCollection<PriorityActionRow> PriorityActions { get; } = new();

    private string? _priorityProvinceFilter;
    public string? PriorityProvinceFilter
    {
        get => _priorityProvinceFilter;
        set { if (_priorityProvinceFilter != value) { _priorityProvinceFilter = value; OnPropertyChanged(); } }
    }

    private string? _priorityActionTypeFilter;
    public string? PriorityActionTypeFilter
    {
        get => _priorityActionTypeFilter;
        set { if (_priorityActionTypeFilter != value) { _priorityActionTypeFilter = value; OnPropertyChanged(); } }
    }

    private IntelConfidence? _priorityMinConfidence;
    public IntelConfidence? PriorityMinConfidence
    {
        get => _priorityMinConfidence;
        set { if (_priorityMinConfidence != value) { _priorityMinConfidence = value; OnPropertyChanged(); } }
    }

    public int PriorityActionsCount => PriorityActions.Count;
    public bool HasPriorityActions => PriorityActions.Count > 0;

    public IReadOnlyList<string> PriorityProvinceOptions { get; } = new[] { "All", "BC", "AB", "WA", "OR", "CA" };
    public IReadOnlyList<string> PriorityActionTypeOptions { get; } = new[]
    {
        "All", "PursuitAngle", "ContactStrategy", "TimingWindow",
        "HowToGetOnRoster", "KorDisplacementRead", "Other",
    };
    public IReadOnlyList<string> PriorityConfidenceOptions { get; } = new[] { "All", "High", "Medium", "Low" };

    public async Task LoadPriorityActionsAsync(CancellationToken ct)
    {
        try
        {
            var filter = new PriorityActionFilter(
                Province: NormalizeFilter(PriorityProvinceFilter),
                ActionType: NormalizeFilter(PriorityActionTypeFilter),
                MinConfidence: ParseMinConfidence(PriorityMinConfidence));
            var rows = await _intelRead.GetPriorityActionsAsync(filter, take: 25, ct).ConfigureAwait(true);
            PriorityActions.Clear();
            foreach (var row in rows) PriorityActions.Add(row);
            OnPropertyChanged(nameof(PriorityActionsCount));
            OnPropertyChanged(nameof(HasPriorityActions));
        }
        catch (OperationCanceledException) { /* expected on tab switch */ }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DashboardViewModel: Priority Actions load failed.");
        }

        static string? NormalizeFilter(string? v) =>
            string.IsNullOrWhiteSpace(v) || string.Equals(v, "All", StringComparison.OrdinalIgnoreCase) ? null : v;

        static IntelConfidence? ParseMinConfidence(IntelConfidence? v) => v;
    }

    /// <summary>Mark an action Done or Dismissed and remove it from the queue.
    /// Returns true on success.</summary>
    public async Task<bool> SetActionStatusAsync(long actionId, string status, CancellationToken ct)
    {
        try
        {
            var user = Environment.UserName;
            var ok = await _intelPersistence.SetActionStatusAsync(actionId, status, user, ct).ConfigureAwait(true);
            if (ok)
            {
                for (var i = PriorityActions.Count - 1; i >= 0; i--)
                {
                    if (PriorityActions[i].ActionId == actionId)
                    {
                        PriorityActions.RemoveAt(i);
                        break;
                    }
                }
                OnPropertyChanged(nameof(PriorityActionsCount));
                OnPropertyChanged(nameof(HasPriorityActions));
            }
            return ok;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DashboardViewModel: SetActionStatus failed for action {ActionId}.", actionId);
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Rows carry the Opportunity.Id so a double-click opens the RFP read view.
    public sealed record FeedRow(long OpportunityId, string Name, string Buyer, string? Province, string Status, string? Deadline);

    internal static DeadlineRow CreateDeadlineRow(Opportunity o, DateTimeOffset now)
        => new(o.Id, Kor.Opportunities.Data.Ingestion.PostingText.DisplayTitle(o.Name), o.BuyerName, o.ProjectProvince,
            OpportunityDeadlineDisplay.Date(o.SubmissionDeadlineUtc),
            o.SubmissionDeadlineUtc is { } d ? OpportunityDeadlineDisplay.Countdown(d, now) : "not specified")
        { IsNew = o.CreatedAtUtc >= now.AddDays(-7), IsUrgent = o.SubmissionDeadlineUtc is { } u && u - now <= TimeSpan.FromDays(3) };

    public sealed record DeadlineRow(long OpportunityId, string Name, string Buyer, string? Province, string? Deadline, string Countdown)
    {
        /// <summary>Posted in the last seven days: the list's "New" tag, which replaced the separate New RFPs card.</summary>
        public bool IsNew { get; init; }

        /// <summary>Closes within three days: the only countdown shown in red, so red means "act now".</summary>
        public bool IsUrgent { get; init; }

        /// <summary>The card's second line: buyer · province · local closing time, blanks skipped.</summary>
        public string Detail => string.Join(" · ", new[] { Buyer, Province, Deadline }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    // Loaded into a per-load cache so dashboard click handlers can resolve a
    // FeedRow/DeadlineRow back to the full Opportunity model.
    private readonly Dictionary<long, Opportunity> _oppCache = new();

    public Opportunity? GetOpportunity(long id) =>
        _oppCache.TryGetValue(id, out var o) ? o : null;

    public ObservableCollection<FeedRow> LatestRfps { get; } = new();

    public ObservableCollection<DeadlineRow> Deadlines { get; } = new();

    public bool HasDeadlines => Deadlines.Count > 0;

    public string RadarCountDisplay
    {
        get => _radarCountDisplay;
        private set => SetField(ref _radarCountDisplay, value);
    }

    public string BidWindowCountDisplay
    {
        get => _bidWindowCountDisplay;
        private set => SetField(ref _bidWindowCountDisplay, value);
    }

    public string OpenSeatCountDisplay
    {
        get => _openSeatCountDisplay;
        private set => SetField(ref _openSeatCountDisplay, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading BD dashboard...";

            var oppsTask = _opportunities.ListAsync(ct, includeClosed: false, includeNonPrime: false);
            var funnelTask = _bdDashboard.GetPipelineFunnelAsync(ct);
            await Task.WhenAll(oppsTask, funnelTask).ConfigureAwait(true);
            var opps = oppsTask.Result;
            var funnel = funnelTask.Result;
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            var thirtyDays = now.AddDays(30);

            RadarCountDisplay = funnel.RadarCount.ToString("N0", CanadianCulture);
            BidWindowCountDisplay = funnel.BidWindowCount.ToString("N0", CanadianCulture);
            OpenSeatCountDisplay = funnel.OpenSeatCount.ToString("N0", CanadianCulture);

            _oppCache.Clear();
            foreach (var o in opps)
            {
                _oppCache[o.Id] = o;
            }

            // A removed record (DismissedAtUtc set: a human "not for us", or
            // BdOpportunityDedup retiring a duplicate) stays in the cache for deep
            // links but is neither news nor closing. 2026-10-14: the top row of
            // "Closing soon" was a retired duplicate whose postings live on its twin.
            // 2026-10-20: only work a structural firm bids on (KorBidFit) reaches the first screen; the prime flag alone let
            // geotechnical, commissioning, electrical and construction-management tenders through. The rest stay in Bids
            // behind "Show".
            var live = opps.Where(o => o.DismissedAtUtc is null && Kor.Opportunities.Data.Ingestion.KorBidFit.IsKorWork(o.Name)).ToList();

            Replace(LatestRfps, live
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(15)
                .Select(o => new FeedRow(
                    o.Id,
                    o.Name,
                    o.BuyerName,
                    o.ProjectProvince,
                    o.Status.ToString(),
                    OpportunityDeadlineDisplay.Date(o.SubmissionDeadlineUtc))));

            // 2026-10-20: one list, every open tender for a structural firm, soonest deadline first, with a "New" tag;
            // the separate New RFPs card repeated the same tenders.
            Replace(Deadlines, live
                .Where(o => o.SubmissionDeadlineUtc >= now)
                .OrderBy(o => o.SubmissionDeadlineUtc)
                .Select(o => CreateDeadlineRow(o, now)));
            OnPropertyChanged(nameof(HasDeadlines));

            var closingIn30 = Deadlines.Count(r => live.Any(o => o.Id == r.OpportunityId && o.SubmissionDeadlineUtc <= thirtyDays));
            StatusMessage = $"{Deadlines.Count:N0} open tenders for a structural firm; {closingIn30:N0} close in the next 30 days; {Deadlines.Count(r => r.IsNew):N0} new this week.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // T4.002 audit fix (2026-05-30): log the failure to Serilog before
            // setting the user-facing status string so post-incident telemetry
            // has a durable record of what broke the BD first screen.
            _logger?.LogError(ex, "BD dashboard load failed.");
            StatusMessage = $"Dashboard load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> rows)
    {
        collection.Clear();
        foreach (var row in rows)
        {
            collection.Add(row);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
