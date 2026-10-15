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

public sealed class BdDirectorViewModel : ObservableObject
{
    public const string PeopleCaption = "Everyone who owns a live pursuit. A live pursuit is Drafting or Submitted. Counts are for that person's pursuits; opens are the BD screens they opened.";
    public const string FunnelCaption = "Live pursuits by stage, how long they have sat there, and what has been decided. Deltek history is counted separately and never added to the live numbers.";

    private readonly IBdDirectorStore _director;
    private readonly IPursuitOverwatchStore _pursuits;
    private readonly ICrmEngagementStore _records;
    private readonly IBdAttributionStore _attribution;
    private IReadOnlyList<PursuitOverwatchRow> _allPursuits = Array.Empty<PursuitOverwatchRow>();
    private BdDirectorPersonRow? _selectedPerson;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _isReassigning;

    public BdDirectorViewModel(IBdDirectorStore director, IPursuitOverwatchStore pursuits,
        ICrmEngagementStore records, IBdAttributionStore attribution)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _pursuits = pursuits ?? throw new ArgumentNullException(nameof(pursuits));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
    }

    public ObservableCollection<BdDirectorPersonRow> People { get; } = new();
    public ObservableCollection<BdDirectorPursuitRow> SelectedPursuits { get; } = new();
    public ObservableCollection<BdDirectorStageRow> Stages { get; } = new();
    public ObservableCollection<BdDirectorDeadlineRow> Undecided { get; } = new();
    public ObservableCollection<AttributionSourceRow> Sources { get; } = new();
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
            AsOf = DateTimeOffset.UtcNow;
            foreach (var property in new[] { nameof(Live), nameof(PeopleCount), nameof(AsOf), nameof(Outcomes),
                         nameof(LostDetails), nameof(DeadlineCaption) })
                OnPropertyChanged(property);
            StatusMessage = "Up to date.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director load failed.");
            StatusMessage = "Could not refresh. The last loaded figures remain; try Refresh again.";
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
