#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>An action on the register, with whether it is late.</summary>
public sealed record BdActionView(BdActionRow Row, bool IsOverdue)
{
    public string StatusText => Row.IsOpen ? (IsOverdue ? "Overdue" : "Open") : Row.Status;
    /// <summary>When it is due, in words: "3 days late", "today", "tomorrow", "Sat 24 Oct"; or how it closed.</summary>
    public string DueText { get; init; } = "";
    public string From => Row.SourceRef ?? Row.MeetingTitle ?? "";
    public string RegionText => Row.Region switch
    {
        "Vancouver/LowerMainland" => "Lower Mainland",
        "VancouverIsland" => "Vancouver Island",
        "Okanagan-BcInterior" => "Okanagan",
        null => "",
        var r => r,
    };
}

/// <summary>A person a one-to-one can be planned with.</summary>
public sealed record BdLeadOption(string Key, string Name);

/// <summary>A meeting, with the text to show for it: its recap once held, otherwise its agenda.</summary>
public sealed record BdMeetingView(BdMeetingRow Row)
{
    public string KindText => Row.Kind switch { "AllHands" => "All-hands", "OneToOne" => "One-to-one", _ => "Meeting" };
    public string Text => Row.Status == "Held" && !string.IsNullOrWhiteSpace(Row.Recap) ? Row.Recap! : Row.Agenda ?? "No agenda yet.";
}

/// <summary>A reference row, with a plain reading of whether it can be used.</summary>
public sealed record BdReferenceView(BdReferenceRow Row)
{
    public string Contact => Row.ContactName ?? "No person named";
    public string OutcomeText => Row.CallOutcome switch
    {
        "NotCalled" => "Not called",
        "LeftMessage" => "Left a message",
        "NoLongerThere" => "No longer there",
        var o => o,
    };
    public bool NeedsAttention => !Row.NamesAPerson || Row.CallOutcome != "Confirmed";
}

/// <summary>
/// BD Director version 2: the prepared hour (Today), the action register, meetings with generated agendas, and the references
/// register. Reads and writes go through <see cref="IBdRhythmStore"/>; nothing here sends anything to anyone.
/// </summary>
public sealed partial class BdRhythmViewModel : ObservableObject
{
    private readonly IBdRhythmStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<BdActionRow> _allActions = Array.Empty<BdActionRow>();
    private DirectorHourContent? _hour;
    private string _hourMessage = "Loading today's hour…";
    private string _actionFilter = "Open";
    private string _ownerFilter = AllOwners;
    private BdActionView? _selectedAction;
    private BdMeetingView? _selectedMeeting;
    private BdReferenceView? _selectedReference;
    private string _status = "";

    public const string AllOwners = "Everyone";

    public BdRhythmViewModel(IBdRhythmStore store) : this(store, () => DateTimeOffset.Now) { }

    public BdRhythmViewModel(IBdRhythmStore store, Func<DateTimeOffset> clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // ---- Today ----
    public DirectorHourContent? Hour { get => _hour; private set { if (SetField(ref _hour, value)) { OnPropertyChanged(nameof(HasHour)); OnPropertyChanged(nameof(TotalMinutesText)); } } }
    public bool HasHour => Hour is not null;
    public string HourMessage { get => _hourMessage; private set => SetField(ref _hourMessage, value); }
    public string TotalMinutesText => Hour is null ? "" : $"About {DirectorHourFile.TotalMinutes(Hour).ToString(CultureInfo.InvariantCulture)} minutes";

    // ---- Actions ----
    public ObservableCollection<BdActionView> Actions { get; } = new();
    public IReadOnlyList<string> ActionFilters { get; } = new[] { "Open", "Overdue", "All" };
    public ObservableCollection<string> Owners { get; } = new() { AllOwners };
    public string ActionFilter { get => _actionFilter; set { if (SetField(ref _actionFilter, value)) FilterActions(); } }
    public string OwnerFilter { get => _ownerFilter; set { if (SetField(ref _ownerFilter, value ?? AllOwners)) FilterActions(); } }
    public BdActionView? SelectedAction { get => _selectedAction; set { if (SetField(ref _selectedAction, value)) OnPropertyChanged(nameof(CanCloseAction)); } }
    public bool CanCloseAction => SelectedAction?.Row.IsOpen == true;
    public string ActionSummary
    {
        get
        {
            var now = _clock();
            var open = _allActions.Count(a => a.IsOpen);
            var overdue = _allActions.Count(a => a.IsOpen && a.DueUtc < now);
            var week = _allActions.Count(a => a.IsOpen && a.DueUtc >= now && a.DueUtc < now.AddDays(7));
            var done = _allActions.Count(a => a.Status == "Done");
            return $"{open} open · {overdue} overdue · {week} due in the next 7 days · {done} done";
        }
    }

    // ---- Meetings ----
    public ObservableCollection<BdMeetingView> Meetings { get; } = new();
    public BdMeetingView? SelectedMeeting { get => _selectedMeeting; set => SetField(ref _selectedMeeting, value); }
    public IReadOnlyList<BdLeadOption> Leads =>
        _allActions.Where(a => a.OwnerKey.Contains('@')).GroupBy(a => (a.OwnerKey, a.OwnerName))
            .Select(g => new BdLeadOption(g.Key.OwnerKey, g.Key.OwnerName)).OrderBy(k => k.Name, StringComparer.Ordinal).ToList();

    // ---- References ----
    public ObservableCollection<BdReferenceView> References { get; } = new();
    public BdReferenceView? SelectedReference { get => _selectedReference; set => SetField(ref _selectedReference, value); }
    public string ReferenceSummary
    {
        get
        {
            var rows = References.Select(r => r.Row).ToList();
            return $"{rows.Count(r => r.NamesAPerson)} of {rows.Count} name a person · {rows.Count(r => r.CallOutcome == "Confirmed")} confirmed by phone · {rows.Count(r => r.CallOutcome == "NotCalled")} not called";
        }
    }

    public string Status { get => _status; private set => SetField(ref _status, value); }

    /// <summary>Posts a line to the status bar, for actions taken in the window (copying, opening a draft).</summary>
    public void Say(string text) => Status = text;

    public async Task LoadAsync(CancellationToken ct)
    {
        Status = "Loading…";
        await Task.WhenAll(LoadHourAsync(ct), LoadActionsAsync(ct), LoadMeetingsAsync(ct), LoadReferencesAsync(ct)).ConfigureAwait(true);
        await LoadScreensAsync(ct).ConfigureAwait(true);
        Status = "Up to date.";
    }

    /// <summary>Today's hour; when none was prepared for today, the most recent of the last seven days, saying so.</summary>
    public async Task LoadHourAsync(CancellationToken ct)
    {
        try
        {
            _hourId = null;
            var today = DateOnly.FromDateTime(_clock().Date);
            for (var back = 0; back < 7; back++)
            {
                var date = today.AddDays(-back);
                var row = await _store.GetHourAsync("BD", date, ct).ConfigureAwait(true);
                if (row is null) continue;
                var (content, errors) = DirectorHourFile.Parse(row.ContentJson);
                if (content is null)
                {
                    Hour = null;
                    HourMessage = "The hour imported for " + date.ToString("d MMM", CultureInfo.InvariantCulture) + " could not be read: " + string.Join(" ", errors);
                    return;
                }

                _hourId = row.Id;
                Hour = content;
                HourMessage = back == 0 ? "" : "No hour was prepared for today. This is the most recent, for " + date.ToString("dddd d MMMM", CultureInfo.InvariantCulture) + ".";
                return;
            }

            Hour = null;
            HourMessage = "No hour has been prepared in the last seven days.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load the prepared hour.");
            Hour = null;
            HourMessage = "Today's hour could not be loaded. Refresh to try again.";
        }
    }

    public async Task LoadActionsAsync(CancellationToken ct)
    {
        try
        {
            _allActions = await _store.ListActionsAsync(ct).ConfigureAwait(true);
            var names = _allActions.Select(a => a.OwnerName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            Owners.Clear();
            Owners.Add(AllOwners);
            foreach (var n in names) Owners.Add(n);
            if (!Owners.Contains(OwnerFilter)) _ownerFilter = AllOwners;
            OnPropertyChanged(nameof(OwnerFilter));
            FilterActions();
            OnPropertyChanged(nameof(Leads));
            BuildScreens();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load the action register.");
            Status = "The action register could not be loaded.";
        }
    }

    public async Task LoadMeetingsAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _store.ListMeetingsAsync(ct).ConfigureAwait(true);
            var keep = SelectedMeeting?.Row.Id;
            Meetings.Clear();
            foreach (var m in rows) Meetings.Add(new BdMeetingView(m));
            SelectedMeeting = Meetings.FirstOrDefault(m => m.Row.Id == keep) ?? Meetings.FirstOrDefault();
            BuildScreens();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load meetings.");
            Status = "Meetings could not be loaded.";
        }
    }

    public async Task LoadReferencesAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _store.ListReferencesAsync(ct).ConfigureAwait(true);
            References.Clear();
            foreach (var r in rows) References.Add(new BdReferenceView(r));
            OnPropertyChanged(nameof(ReferenceSummary));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load references.");
            Status = "References could not be loaded.";
        }
    }

    public async Task<bool> CloseSelectedActionAsync(string status, string? result, string actor, CancellationToken ct)
    {
        if (SelectedAction is not { } a || !a.Row.IsOpen) return false;
        var closed = await _store.CloseActionAsync(a.Row.Id, status, result, actor, ct).ConfigureAwait(true);
        Status = closed ? $"Marked {status.ToLowerInvariant()}: {a.Row.Action}" : "That action was already closed. Refreshed.";
        await LoadActionsAsync(ct).ConfigureAwait(true);
        return closed;
    }

    /// <summary>Plans a one-to-one with the agenda generated from that person's actions.</summary>
    public async Task<long> PlanOneToOneAsync(string leadKey, string leadName, DateTimeOffset whenUtc, string actor, CancellationToken ct)
    {
        var last = Meetings.Where(m => m.Row.Kind == "OneToOne" && m.Row.Status == "Held"
                && string.Equals(m.Row.WithStaffKey, leadKey, StringComparison.OrdinalIgnoreCase))
            .Select(m => (DateTimeOffset?)m.Row.ScheduledForUtc).FirstOrDefault();
        var agenda = BdAgenda.ForOneToOne(leadName, _allActions.Where(a => string.Equals(a.OwnerKey, leadKey, StringComparison.OrdinalIgnoreCase)), _clock(), last);
        var id = await _store.PlanMeetingAsync("OneToOne", agenda.Title, leadKey, whenUtc, agenda.Minutes, $"Ian Lalonde, {leadName}", agenda.ToText(), actor, ct).ConfigureAwait(true);
        Status = "Planned: " + agenda.Title + ". Nothing was sent; copy the agenda into your invite.";
        await LoadMeetingsAsync(ct).ConfigureAwait(true);
        SelectedMeeting = Meetings.FirstOrDefault(m => m.Row.Id == id) ?? SelectedMeeting;
        return id;
    }

    /// <summary>Plans the all-hands with the agenda generated from the whole register.</summary>
    public async Task<long> PlanAllHandsAsync(DateTimeOffset whenUtc, string actor, CancellationToken ct)
    {
        var last = Meetings.Where(m => m.Row.Kind == "AllHands" && m.Row.Status == "Held").Select(m => (DateTimeOffset?)m.Row.ScheduledForUtc).FirstOrDefault();
        var agenda = BdAgenda.ForAllHands(_allActions, _clock(), last);
        var id = await _store.PlanMeetingAsync("AllHands", agenda.Title, null, whenUtc, agenda.Minutes,
            "Ian, Jim, John, Conor, Islam, Omar, Rory", agenda.ToText(), actor, ct).ConfigureAwait(true);
        Status = "Planned the all-hands. Nothing was sent; copy the agenda into your invite.";
        await LoadMeetingsAsync(ct).ConfigureAwait(true);
        SelectedMeeting = Meetings.FirstOrDefault(m => m.Row.Id == id) ?? SelectedMeeting;
        return id;
    }

    public async Task RecordSelectedMeetingAsync(string status, string? recap, CancellationToken ct)
    {
        if (SelectedMeeting is not { } m) return;
        await _store.RecordMeetingAsync(m.Row.Id, status, recap, null, ct).ConfigureAwait(true);
        Status = status == "Held" ? "Recorded as held: " + m.Row.Title : "Cancelled: " + m.Row.Title;
        await LoadMeetingsAsync(ct).ConfigureAwait(true);
    }

    public async Task RecordSelectedReferenceCallAsync(string outcome, string? contactName, string? notes, string actor, CancellationToken ct)
    {
        if (SelectedReference is not { } r) return;
        await _store.RecordReferenceCallAsync(r.Row.Id, outcome, contactName, notes, actor, ct).ConfigureAwait(true);
        Status = "Recorded the call: " + r.Row.Project + ", " + r.Row.Role;
        await LoadReferencesAsync(ct).ConfigureAwait(true);
    }

    private void FilterActions()
    {
        var now = _clock();
        var keep = SelectedAction?.Row.Id;
        Actions.Clear();
        foreach (var a in _allActions)
        {
            var overdue = a.IsOpen && a.DueUtc < now;
            var statusMatch = ActionFilter switch { "Open" => a.IsOpen, "Overdue" => overdue, _ => true };
            if (!statusMatch || (OwnerFilter != AllOwners && a.OwnerName != OwnerFilter)) continue;
            Actions.Add(new BdActionView(a, overdue));
        }

        SelectedAction = Actions.FirstOrDefault(a => a.Row.Id == keep);
        OnPropertyChanged(nameof(ActionSummary));
    }
}
