#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.App.BusinessDevelopment;
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
    private readonly IPursuitContextStore _contextStore;
    private readonly Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore _interestStore;
    private readonly IPursuitFileStore _fileStore;
    private readonly IPursuitFileStorage _fileStorage;

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
        Kor.Opportunities.Data.Awards.ICanonicalOrgStore orgStore,
        IPursuitContextStore contextStore,
        Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore interestStore,
        IPursuitFileStore fileStore,
        IPursuitFileStorage fileStorage)
    {
        _engagementStore = engagementStore;
        _activityStore = activityStore;
        _contactStore = contactStore;
        _opportunityStore = opportunityStore;
        _deltekContextService = deltekContextService;
        _orgStore = orgStore;
        _contextStore = contextStore;
        _interestStore = interestStore;
        _fileStore = fileStore;
        _fileStorage = fileStorage;
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

    /// <summary>
    /// Mine = the owner, or a second seat (AssignedStaffIds, comma-separated mailboxes, as BD Director's "Choose engineer"
    /// writes them): a person given a seat on a pursuit sees it under Mine too. Exact matches only.
    /// </summary>
    internal static bool IsMine(CrmEngagement e, string? actor) =>
        !string.IsNullOrWhiteSpace(actor)
        && (string.Equals(e.OwnerStaffId, actor, StringComparison.OrdinalIgnoreCase)
            || (e.AssignedStaffIds ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(s => string.Equals(s, actor, StringComparison.OrdinalIgnoreCase)));

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

        if (_mineOnly && !IsMine(row.Engagement, CurrentActorUpn))
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
                    CrmStageNames.Display(stage), count, CrmEngagementRowView.BrushFor(stage),
                    stage.ToString(), IsActive: _stageFilterKey == stage.ToString()));
            }
        }

        OnPropertyChanged(nameof(HasStageSummary));
    }

    public ObservableCollection<CrmActivityRowView> Activities { get; } = new();

    public ObservableCollection<CrmContactRowView> Contacts { get; } = new();

    /// <summary>Attachments on the selected pursuit (RFP PDFs, proposals,
    /// call recordings). Bytes live on the LAN share.</summary>
    public ObservableCollection<PursuitFileRowView> Files { get; } = new();

    /// <summary>False when no share is configured — the view hides the Files panel.</summary>
    public bool FilesEnabled => _fileStorage.IsConfigured;

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
            var n = _analytics.TotalEngagements;
            // No win rate (Ian, 21 Oct 2026): most tracked pursuits were never closed won or lost, so any rate from these
            // rows measures the record-keeping, not KOR. The counts stay; the percentage does not.
            var backfill = _analytics.BackfillWins > 0
                ? $"  •  {_analytics.BackfillWins} earlier Deltek wins ({_analytics.BackfillWonFee.ToString("C0", CultureInfo.CurrentCulture)}) counted separately"
                : string.Empty;
            var avgWonFee = _analytics.AvgWonProposedFee > 0
                ? $"  •  avg won fee {_analytics.AvgWonProposedFee.ToString("C0", CultureInfo.CurrentCulture)}"
                : string.Empty;
            return $"{n} pursuit{(n == 1 ? "" : "s")}  •  won {won} / lost {lost} recorded here{avgWonFee}{backfill}";
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
                ? BdDateDisplay.Day(_deltekContext.LatestProjectStart.Value)
                : "—";
            return $"{_deltekContext.ClientName}: {_deltekContext.ProjectCount} project(s), lifetime fee {fee}, last opened {last}.";
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading pursuits…";
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
                ? "No pursuits yet — promote an opportunity from the Opportunities window to start one."
                : $"Loaded {engagements.Count} pursuit{(engagements.Count == 1 ? "" : "s")}.";
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

    private string _warmthSummary = string.Empty;
    private string _pursuitHistorySummary = string.Empty;
    private string _planTakersSummary = string.Empty;

    /// <summary>Plan 3.1: one computed line from the nightly email-warmth rollup —
    /// "Filed correspondence with acme.com: last 12d ago · 9 in 90d · …". Empty
    /// when the buyer is unresolved or has no rollup ("no data", never "cold").</summary>
    public string WarmthSummary
    {
        get => _warmthSummary;
        private set { if (SetField(ref _warmthSummary, value)) OnPropertyChanged(nameof(HasWarmth)); }
    }

    public bool HasWarmth => _warmthSummary.Length > 0;

    /// <summary>Plan 2.2a/2.2b: claim/reassign history + current stage age —
    /// first reader of the (previously write-only) audit tables.</summary>
    public string PursuitHistorySummary
    {
        get => _pursuitHistorySummary;
        private set { if (SetField(ref _pursuitHistorySummary, value)) OnPropertyChanged(nameof(HasPursuitHistory)); }
    }

    public bool HasPursuitHistory => _pursuitHistorySummary.Length > 0;

    /// <summary>Plan 2.3: who else took plans on this RFP (raw portal names —
    /// rendered as text, never faked into org identities). Empty collapses.</summary>
    public string PlanTakersSummary
    {
        get => _planTakersSummary;
        private set { if (SetField(ref _planTakersSummary, value)) OnPropertyChanged(nameof(HasPlanTakers)); }
    }

    public bool HasPlanTakers => _planTakersSummary.Length > 0;

    // ---- Claude's RFP review (migration 377): where the documents and the review stand, and the review itself ----

    private string? _reviewStatus;
    private PursuitReviewRow? _review;

    /// <summary>The pursuit's documents-and-review status in plain words; null hides the strip (not a portal tender, no review).</summary>
    public string? ReviewStatus
    {
        get => _reviewStatus;
        private set { if (SetField(ref _reviewStatus, value)) OnPropertyChanged(nameof(HasReviewStatus)); }
    }

    public bool HasReviewStatus => !string.IsNullOrEmpty(_reviewStatus);

    public PursuitReviewRow? Review
    {
        get => _review;
        private set
        {
            if (SetField(ref _review, value))
            {
                OnPropertyChanged(nameof(HasReview));
                OnPropertyChanged(nameof(ReviewHeadline));
                OnPropertyChanged(nameof(ReviewVerdict));
                OnPropertyChanged(nameof(ReviewVerdictBrush));
                OnPropertyChanged(nameof(ShowReviewStatusLine));
            }
        }
    }

    public bool HasReview => _review is not null;

    private bool _reviewRunning;

    /// <summary>The small status line: shown while there is no review, or while a new one is being read; a finished review needs no caption.</summary>
    public bool ShowReviewStatusLine => _review is null || _reviewRunning;

    /// <summary>For the screen render: puts a status and review on the strip without a database.</summary>
    internal void ShowReviewForRender(string? status, PursuitReviewRow? review, bool running)
    {
        _reviewRunning = running;
        ReviewStatus = status;
        Review = review;
        OnPropertyChanged(nameof(ShowReviewStatusLine));
    }

    /// <summary>The strip's one line beside the verdict: how many questions, and when they are due.</summary>
    public string ReviewHeadline
    {
        get
        {
            if (_review is null) return "";
            var questions = _review.Questions.Count == 0 ? "" : $"{_review.Questions.Count} question{(_review.Questions.Count == 1 ? "" : "s")} for the buyer"
                + (_review.QuestionsDeadline is { } d ? $", due {d}" : "");
            // The next thing to do leads: it is what makes the review something to act on.
            if (_review.Actions.FirstOrDefault() is not { } next)
                return questions.Length > 0 ? questions : "Claude read the RFP: no questions for the buyer.";
            var who = PursuitReviewWindow.PersonName(PursuitRfpReview.ResolveOwner(next.Owner, Selected?.Engagement.OwnerStaffId));
            return $"Next: {next.Action} ({who}, by {next.Due:ddd d MMM})" + (questions.Length > 0 ? "  ·  " + questions : "");
        }
    }

    public string ReviewVerdict => _review is null ? "" : RecommendationLabel(_review.Recommendation).ToUpperInvariant();
    public Brush ReviewVerdictBrush => RecommendationBrush(_review?.Recommendation ?? "decide");

    public static string RecommendationLabel(string recommendation) => recommendation switch
    {
        "go" => "Bid",
        "no-go" => "Don't bid",
        _ => "Your call",
    };

    /// <summary>The verdict's colour, the KOR palette as BD Director uses it: green bid, grey don't, KOR orange your call.</summary>
    public static Brush RecommendationBrush(string recommendation) => recommendation switch
    {
        "go" => BusinessDevelopment.Director.Palette.Green,
        "no-go" => BusinessDevelopment.Director.Palette.Muted,
        _ => BusinessDevelopment.Director.Palette.Orange,
    };

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Reloads the review strip for the selected pursuit (after a review opens or reports).</summary>
    public async Task RefreshReviewAsync()
    {
        if (Selected is { } s) await LoadReviewAsync(s.Id, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task LoadReviewAsync(long engagementId, CancellationToken ct)
    {
        if (AppServices.GetOptional<SqlPursuitReviewStore>() is not { } reviews) return;
        try
        {
            var status = await reviews.StatusAsync(engagementId, ct).ConfigureAwait(true);
            var review = await reviews.LatestAsync(engagementId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
            _reviewRunning = status is { RunId: not null, RunReportedAtUtc: null };
            ReviewStatus = status is null ? null : PursuitRfpReview.Describe(status, DateTimeOffset.UtcNow);
            Review = review;
            OnPropertyChanged(nameof(ShowReviewStatusLine));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "RFP review status load failed for engagement {EngagementId}.", engagementId);
        }
    }

    private async Task LoadDetailAsync(CancellationToken ct)
    {
        ReviewStatus = null;
        Review = null;
        Activities.Clear();
        Contacts.Clear();
        Files.Clear();
        DeltekContext = null;
        ResolvedDeltekClientId = null;
        WarmthSummary = string.Empty;
        PursuitHistorySummary = string.Empty;
        PlanTakersSummary = string.Empty;
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

            if (_fileStorage.IsConfigured)
            {
                // Individually guarded (review fix 2026-07-08): a transient
                // OpportunityFiles fault must NOT jump to the outer catch and
                // take the Deltek roll-up + Phase 2/3 context panels down with it.
                try
                {
                    var files = await _fileStore.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
                    foreach (var f in files)
                    {
                        Files.Add(new PursuitFileRowView(f));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Attachment list load failed for engagement {EngagementId}.", engagementId);
                }
            }

            await LoadReviewAsync(engagementId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;

            // Phase 2/3 context reads AFTER the core panel renders (review nit:
            // fetched activities must not wait on warmth/history round-trips) —
            // each best-effort and individually guarded.
            await LoadPursuitContextAsync(engagementId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;

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

    /// <summary>
    /// Phase 2/3 detail context (plan 2.2a/2.2b/2.3/3.1): email warmth,
    /// claim/reassign history + stage age, plan takers. All read-only, all
    /// best-effort, all guarded against mid-await row switches.
    /// </summary>
    private async Task LoadPursuitContextAsync(long engagementId, CancellationToken ct)
    {
        var row = Selected;
        if (row is null || row.Id != engagementId)
        {
            return;
        }

        // 3.1 — email warmth for the buyer org.
        if (row.Engagement.BuyerCanonicalOrgId is { } orgId)
        {
            try
            {
                var w = await _contextStore.GetWarmthAsync(orgId, ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
                // The rollup only refreshes while a pursuit of this buyer is
                // LIVE — surface the compute date once it's stale so a closed
                // pursuit's numbers read as "as of", never as current (review nit).
                var asOf = w is not null && (DateTimeOffset.Now - w.ComputedAtUtc).TotalDays > 2
                    ? $" (as of {BdDateDisplay.Day(w.ComputedAtUtc)})"
                    : "";
                if (w is { LastTouchUtc: not null })
                {
                    var days = Math.Max(0, (int)(DateTimeOffset.Now - w.LastTouchUtc.Value).TotalDays);
                    var top = string.IsNullOrWhiteSpace(w.TopCorrespondent) ? "" : $" · mostly {w.TopCorrespondent}";
                    WarmthSummary = $"Filed correspondence with @{w.Domain}: last {days}d ago · {w.Emails90d} in 90d · {w.EmailsAllTime} all-time{top}{asOf}";
                }
                else if (w is not null)
                {
                    WarmthSummary = $"No filed correspondence with @{w.Domain}.{asOf}";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Warmth read failed for engagement {EngagementId}.", engagementId);
            }
        }

        // 2.2a/2.2b — claim/reassign history + current stage age.
        try
        {
            var assignments = await _contextStore.ListAssignmentsAsync(engagementId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
            var stageSince = await _contextStore.GetStageSinceAsync(engagementId, (int)row.Engagement.Stage, ct).ConfigureAwait(true)
                             ?? row.Engagement.OpenedAtUtc; // F14 COALESCE contract (pre-history rows)
            if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;

            var parts = new List<string>();
            foreach (var a in assignments)
            {
                var d = BdDateDisplay.Day(a.AtUtc);
                parts.Add(a.Action switch
                {
                    "Grab" => $"Grabbed by {a.ToStaffId} {d}",
                    "Claim" => $"Claimed by {a.ToStaffId} {d}",
                    "Reassign" => $"Reassigned {a.FromStaffId} → {a.ToStaffId} {d}",
                    _ => $"{a.Action} {d}",
                });
            }

            var stageAgeDays = Math.Max(0, (int)(DateTimeOffset.Now - stageSince).TotalDays);
            parts.Add($"In {CrmStageNames.Display(row.Engagement.Stage)} {stageAgeDays}d");
            PursuitHistorySummary = string.Join("  ·  ", parts);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Pursuit-history read failed for engagement {EngagementId}.", engagementId);
        }

        // 2.3 — plan takers (opportunity-backed pursuits only; raw names).
        if (row.OpportunityId is { } oppId)
        {
            try
            {
                var firms = await _interestStore.ListByOpportunityAsync(oppId, ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested || Selected?.Id != engagementId) return;
                if (firms.Count > 0)
                {
                    // Distinct-name counts throughout — raw rows can repeat a
                    // firm across portals (review nit: '+N more' overstated).
                    var distinct = firms.Select(f => f.RawFirmName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var names = distinct.Take(12).ToList();
                    var more = distinct.Count > names.Count ? $" (+{distinct.Count - names.Count} more)" : "";
                    PlanTakersSummary = $"Plan takers ({distinct.Count}): {string.Join(" · ", names)}{more}";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Plan-taker read failed for opportunity {OpportunityId}.", oppId);
            }
        }
    }

    public async Task<CrmEngagement> ChangeStageAsync(CrmEngagementRowView row, CrmEngagementStage newStage, string actor, CancellationToken ct, string? outcomeReason = null)
    {
        // ClosedAtUtc is server-owned (fix F7): the store's UpdateAsync stamps it
        // on entering Won/Lost/Archived, preserves it while terminal, and clears it on
        // reopen — identically for every close door.
        var reason = newStage == row.Engagement.Stage ? outcomeReason ?? row.Engagement.OutcomeReason
            : PursuitCloseReason.IsClosed(newStage) ? PursuitCloseReason.Require(outcomeReason) : null;
        var updated = row.Engagement with { Stage = newStage, OutcomeReason = reason };

        var saved = await _engagementStore.UpdateAsync(updated, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{row.OpportunityKey}: stage → {CrmStageNames.Display(newStage)}.";
        return saved;
    }

    /// <summary>
    /// The lead's own next step and its date, the same fields the weekly to-do list and the Director's asks write. The date
    /// is 5 pm local on the chosen day, as a roll-up reply sets it.
    /// </summary>
    public async Task<CrmEngagement> SetNextStepAsync(CrmEngagementRowView row, string note, DateTime dueDay, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Enter the next step.", nameof(note));
        var due = new DateTimeOffset(DateTime.SpecifyKind(dueDay.Date.AddHours(17), DateTimeKind.Local)).ToUniversalTime();
        var saved = await _engagementStore.UpdateAsync(row.Engagement with { NextActionNote = note.Trim(), NextActionDueUtc = due },
            actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{row.OpportunityKey}: next step due {dueDay:ddd d MMM}.";
        return saved;
    }

    /// <summary>
    /// Marks the pursuit Submitted with the proposal that was sent, stored in the database, the same as the Director's
    /// "Mark submitted": a new PDF is attached first, or the existing submitted attachment is kept.
    /// </summary>
    public async Task<CrmEngagement> MarkSubmittedAsync(CrmEngagementRowView row, BusinessDevelopment.Director.SubmittedDetails submission,
        string actor, CancellationToken ct)
    {
        var record = row.Engagement;
        var error = BusinessDevelopment.Director.SubmittedDetails.Validate(submission.FileName, submission.FolderLink, submission.SourcePath, record.SubmittedFileId);
        if (error is not null) throw new ArgumentException(error, nameof(submission));
        PursuitFile attachment;
        if (!string.IsNullOrWhiteSpace(submission.SourcePath))
        {
            // The one attachment path (database content), the same as a dropped file.
            attachment = (await _fileStorage.StoreAsync(record.Id, record.OpportunityId, row.OpportunityKey, submission.SourcePath!,
                actor, ct).ConfigureAwait(true)).File;
            if (Selected?.Id == record.Id) Files.Insert(0, new PursuitFileRowView(attachment));
        }
        else
        {
            attachment = (await _fileStore.ListByEngagementAsync(record.Id, ct).ConfigureAwait(true))
                .SingleOrDefault(f => f.Id == record.SubmittedFileId)
                ?? throw new InvalidOperationException("The existing submitted attachment is unavailable. Browse to the PDF again.");
        }
        var saved = await _engagementStore.UpdateAsync(record with
        {
            Stage = CrmEngagementStage.Submitted,
            OutcomeReason = null,
            SubmittedFileId = attachment.Id,
            SubmittedFileName = submission.FileName.Trim(),
            FolderLink = string.IsNullOrWhiteSpace(submission.FolderLink) ? record.FolderLink : submission.FolderLink.Trim(),
        }, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{row.OpportunityKey}: submitted, with {attachment.FileName} stored.";
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
            OutcomeReason = string.IsNullOrWhiteSpace(reason) ? fresh.OutcomeReason : PursuitCloseReason.Require(reason),
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
        if (PursuitCloseReason.IsClosed(edited.Stage))
            edited = edited with { OutcomeReason = PursuitCloseReason.Require(edited.OutcomeReason) };
        var saved = edited.Id == 0
            ? await _engagementStore.InsertAsync(edited, actor, ct).ConfigureAwait(true)
            : await _engagementStore.UpdateAsync(edited, actor, ct).ConfigureAwait(true);

        // Both write outputs omit BuyerOrgName. Keep any existing row's buyer;
        // a brand-new BD-tracking pursuit resolves its buyer on the next Refresh.
        ReplaceEngagement(saved);
        StatusMessage = $"Saved engagement {saved.Id}.";
        return saved;
    }

    /// <summary>
    /// Moves a pursuit to a new owner through the Overwatch store (the one logged path: OpportunityAssignmentLog, the linked
    /// RFP's owner, refused when someone else moved it first or it has closed), then reloads so the row shows the new owner.
    /// </summary>
    public async Task<ReassignOutcome> ReassignOwnerAsync(CrmEngagement pursuit, string toOwner, string actor, CancellationToken ct)
    {
        if (AppServices.GetOptional<IPursuitOverwatchStore>() is not { } store)
        {
            StatusMessage = "The owner could not be changed here; use BD Director, Reassign.";
            return ReassignOutcome.Ignored;
        }
        var outcome = await store.ReassignAsync(pursuit.Id, pursuit.OpportunityId, pursuit.OwnerStaffId ?? "", toOwner, actor,
            "Changed in My Pursuits.", ct).ConfigureAwait(true);
        if (outcome == ReassignOutcome.Reassigned) await LoadAsync(ct).ConfigureAwait(true);
        StatusMessage = outcome switch
        {
            ReassignOutcome.Reassigned => $"Pursuit {pursuit.Id} is now owned by {toOwner}; they have been told.",
            ReassignOutcome.OwnerChanged => "Someone else changed the owner first. Refresh to see who owns it.",
            ReassignOutcome.DuplicateForTarget => $"{toOwner} already holds a pursuit for this buyer and region; the owner was not changed.",
            ReassignOutcome.PursuitClosed => "This pursuit is closed (Won or Lost); the owner was not changed.",
            _ => StatusMessage,
        };
        return outcome;
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

    /// <summary>
    /// Stream dropped/browsed files into the pursuit's attachment rows. Returns how many landed; a per-file failure is
    /// reported but doesn't abort the rest. The write targeted engagementId
    /// explicitly, so a mid-copy row switch only skips the UI insert (fix
    /// pattern shared with activity/contact adds).
    /// </summary>
    public async Task<int> AddFilesAsync(
        long engagementId, long? opportunityId, string pursuitLabel, IReadOnlyList<string> sourcePaths, string actor, CancellationToken ct)
    {
        var added = 0;
        var failures = 0;
        foreach (var source in sourcePaths)
        {
            try
            {
                var stored = await _fileStorage.StoreAsync(engagementId, opportunityId, pursuitLabel, source, actor, ct).ConfigureAwait(true);
                var saved = stored.File;

                if (Selected?.Id == engagementId)
                {
                    Files.Insert(0, new PursuitFileRowView(saved));
                }

                added++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                Serilog.Log.Warning(ex, "Attaching '{File}' failed (engagement {EngagementId}).", source, engagementId);
            }
        }

        StatusMessage = failures == 0
            ? $"Attached {added} file{(added == 1 ? "" : "s")}."
            : $"Attached {added}; {failures} could not be attached. Each file must be 250 MB or smaller.";
        return added;
    }

    /// <summary>Remove the attachment row and its content. Legacy rows also remove
    /// their share copy (best-effort). Submitted attachments are protected by the FK.</summary>
    public async Task RemoveFileAsync(PursuitFileRowView row, CancellationToken ct)
    {
        var storedPath = await _fileStore.RemoveAsync(row.Id, ct).ConfigureAwait(true);
        Files.Remove(row);
        if (!string.IsNullOrWhiteSpace(storedPath))
        {
            _fileStorage.TryDelete(storedPath!);
        }

        StatusMessage = $"Removed {row.FileName}.";
    }

    private void ReplaceEngagement(CrmEngagement saved)
    {
        for (int i = 0; i < Engagements.Count; i++)
        {
            if (Engagements[i].Id == saved.Id)
            {
                var opp = Engagements[i].Opportunity;
                Engagements[i] = new CrmEngagementRowView(saved, opp, Engagements[i].Buyer);

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
                sb.AppendLine($"  {CrmStageNames.Display(g.Key)}: {g.Count()}");
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
                sb.AppendLine($"  [{CrmStageNames.Display(r.Engagement.Stage)}] {r.OpportunityKey} {r.ProjectName} ({r.Buyer}) — owner {r.OwnerDisplay}");
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
            sb.AppendLine($"NOTE: {backfillWins} of the Won engagements are a historical Deltek backfill (ExternalSource 'Deltek.CustomProposal') — flat history with no owner and no loss denominator.");
        }

        // Ian, 21 Oct 2026: never quote a win rate from these rows; most tracked pursuits were never closed won or lost.
        sb.AppendLine($"Live pursuits recorded won: {won}; lost: {lost}. KOR's real win rate is not yet known: most tracked pursuits were never closed as won or lost, so never state or estimate a win rate from this data.");

        if (_analytics is { TotalEngagements: > 0 } a && (a.Won + a.Lost) > 0)
        {
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
        sb.AppendLine($"Stage: {CrmStageNames.Display(e.Stage)}");
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

        // Phase 2/3 context lines (plan 2.2/2.3/3.1) — same strings the panel
        // shows, so the AI and the user read identical facts.
        if (HasWarmth) { sb.AppendLine(); sb.AppendLine(WarmthSummary); }
        if (HasPursuitHistory) { sb.AppendLine(PursuitHistorySummary); }
        if (HasPlanTakers) { sb.AppendLine(PlanTakersSummary); }

        return sb.ToString();
    }

    /// <summary>
    /// Plan 2.5: link the Won pursuit to the Deltek project it became.
    /// Read-modify-write on the fresh row; the store's stage-consistency CASE
    /// only persists WonProjectWbs1 while the stage is Won.
    /// </summary>
    public async Task<CrmEngagement> SetWonProjectAsync(CrmEngagement fresh, string wbs1, string actor, CancellationToken ct)
    {
        var updated = fresh with { WonProjectWbs1 = wbs1.Trim() };
        var saved = await _engagementStore.UpdateAsync(updated, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"Linked to Deltek project {saved.WonProjectWbs1}.";
        return saved;
    }
}
