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

/// <summary>
/// A drafted message on a move, with its recipients said briefly when there are many. An owner's roll-up says whose it is and
/// how many pursuits it covers, and shows once it has been opened (its touchstones are recorded then).
/// </summary>
public sealed class HourDraftView : ObservableObject
{
    private bool _covered;

    public HourDraftView(HourDraft draft) => Draft = draft;

    public HourDraft Draft { get; }
    public string To => Draft.To;
    public string Subject => Draft.Subject;
    public string Body => Draft.Body;
    public bool IsRollup => Draft.IsRollup;
    public string KindText => Draft.IsInvite ? "Invite: " : Draft.IsRollup ? $"Roll-up to {Draft.RollupName}: " : "Draft: ";
    public string OutlookLabel => Draft.IsInvite ? "Invite in Outlook" : Draft.IsRollup ? "Review and forward in Outlook" : "Draft in Outlook";
    public string OutlookTip => Draft.IsInvite
        ? "Opens the meeting invite in Outlook with its people and time, unsent. You send it."
        : Draft.IsRollup
            ? $"Opens this roll-up in Outlook, addressed to {Draft.To}, unsent. Opening it marks its {CountText} covered for this week, so the next roll-up moves on."
            : "Opens a new message in Outlook for you to review and send. Nothing is sent from here.";
    public string CountText => Draft.RollupPursuits == 1 ? "1 pursuit" : $"{Draft.RollupPursuits} pursuits";
    /// <summary>"8 pursuits · not opened yet" before it is opened; "8 pursuits" and a green "✓ Opened, covered this week" after.</summary>
    public string RollupText => !Draft.IsRollup ? "" : "   " + CountText + (_covered ? "" : " · not opened yet");
    public string OpenedText => Draft.IsRollup && _covered ? "   ✓ Opened, covered this week" : "";
    public bool IsCovered
    {
        get => _covered;
        set { if (SetField(ref _covered, value)) { OnPropertyChanged(nameof(RollupText)); OnPropertyChanged(nameof(OpenedText)); } }
    }
    public string? WhenText => Draft.IsInvite && Draft.Start is { } start ? DirectorHourEmail.InviteTimeText(start, Draft.Minutes ?? 0) : null;
    public string ToText
    {
        get
        {
            var people = Draft.To.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return people.Length > 2 ? people.Length.ToString(CultureInfo.InvariantCulture) + " people" : Draft.To;
        }
    }
}

/// <summary>An accepted or retired card cited by a move.</summary>
public sealed record HourDoctrineCard(DoctrineCardRow Card)
{
    public long Id => Card.Id;
    public string Title => "From the doctrine: " + Card.Title;
    public bool IsRetired => Card.Status == "retired";
    public string StatusText => IsRetired ? "retired" : "";
}

/// <summary>One move of the hour and what Ian did with it. A move with a result folds away.</summary>
public sealed class HourMoveView : ObservableObject
{
    private string? _result;

    public HourMoveView(HourMove move, string? result)
    {
        Move = move;
        _result = result;
        Drafts = move.Drafts.Select(d => new HourDraftView(d)).ToList();
    }

    public HourMove Move { get; }
    public int Rank => Move.Rank;
    public string Title => Move.Title;
    public IReadOnlyList<string> WhyNow => Move.WhyNow;
    public HourPrinciple? Principle => Move.Principle;
    public IReadOnlyList<HourDraftView> Drafts { get; }
    public IReadOnlyList<string> Steps => Move.Steps;
    public IReadOnlyList<HourLink> Links => Move.Links;
    public ObservableCollection<HourDoctrineCard> Cards { get; } = new();

    public void SetCards(IReadOnlyList<DoctrineCardRow> cards)
    {
        Cards.Clear();
        foreach (var id in Move.Cards)
            if (cards.FirstOrDefault(c => c.Id == id && c.Status is "accepted" or "retired") is { } card)
                Cards.Add(new HourDoctrineCard(card));
    }

    private string _goalsText = "";

    /// <summary>"Serves: Days to collect down to 120" — the operating-plan goals this move advances (migration 372).</summary>
    public string GoalsText { get => _goalsText; private set => SetField(ref _goalsText, value); }

    public void SetGoals(IReadOnlyDictionary<int, string> goals)
    {
        var names = Move.Goals.Select(id => goals.TryGetValue(id, out var title) ? title : "goal " + id).ToList();
        GoalsText = names.Count == 0 ? "" : "Serves: " + string.Join(" · ", names);
    }

    public string? Result
    {
        get => _result;
        set
        {
            if (!SetField(ref _result, value)) return;
            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(IsResolved));
            OnPropertyChanged(nameof(SideText));
        }
    }

    public bool IsResolved => _result is not null;
    public bool IsOpen => _result is null;
    public string SideText => _result switch
    {
        "Done" => "Done",
        "NotToday" => "Not today",
        "NotDoing" => "Not doing",
        _ => Move.Minutes.ToString(CultureInfo.InvariantCulture) + " min",
    };
}

/// <summary>A person on the People screen: what they owe, how late, and when Ian next sees them.</summary>
public sealed record BdPersonCard(BdLeadRow Lead, int Open, int Late, int ThisWeek, DateTimeOffset? NextMeetingUtc, string? LastDone)
{
    public string Key => Lead.StaffKey;
    public string Name => Lead.Name;
    public bool IsLead => Lead.Role == "Lead";
    public string RoleText => Lead.Role switch
    {
        "Manager" => "BD manager",
        "Partner" => "Partner",
        _ => RegionLabel(Lead.Region),
    };
    public string OpenText => Open.ToString(CultureInfo.InvariantCulture) + " open";
    public string LateText => Late.ToString(CultureInfo.InvariantCulture) + " late";
    public string WeekText => ThisWeek.ToString(CultureInfo.InvariantCulture) + " this week";
    public bool HasLate => Late > 0;
    public bool HasThisWeek => ThisWeek > 0;
    public string NextText => !IsLead ? (Lead.Role == "Manager" ? "Your own commitments" : "No one-to-one; a partner")
        : NextMeetingUtc is { } at ? "Next one-to-one " + at.ToLocalTime().ToString("ddd d MMM", CultureInfo.InvariantCulture) : "Next one-to-one: not booked";

    public static string RegionLabel(string? region) => region switch
    {
        "Vancouver/LowerMainland" => "Vancouver and the Lower Mainland",
        "VancouverIsland" => "Vancouver Island",
        "Okanagan-BcInterior" => "Okanagan and the BC Interior",
        "USA" => "United States",
        null => "",
        var r => r,
    };
}

/// <summary>A role heading with the same move instances used by Today's progress and result buttons.</summary>
public sealed record HourSectionView(string Role, string Title, IReadOnlyList<HourPlanLine> Plan,
    IReadOnlyList<HourMoveView> Moves, IReadOnlyList<string> Decisions);

/// <summary>
/// The three screens Ian asked for on 16 October ("v1 was brutal"): Today, where each move is done on its card; People, one card
/// per person with what they owe and their next one-to-one; Pursuits lives on the Director view model. Everything here reads and
/// writes through <see cref="IBdRhythmStore"/> and sends nothing.
/// </summary>
public sealed partial class BdRhythmViewModel
{
    private long? _hourId;
    private string _meKey = "";
    private IReadOnlyList<BdLeadRow> _leads = Array.Empty<BdLeadRow>();
    private IReadOnlyDictionary<int, string> _moveResults = new Dictionary<int, string>();
    private BdPersonCard? _selectedPerson;
    private string? _selectedPersonKey;
    private string _personAgenda = "";
    private IReadOnlyList<DoctrineCardRow> _doctrineCards = Array.Empty<DoctrineCardRow>();

    public void SetDoctrineCards(IReadOnlyList<DoctrineCardRow> cards)
    {
        _doctrineCards = cards;
        foreach (var move in Moves) move.SetCards(cards);
    }

    private IReadOnlyDictionary<int, string> _planGoals = new Dictionary<int, string>();

    /// <summary>The operating plan's goal titles by id, so each move can say which goals it serves.</summary>
    public void SetPlanGoals(IReadOnlyDictionary<int, string> goals)
    {
        _planGoals = goals;
        foreach (var move in Moves) move.SetGoals(goals);
    }

    private HashSet<string> _coveredTouchstones = new(StringComparer.Ordinal);

    /// <summary>Touchstone keys already recorded as drafted: a roll-up whose keys are all here shows as opened.</summary>
    public void SetCoveredTouchstones(IEnumerable<string> keys)
    {
        _coveredTouchstones = new HashSet<string>(keys, StringComparer.Ordinal);
        ApplyCovered();
    }

    private void ApplyCovered()
    {
        foreach (var draft in Moves.SelectMany(m => m.Drafts).Where(d => d.IsRollup))
            draft.IsCovered = draft.Draft.Touchstones!.All(_coveredTouchstones.Contains);
    }

    // ---- Today ----
    public ObservableCollection<HourMoveView> Moves { get; } = new();
    public ObservableCollection<HourSectionView> HourSections { get; } = new();
    public ObservableCollection<BdActionView> MyActions { get; } = new();
    public string MovesProgressText
    {
        get
        {
            var done = Moves.Count(m => m.IsResolved);
            var left = Moves.Where(m => m.IsOpen).Sum(m => m.Move.Minutes);
            return $"{done} of {Moves.Count} moves done · about {left} minutes left";
        }
    }
    public bool IsHourFinished => Moves.Count > 0 && Moves.All(m => m.IsResolved);
    public int TeamLate => _allActions.Count(a => a.IsOpen && a.DueUtc < _clock());
    public string TeamLateText => TeamLate == 1 ? "1 action late across the team" : $"{TeamLate} actions late across the team";
    public string DueTomorrowText
    {
        get
        {
            var today = _clock().Date;
            var next = NextWorkingDay(today);
            var label = next == today.AddDays(1) ? "tomorrow" : next.DayOfWeek.ToString();
            return $"{_allActions.Count(a => a.IsOpen && a.DueUtc.ToLocalTime().Date == next)} due {label}";
        }
    }
    /// <summary>The next working day: on a Friday that is Monday, not Saturday.</summary>
    public static DateTime NextWorkingDay(DateTime today) =>
        today.Date.AddDays(today.DayOfWeek switch { DayOfWeek.Friday => 3, DayOfWeek.Saturday => 2, _ => 1 });

    public string ClosedSinceYesterdayText =>
        $"Since yesterday: {_allActions.Count(a => a.ClosedAtUtc is { } at && at >= _clock().AddDays(-1))} closed";
    public string MyActionsText => $"{_allActions.Count(a => a.IsOpen && IsMe(a.OwnerKey))} open. Tick one to close it with a one-line result.";

    // ---- People ----
    public ObservableCollection<BdPersonCard> PeopleCards { get; } = new();
    public ObservableCollection<BdActionView> PersonActions { get; } = new();
    public BdPersonCard? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!SetField(ref _selectedPerson, value)) return;
            if (value is not null) _selectedPersonKey = value.Key;
            FillPerson();
        }
    }
    public string PersonAgenda { get => _personAgenda; private set => SetField(ref _personAgenda, value); }
    public string PersonTitle => SelectedPerson is { } p ? $"{p.Name}: {p.OpenText}" : "";
    public bool PersonIsLead => SelectedPerson?.IsLead == true;
    public string PersonNote => SelectedPerson switch
    {
        null => "",
        { IsLead: true } => "",
        { Lead.Role: "Manager" } => "Your own list. The all-hands agenda, built from everyone's list, is planned under Data, Meetings.",
        _ => "A partner, not a regional lead: the actions sit here without a one-to-one.",
    };
    public int PeopleLate => TeamLate;

    /// <summary>Whose list is "Your list" on Today: the signed-in person. Before it is known, the team's manager.</summary>
    public void SetMe(string staffKey)
    {
        _meKey = staffKey?.Trim() ?? "";
        BuildScreens();
    }

    private bool IsMe(string ownerKey)
    {
        var me = _meKey.Length > 0 ? _meKey : _leads.FirstOrDefault(l => l.Role == "Manager")?.StaffKey ?? "";
        return me.Length > 0 && string.Equals(ownerKey, me, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadScreensAsync(CancellationToken ct)
    {
        try
        {
            _leads = await _store.ListLeadsAsync(ct).ConfigureAwait(true);
            _moveResults = _hourId is { } id
                ? (await _store.ListMoveResultsAsync(id, ct).ConfigureAwait(true)).ToDictionary(r => r.Rank, r => r.Result)
                : new Dictionary<int, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load the team or the hour's results.");
            Status = "The team or today's results could not be loaded.";
        }

        Moves.Clear();
        HourSections.Clear();
        if (Hour is { } hour)
        {
            foreach (var m in DirectorHourFile.AllMoves(hour).OrderBy(m => m.Rank))
            {
                var view = new HourMoveView(m, _moveResults.TryGetValue(m.Rank, out var r) ? r : null);
                view.SetCards(_doctrineCards);
                view.SetGoals(_planGoals);
                Moves.Add(view);
            }
            if (hour.Kind == "Morning")
                foreach (var section in hour.Sections ?? Array.Empty<HourSection>())
                    HourSections.Add(new HourSectionView(section.Role, section.Title, section.Plan,
                        section.Moves.Select(m => Moves.Single(v => v.Rank == m.Rank)).ToArray(), section.Decisions));
            else HourSections.Add(new HourSectionView("", "", hour.Plan, Moves.ToArray(), Array.Empty<string>()));
            ApplyCovered();
        }
        BuildScreens();
    }

    /// <summary>Records what Ian did with a move. The card folds away; a second answer replaces the first.</summary>
    public async Task RecordMoveAsync(HourMoveView move, string result, string actor, CancellationToken ct, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (_hourId is not { } id) { Status = "This hour was not imported, so nothing can be recorded against it."; return; }
        await _store.RecordMoveResultAsync(id, move.Rank, result, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), actor, ct).ConfigureAwait(true);
        move.Result = result;
        Status = $"Move {move.Rank}: {move.SideText.ToLowerInvariant()}.";
        RaiseToday();
    }

    /// <summary>Closes one open action, from Today or People, and refreshes every screen that counts it.</summary>
    public async Task<bool> CloseActionAsync(BdActionView action, string status, string? result, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.Row.IsOpen) return false;
        var closed = await _store.CloseActionAsync(action.Row.Id, status, result, actor, ct).ConfigureAwait(true);
        Status = closed ? $"Marked {status.ToLowerInvariant()}: {action.Row.Action}" : "That action was already closed. Refreshed.";
        await LoadActionsAsync(ct).ConfigureAwait(true);
        return closed;
    }

    private void BuildScreens()
    {
        var now = _clock();
        MyActions.Clear();
        foreach (var a in _allActions.Where(a => a.IsOpen && IsMe(a.OwnerKey)).OrderBy(a => a.DueUtc).Take(8))
            MyActions.Add(new BdActionView(a, a.DueUtc < now) { DueText = DueWords(a, now) });

        PeopleCards.Clear();
        foreach (var lead in _leads)
        {
            var mine = _allActions.Where(a => string.Equals(a.OwnerKey, lead.StaffKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var next = Meetings.Where(m => m.Row.Kind == "OneToOne" && m.Row.Status == "Planned" && m.Row.ScheduledForUtc >= now.AddHours(-12)
                    && string.Equals(m.Row.WithStaffKey, lead.StaffKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Row.ScheduledForUtc).Select(m => (DateTimeOffset?)m.Row.ScheduledForUtc).FirstOrDefault();
            var lastDone = mine.Where(a => a.Status == "Done").OrderByDescending(a => a.ClosedAtUtc).Select(a => a.Action).FirstOrDefault();
            PeopleCards.Add(new BdPersonCard(lead, mine.Count(a => a.IsOpen), mine.Count(a => a.IsOpen && a.DueUtc < now),
                mine.Count(a => a.IsOpen && a.DueUtc >= now && a.DueUtc < now.AddDays(7)), next, lastDone));
        }

        _selectedPerson = PeopleCards.FirstOrDefault(p => p.Key == _selectedPersonKey) ?? PeopleCards.FirstOrDefault(p => p.IsLead) ?? PeopleCards.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPerson));
        FillPerson();
        RaiseToday();
    }

    private void FillPerson()
    {
        var now = _clock();
        PersonActions.Clear();
        if (SelectedPerson is { } p)
        {
            var mine = _allActions.Where(a => string.Equals(a.OwnerKey, p.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var a in mine.OrderBy(a => a.IsOpen ? 0 : 1).ThenBy(a => a.DueUtc))
                PersonActions.Add(new BdActionView(a, a.IsOpen && a.DueUtc < now) { DueText = DueWords(a, now) });
            var last = Meetings.Where(m => m.Row.Kind == "OneToOne" && m.Row.Status == "Held"
                    && string.Equals(m.Row.WithStaffKey, p.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Row.ScheduledForUtc).Select(m => (DateTimeOffset?)m.Row.ScheduledForUtc).FirstOrDefault();
            PersonAgenda = p.IsLead ? BdAgenda.ForOneToOne(p.Name, mine, now, last).ToText() : "";
        }
        else PersonAgenda = "";

        OnPropertyChanged(nameof(PersonTitle));
        OnPropertyChanged(nameof(PersonIsLead));
        OnPropertyChanged(nameof(PersonNote));
    }

    /// <summary>What a question about today carries: the hour, how the team stands, and the situations that are live.</summary>
    public (string Subject, DoctrineContext Context, IReadOnlyList<string> Facts) TodayAskContext()
    {
        var facts = new List<string>();
        if (Hour is { } h)
        {
            facts.Add($"Today's prepared hour: {h.Title}. {MovesProgressText}.");
            facts.AddRange(Moves.Select(m => $"Move {m.Rank}: {m.Title} ({m.SideText})"));
        }
        else facts.Add(HourMessage);
        facts.Add($"{TeamLateText}; {DueTomorrowText}; {ClosedSinceYesterdayText}.");
        facts.AddRange(PeopleCards.Select(p => $"{p.Name} ({p.RoleText}): {p.OpenText}, {p.LateText}, {p.WeekText}. {p.NextText}."));
        var situations = new List<string> { "team-cadence" };
        if (TeamLate > 0) situations.Add("late-action");
        return ("today", new DoctrineContext("hour", situations), facts);
    }

    /// <summary>What a question about the selected person carries: their list, how late, and their next one-to-one.</summary>
    public (string Subject, DoctrineContext Context, IReadOnlyList<string> Facts)? PersonAskContext()
    {
        if (SelectedPerson is not { } p) return null;
        var facts = new List<string> { $"{p.Name}: {p.RoleText}. {p.OpenText}, {p.LateText}, {p.WeekText}. {p.NextText}." };
        facts.AddRange(PersonActions.Select(a => $"{(a.Row.IsOpen ? "Open" : a.Row.Status)}: {a.Row.Action} ({a.DueText}; {a.From})"
            + (string.IsNullOrWhiteSpace(a.Row.Result) ? "" : $" Result: {a.Row.Result}")));
        if (p.IsLead && PersonAgenda.Length > 0) facts.Add("Their one-to-one agenda as built now:\n" + PersonAgenda);
        var situations = new List<string> { "team-cadence" };
        if (p.Late > 0) situations.Add("late-action");
        return (p.Name, new DoctrineContext(p.IsLead ? "one-to-one" : "person", situations, Region: p.Lead.Region, Person: p.Key), facts);
    }

    internal static string DueWords(BdActionRow a, DateTimeOffset now)
    {
        if (!a.IsOpen) return a.Status;
        var days = (a.DueUtc.ToLocalTime().Date - now.ToLocalTime().Date).Days;
        return days switch
        {
            < -1 => (-days).ToString(CultureInfo.InvariantCulture) + " days late",
            -1 => "1 day late",
            0 => "today",
            1 => "tomorrow",
            _ => a.DueUtc.ToLocalTime().ToString("ddd d MMM", CultureInfo.InvariantCulture),
        };
    }

    private void RaiseToday()
    {
        OnPropertyChanged(nameof(MovesProgressText));
        OnPropertyChanged(nameof(IsHourFinished));
        OnPropertyChanged(nameof(TeamLate));
        OnPropertyChanged(nameof(TeamLateText));
        OnPropertyChanged(nameof(DueTomorrowText));
        OnPropertyChanged(nameof(ClosedSinceYesterdayText));
        OnPropertyChanged(nameof(MyActionsText));
        OnPropertyChanged(nameof(PeopleLate));
    }
}
