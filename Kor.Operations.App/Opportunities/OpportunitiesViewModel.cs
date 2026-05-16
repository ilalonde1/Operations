#nullable enable
using System;
using System.Collections.Generic;
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
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Sources;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// ViewModel for <c>OpportunitiesWindow</c>. Holds the loaded list, the
/// selected row, and the heartbeat status string for the top-of-window banner.
/// Implements <see cref="IAiContextProvider"/> per the firm-wide rule that
/// every feature module exposes its data to the AI.
/// </summary>
public sealed class OpportunitiesViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Opportunities (BD)";

    // Heartbeat staleness thresholds. Mirror the FileSync convention: green
    // < 2x heartbeat interval, amber up to 5x, red beyond. The Worker beats
    // every 60s, so 2 minutes / 5 minutes here.
    private static readonly TimeSpan HeartbeatStaleAmber = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HeartbeatStaleRed = TimeSpan.FromMinutes(5);

    private const int RecentRunsToShow = 25;

    private readonly IOpportunityStore _store;
    private readonly IHeartbeatStore _heartbeatStore;
    private readonly IOpportunityScoringService _scoringService;
    private readonly IIngestionRunStore _ingestionRunStore;
    private readonly IIngestionTriggerStore _ingestionTriggerStore;
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly IDeltekClientContextService _deltekContextService;

    // Frozen so the VM can hand them out cross-thread (XAML binds on UI thread but
    // RefreshHeartbeatAsync runs the assignment off the UI thread). Mirrors the
    // FileSync KPI-brush convention.
    private static readonly Brush HealthGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
    private static readonly Brush HealthAmber = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));
    private static readonly Brush HealthRed = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));
    private static readonly Brush HealthNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));

    private OpportunityRowView? _selected;
    private DeltekClientIntelligence? _selectedIntelligence;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private string _heartbeatLine = "Heartbeat: not yet loaded.";
    private string _heartbeatHealth = "Unknown";
    private Brush _heartbeatBrush = HealthNeutral;

    // Filter state
    private string _filterText = string.Empty;
    private OpportunityStatus? _statusFilter;
    private RelevanceTier? _tierFilter;
    private string? _provinceFilter;

    public OpportunitiesViewModel(
        IOpportunityStore store,
        IHeartbeatStore heartbeatStore,
        IOpportunityScoringService scoringService,
        IIngestionRunStore ingestionRunStore,
        IIngestionTriggerStore ingestionTriggerStore,
        IOpportunitySourceStore sourceStore,
        IDeltekClientContextService deltekContextService)
    {
        _store = store;
        _heartbeatStore = heartbeatStore;
        _scoringService = scoringService;
        _ingestionRunStore = ingestionRunStore;
        _ingestionTriggerStore = ingestionTriggerStore;
        _sourceStore = sourceStore;
        _deltekContextService = deltekContextService ?? throw new ArgumentNullException(nameof(deltekContextService));

        FilteredOpportunitiesView = CollectionViewSource.GetDefaultView(Opportunities);
        FilteredOpportunitiesView.Filter = OpportunityFilterPredicate;
    }

    public ObservableCollection<OpportunityRowView> Opportunities { get; } = new();

    /// <summary>Filter-aware projection bound by the DataGrid. <see cref="Opportunities"/>
    /// stays as the full set so AI context, headlines, etc. see everything.</summary>
    public ICollectionView FilteredOpportunitiesView { get; }

    public ObservableCollection<IngestionRunRowView> IngestionRuns { get; } = new();

    public OpportunityRowView? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                _ = LoadSelectedIntelligenceAsync(CancellationToken.None);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string HeartbeatLine
    {
        get => _heartbeatLine;
        private set => SetField(ref _heartbeatLine, value);
    }

    /// <summary>One of <c>Green</c> / <c>Amber</c> / <c>Red</c> / <c>Unknown</c>.</summary>
    public string HeartbeatHealth
    {
        get => _heartbeatHealth;
        private set => SetField(ref _heartbeatHealth, value);
    }

    public Brush HeartbeatBrush
    {
        get => _heartbeatBrush;
        private set => SetField(ref _heartbeatBrush, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                FilteredOpportunitiesView.Refresh();
            }
        }
    }

    /// <summary>Selected status filter or null for "all".</summary>
    public OpportunityStatus? StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetField(ref _statusFilter, value))
            {
                FilteredOpportunitiesView.Refresh();
            }
        }
    }

    /// <summary>Selected tier filter or null for "all".</summary>
    public RelevanceTier? TierFilter
    {
        get => _tierFilter;
        set
        {
            if (SetField(ref _tierFilter, value))
            {
                FilteredOpportunitiesView.Refresh();
            }
        }
    }

    /// <summary>Two-letter province (e.g. "BC") or null for "all".</summary>
    public string? ProvinceFilter
    {
        get => _provinceFilter;
        set
        {
            if (SetField(ref _provinceFilter, value))
            {
                FilteredOpportunitiesView.Refresh();
            }
        }
    }

    /// <summary>For the WPF combo: status options, plus a leading "All" sentinel.</summary>
    public IReadOnlyList<OpportunityStatus?> StatusFilterOptions { get; } = BuildStatusOptions();

    public IReadOnlyList<RelevanceTier?> TierFilterOptions { get; } = BuildTierOptions();

    public IReadOnlyList<string?> ProvinceFilterOptions { get; } = new string?[] { null, "BC", "AB", "ON", "QC", "Other" };

    private static IReadOnlyList<OpportunityStatus?> BuildStatusOptions()
    {
        var list = new List<OpportunityStatus?> { null };
        foreach (var s in Enum.GetValues<OpportunityStatus>())
        {
            list.Add(s);
        }

        return list;
    }

    private static IReadOnlyList<RelevanceTier?> BuildTierOptions()
    {
        var list = new List<RelevanceTier?> { null };
        foreach (var t in Enum.GetValues<RelevanceTier>())
        {
            list.Add(t);
        }

        return list;
    }

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading…";
        try
        {
            var rows = await _store.ListAsync(ct).ConfigureAwait(true);

            var preservedKey = Selected?.OpportunityKey;
            Opportunities.Clear();
            foreach (var r in rows)
            {
                Opportunities.Add(new OpportunityRowView(r));
            }

            if (!string.IsNullOrEmpty(preservedKey))
            {
                Selected = Opportunities.FirstOrDefault(r => r.OpportunityKey == preservedKey);
            }

            await RefreshHeartbeatAsync(ct).ConfigureAwait(true);
            await RefreshIngestionRunsAsync(ct).ConfigureAwait(true);

            StatusMessage = rows.Count == 0
                ? "No opportunities yet — click \"New Opportunity\" or \"Run CanadaBuys Now\" to populate."
                : $"Loaded {rows.Count} opportunit{(rows.Count == 1 ? "y" : "ies")}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshIngestionRunsAsync(CancellationToken ct)
    {
        try
        {
            var runs = await _ingestionRunStore.ListRecentAsync(RecentRunsToShow, ct).ConfigureAwait(true);
            IngestionRuns.Clear();
            foreach (var r in runs)
            {
                IngestionRuns.Add(new IngestionRunRowView(r));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ingestion-run refresh failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task LoadSelectedIntelligenceAsync(CancellationToken ct)
    {
        _selectedIntelligence = null;
        if (_selected?.Model.DeltekClientId is not { } clientId
            || string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        try
        {
            _selectedIntelligence = await _deltekContextService
                .LoadAsync(clientId, ct)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Best-effort - never block the grid. AI just won't see the rich
            // Deltek block this turn. The intelligence-window button still
            // works on click and surfaces the same error there.
        }
    }

    /// <summary>
    /// Inserts a row into <c>opportunities.IngestionTriggers</c> with status
    /// <c>Pending</c>. The Worker's IngestionTriggerPoller picks it up within
    /// one poll cycle (default 30s) and runs the matching provider. We don't
    /// wait for completion here — the caller polls / refreshes.
    /// </summary>
    public async Task<Guid> RequestRunAsync(string sourceName, string requestedBy, CancellationToken ct)
    {
        var source = await _sourceStore.GetByNameAsync(sourceName, ct).ConfigureAwait(true);
        if (source is null)
        {
            throw new InvalidOperationException(
                $"OpportunitySource '{sourceName}' isn't configured. Make sure the Worker has run at least once " +
                "(SourceBootstrapHostedService creates the row on first start).");
        }

        if (!source.IsEnabled)
        {
            throw new InvalidOperationException($"OpportunitySource '{sourceName}' is disabled.");
        }

        var triggerId = await _ingestionTriggerStore.EnqueueAsync(source.Id, requestedBy, ct).ConfigureAwait(true);
        StatusMessage = $"Run requested for {sourceName} (trigger {triggerId:N}). Worker will pick this up shortly.";
        return triggerId;
    }

    public async Task<Opportunity> InsertAsync(Opportunity draft, string actor, CancellationToken ct)
    {
        var scored = ApplyScore(draft);
        var saved = await _store.InsertAsync(scored, actor, ct).ConfigureAwait(true);
        Opportunities.Insert(0, new OpportunityRowView(saved));
        Selected = Opportunities[0];
        StatusMessage = $"Inserted {saved.OpportunityKey} (score {FormatScore(saved)}).";
        return saved;
    }

    public async Task<Opportunity> UpdateAsync(Opportunity edited, string actor, CancellationToken ct)
    {
        var scored = ApplyScore(edited);
        var saved = await _store.UpdateAsync(scored, actor, ct).ConfigureAwait(true);
        ReplaceRow(saved);
        StatusMessage = $"Updated {saved.OpportunityKey} (score {FormatScore(saved)}).";
        return saved;
    }

    /// <summary>
    /// Scores the draft and returns a copy with RelevanceScore + RelevanceTier
    /// populated. Status changes are intentionally NOT re-scored (use the
    /// admin "Recalc all" button or re-edit the row to refresh) - keeps the
    /// status-transition path cheap.
    /// </summary>
    private Opportunity ApplyScore(Opportunity draft)
    {
        var result = _scoringService.Score(draft);
        return draft with
        {
            RelevanceScore = result.Score,
            RelevanceTier = result.Tier,
        };
    }

    private static string FormatScore(Opportunity o) =>
        o.RelevanceScore.HasValue
            ? $"{o.RelevanceScore.Value:0.##} {o.RelevanceTier}"
            : "—";

    public async Task<Opportunity> ChangeStatusAsync(
        OpportunityRowView row,
        OpportunityStatus newStatus,
        string actor,
        CancellationToken ct)
    {
        var saved = await _store.ChangeStatusAsync(row.Id, newStatus, row.Model.RowVersion, actor, ct).ConfigureAwait(true);
        ReplaceRow(saved);
        StatusMessage = $"{saved.OpportunityKey}: {row.Status} → {newStatus}.";
        return saved;
    }

    private void ReplaceRow(Opportunity saved)
    {
        for (int i = 0; i < Opportunities.Count; i++)
        {
            if (Opportunities[i].Id == saved.Id)
            {
                Opportunities[i] = new OpportunityRowView(saved);
                Selected = Opportunities[i];
                return;
            }
        }

        // Should not happen for an UPDATE/ChangeStatus; treat as insert.
        Opportunities.Insert(0, new OpportunityRowView(saved));
        Selected = Opportunities[0];
    }

    private bool OpportunityFilterPredicate(object obj)
    {
        if (obj is not OpportunityRowView row)
        {
            return false;
        }

        if (StatusFilter is { } status && row.Model.Status != status)
        {
            return false;
        }

        if (TierFilter is { } tier && row.Model.RelevanceTier != tier)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ProvinceFilter))
        {
            // "Other" is anything not BC/AB/ON/QC; null means "all" (handled above).
            var rowProv = row.Model.ProjectProvince ?? string.Empty;
            if (string.Equals(ProvinceFilter, "Other", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(rowProv, "BC", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rowProv, "AB", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rowProv, "ON", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rowProv, "QC", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (!string.Equals(rowProv, ProvinceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        var needle = FilterText.Trim();
        return Contains(row.Name, needle)
            || Contains(row.OpportunityKey, needle)
            || Contains(row.BuyerName, needle)
            || Contains(row.Model.ProjectCity, needle)
            || Contains(row.Model.ProjectProvince, needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _heartbeatStore.ListAsync(ct).ConfigureAwait(true);
            var hb = rows.FirstOrDefault(r => r.ServiceName == "Kor.Opportunities.Worker");
            if (hb is null)
            {
                HeartbeatLine = "Worker heartbeat: never seen.";
                HeartbeatHealth = "Red";
                HeartbeatBrush = HealthRed;
                return;
            }

            var age = DateTimeOffset.UtcNow - hb.LastBeatUtc.UtcDateTime;
            (HeartbeatHealth, HeartbeatBrush) = age switch
            {
                _ when age < HeartbeatStaleAmber => ("Green", HealthGreen),
                _ when age < HeartbeatStaleRed => ("Amber", HealthAmber),
                _ => ("Red", HealthRed),
            };

            HeartbeatLine = $"Worker {hb.MachineName} v{hb.Version ?? "?"} — last beat {Humanize(age)} ago.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HeartbeatLine = $"Heartbeat read failed: {ex.GetType().Name}.";
            HeartbeatHealth = "Red";
            HeartbeatBrush = HealthRed;
        }
    }

    private static string Humanize(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{span.TotalSeconds:F0}s"
        : span.TotalMinutes < 90 ? $"{span.TotalMinutes:F0}m"
        : $"{span.TotalHours:F1}h";

    // ------------------------------------------------------------------------
    // IAiContextProvider
    // ------------------------------------------------------------------------

    public string ProviderName => Provider;

    public bool HasData => Opportunities.Count > 0;

    public string BuildContext()
    {
        // Snapshot Opportunities + IngestionRuns up front. BuildContext runs
        // on AppAiContextBuilder's worker thread while the BD refresh paths
        // mutate these on the UI thread; without the snapshot a mid-Ask
        // refresh silently drops this section (Batch 102 audit pattern).
        var opportunities = Opportunities.ToArray();
        var ingestionRuns = IngestionRuns.ToArray();

        // Firm-wide pursuit pipeline summary. Group by status, list deadlines
        // within 30 days, list anything in Pursuing/ProposalSubmitted (the hot list).
        var sb = new StringBuilder();
        sb.AppendLine($"Total opportunities tracked: {opportunities.Length}.");

        var byStatus = opportunities
            .GroupBy(r => r.Model.Status)
            .OrderBy(g => (int)g.Key)
            .ToList();
        if (byStatus.Count > 0)
        {
            sb.AppendLine("By status:");
            foreach (var g in byStatus)
            {
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
        }

        var hot = opportunities
            .Where(r => r.Model.Status is OpportunityStatus.Pursuing or OpportunityStatus.ProposalSubmitted)
            .OrderBy(r => r.Model.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue)
            .Take(20)
            .ToList();
        if (hot.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Hot list (Pursuing / ProposalSubmitted):");
            foreach (var r in hot)
            {
                var deadline = r.Model.SubmissionDeadlineUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
                sb.AppendLine($"  [{r.Model.Status}] {r.OpportunityKey} — {r.Name} ({r.BuyerName}); deadline {deadline}; owner {r.OwnerStaffId}");
            }
        }

        var imminent = opportunities
            .Where(r => r.Model.SubmissionDeadlineUtc.HasValue
                        && r.Model.SubmissionDeadlineUtc.Value <= DateTimeOffset.UtcNow.AddDays(30)
                        && r.Model.Status is not OpportunityStatus.Won
                            and not OpportunityStatus.Lost
                            and not OpportunityStatus.NoBid
                            and not OpportunityStatus.Withdrawn)
            .OrderBy(r => r.Model.SubmissionDeadlineUtc!.Value)
            .Take(20)
            .ToList();
        if (imminent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Deadlines within 30 days (open pursuits):");
            foreach (var r in imminent)
            {
                sb.AppendLine($"  {r.Model.SubmissionDeadlineUtc!.Value:yyyy-MM-dd} — {r.OpportunityKey} {r.Name} ({r.Model.Status})");
            }
        }

        if (ingestionRuns.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent ingestion runs:");
            foreach (var r in ingestionRuns.Take(5))
            {
                sb.AppendLine($"  {r.StartedDisplay} {r.ProviderName} — {r.StatusDisplay}; {r.CountsDisplay}");
            }
        }

        // Methodology emission removed in Batch 92c — MCP tool descriptions
        // + system prompt carry KOR's opportunity-status / relevance-tier /
        // discipline taxonomy canonically (once tooling for BD lands).
        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        var s = Selected;
        if (s is null)
        {
            return string.Empty;
        }

        var m = s.Model;
        var sb = new StringBuilder();
        sb.AppendLine($"Selected opportunity: {m.OpportunityKey} — {m.Name}");
        sb.AppendLine($"Buyer: {m.BuyerName} ({m.BuyerType})");
        sb.AppendLine($"Status: {m.Status}");
        sb.AppendLine($"Discipline: {m.Discipline}");
        if (!string.IsNullOrWhiteSpace(s.Location))
        {
            sb.AppendLine($"Location: {s.Location}");
        }

        if (m.EstimatedValue.HasValue)
        {
            sb.AppendLine($"Estimated value: {m.EstimatedValue.Value:N0} {m.EstimatedValueCurrency}");
        }

        if (m.SubmissionDeadlineUtc.HasValue)
        {
            sb.AppendLine($"Deadline: {m.SubmissionDeadlineUtc.Value:yyyy-MM-dd HH:mm zzz}");
        }

        if (!string.IsNullOrWhiteSpace(m.OwnerStaffId))
        {
            sb.AppendLine($"Owner: {m.OwnerStaffId}");
        }

        if (m.RelevanceScore.HasValue)
        {
            sb.AppendLine($"Relevance: {m.RelevanceScore.Value} ({m.RelevanceTier})");
        }

        if (_selectedIntelligence is { } dc && (dc.ProjectCount > 0 || dc.Company is not null))
        {
            sb.AppendLine();
            DeltekClientIntelligenceFormatter.Append(sb, dc);
        }

        return sb.ToString();
    }
}
