#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.Services;
using Kor.Operations.Core;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class BdDirectorViewModel : ObservableObject, IDisposable
{
    public const string PeopleCaption = "Everyone who owns a live pursuit. A live pursuit is Pending or Submitted. Counts are for that person's pursuits; opens are the BD screens they opened.";
    public const string FunnelCaption = "Live pursuits by stage, how long they have sat there, and what has been decided. Deltek history is counted separately and never added to the live numbers.";
    public const string NumberCaption = "Targets you set, against what the records show today. Won fee and proposals submitted are not offered as targets.";
    public const string AccountsCaption = "The firm's Deltek clients, most KOR projects first. Counts are Deltek records, proposals included, unless the row says wins. Select an account to read its Deltek summary.";
    public const string AsksCaption = "One bounded thing, for one person, by a date. Done and declined asks stay on the record.";
    public const string DecisionsCaption = "What was decided, why, and what was expected. A decision is never edited; it is scored Right, Wrong or Unclear when its review date comes.";
    public const string KnowledgeCaption = "What the books assert, one sentence each, cited to the passage it rests on. Keep what holds for KOR; retire what does not. Nothing is deleted.";
    public const string MarketCaption = "What is coming: signals on the firms we watch, planned projects not yet procured, RFPs closing, and seats no structural engineer holds yet. All of it is what the BD Dashboard already reads.";
    public const string BriefCaption = "The Director's brief: five questions a week over these tabs and the rule bank, written from the week's export and imported here. Open a citation to read the passage.";
    public const string PendingBidsCaption = "Bids in flight: each pursuit that came from an RFP, who holds it, when it is due, and whether it went in. Pursuits without an RFP behind them are relationship work and stay on People and Accounts.";

    private readonly IBdDirectorStore _director;
    private readonly IPursuitOverwatchStore _pursuits;
    private readonly ICrmEngagementStore _records;
    private readonly IBdAttributionStore _attribution;
    private readonly IBdDirectorLedgerStore _ledger;
    private readonly IDeltekClientContextService _deltek;
    private readonly IKnowledgeClaimStore _claims;
    private readonly IBdDashboardStore _dashboard;
    private readonly IOpportunityStore _opportunities;
    private readonly IDirectorBriefStore _brief;
    private readonly IPursuitFileStore _files;
    private readonly ICrmActivityStore _activities;
    private readonly IBdStaffDirectory _staff;
    private CancellationTokenSource? _briefCts;
    private BdDirectorCitationRow? _selectedBriefCitation;
    private readonly CancellationTokenSource _knowledgeLifetime = new();
    private CancellationTokenSource? _claimsCts;
    private CancellationTokenSource? _passageCts;
    private BdKnowledgeSourceRow? _selectedBook;
    private BdKnowledgeConceptRow? _selectedConcept;
    private BdKnowledgeClaimRow? _selectedClaim;
    private IReadOnlyDictionary<long, ClaimVerdict> _claimVerdicts = new Dictionary<long, ClaimVerdict>();
    private string _claimFilter = "Unreviewed";
    private bool _sampleClaims;
    private CancellationTokenSource? _accountCts;
    private CancellationTokenSource? _asksCts;
    private BdDirectorAccountRow? _selectedAccount;
    private BdDirectorTargetRow? _selectedTarget;
    private BdDirectorAskRow? _selectedAsk;
    private BdDirectorDecisionRow? _selectedDecision;
    private string _askStatusFilter = "Open";
    private IReadOnlyList<PursuitOverwatchRow> _allPursuits = Array.Empty<PursuitOverwatchRow>();
    private BdDirectorPersonRow? _selectedPerson;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _isReassigning;
    private IReadOnlyList<PendingBidRow> _allPendingBids = Array.Empty<PendingBidRow>();
    private DateTimeOffset _bidsAsOf = DateTimeOffset.UtcNow;
    private bool _mineOnly;
    private string _currentActorUpn = "";
    private BdDirectorPendingBidRow? _selectedPendingBid;

    public BdDirectorViewModel(IBdDirectorStore director, IPursuitOverwatchStore pursuits,
        ICrmEngagementStore records, IBdAttributionStore attribution, IBdDirectorLedgerStore ledger,
        IDeltekClientContextService deltek, IKnowledgeClaimStore claims, IBdDashboardStore dashboard,
        IOpportunityStore opportunities, IDirectorBriefStore brief, IPursuitFileStore files,
        ICrmActivityStore activities, IBdStaffDirectory staff)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _pursuits = pursuits ?? throw new ArgumentNullException(nameof(pursuits));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _deltek = deltek ?? throw new ArgumentNullException(nameof(deltek));
        _claims = claims ?? throw new ArgumentNullException(nameof(claims));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _opportunities = opportunities ?? throw new ArgumentNullException(nameof(opportunities));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
    }

    public ObservableCollection<BdDirectorPersonRow> People { get; } = new();
    public ObservableCollection<BdDirectorMyDayRow> MyDay { get; } = new();
    public ObservableCollection<string> MyDayRegions { get; } = new();
    public string MyDayMessage { get; private set; } = "Your next conversations, asks and untracked clients.";
    public ObservableCollection<BdDirectorPursuitRow> SelectedPursuits { get; } = new();
    public ObservableCollection<BdDirectorStageRow> Stages { get; } = new();
    public ObservableCollection<BdDirectorDeadlineRow> Undecided { get; } = new();
    public ObservableCollection<AttributionSourceRow> Sources { get; } = new();
    public ObservableCollection<BdDirectorTargetRow> Targets { get; } = new();
    public ObservableCollection<BdDirectorAccountRow> Accounts { get; } = new();
    public ObservableCollection<BdDirectorAskRow> Asks { get; } = new();
    public ObservableCollection<BdDirectorDecisionRow> Decisions { get; } = new();
    public string BriefTrackRecordSummary { get; private set; } = "";
    public string BriefTrackRecordBreakdown { get; private set; } = "";
    public ObservableCollection<BdKnowledgeSourceRow> KnowledgeBooks { get; } = new();
    public ObservableCollection<BdKnowledgeClaimRow> KnowledgeClaims { get; } = new();
    public ObservableCollection<BdKnowledgeConceptRow> KnowledgeConcepts { get; } = new();
    public ObservableCollection<BdDirectorSignal> MarketSignals { get; } = new();
    public ObservableCollection<DashboardViewModel.DeadlineRow> MarketDeadlines { get; } = new();
    public ObservableCollection<BdForwardPipelineRow> ForwardPipeline { get; } = new();
    public ObservableCollection<BdOpenStructuralSeatRow> OpenSeats { get; } = new();
    public ObservableCollection<BdDirectorBriefRow> BriefSections { get; } = new();
    public ObservableCollection<BdDirectorPendingBidRow> PendingBids { get; } = new();
    public int RelationshipPursuits { get; private set; }
    public string PendingBidsSummary { get; private set; } = "";
    public BdDirectorPendingBidRow? SelectedPendingBid
    {
        get => _selectedPendingBid;
        set => SetField(ref _selectedPendingBid, value);
    }
    public string CurrentActorUpn
    {
        get => _currentActorUpn;
        set { if (SetField(ref _currentActorUpn, value.Trim())) FilterPendingBids(); }
    }
    public bool MineOnly
    {
        get => _mineOnly;
        set { if (SetField(ref _mineOnly, value)) FilterPendingBids(); }
    }
    public string MarketFunnel { get; private set; } = "";
    public string BriefMessage { get; private set; } = "";
    public string CitationStatus { get; private set; } = "";
    public BdDirectorCitationRow? SelectedBriefCitation
    {
        get => _selectedBriefCitation;
        set
        {
            if (!SetField(ref _selectedBriefCitation, value)) return;
            _selectedClaim = null; OnPropertyChanged(nameof(SelectedClaim));
            _passageCts?.Cancel(); _passageCts?.Dispose();
            _passageCts = CancellationTokenSource.CreateLinkedTokenSource(_knowledgeLifetime.Token);
            PassageLoadTask = LoadBriefPassageAsync(value, _passageCts.Token);
        }
    }
    public IReadOnlyList<string> ClaimFilters { get; } = new[] { "Unreviewed", "Kept", "Retired", "All" };
    public Task ClaimsLoadTask { get; private set; } = Task.CompletedTask;
    public Task PassageLoadTask { get; private set; } = Task.CompletedTask;
    public KnowledgePassageText? SelectedPassage { get; private set; }
    public string PassageMessage { get; private set; } = "Select a claim to read its passage.";
    public string ClaimsCaption => SelectedConcept is { } concept
        ? $"Concept · {concept.Concept.Name} · {concept.Concept.Claims} claims from {concept.Concept.Sources} books"
        : SelectedBook is { } book ? $"Book · {book.Source.Title} · {(_sampleClaims ? "Sample of unreviewed claims" : ClaimFilter)}"
        : "Select a book or concept to read its claims.";
    public string KnowledgeMessage { get; private set; } = "";
    public BdKnowledgeSourceRow? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (!SetField(ref _selectedBook, value)) return;
            _selectedConcept = null;
            OnPropertyChanged(nameof(SelectedConcept));
            _sampleClaims = false;
            ClaimsLoadTask = LoadKnowledgeClaimsAsync();
        }
    }
    public BdKnowledgeConceptRow? SelectedConcept
    {
        get => _selectedConcept;
        set
        {
            if (!SetField(ref _selectedConcept, value)) return;
            _sampleClaims = false;
            ClaimsLoadTask = LoadKnowledgeClaimsAsync();
        }
    }
    public string ClaimFilter
    {
        get => _claimFilter;
        set
        {
            if (!ClaimFilters.Contains(value) || !SetField(ref _claimFilter, value)) return;
            _selectedConcept = null;
            OnPropertyChanged(nameof(SelectedConcept));
            _sampleClaims = false;
            ClaimsLoadTask = LoadKnowledgeClaimsAsync();
        }
    }
    public BdKnowledgeClaimRow? SelectedClaim
    {
        get => _selectedClaim;
        set
        {
            if (!SetField(ref _selectedClaim, value)) return;
            _selectedBriefCitation = null; OnPropertyChanged(nameof(SelectedBriefCitation));
            CitationStatus = ""; OnPropertyChanged(nameof(CitationStatus));
            _passageCts?.Cancel(); _passageCts?.Dispose();
            _passageCts = CancellationTokenSource.CreateLinkedTokenSource(_knowledgeLifetime.Token);
            PassageLoadTask = LoadPassageAsync(value, _passageCts.Token);
        }
    }
    public IReadOnlyList<string> AskStatuses { get; } = new[] { "Open", "Done", "Declined", "Withdrawn", "All" };
    public string FeeCoverage { get; private set; } = "";
    public BdDirectorTargetRow? SelectedTarget { get => _selectedTarget; set => SetField(ref _selectedTarget, value); }
    public BdDirectorAskRow? SelectedAsk { get => _selectedAsk; set => SetField(ref _selectedAsk, value); }
    public BdDirectorDecisionRow? SelectedDecision { get => _selectedDecision; set => SetField(ref _selectedDecision, value); }
    public DeltekClientIntelligence? SelectedAccountDeltek { get; private set; }
    public string AccountMessage { get; private set; } = "Select an account to read its Deltek summary.";
    public bool HasAccountDeltek => SelectedAccountDeltek is not null;
    public bool HasAccountAr => SelectedAccountDeltek?.Ar is not null;
    public bool HasAccountPostedThrough => SelectedAccountDeltek?.SummaryPostedThrough is not null;
    public string AccountDegradedMessage => SelectedAccountDeltek?.HasDegradedSections == true ? "Some Deltek sections could not be read." : "";
    public Task AccountLoadTask { get; private set; } = Task.CompletedTask;
    public Task AsksLoadTask { get; private set; } = Task.CompletedTask;
    public BdDirectorAccountRow? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!SetField(ref _selectedAccount, value)) return;
            _accountCts?.Cancel();
            _accountCts?.Dispose();
            _accountCts = new CancellationTokenSource();
            AccountLoadTask = LoadSelectedAccountAsync(value, _accountCts.Token);
        }
    }
    public string AskStatusFilter
    {
        get => _askStatusFilter;
        set
        {
            if (value is null || !AskStatuses.Contains(value) || !SetField(ref _askStatusFilter, value)) return;
            AsksLoadTask = ReloadAsksSafelyAsync();
        }
    }
    public int Live { get; private set; }
    public int PeopleCount => People.Count;
    public DateTimeOffset? AsOf { get; private set; }
    public string Outcomes { get; private set; } = "";
    public string LostDetails { get; private set; } = "";
    public string DeadlineCaption { get; private set; } = "";
    public string SelectedPersonCaption => SelectedPerson is { } person
        ? $"Live pursuits · {person.DisplayName}"
        : "Select a person to see their live pursuits.";

    public BdDirectorPersonRow? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!SetField(ref _selectedPerson, value)) return;
            FilterPursuits();
            OnPropertyChanged(nameof(SelectedPersonCaption));
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
        private set
        {
            if (SetField(ref _isLoading, value)) OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public bool CanRefresh => !IsLoading && !_isReassigning;

    public async Task LoadAsync(CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Loading…";
        try
        {
            var peopleTask = _director.GetPeopleAsync(ct);
            var funnelTask = _director.GetFunnelAsync(ct);
            var pursuitsTask = _pursuits.ListAsync(ct);
            var recordsTask = _records.ListAsync(ct);
            var attributionTask = _attribution.GetSummaryAsync(ct);
            var bidsTask = _director.ListPendingBidsAsync(ct);
            var relationshipsTask = _director.CountRelationshipPursuitsAsync(ct);
            await Task.WhenAll(peopleTask, funnelTask, pursuitsTask, recordsTask, attributionTask,
                bidsTask, relationshipsTask).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var people = await peopleTask.ConfigureAwait(true);
            var funnel = await funnelTask.ConfigureAwait(true);
            var records = await recordsTask.ConfigureAwait(true);
            var attribution = await attributionTask.ConfigureAwait(true);
            _allPendingBids = await bidsTask.ConfigureAwait(true);
            RelationshipPursuits = await relationshipsTask.ConfigureAwait(true);
            _bidsAsOf = DateTimeOffset.UtcNow;
            FilterPendingBids();
            OnPropertyChanged(nameof(RelationshipPursuits));
            var snapshot = CrmAnalyticsService.Compute(records.ToArray(), new Dictionary<long, Opportunity>());
            var selectedKey = SelectedPerson?.PersonKey;
            _allPursuits = await pursuitsTask.ConfigureAwait(true);
            People.Clear();
            foreach (var person in people.OrderByDescending(p => p.Live).ThenBy(p => p.DisplayName))
                People.Add(new BdDirectorPersonRow(person));
            SelectedPerson = People.FirstOrDefault(p => string.Equals(p.PersonKey, selectedKey, StringComparison.OrdinalIgnoreCase))
                             ?? People.FirstOrDefault();
            FilterPursuits();

            Stages.Clear();
            foreach (var stage in new[] { CrmEngagementStage.Drafting, CrmEngagementStage.Submitted })
            {
                var count = snapshot.ByStage.FirstOrDefault(s => s.Stage == stage)?.Count ?? 0;
                Stages.Add(new BdDirectorStageRow(CrmStageNames.Display(stage), count,
                    funnel.MedianDaysInStage.TryGetValue((int)stage, out var median) ? median : null));
            }
            Live = Stages.Sum(s => s.Pursuits);
            Outcomes = snapshot.Won + snapshot.Lost == 0
                ? "No live pursuit has been decided yet."
                : $"Won {snapshot.Won} · Lost {snapshot.Lost} · win rate {snapshot.WinRate.ToString("P0", CultureInfo.CurrentCulture)} on decided live pursuits · {snapshot.BackfillWins} Deltek wins kept separate";
            LostDetails = $"Of the lost: {funnel.LostWithCompetitor} name the competitor, {funnel.LostWithReason} give a reason.";
            DeadlineCaption = $"Submitted or pending, deadline more than 30 days gone, no outcome recorded. {funnel.UndecidedNoDeadline} more have no deadline on file.";
            Undecided.Clear();
            foreach (var row in funnel.UndecidedPastDeadline)
            {
                var owner = People.FirstOrDefault(p => p.OwnerKeys.Contains(row.OwnerKey ?? "", StringComparer.OrdinalIgnoreCase));
                Undecided.Add(new BdDirectorDeadlineRow(row.EngagementId, row.ProjectName, row.Buyer,
                    owner?.DisplayName ?? row.OwnerKey ?? "Unassigned", row.Deadline));
            }
            Sources.Clear();
            foreach (var source in attribution.BySource) Sources.Add(source);
            foreach (var property in new[] { nameof(Live), nameof(PeopleCount), nameof(Outcomes),
                         nameof(LostDetails), nameof(DeadlineCaption) })
                OnPropertyChanged(property);
            await LoadTargetsAsync(ct).ConfigureAwait(true);
            await LoadAccountsAsync(ct).ConfigureAwait(true);
            await LoadAsksAsync(ct).ConfigureAwait(true);
            await LoadDecisionsAsync(ct).ConfigureAwait(true);
            await LoadKnowledgeAsync(ct).ConfigureAwait(true);
            await LoadMarketAsync(ct).ConfigureAwait(true);
            await LoadBriefAsync(ct).ConfigureAwait(true);
            await LoadMyDayAsync(records, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
            AsOf = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(AsOf));
            StatusMessage = "Up to date.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director load failed.");
            StatusMessage = "Could not refresh all figures. Try Refresh again.";
        }
        finally { IsLoading = false; }
    }

    public async Task LoadMyDayAsync(CancellationToken ct, DateTimeOffset? nowUtc = null)
    {
        var records = await _records.ListAsync(ct).ConfigureAwait(true);
        await LoadMyDayAsync(records, nowUtc ?? DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
    }

    private async Task LoadMyDayAsync(IReadOnlyList<CrmEngagement> records, DateTimeOffset now, CancellationToken ct)
    {
        var actor = CurrentActorUpn;
        MyDay.Clear();
        ReplaceRows(MyDayRegions, records.Where(BdMyDayRules.IsLive).Select(e => BdMyDayRules.RegionKey(e.Region))
            .Where(r => r.Length > 0).Distinct().OrderBy(r => r));
        if (string.IsNullOrWhiteSpace(actor))
        {
            MyDayMessage = "Sign in to see your day.";
            OnPropertyChanged(nameof(MyDayMessage));
            return;
        }
        var staff = await _staff.GetAllAsync(ct).ConfigureAwait(true);
        if (!staff.Any(s => string.Equals(s.Mailbox, actor, StringComparison.OrdinalIgnoreCase)))
        {
            MyDayMessage = "Your signed-in mailbox is not in the staff directory.";
            OnPropertyChanged(nameof(MyDayMessage));
            return;
        }
        var items = await _director.GetCallSheetAsync(actor, now, ct).ConfigureAwait(true);
        var historyByClient = new Dictionary<string, DeltekClientIntelligence?>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<BdDirectorMyDayRow>();
        foreach (var item in CallSheetOrdering.Order(items, now))
        {
            DeltekClientIntelligence? history = null;
            if (!string.IsNullOrWhiteSpace(item.DeltekClientId) && !historyByClient.TryGetValue(item.DeltekClientId, out history))
            {
                try { history = await _deltek.LoadAsync(item.DeltekClientId, ct).ConfigureAwait(true); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Warning(ex, "My day client history could not be read."); }
                historyByClient[item.DeltekClientId] = history;
            }
            var pursuit = records.FirstOrDefault(e => e.Id == item.EngagementId);
            var candidates = item.Kind == CallSheetKind.Pursuit && pursuit is not null
                ? BdMyDayRules.Candidates(pursuit, actor, staff, records, history, item.Buyer) : Array.Empty<BdEngineerCandidate>();
            rows.Add(new BdDirectorMyDayRow(item, now, history, candidates, BdMyDayRules.SeatDisplay(pursuit?.AssignedStaffIds, staff)));
        }
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(actor, CurrentActorUpn, StringComparison.OrdinalIgnoreCase)) return;
        ReplaceRows(MyDay, rows);
        MyDayMessage = rows.Count == 0 ? "No conversations or asks to show today." : "Your next conversations, asks and untracked clients.";
        OnPropertyChanged(nameof(MyDayMessage));
    }

    private async Task RefreshMyDayAfterWriteAsync(string result, CancellationToken ct)
    {
        try
        {
            await LoadMyDayAsync(ct).ConfigureAwait(true);
            var people = await _director.GetPeopleAsync(ct).ConfigureAwait(true);
            var selectedKey = SelectedPerson?.PersonKey;
            ReplaceRows(People, people.Select(p => new BdDirectorPersonRow(p)));
            SelectedPerson = People.FirstOrDefault(p => p.PersonKey == selectedKey) ?? People.FirstOrDefault();
            OnPropertyChanged(nameof(PeopleCount));
            StatusMessage = result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "My day write completed but refresh failed.");
            StatusMessage = result + " Refresh could not finish; try Refresh.";
        }
    }

    private async Task<(string Actor, BdStaffRow Staff, IReadOnlyList<BdStaffRow> Directory)> MyDayActorAsync(CancellationToken ct)
    {
        var actor = CurrentActorUpn;
        var directory = await _staff.GetAllAsync(ct).ConfigureAwait(true);
        var staff = directory.Where(s => s.IsActive && !string.IsNullOrWhiteSpace(actor)
                && string.Equals(s.Mailbox, actor, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Id).FirstOrDefault()
            ?? throw new InvalidOperationException("Your signed-in mailbox must resolve to an active staff member.");
        return (actor, staff, directory);
    }

    public async Task LogMyDayAsync(BdDirectorMyDayRow row, CrmActivityType type, CancellationToken ct)
    {
        if (!row.CanLog) throw new InvalidOperationException("Start tracking this account before logging a touch.");
        var kind = type switch { CrmActivityType.Call => "Call", CrmActivityType.EmailOut => "Email", CrmActivityType.Meeting => "Meeting",
            _ => throw new ArgumentOutOfRangeException(nameof(type)) };
        var (actor, _, _) = await MyDayActorAsync(ct).ConfigureAwait(true);
        await _activities.AppendAsync(new CrmActivity
        {
            EngagementId = row.Item.EngagementId!.Value, ActivityType = type, Subject = "My day: " + kind,
            CreatedBy = actor, OccurredAtUtc = DateTimeOffset.UtcNow,
        }, actor, ct).ConfigureAwait(true);
        await RefreshMyDayAfterWriteAsync(kind + " logged.", ct).ConfigureAwait(true);
    }

    public async Task ChooseEngineerAsync(BdDirectorMyDayRow row, BdEngineerCandidate candidate, CancellationToken ct)
    {
        var (actor, _, directory) = await MyDayActorAsync(ct).ConfigureAwait(true);
        if (row.Item.Kind != CallSheetKind.Pursuit || row.Item.EngagementId != candidate.EngagementId)
            throw new InvalidOperationException("Choose an engineer on the pursuit they belong to.");
        var current = await _records.GetByIdAsync(candidate.EngagementId, ct).ConfigureAwait(true)
            ?? throw new InvalidOperationException("This pursuit is no longer available.");
        var all = await _records.ListAsync(ct).ConfigureAwait(true);
        var eligible = current.BuyerCanonicalOrgId == row.Item.BuyerOrgId
            ? BdMyDayRules.Candidates(current, actor, directory, all, row.History, row.Item.Buyer)
                .FirstOrDefault(c => string.Equals(c.Staff.Mailbox, candidate.Staff.Mailbox, StringComparison.OrdinalIgnoreCase)) : null;
        if (eligible is null)
        {
            await RefreshMyDayAfterWriteAsync("This pursuit or candidate changed. My day has been reloaded; choose again.", ct).ConfigureAwait(true);
            return;
        }
        try
        {
            await _records.UpdateAsync(current with { AssignedStaffIds = BdMyDayRules.AppendSeat(current.AssignedStaffIds, eligible.Staff.StaffKey) }, actor, ct).ConfigureAwait(true);
        }
        catch (CrmConcurrencyException)
        {
            await RefreshMyDayAfterWriteAsync("Someone changed this pursuit. My day has been reloaded; no seat or ask was saved.", ct).ConfigureAwait(true);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var title = string.IsNullOrWhiteSpace(row.Item.Pursuit) ? row.Item.Buyer : row.Item.Pursuit;
        var ask = $"Technical lead on {title}: {(string.IsNullOrWhiteSpace(current.NextActionNote) ? "first conversation" : current.NextActionNote)}";
        try
        {
            await _ledger.CreateAskAsync(new BdAsk(0, eligible.Staff.StaffKey, current.Id, current.BuyerCanonicalOrgId,
                ask.Length <= 500 ? ask : ask[..500], null, 60, now.AddDays(7), actor, now, "Open", null, null, null), ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "My day second seat saved, but technical-lead ask failed.");
            await RefreshMyDayAfterWriteAsync("Second seat saved, but the technical-lead ask could not be created. Create the ask from Asks.", ct).ConfigureAwait(true);
            return;
        }
        await RefreshMyDayAfterWriteAsync("Second seat saved and technical-lead ask created.", ct).ConfigureAwait(true);
    }

    public async Task StartTrackingAsync(BdDirectorMyDayRow row, CancellationToken ct)
    {
        if (!row.IsAccount || row.Item.BuyerOrgId is not { } buyer) throw new InvalidOperationException("Choose a client account.");
        var (actor, staff, directory) = await MyDayActorAsync(ct).ConfigureAwait(true);
        var records = await _records.ListAsync(ct).ConfigureAwait(true);
        var tracked = records.Where(e => e.BuyerCanonicalOrgId == buyer && BdMyDayRules.IsLive(e)).OrderBy(e => e.Id).FirstOrDefault();
        if (tracked is not null)
        {
            var owner = BdMyDayRules.Resolve(tracked.OwnerStaffId, directory);
            row.ShowTracked(tracked, owner?.DisplayName ?? owner?.Mailbox ?? tracked.OwnerStaffId ?? "Unassigned");
            StatusMessage = row.TrackedBy + ". Join as second seat to work on this pursuit.";
            return;
        }
        var region = BdMyDayRules.RegionKey(row.SelectedRegion);
        var regions = records.Where(BdMyDayRules.IsLive).Select(e => BdMyDayRules.RegionKey(e.Region)).Where(r => r.Length > 0).Distinct().ToArray();
        if (!regions.Contains(region)) throw new InvalidOperationException("Choose a region currently used by the team.");
        try
        {
            await _records.InsertAsync(new CrmEngagement
            {
                OwnerStaffId = staff.StaffKey, Stage = CrmEngagementStage.Drafting, BuyerCanonicalOrgId = buyer,
                Region = region, PotentialProjects = row.Item.Buyer, ExternalSource = "BdDirector.MyDay",
            }, actor, ct).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            // This includes the store's duplicate refusal. Retain the account card so
            // another click can find and offer the pursuit that won a creation race.
            StatusMessage = "Could not start tracking: " + ex.Message;
            return;
        }
        await RefreshMyDayAfterWriteAsync("Tracking started.", ct).ConfigureAwait(true);
    }

    public async Task JoinSecondSeatAsync(BdDirectorMyDayRow row, CancellationToken ct)
    {
        if (!row.IsAccount || row.TrackedPursuit is not { } tracked) throw new InvalidOperationException("Find the tracked pursuit first.");
        var (actor, staff, directory) = await MyDayActorAsync(ct).ConfigureAwait(true);
        var current = await _records.GetByIdAsync(tracked.Id, ct).ConfigureAwait(true);
        if (current is null || !BdMyDayRules.IsLive(current) || current.BuyerCanonicalOrgId != row.Item.BuyerOrgId)
        {
            await RefreshMyDayAfterWriteAsync("The tracked pursuit changed. My day has been reloaded.", ct).ConfigureAwait(true);
            return;
        }
        if (BdMyDayRules.IsActor(current.OwnerStaffId, actor, directory)
            || BdMyDayRules.Assigned(current.AssignedStaffIds).Any(key => BdMyDayRules.IsActor(key, actor, directory)))
        {
            await RefreshMyDayAfterWriteAsync("You already have a seat on this pursuit.", ct).ConfigureAwait(true);
            return;
        }
        try
        {
            await _records.UpdateAsync(current with { AssignedStaffIds = BdMyDayRules.AppendSeat(current.AssignedStaffIds, staff.StaffKey) }, actor, ct).ConfigureAwait(true);
        }
        catch (CrmConcurrencyException)
        {
            await RefreshMyDayAfterWriteAsync("Someone changed this pursuit. My day has been reloaded; your seat was not saved.", ct).ConfigureAwait(true);
            return;
        }
        await RefreshMyDayAfterWriteAsync("Joined as second seat.", ct).ConfigureAwait(true);
    }

    private void FilterPendingBids()
    {
        var selectedId = SelectedPendingBid?.Bid.EngagementId;
        ReplaceRows(PendingBids, _allPendingBids.OrderBy(b => b.DueUtc is null).ThenBy(b => b.DueUtc)
            .ThenBy(b => b.EngagementId).Select(b => new BdDirectorPendingBidRow(b, _bidsAsOf))
            .Where(row => !MineOnly || row.IsMine(CurrentActorUpn)));
        SelectedPendingBid = PendingBids.FirstOrDefault(b => b.Bid.EngagementId == selectedId);
        PendingBidsSummary = SummarizePendingBids(PendingBids, RelationshipPursuits, _bidsAsOf);
        OnPropertyChanged(nameof(PendingBidsSummary));
    }

    internal static string SummarizePendingBids(IEnumerable<BdDirectorPendingBidRow> rows, int relationships, DateTimeOffset now)
    {
        var bids = rows.ToArray();
        var pending = bids.Where(b => b.IsPending).ToArray();
        var assignees = pending.GroupBy(b => b.Bid.OwnerMailbox ?? b.Bid.OwnerStaffId ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.First().Assignee, Count = g.Count() }).OrderBy(g => g.Name)
            .Select(g => $"{g.Name} {g.Count}");
        var weeks = pending.Where(b => b.Bid.DueUtc.HasValue).Select(b => BdDateDisplay.WeekStart(b.Bid.DueUtc!.Value)).ToArray();
        var thisWeek = BdDateDisplay.WeekStart(now);
        var nextWeek = thisWeek.AddDays(7);
        var byAssignee = string.Join(", ", assignees);
        return $"{pending.Length} pending · {bids.Count(b => b.Bid.Stage == CrmEngagementStage.Submitted)} submitted · by assignee: {(byAssignee.Length == 0 ? "none" : byAssignee)}"
            + $" · due this week {weeks.Count(w => w == thisWeek)}, next week {weeks.Count(w => w == nextWeek)}, later {weeks.Count(w => w > nextWeek)}"
            + $" · {relationships} relationship pursuits not shown";
    }

    public async Task MarkSubmittedAsync(BdDirectorPendingBidRow row, SubmittedDetails? submission, string actor, CancellationToken ct)
    {
        if (submission is null) return;
        var error = SubmittedDetails.Validate(submission.FileName, submission.FolderLink, submission.SourcePath, row.Bid.SubmittedFileId);
        if (error is not null) throw new ArgumentException(error, nameof(submission));
        // Fetch the current row and preserve its concurrency token and other fields.
        var record = await _records.GetByIdAsync(row.Bid.EngagementId, ct).ConfigureAwait(true);
        if (record is null || record.OpportunityId != row.Bid.OpportunityId
            || (record.Stage != CrmEngagementStage.Drafting && !(record.Stage == CrmEngagementStage.Submitted && record.SubmittedFileId.HasValue)))
            throw new InvalidOperationException("This bid is no longer pending. Refresh and try again.");
        PursuitFile attachment;
        var added = !string.IsNullOrWhiteSpace(submission.SourcePath);
        if (added)
        {
            await using var content = new FileStream(submission.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            SqlPursuitFileStore.ValidateLength(content.Length);
            attachment = await _files.AddAsync(record.Id, record.OpportunityId, submission.FileName.Trim(), content,
                content.Length, "application/pdf", actor, ct).ConfigureAwait(true);
        }
        else
        {
            attachment = (await _files.ListByEngagementAsync(record.Id, ct).ConfigureAwait(true))
                .SingleOrDefault(f => f.Id == record.SubmittedFileId)
                ?? throw new InvalidOperationException("The existing submitted attachment is unavailable. Browse to the PDF again.");
        }
        try
        {
            await _records.UpdateAsync(record with
            {
                Stage = CrmEngagementStage.Submitted,
                SubmittedFileId = attachment.Id,
                SubmittedFileName = attachment.FileName,
                FolderLink = DirectorDialogValues.Optional(submission.FolderLink),
            }, actor, ct).ConfigureAwait(true);
        }
        catch (Exception ex) when (added)
        {
            StatusMessage = $"Attached {attachment.FileName}, but could not mark the bid submitted. The attachment remains on the pursuit. Refresh and try again.";
            throw new InvalidOperationException(StatusMessage, ex);
        }
        await LoadAsync(ct).ConfigureAwait(true);
        StatusMessage = StatusMessage == "Up to date." ? "Bid marked submitted."
            : "The bid was marked submitted. Could not refresh the figures; try Refresh again.";
    }

    public async Task<PursuitFile> GetSubmittedFileAsync(BdDirectorPendingBidRow row, CancellationToken ct)
        => (await _files.ListByEngagementAsync(row.Bid.EngagementId, ct).ConfigureAwait(true))
            .SingleOrDefault(f => f.Id == row.Bid.SubmittedFileId)
            ?? throw new FileNotFoundException("The submitted attachment could not be found. Refresh and try again.");

    public Task<Opportunity?> GetBidOpportunityAsync(BdDirectorPendingBidRow row, CancellationToken ct)
        => _opportunities.GetByIdAsync(row.Bid.OpportunityId, ct);

    public async Task SaveBidOpportunityAsync(Opportunity opportunity, string actor, CancellationToken ct)
    {
        await _opportunities.UpdateAsync(opportunity, actor, ct).ConfigureAwait(true);
        await LoadAsync(ct).ConfigureAwait(true);
        StatusMessage = StatusMessage == "Up to date." ? "Opportunity saved."
            : "The opportunity was saved. Could not refresh the figures; try Refresh again.";
    }

    internal BdDirectorPursuitRow? PursuitForBid(BdDirectorPendingBidRow row)
        => _allPursuits.FirstOrDefault(p => p.EngagementId == row.Bid.EngagementId) is { } pursuit
            ? new BdDirectorPursuitRow(pursuit) : null;

    private void FilterPursuits()
    {
        SelectedPursuits.Clear();
        if (SelectedPerson is not { } person) return;
        var keys = person.OwnerKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _allPursuits.Where(p => keys.Contains(p.OwnerStaffId)
                     && (p.Stage == (int)CrmEngagementStage.Drafting || p.Stage == (int)CrmEngagementStage.Submitted)))
            SelectedPursuits.Add(new BdDirectorPursuitRow(row));
    }

    public async Task LoadTargetsAsync(CancellationToken ct)
    {
        var targets = await _ledger.ListTargetsAsync(ct).ConfigureAwait(true);
        var keys = targets.Select(t => new BdTargetKey(t.Metric, t.Scope, t.ScopeKey, t.PeriodStartUtc, t.PeriodEndUtc)).ToArray();
        var actuals = await _director.GetTargetActualsAsync(keys, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        SelectedTarget = null;
        Targets.Clear();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            Targets.Add(new BdDirectorTargetRow(target, actuals.Actuals.TryGetValue(keys[i], out var actual) ? actual : 0)
            {
                PersonDisplayName = target.Scope == "Person"
                    ? People.FirstOrDefault(p => string.Equals(p.PersonKey, target.ScopeKey, StringComparison.OrdinalIgnoreCase))?.DisplayName : null,
            });
        }
        FeeCoverage = $"{actuals.Live} live pursuits; {actuals.LiveWithFee} carry a fee.";
        OnPropertyChanged(nameof(FeeCoverage));
    }

    public async Task LoadAccountsAsync(CancellationToken ct)
    {
        var rows = await _director.GetAccountsAsync(1000, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        SelectedAccount = null;
        Accounts.Clear();
        foreach (var account in rows) Accounts.Add(new BdDirectorAccountRow(account));
    }

    private async Task LoadSelectedAccountAsync(BdDirectorAccountRow? row, CancellationToken ct)
    {
        SelectedAccountDeltek = null;
        AccountMessage = row is null ? "Select an account to read its Deltek summary." : "Reading Deltek summary…";
        NotifyAccount();
        if (row is null) return;
        try
        {
            var summary = await _deltek.LoadAsync(row.Account.ClendorClientId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !ReferenceEquals(row, SelectedAccount)) return;
            SelectedAccountDeltek = summary;
            AccountMessage = summary is null ? "No Deltek record could be read for this client." : "";
            NotifyAccount();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Selection changed or window closed. */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(row, SelectedAccount)) return;
            Log.Warning(ex, "BD Director account summary failed.");
            AccountMessage = "No Deltek record could be read for this client.";
            NotifyAccount();
        }
    }

    private void NotifyAccount()
    {
        foreach (var property in new[] { nameof(SelectedAccountDeltek), nameof(AccountMessage), nameof(HasAccountDeltek),
                     nameof(HasAccountAr), nameof(HasAccountPostedThrough), nameof(AccountDegradedMessage) }) OnPropertyChanged(property);
    }

    public async Task LoadAsksAsync(CancellationToken ct)
    {
        _asksCts?.Cancel();
        _asksCts?.Dispose();
        _asksCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _asksCts.Token;
        IReadOnlyList<BdAsk> rows;
        try
        {
            rows = await _ledger.ListAsksAsync(AskStatusFilter == "All" ? null : AskStatusFilter, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // A newer filter owns the asks; keep the rest of a window refresh going.
            return;
        }
        SelectedAsk = null;
        Asks.Clear();
        foreach (var ask in rows)
        {
            var person = People.FirstOrDefault(p => p.OwnerKeys.Contains(ask.ToStaffKey, StringComparer.OrdinalIgnoreCase)
                || string.Equals(p.PersonKey, ask.ToStaffKey, StringComparison.OrdinalIgnoreCase));
            var about = _allPursuits.FirstOrDefault(p => p.EngagementId == ask.EngagementId)?.ProjectName
                ?? Accounts.FirstOrDefault(a => a.Account.CanonicalOrgId == ask.CanonicalOrgId)?.DisplayName ?? "—";
            Asks.Add(new BdDirectorAskRow(ask, person?.DisplayName ?? ask.ToStaffKey, about));
        }
    }

    private async Task ReloadAsksSafelyAsync()
    {
        try { await LoadAsksAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Another filter superseded this read. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director asks failed.");
            StatusMessage = "Could not read asks. Try Refresh again.";
        }
    }

    public async Task LoadDecisionsAsync(CancellationToken ct)
    {
        var rows = await _ledger.ListDecisionsAsync(ct).ConfigureAwait(true);
        var track = await _ledger.GetBriefTrackRecordAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        SelectedDecision = null;
        Decisions.Clear();
        foreach (var decision in rows) Decisions.Add(new BdDirectorDecisionRow(decision));
        BriefTrackRecordSummary = $"Brief calls: {track.Scored} scored · {track.Right} right · {track.Wrong} wrong · {track.Unclear} unclear · {track.PendingPastReview} past review unscored";
        BriefTrackRecordBreakdown = "By section: " + Split(track.BySection.Select(s => (s.Section == "ThisWeek" ? "This week" : s.Section, s.Right, s.Wrong)))
            + "\nBy book: " + Split(track.ByBook);
        OnPropertyChanged(nameof(BriefTrackRecordSummary));
        OnPropertyChanged(nameof(BriefTrackRecordBreakdown));
    }

    private static string Split(IEnumerable<(string Name, int Right, int Wrong)> groups)
    {
        var text = string.Join("; ", groups.Select(g => $"{g.Name} {g.Right} right · {g.Wrong} wrong"));
        return text.Length == 0 ? "none" : text;
    }

    public async Task SetTargetAsync(BdTarget target, string actor, CancellationToken ct)
    {
        await _ledger.SetTargetAsync(target with { SetBy = actor }, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(() => LoadTargetsAsync(ct), "Target saved.").ConfigureAwait(true);
    }

    public async Task RetireTargetAsync(long id, string actor, CancellationToken ct)
    {
        await _ledger.RetireTargetAsync(id, actor, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(() => LoadTargetsAsync(ct), "Target retired.").ConfigureAwait(true);
    }

    public async Task CreateAskAsync(BdAsk ask, string actor, CancellationToken ct)
    {
        await _ledger.CreateAskAsync(ask with { AskedBy = actor }, ct).ConfigureAwait(true);
        var mirrored = true;
        if (ask.EngagementId is { } id)
        {
            try
            {
                var pursuit = await _records.GetByIdAsync(id, ct).ConfigureAwait(true)
                    ?? throw new InvalidOperationException("The pursuit could not be read.");
                await _records.UpdateAsync(pursuit with { NextActionNote = ask.Ask, NextActionDueUtc = ask.DueUtc }, actor, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                mirrored = false;
                Log.Warning(ex, "BD Director saved ask but next action could not be mirrored.");
            }
        }
        // The ledger is already committed. A failed mirror must not prompt a second ask.
        await LoadAsync(ct).ConfigureAwait(true);
        StatusMessage = !mirrored ? "Ask saved. The pursuit's next action could not be updated; the ask remains on the person's dashboard."
            : StatusMessage == "Up to date." ? "Ask saved." : "Ask saved. Could not refresh the figures; try Refresh again.";
    }

    public async Task WithdrawAskAsync(long id, string actor, CancellationToken ct)
    {
        await _ledger.CloseAskAsync(id, "Withdrawn", null, actor, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(async () =>
        {
            await LoadAccountsAsync(ct).ConfigureAwait(true);
            await LoadAsksAsync(ct).ConfigureAwait(true);
        }, "Ask withdrawn.").ConfigureAwait(true);
    }

    public async Task RecordDecisionAsync(BdDecision decision, string actor, CancellationToken ct)
    {
        await _ledger.RecordDecisionAsync(decision with { DecidedBy = actor }, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(() => LoadDecisionsAsync(ct), "Decision recorded.").ConfigureAwait(true);
    }

    public async Task ScoreDecisionAsync(long id, string outcome, string? note, string actor, CancellationToken ct)
    {
        await _ledger.ScoreDecisionAsync(id, outcome, note, actor, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(async () =>
        {
            await LoadDecisionsAsync(ct).ConfigureAwait(true);
            await LoadKnowledgeAsync(ct).ConfigureAwait(true);
            await LoadBriefAsync(ct).ConfigureAwait(true);
        }, "Decision scored.").ConfigureAwait(true);
    }

    private async Task RefreshAfterWriteAsync(Func<Task> refresh, string saved)
    {
        try { await refresh().ConfigureAwait(true); StatusMessage = saved; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director saved a change but could not refresh.");
            StatusMessage = saved + " Could not refresh; try Refresh again.";
        }
    }

    public async Task LoadKnowledgeAsync(CancellationToken ct)
    {
        var booksTask = _claims.ListSourcesAsync(ct);
        var conceptsTask = _claims.ListConceptsAsync(ct);
        var verdictsTask = _claims.ListClaimVerdictsAsync(ct);
        await Task.WhenAll(booksTask, conceptsTask, verdictsTask).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        _claimVerdicts = (await verdictsTask.ConfigureAwait(true)).ToDictionary(v => v.ClaimId);
        var bookId = SelectedBook?.Source.Id;
        var conceptId = SelectedConcept?.Concept.Id;
        // Set backing fields during a whole-tab refresh; start one list read afterwards.
        KnowledgeBooks.Clear();
        foreach (var book in await booksTask.ConfigureAwait(true)) KnowledgeBooks.Add(new BdKnowledgeSourceRow(book));
        KnowledgeConcepts.Clear();
        foreach (var concept in await conceptsTask.ConfigureAwait(true)) KnowledgeConcepts.Add(new BdKnowledgeConceptRow(concept));
        _selectedBook = KnowledgeBooks.FirstOrDefault(b => b.Source.Id == bookId) ?? KnowledgeBooks.FirstOrDefault();
        _selectedConcept = KnowledgeConcepts.FirstOrDefault(c => c.Concept.Id == conceptId);
        OnPropertyChanged(nameof(SelectedBook));
        OnPropertyChanged(nameof(SelectedConcept));
        ClaimsLoadTask = LoadKnowledgeClaimsAsync();
        await ClaimsLoadTask.ConfigureAwait(true);
    }

    public Task SampleKnowledgeAsync()
    {
        _selectedConcept = null;
        OnPropertyChanged(nameof(SelectedConcept));
        _claimFilter = "Unreviewed";
        OnPropertyChanged(nameof(ClaimFilter));
        _sampleClaims = true;
        return ClaimsLoadTask = LoadKnowledgeClaimsAsync();
    }

    private async Task LoadKnowledgeClaimsAsync()
    {
        _claimsCts?.Cancel(); _claimsCts?.Dispose();
        _claimsCts = CancellationTokenSource.CreateLinkedTokenSource(_knowledgeLifetime.Token);
        var ct = _claimsCts.Token;
        var book = SelectedBook;
        var concept = SelectedConcept;
        var sample = _sampleClaims;
        SelectedClaim = null;
        KnowledgeClaims.Clear();
        KnowledgeMessage = "";
        OnPropertyChanged(nameof(ClaimsCaption));
        OnPropertyChanged(nameof(KnowledgeMessage));
        if (book is null && concept is null) return;
        try
        {
            var rows = concept is not null
                ? await _claims.ListClaimsByConceptAsync(concept.Concept.Id, 1000, ct).ConfigureAwait(true)
                : sample ? await _claims.SampleUnreviewedAsync(book!.Source.Id, 20, ct).ConfigureAwait(true)
                : await _claims.ListClaimsAsync(book!.Source.Id, ClaimFilter, 1000, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            foreach (var row in rows) KnowledgeClaims.Add(new BdKnowledgeClaimRow(row, _claimVerdicts.GetValueOrDefault(row.Id)));
            KnowledgeMessage = rows.Count == 1000 ? "Showing the first 1,000 claims. Choose a filter or sample to narrow the list."
                : rows.Count == 0 ? "No claims in this view." : $"{rows.Count} claims shown.";
            OnPropertyChanged(nameof(KnowledgeMessage));
            SelectedClaim = KnowledgeClaims.FirstOrDefault();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* New selection or window closure. */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            Log.Warning(ex, "BD Director claims could not be read.");
            KnowledgeMessage = "Could not read claims. Try Refresh again.";
            OnPropertyChanged(nameof(KnowledgeMessage));
        }
    }

    private async Task LoadPassageAsync(BdKnowledgeClaimRow? row, CancellationToken ct)
    {
        SelectedPassage = null;
        PassageMessage = row is null ? "Select a claim to read its passage." : "Reading passage…";
        NotifyPassage();
        if (row is null) return;
        try
        {
            var passage = await _claims.GetPassageAsync(row.Claim.PassageId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !ReferenceEquals(row, SelectedClaim)) return;
            SelectedPassage = passage;
            PassageMessage = passage is null ? "The passage could not be read. Try Refresh again." : "";
            NotifyPassage();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* New selection or window closure. */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(row, SelectedClaim)) return;
            Log.Warning(ex, "BD Director passage could not be read.");
            PassageMessage = "The passage could not be read. Try Refresh again.";
            NotifyPassage();
        }
    }

    private void NotifyPassage()
    {
        OnPropertyChanged(nameof(SelectedPassage));
        OnPropertyChanged(nameof(PassageMessage));
    }

    public async Task KeepClaimAsync(BdKnowledgeClaimRow row, string actor, CancellationToken ct)
    {
        if (!row.CanReview) return;
        await _claims.KeepAsync(row.Claim.Id, actor, ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(() => ReloadKnowledgeReviewAsync(row.Claim.SourceId, ct), "Claim kept.").ConfigureAwait(true);
    }

    public async Task RetireClaimAsync(BdKnowledgeClaimRow row, string actor, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200) throw new ArgumentException("Give a reason of 1–200 characters.", nameof(reason));
        if (!row.CanReview) return;
        await _claims.RetireAsync(row.Claim.Id, actor, reason.Trim(), ct).ConfigureAwait(true);
        await RefreshAfterWriteAsync(() => ReloadKnowledgeReviewAsync(row.Claim.SourceId, ct), "Claim retired.").ConfigureAwait(true);
    }

    private async Task ReloadKnowledgeReviewAsync(long sourceId, CancellationToken ct)
    {
        var books = await _claims.ListSourcesAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var updated = books.FirstOrDefault(b => b.Id == sourceId);
        if (updated is not null) KnowledgeBooks.FirstOrDefault(b => b.Source.Id == sourceId)?.Update(updated);
        ClaimsLoadTask = LoadKnowledgeClaimsAsync();
        await ClaimsLoadTask.ConfigureAwait(true);
    }

    public async Task LoadMarketAsync(CancellationToken ct)
    {
        var signalsTask = _director.GetRecentSignalsAsync(30, 25, ct);
        var forwardTask = _dashboard.GetForwardPipelineAsync(12, ct);
        var seatsTask = _dashboard.GetOpenStructuralSeatsAsync(10, ct);
        var funnelTask = _dashboard.GetPipelineFunnelAsync(ct);
        var opportunitiesTask = _opportunities.ListAsync(ct, includeClosed: false, includeNonPrime: false);
        await Task.WhenAll(signalsTask, forwardTask, seatsTask, funnelTask, opportunitiesTask).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        ReplaceRows(MarketSignals, await signalsTask.ConfigureAwait(true));
        ReplaceRows(ForwardPipeline, await forwardTask.ConfigureAwait(true));
        ReplaceRows(OpenSeats, await seatsTask.ConfigureAwait(true));
        var funnel = await funnelTask.ConfigureAwait(true);
        MarketFunnel = $"{funnel.RadarCount} on the radar · {funnel.BidWindowCount} in a bid window · {funnel.OpenSeatCount} open seats";
        OnPropertyChanged(nameof(MarketFunnel));
        var now = DateTimeOffset.UtcNow;
        var thirtyDays = now.AddDays(30);
        ReplaceRows(MarketDeadlines, (await opportunitiesTask.ConfigureAwait(true))
            .Where(o => o.DismissedAtUtc is null && o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= thirtyDays)
            .OrderBy(o => o.SubmissionDeadlineUtc).Select(o => DashboardViewModel.CreateDeadlineRow(o, now)));
    }

    public async Task LoadBriefAsync(CancellationToken ct)
    {
        _briefCts?.Cancel(); _briefCts?.Dispose();
        _briefCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _knowledgeLifetime.Token);
        var token = _briefCts.Token;
        try
        {
            var rows = await _brief.GetLatestAsync(token).ConfigureAwait(true);
            var resolved = new Dictionary<long, KnowledgeClaimRow?>();
            var sections = new List<BdDirectorBriefRow>();
            foreach (var row in rows)
            {
                var citations = new List<BdDirectorCitationRow>();
                foreach (var citation in row.Citations)
                {
                    if (!resolved.TryGetValue(citation.ClaimId, out var claim))
                    {
                        claim = await _claims.GetClaimAsync(citation.ClaimId, token).ConfigureAwait(true);
                        resolved[citation.ClaimId] = claim;
                    }
                    citations.Add(new(citation, claim));
                }
                sections.Add(new(row, citations, Decisions.FirstOrDefault(d => d.Record.Id == row.DecisionId)?.Record));
            }
            token.ThrowIfCancellationRequested();
            SelectedBriefCitation = null;
            ReplaceRows(BriefSections, sections);
            BriefMessage = rows.Count == 0 ? "No brief has been imported yet." : "Latest imported sections. Dates show when each was imported.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !ct.IsCancellationRequested) { return; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            Log.Warning(ex, "Director brief could not be read.");
            BriefMessage = "Could not refresh the brief. Previously loaded sections may be out of date.";
        }
        OnPropertyChanged(nameof(BriefMessage));
    }

    public async Task AcknowledgeBriefAsync(BdDirectorBriefRow row, string actor, CancellationToken ct)
    {
        await _brief.AcknowledgeAsync(row.Brief.Id, actor, ct).ConfigureAwait(true);
        await LoadBriefAsync(ct).ConfigureAwait(true);
    }

    private async Task LoadBriefPassageAsync(BdDirectorCitationRow? row, CancellationToken ct)
    {
        SelectedPassage = null;
        CitationStatus = row?.Status ?? "";
        PassageMessage = row is null ? "Select a citation to read its passage." : "Reading passage…";
        OnPropertyChanged(nameof(CitationStatus)); NotifyPassage();
        if (row is null) return;
        try
        {
            var passage = await _claims.GetPassageAsync(row.PassageId, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !ReferenceEquals(row, SelectedBriefCitation)) return;
            SelectedPassage = passage;
            PassageMessage = passage is null ? "The passage could not be read. Try Refresh again." : "";
            NotifyPassage();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Another selection or window closure. */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            Log.Warning(ex, "Director citation passage could not be read.");
            PassageMessage = "The passage could not be read. Try Refresh again."; NotifyPassage();
        }
    }
    private static void ReplaceRows<T>(ObservableCollection<T> collection, IEnumerable<T> rows)
    { collection.Clear(); foreach (var row in rows) collection.Add(row); }

    public void Dispose()
    {
        _knowledgeLifetime.Cancel();
        _briefCts?.Cancel(); _briefCts?.Dispose(); _briefCts = null;
        _claimsCts?.Cancel(); _claimsCts?.Dispose(); _claimsCts = null;
        _passageCts?.Cancel(); _passageCts?.Dispose(); _passageCts = null;
        _knowledgeLifetime.Dispose();
        _accountCts?.Cancel(); _accountCts?.Dispose(); _accountCts = null;
        _asksCts?.Cancel(); _asksCts?.Dispose(); _asksCts = null;
    }

    public async Task<ReassignOutcome> ReassignAsync(BdDirectorPursuitRow row, string toOwner,
        string byStaff, string? reason, CancellationToken ct)
    {
        if (_isReassigning || IsLoading) return ReassignOutcome.Ignored;
        _isReassigning = true;
        OnPropertyChanged(nameof(CanRefresh));
        try
        {
            var pursuit = row.Pursuit;
            var outcome = await _pursuits.ReassignAsync(pursuit.EngagementId, pursuit.OpportunityId,
                pursuit.OwnerStaffId, toOwner, byStaff, reason, ct).ConfigureAwait(true);
            if (outcome == ReassignOutcome.Reassigned)
            {
                await LoadAsync(ct).ConfigureAwait(true);
                // Preserve a reload failure message: the write has already succeeded.
                StatusMessage = StatusMessage == "Up to date."
                    ? $"Reassigned “{row.ProjectName}” from {pursuit.OwnerStaffId} to {toOwner}."
                    : "The pursuit was reassigned. Could not refresh the figures; try Refresh again.";
            }
            else
            {
                StatusMessage = outcome switch
                {
                    ReassignOutcome.OwnerChanged => $"“{row.ProjectName}” was already moved by someone else — refresh to see who owns it.",
                    ReassignOutcome.DuplicateForTarget => $"{toOwner} already holds a pursuit for this buyer/region — not moved.",
                    ReassignOutcome.PursuitClosed => $"“{row.ProjectName}” has closed (Won/Lost) since the board loaded — not moved. Refresh the board.",
                    _ => StatusMessage,
                };
            }
            return outcome;
        }
        finally
        {
            _isReassigning = false;
            OnPropertyChanged(nameof(CanRefresh));
        }
    }
}
