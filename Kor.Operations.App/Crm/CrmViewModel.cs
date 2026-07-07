#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Crm;

/// <summary>
/// ViewModel behind <c>CrmView</c> (the Pursuits screen). Holds the
/// engagement list (left master), the activity log + contacts for the
/// selected engagement (right detail), and exposes
/// <see cref="IAiContextProvider"/> so the pipeline plus pursuit notes feed
/// the AI context.
/// </summary>
public sealed class CrmViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "CRM (BD)";

    private readonly ICrmEngagementStore _engagementStore;
    private readonly ICrmActivityStore _activityStore;
    private readonly ICrmContactStore _contactStore;
    private readonly IOpportunityStore _opportunityStore;
    private readonly IDeltekClientContextService _deltekContextService;
    private readonly Kor.Opportunities.Data.Awards.ICanonicalOrgStore _orgStore;

    private CrmEngagementRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private DeltekClientIntelligence? _deltekContext;
    private CrmAnalyticsSnapshot? _analytics;
    private CancellationTokenSource? _detailCts;
    private CrmEngagementRowView[] _contextSnapshot = Array.Empty<CrmEngagementRowView>();

    // Plan 1.1 — view-level filters. Filtering happens on the default
    // ICollectionView over Engagements, so the underlying collection (and the
    // AI context snapshot, which deliberately stays FULL-population — the
    // assistant must see all of reality, not the user's current filter) is
    // never reduced.
    private string? _stageFilterKey;                    // null = live default (Drafting + Submitted)
    private string _searchText = string.Empty;
    private bool _mineOnly;
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(250));

    /// <summary>Resolved identity of the signed-in user (set by the view on
    /// load) — the "Mine" toggle matches OwnerStaffId against this. Exact
    /// match only: legacy first-name owners stay D2's problem, no guessing.</summary>
    public string? CurrentActorUpn { get; set; }

    /// <summary>When set before the first load (e.g. by the Overwatch board's
    /// double-click), that engagement arrives selected. Consumed once.</summary>
    public long? PendingSelectEngagementId { get; set; }

    public CrmViewModel(
        ICrmEngagementStore engagementStore,
        ICrmActivityStore activityStore,
        ICrmContactStore contactStore,
        IOpportunityStore opportunityStore,
        IDeltekClientContextService deltekContextService,
        Kor.Opportunities.Data.Awards.ICanonicalOrgStore orgStore)
    {
        _engagementStore = engagementStore;
        _activityStore = activityStore;
        _contactStore = contactStore;
        _opportunityStore = opportunityStore;
        _deltekContextService = deltekContextService;
        _orgStore = orgStore;
    }

    public ObservableCollection<CrmEngagementRowView> Engagements { get; } = new();

    /// <summary>Install the row filter on the default collection view (the
    /// grid's ItemsSource binding resolves to the same view). Idempotent;
    /// called from the view's Loaded (UI thread).</summary>
    public void EnsureViewFilterInstalled()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Engagements);
        view.Filter ??= FilterRow;
    }

    private bool FilterRow(object item)
    {
        if (item is not CrmEngagementRowView row)
        {
            return false;
        }

        var stage = row.Engagement.Stage;
        var stageOk = _stageFilterKey is null
            ? stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted
            : Enum.TryParse<CrmEngagementStage>(_stageFilterKey, out var wanted) && stage == wanted;
        if (!stageOk)
        {
            return false;
        }

        if (_mineOnly && !string.Equals(row.Engagement.OwnerStaffId, CurrentActorUpn, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var q = _searchText.Trim();
            var hit = Contains(row.OpportunityKey, q)
                   || Contains(row.ProjectName, q)
                   || Contains(row.Buyer, q)
                   || Contains(row.OwnerDisplay, q);
            if (!hit)
            {
                return false;
            }
        }

        return true;

        static bool Contains(string? s, string q)
            => !string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Debounced grid search over key / project / buyer / owner.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                _searchDebouncer.Run(async () =>
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshEngagementsView));
            }
        }
    }

    /// <summary>Show only pursuits owned by <see cref="CurrentActorUpn"/>.</summary>
    public bool MineOnly
    {
        get => _mineOnly;
        set
        {
            if (SetField(ref _mineOnly, value))
            {
                RefreshEngagementsView();
            }
        }
    }

    /// <summary>Pill click: filter to that stage; clicking the active pill (or
    /// the Live pill) returns to the live default.</summary>
    public void SetStageChipFilter(string key)
    {
        _stageFilterKey = key == LiveChipKey || key == _stageFilterKey ? null : key;
        RebuildStageChips();
        RefreshEngagementsView();
    }

    private void RefreshEngagementsView()
    {
        System.Windows.Data.CollectionViewSource.GetDefaultView(Engagements).Refresh();
    }

    private void RebuildStageChips()
    {
        StageSummary.Clear();
        var rows = Engagements.Select(r => r.Engagement).ToList();

        var liveCount = rows.Count(e => e.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted);
        StageSummary.Add(new StageChip(
            "Live", liveCount, CrmEngagementRowView.BrushFor(CrmEngagementStage.Drafting),
            LiveChipKey, IsActive: _stageFilterKey is null));

        foreach (var stage in new[]
                 {
                     CrmEngagementStage.Won, CrmEngagementStage.Submitted,
                     CrmEngagementStage.Drafting, CrmEngagementStage.Lost,
                 })
        {
            var count = rows.Count(e => e.Stage == stage);
            if (count > 0)
            {
                StageSummary.Add(new StageChip(
                    stage.ToString(), count, CrmEngagementRowView.BrushFor(stage),
                    stage.ToString(), IsActive: _stageFilterKey == stage.ToString()));
            }
        }

        OnPropertyChanged(nameof(HasStageSummary));
    }

    public ObservableCollection<CrmActivityRowView> Activities { get; } = new();

    public ObservableCollection<CrmContactRowView> Contacts { get; } = new();

    /// <summary>Per-stage pipeline counts shown as coloured pills above the list.
    /// Plan 1.1: the pills are CLICK-FILTERS — the default view shows live
    /// pursuits (Drafting + Submitted); clicking Won/Lost reveals the closed
    /// history; clicking the active pill returns to the live default.</summary>
    public ObservableCollection<StageChip> StageSummary { get; } = new();
    public bool HasStageSummary => StageSummary.Count > 0;

    public sealed record StageChip(string Label, int Count, Brush Brush, string Key, bool IsActive);

    /// <summary>Chip key for the default live view (Drafting + Submitted).</summary>
    public const string LiveChipKey = "Live";

    public IReadOnlyList<CrmEngagementStage> StageOptions { get; } = Enum.GetValues<CrmEngagementStage>();

    /// <summary>Friendly display names for the activity-type picker (plan 1.2 —
    /// "EmailOut" is developer spelling, not user spelling).</summary>
    public sealed record ActivityTypeOption(CrmActivityType Value, string Label);

    public IReadOnlyList<ActivityTypeOption> ActivityTypeOptions { get; } = new[]
    {
        new ActivityTypeOption(CrmActivityType.Note, "Note"),
        new ActivityTypeOption(CrmActivityType.Call, "Call"),
        new ActivityTypeOption(CrmActivityType.Meeting, "Meeting"),
        new ActivityTypeOption(CrmActivityType.EmailOut, "Email out"),
        new ActivityTypeOption(CrmActivityType.EmailIn, "Email in"),
        new ActivityTypeOption(CrmActivityType.Deliverable, "Deliverable"),
        new ActivityTypeOption(CrmActivityType.Other, "Other"),
    };

    public CrmEngagementRowView? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                // T2.001 audit fix (2026-05-30): per-selection CancellationTokenSource
                // so a slow previous detail load can't overwrite Activities / Contacts /
                // DeltekContext after the user has clicked a different row. LoadDetailAsync
                // also captures the engagement id and re-checks before mutating the
                // observable collections.
                var prev = _detailCts;
                _detailCts = new CancellationTokenSource();
                prev?.Cancel();
                prev?.Dispose();
                _ = LoadDetailAsync(_detailCts.Token);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public void SetStatusMessage(string message)
    {
        StatusMessage = message ?? string.Empty;
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>
    /// Deltek roll-up for the selected engagement's client. Null when the
    /// engagement's Opportunity has no DeltekClientId or the lookup hasn't
    /// landed yet. Refreshed every time <see cref="Selected"/> changes.
    /// </summary>
    public DeltekClientIntelligence? DeltekContext
    {
        get => _deltekContext;
        private set
        {
            if (SetField(ref _deltekContext, value))
            {
                OnPropertyChanged(nameof(DeltekContextSummary));
                OnPropertyChanged(nameof(HasDeltekContext));
            }
        }
    }

    public bool HasDeltekContext => _deltekContext is { ProjectCount: > 0 };

    /// <summary>
    /// Aggregate roll-up of the loaded engagements: stage breakdown, win rate
    /// total + by buyer type / owner, average won/lost fees, average pursuit
    /// duration. Refreshed every <see cref="LoadAsync"/>.
    /// </summary>
    public CrmAnalyticsSnapshot? Analytics
    {
        get => _analytics;
        private set
        {
            if (SetField(ref _analytics, value))
            {
                OnPropertyChanged(nameof(AnalyticsHeadline));
                OnPropertyChanged(nameof(AnalyticsBuyerTypeSummary));
                OnPropertyChanged(nameof(HasAnalytics));
            }
        }
    }

    public bool HasAnalytics => _analytics is { TotalEngagements: > 0 };

    /// <summary>One-line headline, populations segmented (fix F12): the 177
    /// backfilled Deltek wins never blend into the live win rate.</summary>
    public string AnalyticsHeadline
    {
        get
        {
            if (_analytics is null || _analytics.TotalEngagements == 0) return string.Empty;
            var won = _analytics.Won;
            var lost = _analytics.Lost;
            var rate = (won + lost) > 0 ? _analytics.WinRate.ToString("P0") : "— (no live outcomes yet)";
            var backfill = _analytics.BackfillWins > 0
                ? $"  •  {_analytics.BackfillWins} historical Deltek wins ({_analytics.BackfillWonFee.ToString("C0", CultureInfo.CurrentCulture)}, backfill)"
                : string.Empty;
            var avgWonFee = _analytics.AvgWonProposedFee > 0
                ? $"  •  avg won fee {_analytics.AvgWonProposedFee.ToString("C0", CultureInfo.CurrentCulture)}"
                : string.Empty;
            return $"{_analytics.TotalEngagements} engagement(s){backfill}  •  live: {won}W / {lost}L  •  live win rate {rate}{avgWonFee}";
        }
    }

    /// <summary>Top 3 buyer types by win rate (with at least 1 resolved engagement).</summary>
    public string AnalyticsBuyerTypeSummary
    {
        get
        {
            if (_analytics is null) return string.Empty;
            var rows = _analytics.ByBuyerType
                .Where(b => b.Won + b.Lost > 0)
                .OrderByDescending(b => b.WinRate)
                .ThenByDescending(b => b.Won + b.Lost)
                .Take(3)
                .Select(b => $"{b.Bucket} {b.Won}/{b.Won + b.Lost} ({b.WinRate:P0})");
            return string.Join("  •  ", rows);
        }
    }

    /// <summary>One-line UI summary of the Deltek roll-up. Empty when no context.</summary>
    public string DeltekContextSummary
    {
        get
        {
            if (_deltekContext is null) return string.Empty;
            var fee = _deltekContext.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture);
            var last = _deltekContext.LatestProjectStart.HasValue
                ? _deltekContext.LatestProjectStart.Value.ToString("yyyy-MM-dd")
                : "—";
            return $"{_deltekContext.ClientName}: {_deltekContext.ProjectCount} project(s), lifetime fee {fee}, last opened {last}.";
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading engagements…";
        try
        {
            var engagements = await _engagementStore.ListAsync(ct).ConfigureAwait(true);
            var opportunities = await _opportunityStore.ListAsync(ct).ConfigureAwait(true);
            var oppById = opportunities.ToDictionary(o => o.Id);

            var preservedId = Selected?.Id;
            Engagements.Clear();
            foreach (var e in engagements)
            {
                // BD-tracking engagements (OpportunityId IS NULL) have no parent Opportunity;
                // CrmEngagementRowView's HasOpportunity / display fallbacks handle the null case.
                Opportunity? opp = null;
                if (e.OpportunityId.HasValue)
                {
                    oppById.TryGetValue(e.OpportunityId.Value, out opp);
                }
                Engagements.Add(new CrmEngagementRowView(e, opp));
            }

            // Pending scoped-open (Overwatch double-click / workspace deep-link)
            // wins over preservation.
            var pendingId = PendingSelectEngagementId;
            PendingSelectEngagementId = null;
            var pendingRow = pendingId.HasValue ? Engagements.FirstOrDefault(r => r.Id == pendingId.Value) : null;
            // Preserve/first-row fallbacks are FILTER-AWARE (review fix
            // 2026-07-07): the raw store order is UpdatedAtUtc DESC, so a
            // just-closed pursuit sorts first and would otherwise arrive as an
            // invisible selection under the live default. pendingRow is exempt —
            // the reset below makes it visible.
            Selected = pendingRow
                ?? (preservedId.HasValue
                    ? Engagements.FirstOrDefault(r => r.Id == preservedId.Value && FilterRow(r))
                    : null)
                ?? Engagements.FirstOrDefault(FilterRow);

            // A deep-linked pursuit must be VISIBLE: if the current filters would
            // hide it (e.g. a closed pursuit under the live default, or an active
            // Mine/search filter), reset them to show the row (plan 1.1).
            if (pendingRow is not null && !FilterRow(pendingRow))
            {
                _stageFilterKey = pendingRow.Engagement.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted
                    ? null
                    : pendingRow.Engagement.Stage.ToString();
                if (_mineOnly) { _mineOnly = false; OnPropertyChanged(nameof(MineOnly)); }
                if (_searchText.Length > 0) { _searchText = string.Empty; OnPropertyChanged(nameof(SearchText)); }
            }

            // FULL population, independent of grid filters — the AI must see
            // all of reality (documented decision, plan 1.1).
            _contextSnapshot = Engagements.ToArray();

            // Per-stage pipeline pills (click-filters, plan 1.1) + re-apply the
            // current filter to the refilled collection.
            RebuildStageChips();
            RefreshEngagementsView();

            // Refresh the analytics snapshot off the same data we just loaded.
            // Pure projection — no extra DB round-trip.
            Analytics = CrmAnalyticsService.Compute(engagements, oppById);

            StatusMessage = engagements.Count == 0
                ? "No CRM engagements yet — promote an opportunity from the Opportunities window to start tracking."
                : $"Loaded {engagements.Count} engagement{(engagements.Count == 1 ? "" : "s")}.";
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

    /// <summary>The Deltek client id the detail panel resolved for the selected
    /// engagement (buyer-org Clendor path first, opportunity fallback). The
    /// "Details" client-intelligence drill uses this — same key as the panel.</summary>
    public string? ResolvedDeltekClientId { get; private set; }

    private async Task LoadDetailAsync(CancellationToken ct)
    {
        Activities.Clear();
        Contacts.Clear();
        DeltekContext = null;
        ResolvedDeltekClientId = null;
        if (Selected is null)
        {
            return;
        }

        try
        {
            var engagementId = Selected.Id;
            var activities = await _activityStore.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true);

            // T2.001 (2026-05-30): if the user switched selection during the
            // await, Selected.Id will have changed — don't stomp the new
            // selection's collections with this stale load's results.
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;

            var contacts = await _contactStore.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;

            foreach (var a in activities)
            {
                Activities.Add(new CrmActivityRowView(a));
            }

            foreach (var c in contacts)
            {
                Contacts.Add(new CrmContactRowView(c));
            }

            // Plan 1.3 (re-key): the Deltek roll-up keys on the engagement's
            // BuyerCanonicalOrgId → CanonicalOrg.ClendorClientId — the path that
            // reaches ~82% of engagements — with the linked Opportunity's
            // DeltekClientId as fallback (populated on 0/1,646 opportunities
            // today, kept for correctness). The old key was fallback-only, so
            // the panel rendered for 0% of engagements. Resolution is by org
            // ID, never name equality. ODBC/lookup failure leaves DeltekContext
            // null — never blocks the rest of the detail load.
            string? deltekId = null;
            if (Selected.Engagement.BuyerCanonicalOrgId is { } buyerOrgId)
            {
                try
                {
                    var org = await _orgStore.GetCanonicalOrgAsync(buyerOrgId, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
                    deltekId = string.IsNullOrWhiteSpace(org?.ClendorClientId) ? null : org!.ClendorClientId;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Canonical-org lookup for engagement {EngagementId} failed; Deltek panel falls back to the opportunity key.", engagementId);
                }
            }

            deltekId ??= Selected?.Opportunity?.DeltekClientId;
            ResolvedDeltekClientId = string.IsNullOrWhiteSpace(deltekId) ? null : deltekId;

            if (!string.IsNullOrWhiteSpace(deltekId))
            {
                try
                {
                    var deltek = await _deltekContextService.LoadAsync(deltekId, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
                    DeltekContext = deltek;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Deltek client lookup failed: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Detail load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task<CrmEngagement> ChangeStageAsync(CrmEngagementRowView row, CrmEngagementStage newStage, string actor, CancellationToken ct)
    {
        // ClosedAtUtc is server-owned (fix F7): the store's UpdateAsync stamps it
        // on entering Won/Lost, preserves it while terminal, and clears it on
        // reopen — identically for every close door.
        var updated = row.Engagement with { Stage = newStage };

        var saved = await _engagementStore.UpdateAsync(updated, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{row.OpportunityKey}: stage → {newStage}.";
        return saved;
    }

    /// <summary>
    /// Writes the optional close-out facts captured by the Lost-outcome prompt
    /// (plan 1.5). Read-modify-write over the freshly saved row so nothing else
    /// is disturbed; RowVersion guards the race.
    /// </summary>
    public async Task<CrmEngagement> SetLostOutcomeAsync(
        CrmEngagement fresh, WonLostOutcome outcome, long? lostToCanonicalOrgId, string? reason, string actor, CancellationToken ct)
    {
        var updated = fresh with
        {
            WonLostOutcome = outcome,
            LostToCanonicalOrgId = outcome == Kor.Opportunities.Core.Models.WonLostOutcome.Lost ? lostToCanonicalOrgId : null,
            OutcomeReason = string.IsNullOrWhiteSpace(reason) ? fresh.OutcomeReason : reason.Trim(),
        };

        var saved = await _engagementStore.UpdateAsync(updated, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{OutcomeLabel(outcome)} outcome recorded.";
        return saved;
    }

    private static string OutcomeLabel(WonLostOutcome o) => o switch
    {
        Kor.Opportunities.Core.Models.WonLostOutcome.NoBid => "No-bid",
        Kor.Opportunities.Core.Models.WonLostOutcome.Withdrawn => "Withdrawn",
        _ => o.ToString(),
    };

    public async Task<CrmEngagement> SaveEngagementAsync(CrmEngagement edited, string actor, CancellationToken ct)
    {
        var saved = edited.Id == 0
            ? await _engagementStore.InsertAsync(edited, actor, ct).ConfigureAwait(true)
            : await _engagementStore.UpdateAsync(edited, actor, ct).ConfigureAwait(true);

        ReplaceEngagement(saved);
        StatusMessage = $"Saved engagement {saved.Id}.";
        return saved;
    }

    /// <summary>
    /// Appends one activity. Plan 1.2: <paramref name="occurredAtUtc"/> lets the
    /// user record WHEN it actually happened (Friday's call logged on Monday);
    /// null = now. <paramref name="contactId"/> soft-links the engagement
    /// contact it was with. Append-only always — backdating is a parameter,
    /// never a mutation.
    /// </summary>
    public async Task<CrmActivity> AppendActivityAsync(
        long engagementId, CrmActivityType type, string subject, string? body, string actor, CancellationToken ct,
        DateTimeOffset? occurredAtUtc = null, long? contactId = null)
    {
        var activity = new CrmActivity
        {
            EngagementId = engagementId,
            ActivityType = type,
            Subject = subject,
            Body = body,
            OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow,
            ContactId = contactId,
        };

        var saved = await _activityStore.AppendAsync(activity, actor, ct).ConfigureAwait(true);

        // The DB write targeted engagementId explicitly; only the detail panel is
        // selection-bound. If the user switched rows during the await, inserting
        // here would show the activity under the WRONG engagement (fix F10 — same
        // guard LoadDetailAsync already uses).
        if (Selected?.Id == engagementId)
        {
            Activities.Insert(0, new CrmActivityRowView(saved));
        }

        StatusMessage = $"Logged {type}: {subject}.";
        return saved;
    }

    public async Task<CrmContact> AddContactAsync(long engagementId, string displayName, string? role, string? email, string? phone, bool isPrimary, string actor, CancellationToken ct)
    {
        var contact = new CrmContact
        {
            EngagementId = engagementId,
            DisplayName = displayName,
            Role = role,
            Email = email,
            Phone = phone,
            IsPrimary = isPrimary,
        };

        var saved = await _contactStore.InsertAsync(contact, actor, ct).ConfigureAwait(true);

        // Same stale-selection guard as AppendActivityAsync (fix F10): the row
        // may have changed during the await; the write itself was correct.
        if (Selected?.Id == engagementId)
        {
            Contacts.Add(new CrmContactRowView(saved));
        }

        StatusMessage = $"Added contact {displayName}.";
        return saved;
    }

    public async Task DeleteContactAsync(CrmContactRowView row, CancellationToken ct)
    {
        await _contactStore.DeleteAsync(row.Id, row.Model.RowVersion, ct).ConfigureAwait(true);
        Contacts.Remove(row);
        StatusMessage = $"Removed contact {row.DisplayName}.";
    }

    private void ReplaceEngagement(CrmEngagement saved)
    {
        for (int i = 0; i < Engagements.Count; i++)
        {
            if (Engagements[i].Id == saved.Id)
            {
                var opp = Engagements[i].Opportunity;
                Engagements[i] = new CrmEngagementRowView(saved, opp);

                // Review fix 2026-07-07: a stage change can move the row out of
                // the current filter (mark Lost under the live view). Keeping it
                // Selected while INVISIBLE let the toolbar silently act on a row
                // no one could see. Re-select only when still visible; refresh
                // the pills so counts reflect the change either way.
                Selected = FilterRow(Engagements[i]) ? Engagements[i] : null;
                RebuildStageChips();
                RefreshEngagementsView();
                return;
            }
        }
    }

    // ------------------------------------------------------------------------
    // IAiContextProvider
    // ------------------------------------------------------------------------

    public string ProviderName => Provider;

    public bool HasData => _contextSnapshot.Length > 0;

    public string BuildContext()
    {
        // Read the UI-thread-swapped snapshot (set at the end of LoadAsync) —
        // ToArray() on the live collection from the ask path was itself a
        // latent cross-thread race (audit 2026-07-01 1.4).
        var engagements = _contextSnapshot;

        var sb = new StringBuilder();
        sb.AppendLine($"Total CRM engagements: {engagements.Length}.");

        var byStage = engagements
            .GroupBy(r => r.Engagement.Stage)
            .OrderBy(g => (int)g.Key)
            .ToList();
        if (byStage.Count > 0)
        {
            sb.AppendLine("By stage:");
            foreach (var g in byStage)
            {
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
        }

        var hot = engagements
            .Where(r => r.Engagement.Stage is CrmEngagementStage.Submitted)
            .OrderByDescending(r => r.Engagement.UpdatedAtUtc)
            .Take(15)
            .ToList();
        if (hot.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Active pursuits in Submitted:");
            foreach (var r in hot)
            {
                sb.AppendLine($"  [{r.Engagement.Stage}] {r.OpportunityKey} {r.ProjectName} ({r.Buyer}) — owner {r.OwnerDisplay}");
            }
        }

        // Fix F12: segment populations before quoting any rate — the 177
        // backfilled Deltek wins have no owner and no loss denominator, so a
        // blended rate is a vanity 100%.
        var backfillWins = engagements.Count(r =>
            CrmAnalyticsService.IsBackfill(r.Engagement) && r.Engagement.Stage == CrmEngagementStage.Won);
        var liveRows = engagements.Where(r => !CrmAnalyticsService.IsBackfill(r.Engagement)).ToArray();
        var won = liveRows.Count(r => r.Engagement.Stage == CrmEngagementStage.Won);
        var lost = liveRows.Count(r => r.Engagement.Stage == CrmEngagementStage.Lost);
        sb.AppendLine();
        if (backfillWins > 0)
        {
            sb.AppendLine($"NOTE: {backfillWins} of the Won engagements are a historical Deltek backfill (ExternalSource 'Deltek.CustomProposal') — flat history with no owner and no loss denominator. Never quote a win rate that blends them with live pursuits.");
        }

        sb.AppendLine((won + lost) > 0
            ? $"Live win rate (won vs lost, backfill excluded): {won} / {won + lost} = {(double)won / (won + lost):P0}"
            : "Live win rate: no live pursuit outcomes recorded yet.");

        if (_analytics is { TotalEngagements: > 0 } a && (a.Won + a.Lost) > 0)
        {
            var topBuyer = a.ByBuyerType
                .Where(b => b.Won + b.Lost > 0)
                .OrderByDescending(b => b.WinRate)
                .ThenByDescending(b => b.Won + b.Lost)
                .FirstOrDefault();
            if (topBuyer is not null)
            {
                sb.AppendLine($"Best-performing buyer type: {topBuyer.Bucket} ({topBuyer.Won}/{topBuyer.Won + topBuyer.Lost} = {topBuyer.WinRate:P0}).");
            }

            if (a.AvgPursuitDuration.HasValue)
            {
                sb.AppendLine($"Avg time from open to outcome: {a.AvgPursuitDuration.Value.TotalDays:F0} day(s).");
            }
        }

        // Methodology emission removed in Batch 92c — MCP tool descriptions
        // + system prompt now carry KOR methodology canonically.
        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        if (Selected is null)
        {
            return string.Empty;
        }

        var e = Selected.Engagement;
        var sb = new StringBuilder();
        sb.AppendLine($"Selected engagement: {Selected.OpportunityKey} — {Selected.ProjectName}");
        sb.AppendLine($"Buyer: {Selected.Buyer}");
        sb.AppendLine($"Stage: {e.Stage}");
        if (!string.IsNullOrWhiteSpace(e.OwnerStaffId))
        {
            sb.AppendLine($"Owner: {e.OwnerStaffId}");
        }

        if (e.ProposedFee.HasValue)
        {
            sb.AppendLine($"Proposed fee: {e.ProposedFee.Value.ToString("C0", CultureInfo.CurrentCulture)}");
        }

        if (e.TargetMargin.HasValue)
        {
            sb.AppendLine($"Target margin: {e.TargetMargin.Value:0.#}%");
        }

        // Snapshot the two collections (Batch 102 audit pattern) — both are
        // ObservableCollection mutated on the UI thread, BuildLocalContext
        // runs on the AI worker thread.
        var activities = Activities.ToArray();
        var contacts = Contacts.ToArray();

        if (activities.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent activity:");
            foreach (var a in activities.Take(8))
            {
                sb.AppendLine($"  {a.OccurredDisplay} {a.TypeDisplay}: {a.Subject}");
            }
        }

        if (contacts.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Contacts:");
            foreach (var c in contacts)
            {
                var primary = c.IsPrimary ? " (primary)" : string.Empty;
                sb.AppendLine($"  {c.DisplayName}{primary} — {c.Role}; {c.Email}; {c.Phone}");
            }
        }

        if (_deltekContext is { } dc && (dc.ProjectCount > 0 || dc.Company is not null))
        {
            sb.AppendLine();
            DeltekClientIntelligenceFormatter.Append(sb, dc);
        }

        return sb.ToString();
    }
}
