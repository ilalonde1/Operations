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
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class DashboardViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // T5.001 audit fix (2026-05-30): every BD VM exposes itself to the AI
    // context builder so the assistant can answer questions grounded in
    // what's actually on screen.
    public string ProviderName => "BD Dashboard";
    public bool HasData => LatestRfps.Count > 0 || ForwardPipeline.Count > 0 || CompetitorWatch.Count > 0;
    public string BuildContext()
        => $"BD Dashboard — funnel: {OpenSeatCountDisplay} open seats, {BidWindowCountDisplay} in bid window, {RadarCountDisplay} radar; pipeline: {TotalPipelineDisplay} total / {OpenRfpsDisplay} RFPs / {UpcomingProjectsDisplay} upcoming / {ClosingSoonDisplay} closing in 7d; pipeline value {PipelineValueDisplay}.";
    public string BuildLocalContext() => BuildContext();

    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    // U4: large pipeline sums ($59B) overflowed the fixed-width stat tile and
    // clipped to a broken "$59,014,997,". Show a compact, readable magnitude.
    private static string FormatCompactCad(decimal v)
    {
        if (v >= 1_000_000_000m) return string.Format(CanadianCulture, "${0:0.0}B", v / 1_000_000_000m);
        if (v >= 1_000_000m) return string.Format(CanadianCulture, "${0:0.0}M", v / 1_000_000m);
        if (v >= 1_000m) return string.Format(CanadianCulture, "${0:0}K", v / 1_000m);
        return string.Format(CanadianCulture, "{0:C0}", v);
    }

    private readonly IPrimePipelineStore _primePipeline;
    private readonly IOpportunityStore _opportunities;
    private readonly IBdDashboardStore _bdDashboard;
    private readonly IntelReadService _intelRead;
    private readonly IntelPersistenceService _intelPersistence;
    private string _totalPipelineDisplay = "0";
    private string _openRfpsDisplay = "0";
    private string _upcomingProjectsDisplay = "0";
    private string _closingSoonDisplay = "0";
    private string _pipelineValueDisplay = "$0";
    private int _radarCount;
    private int _bidWindowCount;
    private int _openSeatCount;
    private string _radarCountDisplay = "0";
    private string _bidWindowCountDisplay = "0";
    private string _openSeatCountDisplay = "0";
    private string _statusMessage = "Ready.";

    private readonly Microsoft.Extensions.Logging.ILogger<DashboardViewModel>? _logger;

    public DashboardViewModel(
        IPrimePipelineStore primePipeline,
        IOpportunityStore opportunities,
        IBdDashboardStore bdDashboard,
        IntelReadService intelRead,
        IntelPersistenceService intelPersistence,
        Microsoft.Extensions.Logging.ILogger<DashboardViewModel>? logger = null)
    {
        _primePipeline = primePipeline ?? throw new ArgumentNullException(nameof(primePipeline));
        _opportunities = opportunities ?? throw new ArgumentNullException(nameof(opportunities));
        _bdDashboard = bdDashboard ?? throw new ArgumentNullException(nameof(bdDashboard));
        _intelRead = intelRead ?? throw new ArgumentNullException(nameof(intelRead));
        _intelPersistence = intelPersistence ?? throw new ArgumentNullException(nameof(intelPersistence));
        _logger = logger;
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

    public sealed record ChartBar(string Label, double Value, double Max);

    // IDs (OpportunityId, OrgId) carried through so the dashboard rows can drill
    // into the canonical record on double-click — Latest RFPs / Deadlines open the
    // OpportunityEntryDialog, Open Structural Seats / Competitor Watch open the
    // OrgDossierWindow, Forward Pipeline opens the PursuitBrief.
    public sealed record FeedRow(long OpportunityId, string Name, string Buyer, string? Province, string Status, string? Deadline);

    public sealed record DeadlineRow(long OpportunityId, string Name, string? Province, string? Deadline, int DaysLeft);

    public sealed record OpenStructuralSeatRow(long OrgId, string DisplayName, string? Market, string? Status, string? DisplacementRead);

    public sealed record CompetitorWatchPanelRow(long OrgId, string DisplayName, string? CapacityRead);

    public sealed record ForwardPipelinePanelRow(long Id, string ProjectName, string? ProponentName, string? Province, string CostDisplay);

    // Loaded into a per-load cache so dashboard click handlers can resolve a
    // FeedRow/DeadlineRow back to the full Opportunity model (the entry dialog
    // wants the model, not just the id).
    private readonly Dictionary<long, Opportunity> _oppCache = new();

    public Opportunity? GetOpportunity(long id) =>
        _oppCache.TryGetValue(id, out var o) ? o : null;

    public ObservableCollection<FeedRow> LatestRfps { get; } = new();

    public ObservableCollection<ChartBar> SectorBars { get; } = new();

    public ObservableCollection<ChartBar> MarketBars { get; } = new();

    public ObservableCollection<ChartBar> StageBars { get; } = new();

    public ObservableCollection<ChartBar> MarketValueBars { get; } = new();

    public ObservableCollection<ChartBar> DeadlineMonthBars { get; } = new();

    public ObservableCollection<DeadlineRow> Deadlines { get; } = new();

    public ObservableCollection<OpenStructuralSeatRow> OpenStructuralSeats { get; } = new();

    public ObservableCollection<CompetitorWatchPanelRow> CompetitorWatch { get; } = new();

    public ObservableCollection<ForwardPipelinePanelRow> ForwardPipeline { get; } = new();

    public string TotalPipelineDisplay
    {
        get => _totalPipelineDisplay;
        private set => SetField(ref _totalPipelineDisplay, value);
    }

    public string OpenRfpsDisplay
    {
        get => _openRfpsDisplay;
        private set => SetField(ref _openRfpsDisplay, value);
    }

    public string UpcomingProjectsDisplay
    {
        get => _upcomingProjectsDisplay;
        private set => SetField(ref _upcomingProjectsDisplay, value);
    }

    public string ClosingSoonDisplay
    {
        get => _closingSoonDisplay;
        private set => SetField(ref _closingSoonDisplay, value);
    }

    public string PipelineValueDisplay
    {
        get => _pipelineValueDisplay;
        private set => SetField(ref _pipelineValueDisplay, value);
    }

    public int RadarCount
    {
        get => _radarCount;
        private set => SetField(ref _radarCount, value);
    }

    public int BidWindowCount
    {
        get => _bidWindowCount;
        private set => SetField(ref _bidWindowCount, value);
    }

    public int OpenSeatCount
    {
        get => _openSeatCount;
        private set => SetField(ref _openSeatCount, value);
    }

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

            // T3.001 audit fix (2026-05-30): kick off all 6 independent reads
            // in parallel via Task.WhenAll instead of awaiting one at a time.
            // Each store opens its own SqlConnection per call so there's no
            // shared-state contention. Latency drops from 6x to ~1x slowest
            // call on the BD first screen.
            var pipelineTask = _primePipeline.GetAllAsync(ct);
            var oppsTask = _opportunities.ListAsync(ct, includeClosed: false, includeNonPrime: false);
            var openStructuralSeatsTask = _bdDashboard.GetOpenStructuralSeatsAsync(12, ct);
            var competitorWatchTask = _bdDashboard.GetCompetitorWatchAsync(12, ct);
            var forwardPipelineTask = _bdDashboard.GetForwardPipelineAsync(12, ct);
            var funnelTask = _bdDashboard.GetPipelineFunnelAsync(ct);
            await Task.WhenAll(pipelineTask, oppsTask, openStructuralSeatsTask, competitorWatchTask, forwardPipelineTask, funnelTask).ConfigureAwait(true);
            var pipeline = pipelineTask.Result;
            var opps = oppsTask.Result;
            var openStructuralSeats = openStructuralSeatsTask.Result;
            var competitorWatch = competitorWatchTask.Result;
            var forwardPipeline = forwardPipelineTask.Result;
            var funnel = funnelTask.Result;
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            var sevenDays = now.AddDays(7);
            var thirtyDays = now.AddDays(30);
            var sixMonths = now.AddMonths(6);

            TotalPipelineDisplay = pipeline.Count.ToString("N0", CanadianCulture);
            OpenRfpsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Open RFP", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            UpcomingProjectsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Pipeline Project", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            ClosingSoonDisplay = opps.Count(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= sevenDays).ToString("N0", CanadianCulture);
            PipelineValueDisplay = FormatCompactCad(pipeline.Sum(r => r.EstimatedValueCad ?? 0m));
            RadarCount = funnel.RadarCount;
            BidWindowCount = funnel.BidWindowCount;
            OpenSeatCount = funnel.OpenSeatCount;
            RadarCountDisplay = funnel.RadarCount.ToString("N0", CanadianCulture);
            BidWindowCountDisplay = funnel.BidWindowCount.ToString("N0", CanadianCulture);
            OpenSeatCountDisplay = funnel.OpenSeatCount.ToString("N0", CanadianCulture);

            // Refresh the opp cache so click handlers can resolve OpportunityId
            // back to a full Opportunity model.
            _oppCache.Clear();
            foreach (var o in opps)
            {
                _oppCache[o.Id] = o;
            }

            Replace(LatestRfps, opps
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(15)
                .Select(o => new FeedRow(
                    o.Id,
                    o.Name,
                    o.BuyerName,
                    o.ProjectProvince,
                    o.Status.ToString(),
                    o.SubmissionDeadlineUtc?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

            Replace(SectorBars, BuildBars(pipeline
                // U4: canonicalize case so "Education" and "education" are one bar.
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Sector)
                    ? "Other"
                    : CanadianCulture.TextInfo.ToTitleCase(r.Sector!.Trim().ToLowerInvariant()))
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(MarketBars, BuildBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(StageBars, BuildBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Stage) ? "" : r.Stage!)
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(MarketValueBars, BuildValueBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
                .Select(g => (Label: g.Key, Value: (double)(g.Sum(r => r.EstimatedValueCad ?? 0m) / 1_000_000m)))));

            Replace(DeadlineMonthBars, BuildChronologicalBars(opps
                .Where(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= sixMonths)
                .GroupBy(o =>
                {
                    var deadline = o.SubmissionDeadlineUtc!.Value.LocalDateTime;
                    return new DateTime(deadline.Year, deadline.Month, 1);
                })
                .OrderBy(g => g.Key)
                .Select(g => (Label: g.Key.ToString("MMM yy", CultureInfo.InvariantCulture), Count: g.Count()))));

            Replace(Deadlines, opps
                .Where(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= thirtyDays)
                .OrderBy(o => o.SubmissionDeadlineUtc)
                .Select(o => new DeadlineRow(
                    o.Id,
                    o.Name,
                    o.ProjectProvince,
                    o.SubmissionDeadlineUtc?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    o.SubmissionDeadlineUtc.HasValue ? (int)Math.Floor((o.SubmissionDeadlineUtc.Value - now).TotalDays) : 0)));

            Replace(OpenStructuralSeats, openStructuralSeats.Select(r => new OpenStructuralSeatRow(
                r.OrgId,
                r.DisplayName,
                r.Market,
                r.Status,
                r.DisplacementRead)));

            Replace(CompetitorWatch, competitorWatch.Select(r => new CompetitorWatchPanelRow(
                r.OrgId,
                r.DisplayName,
                r.CapacityRead)));

            Replace(ForwardPipeline, forwardPipeline.Select(r => new ForwardPipelinePanelRow(
                r.Id,
                r.ProjectName,
                r.ProponentName,
                r.Province,
                FormatCurrency(r.EstimatedCostCad))));

            StatusMessage = $"Loaded {pipeline.Count:N0} pipeline rows and {opps.Count:N0} opportunities.";
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

    private static ChartBar[] BuildBars(IEnumerable<(string Label, int Count)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Count);
        return list.Select(g => new ChartBar(g.Label, g.Count, max)).ToArray();
    }

    private static ChartBar[] BuildValueBars(IEnumerable<(string Label, double Value)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Value);
        return list.Select(g => new ChartBar(g.Label, g.Value, max)).ToArray();
    }

    private static ChartBar[] BuildChronologicalBars(IEnumerable<(string Label, int Count)> groups)
    {
        var list = groups.ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Count);
        return list.Select(g => new ChartBar(g.Label, g.Count, max)).ToArray();
    }

    private static string FormatCurrency(decimal? value)
        => value.HasValue ? string.Format(CanadianCulture, "{0:C0}", value.Value) : "";

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
