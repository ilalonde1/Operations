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
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Observations;
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
    private readonly ICrmEngagementStore _engagementStore;
    private readonly ICrmActivityStore _activityStore;
    private readonly ICrmContactStore _contactStore;
    private readonly IOpportunityObservationStore _observationStore;
    private readonly IOpportunityInterestedFirmStore _interestStore;

    // Frozen so the VM can hand them out cross-thread (XAML binds on UI thread but
    // RefreshHeartbeatAsync runs the assignment off the UI thread). Mirrors the
    // FileSync KPI-brush convention. Pulled from KorTheme.xaml when the app is up
    // so retuning the theme retunes the heartbeat strip; literal fallbacks match
    // the theme values so static-init order can't crash.
    private static readonly Brush HealthGreen = ResolveOrFallback("Risk.HighConfidence.Foreground", 0x16, 0x65, 0x34);
    private static readonly Brush HealthAmber = ResolveOrFallback("Risk.AtRisk.Foreground", 0xA1, 0x62, 0x07);
    private static readonly Brush HealthRed = ResolveOrFallback("Risk.Critical.Foreground", 0xB9, 0x1C, 0x1C);
    private static readonly Brush HealthNeutral = ResolveOrFallback("CorporateBlue", 0x2F, 0x54, 0x96);

    private OpportunityRowView? _selected;
    private CrmEngagement? _selectedEngagement;
    private DeltekClientIntelligence? _selectedIntelligence;
    private string? _selectedSourceUrl;
    private string? _selectedDescription;
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
    private bool _primeConsultantOnly = true;

    public OpportunitiesViewModel(
        IOpportunityStore store,
        IHeartbeatStore heartbeatStore,
        IOpportunityScoringService scoringService,
        IIngestionRunStore ingestionRunStore,
        IIngestionTriggerStore ingestionTriggerStore,
        IOpportunitySourceStore sourceStore,
        IDeltekClientContextService deltekContextService,
        ICrmEngagementStore engagementStore,
        ICrmActivityStore activityStore,
        ICrmContactStore contactStore,
        IOpportunityObservationStore observationStore,
        IOpportunityInterestedFirmStore interestStore)
    {
        _store = store;
        _heartbeatStore = heartbeatStore;
        _scoringService = scoringService;
        _ingestionRunStore = ingestionRunStore;
        _ingestionTriggerStore = ingestionTriggerStore;
        _sourceStore = sourceStore;
        _deltekContextService = deltekContextService ?? throw new ArgumentNullException(nameof(deltekContextService));
        _engagementStore = engagementStore ?? throw new ArgumentNullException(nameof(engagementStore));
        _activityStore = activityStore ?? throw new ArgumentNullException(nameof(activityStore));
        _contactStore = contactStore ?? throw new ArgumentNullException(nameof(contactStore));
        _observationStore = observationStore ?? throw new ArgumentNullException(nameof(observationStore));
        _interestStore = interestStore ?? throw new ArgumentNullException(nameof(interestStore));

        FilteredOpportunitiesView = CollectionViewSource.GetDefaultView(Opportunities);
        FilteredOpportunitiesView.Filter = OpportunityFilterPredicate;
    }

    public ObservableCollection<OpportunityRowView> Opportunities { get; } = new();

    /// <summary>Filter-aware projection bound by the DataGrid. <see cref="Opportunities"/>
    /// stays as the full set so AI context, headlines, etc. see everything.</summary>
    public ICollectionView FilteredOpportunitiesView { get; }

    public ObservableCollection<IngestionRunRowView> IngestionRuns { get; } = new();

    /// <summary>The CrmEngagement linked to <see cref="Selected"/>, or null if none yet.</summary>
    public CrmEngagement? SelectedEngagement
    {
        get => _selectedEngagement;
        private set
        {
            if (SetField(ref _selectedEngagement, value))
            {
                OnPropertyChanged(nameof(HasEngagement));
                OnPropertyChanged(nameof(NoEngagement));
                OnPropertyChanged(nameof(SelectedEngagementStageDisplay));
            }
        }
    }

    public bool HasSelected => Selected is not null;
    public bool HasEngagement => _selectedEngagement is not null;
    public bool NoEngagement => Selected is not null && _selectedEngagement is null;

    public string SelectedEngagementStageDisplay =>
        _selectedEngagement is null ? "" : _selectedEngagement.Stage.ToString();

    /// <summary>URL of the most recent linked OpportunityObservation, if any. Used by the
    /// "Open RFP" button on the detail panel.</summary>
    public string? SelectedSourceUrl
    {
        get => _selectedSourceUrl;
        private set
        {
            if (SetField(ref _selectedSourceUrl, value))
            {
                OnPropertyChanged(nameof(HasSourceUrl));
            }
        }
    }

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(_selectedSourceUrl);

    public string? SelectedDescription
    {
        get => _selectedDescription;
        private set
        {
            if (SetField(ref _selectedDescription, value))
            {
                OnPropertyChanged(nameof(HasDescription));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(_selectedDescription);

    /// <summary>Sorted, signed scoring factors for <see cref="Selected"/>.
    /// Populated by <see cref="LoadSelectedDetailAsync"/>; empty when the
    /// opportunity has no matched terms or no row is selected.</summary>
    public ObservableCollection<ScoreFactor> SelectedScoreFactors { get; } = new();

    public bool HasScoreFactors => SelectedScoreFactors.Count > 0;

    /// <summary>Header text for the "Why this score?" Expander, includes
    /// the factor count so users know how much there is to expand into.</summary>
    public string ScoreFactorsHeader
    {
        get
        {
            var n = SelectedScoreFactors.Count;
            return n == 1 ? "Why this score? (1 factor)" : $"Why this score? ({n} factors)";
        }
    }

    public ObservableCollection<CrmActivity> SelectedActivities { get; } = new();
    public ObservableCollection<CrmContact> SelectedContacts { get; } = new();
    public ObservableCollection<OpportunityInterestedFirm> SelectedInterestedFirms { get; } = new();
    public bool HasSelectedInterestedFirms => SelectedInterestedFirms.Count > 0;

    public OpportunityRowView? Selected
    {
        get => _selected;
        set
        {
            var oldId = _selected?.Model.Id;
            var newId = value?.Model.Id;
            if (!SetField(ref _selected, value))
            {
                return;
            }

            if (oldId != newId)
            {
                // Different opportunity selected - clear stale per-row state
                // synchronously so write paths can never see the previous row's
                // engagement and write against the wrong pursuit.
                SelectedEngagement = null;
                SelectedSourceUrl = null;
                SelectedDescription = null;
                SelectedScoreFactors.Clear();
                OnPropertyChanged(nameof(HasScoreFactors));
                OnPropertyChanged(nameof(ScoreFactorsHeader));
                SelectedActivities.Clear();
                SelectedContacts.Clear();
            }

            OnPropertyChanged(nameof(HasSelected));
            OnPropertyChanged(nameof(NoEngagement));
            _ = LoadSelectedIntelligenceAsync(CancellationToken.None);
            _ = LoadSelectedDetailAsync(CancellationToken.None);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
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

    public bool PrimeConsultantOnly
    {
        get => _primeConsultantOnly;
        set
        {
            if (SetField(ref _primeConsultantOnly, value))
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

    /// <summary>
    /// Looks up a brush from <c>App.Current.Resources</c>; if unavailable (static
    /// init runs before App.xaml loads, or the key is missing), falls back to the
    /// supplied RGB literal. Keeps the heartbeat strip retunable from KorTheme.xaml
    /// without risking a NullReferenceException at type-load time.
    /// </summary>
    private static Brush ResolveOrFallback(string key, byte r, byte g, byte b)
    {
        if (System.Windows.Application.Current?.Resources[key] is Brush brush)
        {
            return brush;
        }

        return Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading…";
        try
        {
            var rows = await _store.ListAsync(ct, includeClosed: false, includeNonPrime: true).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

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
                ? "No opportunities yet — click \"New Opportunity\" or pick a source from \"Run Source ▾\" to populate."
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
        var captured = _selected;
        if (captured is null
            || captured.Model.DeltekClientId is not { } clientId
            || string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        try
        {
            var loaded = await _deltekContextService
                .LoadAsync(clientId, ct)
                .ConfigureAwait(true);
            if (_selected?.Model.Id != captured.Model.Id
                || !string.Equals(_selected?.Model.DeltekClientId, clientId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedIntelligence = loaded;
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
    /// Fetches the CrmEngagement (if any) linked to <see cref="Selected"/>, plus
    /// its activity log and contact list. Best-effort: a DB hiccup leaves the
    /// detail panel empty rather than crashing.
    /// </summary>
    private async Task LoadSelectedDetailAsync(CancellationToken ct)
    {
        var current = _selected;
        if (current is null)
        {
            SelectedEngagement = null;
            SelectedSourceUrl = null;
            SelectedDescription = null;
            SelectedScoreFactors.Clear();
            OnPropertyChanged(nameof(HasScoreFactors));
            OnPropertyChanged(nameof(ScoreFactorsHeader));
            SelectedActivities.Clear();
            SelectedContacts.Clear();
            SelectedInterestedFirms.Clear();
            OnPropertyChanged(nameof(HasSelectedInterestedFirms));
            return;
        }

        try
        {
            var engagement = await _engagementStore.GetByOpportunityAsync(current.Model.Id, ct).ConfigureAwait(true);
            if (_selected?.Model.Id != current.Model.Id)
            {
                return;
            }

            SelectedEngagement = engagement;
            SelectedSourceUrl = null;
            SelectedDescription = null;
            SelectedScoreFactors.Clear();
            OnPropertyChanged(nameof(HasScoreFactors));
            OnPropertyChanged(nameof(ScoreFactorsHeader));
            SelectedActivities.Clear();
            SelectedContacts.Clear();
            SelectedInterestedFirms.Clear();
            OnPropertyChanged(nameof(HasSelectedInterestedFirms));

            // Source detail: pull observations and pick the freshest usable URL and description.
            try
            {
                var observations = await _observationStore.ListByOpportunityAsync(current.Model.Id, ct).ConfigureAwait(true);
                if (_selected?.Model.Id == current.Model.Id)
                {
                    var freshest = observations
                        .Where(o => o.IsActive)
                        .OrderByDescending(o => o.PostedDateUtc ?? o.IngestedAtUtc)
                        .ToList();

                    SelectedSourceUrl = freshest
                        .Where(o => !string.IsNullOrWhiteSpace(o.Url)
                                    && (o.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                        || o.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                        .Select(o => o.Url)
                        .FirstOrDefault();

                    SelectedDescription = freshest
                        .Where(o => !string.IsNullOrWhiteSpace(o.Description))
                        .Select(o => o.Description)
                        .FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                // T4.003 audit fix: best-effort but logged so the data gap is
                // diagnosable from the log even when the panel falls back gracefully.
                Serilog.Log.Warning(ex, "Observation lookup for opportunity {OpportunityId} failed; URL/description left null.", current.Model.Id);
            }

            try
            {
                var explanation = _scoringService.Explain(current.Model);
                if (_selected?.Model.Id == current.Model.Id)
                {
                    foreach (var f in explanation.Factors)
                    {
                        SelectedScoreFactors.Add(f);
                    }
                    OnPropertyChanged(nameof(HasScoreFactors));
                    OnPropertyChanged(nameof(ScoreFactorsHeader));
                }
            }
            catch (Exception ex)
            {
                // T4.003 audit fix: log the failure so silent score-factor losses
                // are traceable post-incident.
                Serilog.Log.Warning(ex, "Score explanation for opportunity {OpportunityId} failed; factors left empty.", current.Model.Id);
            }

            if (engagement is null)
            {
                return;
            }

            var activities = await _activityStore.ListByEngagementAsync(engagement.Id, ct).ConfigureAwait(true);
            var contacts = await _contactStore.ListByEngagementAsync(engagement.Id, ct).ConfigureAwait(true);
            if (_selected?.Model.Id != current.Model.Id)
            {
                return;
            }

            foreach (var a in activities.OrderByDescending(x => x.OccurredAtUtc).Take(20))
            {
                if (!SelectedActivities.Any(x => x.Id == a.Id))
                {
                    SelectedActivities.Add(a);
                }
            }

            foreach (var c in contacts.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (!SelectedContacts.Any(x => x.Id == c.Id))
                {
                    SelectedContacts.Add(c);
                }
            }

            try
            {
                var firms = await _interestStore.ListByOpportunityAsync(current.Model.Id, ct).ConfigureAwait(true);
                if (_selected?.Model.Id == current.Model.Id)
                {
                    SelectedInterestedFirms.Clear();
                    foreach (var f in firms)
                        SelectedInterestedFirms.Add(f);
                    OnPropertyChanged(nameof(HasSelectedInterestedFirms));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Interested firm load for opportunity {OpportunityId} failed.", current.Model.Id);
            }
        }
        catch (OperationCanceledException)
        {
            // window closing or row changed
        }
        catch (Exception ex)
        {
            // Round 37c (BD-AUDIT-20260530-R2 T4.003): the inner catches got
            // a logger in Round 35b but this outer net was missed — every
            // unhandled detail-load failure (engagement store crash, activity
            // listing crash, contact store crash) cleared the panels silently.
            // Now it logs the stack first so the root cause is traceable
            // before the UI fallback kicks in.
            Serilog.Log.Warning(ex, "Opportunity detail load failed for opportunity {OpportunityId}; panels cleared.", current.Model.Id);

            if (_selected?.Model.Id == current.Model.Id)
            {
                SelectedEngagement = null;
                SelectedSourceUrl = null;
                SelectedDescription = null;
                SelectedScoreFactors.Clear();
                OnPropertyChanged(nameof(HasScoreFactors));
                OnPropertyChanged(nameof(ScoreFactorsHeader));
                SelectedActivities.Clear();
                SelectedContacts.Clear();
                SelectedInterestedFirms.Clear();
                OnPropertyChanged(nameof(HasSelectedInterestedFirms));
            }

            // best-effort detail load; leave panels empty
        }
    }

    /// <summary>Ensures a CrmEngagement exists for the currently selected
    /// opportunity. Public entrypoint for UI buttons that don't capture the
    /// row themselves.</summary>
    public Task<CrmEngagement> EnsureEngagementAsync(string actor, CancellationToken ct)
    {
        var current = _selected ?? throw new InvalidOperationException("No opportunity selected.");
        return EnsureEngagementForAsync(current, actor, ct);
    }

    /// <summary>Worker form that operates on an explicitly-captured row.
    /// Use this from write paths so a row-switch mid-await cannot redirect
    /// the write to the wrong opportunity.</summary>
    private async Task<CrmEngagement> EnsureEngagementForAsync(
        OpportunityRowView row, string actor, CancellationToken ct)
    {
        // Trust the cached engagement only if it belongs to this row. This
        // defends against row-switch races with in-flight detail loads.
        if (_selectedEngagement is { } cached && cached.OpportunityId == row.Model.Id)
        {
            return cached;
        }

        // No cached engagement (or stale cache): fetch fresh, then create if
        // missing. The existence check prevents duplicate draft engagements.
        var existing = await _engagementStore.GetByOpportunityAsync(row.Model.Id, ct).ConfigureAwait(true);
        if (existing is not null)
        {
            if (_selected?.Model.Id == row.Model.Id && _selectedEngagement is null)
            {
                SelectedEngagement = existing;
            }
            return existing;
        }

        var draft = new CrmEngagement
        {
            OpportunityId = row.Model.Id,
            Stage = CrmEngagementStage.Drafting,
            OwnerStaffId = row.Model.OwnerStaffId,
        };
        var saved = await _engagementStore.InsertAsync(draft, actor, ct).ConfigureAwait(true);

        // Bump status using the captured row, not _selected, so a row switch
        // mid-write cannot redirect the bump to a different opportunity.
        if (row.Model.Status is OpportunityStatus.New)
        {
            try
            {
                await ChangeStatusAsync(row, OpportunityStatus.Pursuing, actor, ct).ConfigureAwait(true);
            }
            catch (OpportunityConcurrencyException)
            {
                // engagement exists, status bump is a courtesy
            }
        }

        if (_selected?.Model.Id == row.Model.Id && _selectedEngagement is null)
        {
            SelectedEngagement = saved;
        }
        return saved;
    }

    /// <summary>
    /// Logs a Note-type activity against the selected opportunity. Creates the
    /// engagement first if one doesn't exist yet (the lightweight-journal flow).
    /// </summary>
    public async Task LogActivityAsync(string subject, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject required.", nameof(subject));
        }
        var current = _selected;
        if (current is null)
        {
            return;
        }

        var engagement = await EnsureEngagementForAsync(current, actor, ct).ConfigureAwait(true);
        var activity = new CrmActivity
        {
            EngagementId = engagement.Id,
            ActivityType = CrmActivityType.Note,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Subject = subject.Trim(),
        };
        var saved = await _activityStore.AppendAsync(activity, actor, ct).ConfigureAwait(true);

        // Only update UI if we're still on this opportunity. Dedup because a
        // concurrent detail reload may have already populated this row.
        if (_selected?.Model.Id == current.Model.Id)
        {
            if (!SelectedActivities.Any(x => x.Id == saved.Id))
            {
                SelectedActivities.Insert(0, saved);
            }
            StatusMessage = $"Activity logged on {engagement.Stage} pursuit.";
        }
    }

    /// <summary>
    /// Adds a contact against the selected opportunity. Creates the engagement
    /// first if one doesn't exist yet. Email/phone are optional.
    /// </summary>
    public async Task AddContactAsync(string displayName, string? email, string? phone, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Name required.", nameof(displayName));
        }
        var current = _selected;
        if (current is null)
        {
            return;
        }
        var shouldBePrimary = SelectedContacts.Count == 0;

        var engagement = await EnsureEngagementForAsync(current, actor, ct).ConfigureAwait(true);
        var contact = new CrmContact
        {
            EngagementId = engagement.Id,
            DisplayName = displayName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            IsPrimary = shouldBePrimary,
        };
        var saved = await _contactStore.InsertAsync(contact, actor, ct).ConfigureAwait(true);

        if (_selected?.Model.Id == current.Model.Id)
        {
            if (!SelectedContacts.Any(x => x.Id == saved.Id))
            {
                SelectedContacts.Add(saved);
            }
            StatusMessage = $"Contact added to {engagement.Stage} pursuit.";
        }
    }

    /// <summary>
    /// Enabled OpportunitySources that have an automated provider behind them -
    /// i.e., something a manual "Run Now" trigger can actually dispatch.
    /// Excludes BdOutreach / Manual / Unknown source types.
    /// </summary>
    public async Task<IReadOnlyList<OpportunitySource>> ListRunnableSourcesAsync(CancellationToken ct)
    {
        var all = await _sourceStore.ListEnabledAsync(ct).ConfigureAwait(true);
        return all
            .Where(s => s.SourceType is not (OpportunitySourceType.BdOutreach
                                          or OpportunitySourceType.Manual
                                          or OpportunitySourceType.Unknown))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (PrimeConsultantOnly && row.Model.IsPrimeConsultantRfp != true)
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
        // within 30 days, list anything in Pursuing/Submitted (the hot list).
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
            .Where(r => r.Model.Status is OpportunityStatus.Pursuing or OpportunityStatus.Submitted)
            .OrderBy(r => r.Model.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue)
            .Take(20)
            .ToList();
        if (hot.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Hot list (Pursuing / Submitted):");
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
                            and not OpportunityStatus.Lost)
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
