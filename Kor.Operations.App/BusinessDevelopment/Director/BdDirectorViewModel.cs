#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Operations.Core;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class BdDirectorViewModel : ObservableObject, IDisposable
{
    public const string PeopleCaption = "Everyone who owns a live pursuit. A live pursuit is Drafting or Submitted. Counts are for that person's pursuits; opens are the BD screens they opened.";
    public const string FunnelCaption = "Live pursuits by stage, how long they have sat there, and what has been decided. Deltek history is counted separately and never added to the live numbers.";
    public const string NumberCaption = "Targets you set, against what the records show today. Won fee and proposals submitted are not offered because no live pursuit carries a fee and none has reached Submitted.";
    public const string AccountsCaption = "The firm's Deltek clients, most KOR projects first. Counts are Deltek records, proposals included, unless the row says wins. Select an account to read its Deltek summary.";
    public const string AsksCaption = "One bounded thing, for one person, by a date. Done and declined asks stay on the record.";
    public const string DecisionsCaption = "What was decided, why, and what was expected. A decision is never edited; it is scored Right, Wrong or Unclear when its review date comes.";

    private readonly IBdDirectorStore _director;
    private readonly IPursuitOverwatchStore _pursuits;
    private readonly ICrmEngagementStore _records;
    private readonly IBdAttributionStore _attribution;
    private readonly IBdDirectorLedgerStore _ledger;
    private readonly IDeltekClientContextService _deltek;
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

    public BdDirectorViewModel(IBdDirectorStore director, IPursuitOverwatchStore pursuits,
        ICrmEngagementStore records, IBdAttributionStore attribution, IBdDirectorLedgerStore ledger,
        IDeltekClientContextService deltek)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _pursuits = pursuits ?? throw new ArgumentNullException(nameof(pursuits));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _deltek = deltek ?? throw new ArgumentNullException(nameof(deltek));
    }

    public ObservableCollection<BdDirectorPersonRow> People { get; } = new();
    public ObservableCollection<BdDirectorPursuitRow> SelectedPursuits { get; } = new();
    public ObservableCollection<BdDirectorStageRow> Stages { get; } = new();
    public ObservableCollection<BdDirectorDeadlineRow> Undecided { get; } = new();
    public ObservableCollection<AttributionSourceRow> Sources { get; } = new();
    public ObservableCollection<BdDirectorTargetRow> Targets { get; } = new();
    public ObservableCollection<BdDirectorAccountRow> Accounts { get; } = new();
    public ObservableCollection<BdDirectorAskRow> Asks { get; } = new();
    public ObservableCollection<BdDirectorDecisionRow> Decisions { get; } = new();
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
            await Task.WhenAll(peopleTask, funnelTask, pursuitsTask, recordsTask, attributionTask).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var people = await peopleTask.ConfigureAwait(true);
            var funnel = await funnelTask.ConfigureAwait(true);
            var records = await recordsTask.ConfigureAwait(true);
            var attribution = await attributionTask.ConfigureAwait(true);
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
                Stages.Add(new BdDirectorStageRow(stage.ToString(), count,
                    funnel.MedianDaysInStage.TryGetValue((int)stage, out var median) ? median : null));
            }
            Live = Stages.Sum(s => s.Pursuits);
            Outcomes = snapshot.Won + snapshot.Lost == 0
                ? "No live pursuit has been decided yet."
                : $"Won {snapshot.Won} · Lost {snapshot.Lost} · win rate {snapshot.WinRate.ToString("P0", CultureInfo.CurrentCulture)} on decided live pursuits · {snapshot.BackfillWins} Deltek wins kept separate";
            LostDetails = $"Of the lost: {funnel.LostWithCompetitor} name the competitor, {funnel.LostWithReason} give a reason.";
            DeadlineCaption = $"Submitted or drafting, deadline more than 30 days gone, no outcome recorded. {funnel.UndecidedNoDeadline} more have no deadline on file.";
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
        ct.ThrowIfCancellationRequested();
        SelectedDecision = null;
        Decisions.Clear();
        foreach (var decision in rows) Decisions.Add(new BdDirectorDecisionRow(decision));
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
        await RefreshAfterWriteAsync(() => LoadDecisionsAsync(ct), "Decision scored.").ConfigureAwait(true);
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

    public void Dispose()
    {
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
