#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// BD Director, version 3 (Ian, 21 Oct 2026: "Clarity is powerful", KOR palette): Today, Pursuits and Team, built from what
/// the window already loads plus one board read. Decisions first, one action per card, colour only where it means something
/// (the verdict; a deadline close or overdue). Nothing here tracks who opened what. Pure apart from its inputs, so a test and
/// the render harness can draw it without a database.
/// </summary>
public sealed class BdDirectorHomeViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ObservableCollection<DirectorCard> Decisions { get; } = new();
    public ObservableCollection<DirectorWeekItem> Week { get; } = new();
    public ObservableCollection<DirectorListItem> MyList { get; } = new();
    public ObservableCollection<DirectorCard> Deciding { get; } = new();
    public ObservableCollection<DirectorCard> Submitted { get; } = new();
    public ObservableCollection<DirectorTeamCard> Team { get; } = new();

    public string Greeting { get; private set; } = "";
    public string Subtitle { get; private set; } = "";
    public string PursuitsSubtitle { get; private set; } = "";
    public string TeamSubtitle { get; private set; } = "";
    public DirectorTeamCard? Nudge { get; private set; }
    public bool HasNudge => Nudge is not null;
    public bool HasDecisions => Decisions.Count > 0;
    public bool HasWeek => Week.Count > 0;
    public bool HasMyList => MyList.Count > 0;
    public bool WeekEmpty => Week.Count == 0;
    public bool ListEmpty => MyList.Count == 0;

    /// <summary>Ticks one ask off Today's list in place, once the store has closed it.</summary>
    public void RemoveFromList(DirectorListItem item)
    {
        MyList.Remove(item);
        Changed(nameof(HasMyList)); Changed(nameof(ListEmpty));
    }
    public int DecisionCount => Decisions.Count;
    public string DecidingHeader => $"Deciding  {Deciding.Count}";
    public string SubmittedHeader => $"Submitted  {Submitted.Count}";

    private string _hourTitle = "";
    private string _hourProgress = "";
    /// <summary>The prepared hour's title and move count, from the rhythm the window already loads.</summary>
    public string HourTitle { get => _hourTitle; set { _hourTitle = value; Changed(); Changed(nameof(HasHour)); } }
    public string HourProgress { get => _hourProgress; set { _hourProgress = value; Changed(); } }
    public bool HasHour => !string.IsNullOrWhiteSpace(_hourTitle);

    private bool _mineOnly;
    private IReadOnlyList<TenderBoardRow> _board = Array.Empty<TenderBoardRow>();
    private string _actor = "";
    private DateTimeOffset _now;

    /// <summary>Pursuits: only the actor's, or everyone's.</summary>
    public bool MineOnly { get => _mineOnly; set { _mineOnly = value; Changed(); FillBoard(); } }

    private DirectorCard? _selected;
    /// <summary>The pursuit open in the side panel on Pursuits.</summary>
    public DirectorCard? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            foreach (var c in Deciding.Concat(Submitted)) c.IsSelected = ReferenceEquals(c, value);
            Changed(); Changed(nameof(HasSelected));
            ReassignTo = null;
        }
    }
    public bool HasSelected => _selected is not null;

    public ObservableCollection<DirectorPerson> People { get; } = new();

    private DirectorPerson? _reassignTo;
    /// <summary>The person picked in the side panel; the preview says what they will get before anything moves.</summary>
    public DirectorPerson? ReassignTo
    {
        get => _reassignTo;
        set { _reassignTo = value; Changed(); Changed(nameof(ReassignPreview)); Changed(nameof(CanReassign)); Changed(nameof(ReassignButton)); }
    }
    public bool CanReassign => _reassignTo is not null && _selected is not null
        && !string.Equals(_reassignTo.Mailbox, _selected.Row.OwnerMailbox, StringComparison.OrdinalIgnoreCase);
    public string ReassignButton => _reassignTo is null ? "Choose someone" : $"Give it to {First(_reassignTo.Name)}";

    public string ReassignPreview
    {
        get
        {
            if (_selected is null || _reassignTo is null) return "";
            if (!CanReassign) return $"{_reassignTo.Name} already owns it.";
            var who = First(_reassignTo.Name);
            var theirs = _selected.Row.Actions.Count(a => a.Owner.Equals("owner", StringComparison.OrdinalIgnoreCase));
            var ians = _selected.Row.Actions.Count(a => a.Owner.Equals("Ian", StringComparison.OrdinalIgnoreCase));
            var text = $"{who} gets an email now";
            if (_selected.Row.Recommendation is not null) text += " with Claude's verdict";
            text += theirs > 0 ? $" and {theirs} action{(theirs == 1 ? "" : "s")}, which move to their My day." : ", and a \"take it over\" item on their My day.";
            if (ians > 0) text += $" Your {ians} stay with you.";
            return text;
        }
    }

    /// <summary>Fills every screen from one load. Called on window load and Refresh only.</summary>
    public void Load(IReadOnlyList<TenderBoardRow> board, IReadOnlyList<BdAsk> myAsks, IReadOnlyList<BdDirectorPersonRow> people,
        IReadOnlyList<DirectorPerson> roster, string actor, string? actorName, DateTimeOffset now)
    {
        _board = board;
        _actor = actor;
        _now = now;
        var local = now.ToLocalTime();
        var first = First(actorName ?? actor.Split('@')[0]);
        Greeting = $"{(local.Hour < 12 ? "Good morning" : local.Hour < 17 ? "Good afternoon" : "Good evening")}, {first}";

        Decisions.Clear();
        foreach (var row in board.Where(r => r.Stage == 1 && r.Recommendation is not null)
                     .OrderBy(r => r.Actions.Where(a => IsMine(a, r)).Select(a => (DateOnly?)a.Due).Min() ?? DateOnly.MaxValue)
                     .ThenBy(r => r.DeadlineUtc ?? DateTimeOffset.MaxValue))
            Decisions.Add(new DirectorCard(row, actor, now));

        Week.Clear();
        var horizon = now.AddDays(21);
        var items = new List<DirectorWeekItem>();
        foreach (var r in board.Where(r => r.Stage == 1))
        {
            var name = ShortBuyer(r.Buyer) is { Length: > 0 } b ? b : Short(r.Project);
            if (r.DeadlineUtc is { } d && d >= now && d <= horizon)
                items.Add(new DirectorWeekItem(d, $"{name} closes", r.Recommendation is null ? Owner(r) : $"{Verdict(r.Recommendation)} · {Owner(r)}", DeadlineTag(d, now), d.ToLocalTime().TimeOfDay.TotalMinutes > 0));
            // The actor's own actions are on "Your list"; here are the dates and what others owe.
            foreach (var a in r.Actions.Where(a => !IsMine(a, r)))
            {
                var due = new DateTimeOffset(a.Due.ToDateTime(new TimeOnly(17, 0)), TimeSpan.FromHours(-6));
                if (due <= now.AddDays(10) && due >= now.AddDays(-3))
                    items.Add(new DirectorWeekItem(due, $"{name}: {a.Action}", WhoFor(a, r), DeadlineTag(due, now), false));
            }
        }
        foreach (var w in items.OrderBy(i => i.When).Take(8)) Week.Add(w);

        MyList.Clear();
        foreach (var a in myAsks.OrderBy(a => a.DueUtc).Take(8))
        {
            var pursuit = board.FirstOrDefault(r => r.EngagementId == a.EngagementId);
            MyList.Add(new DirectorListItem(a.Id, a.Ask, $"by {a.DueUtc.ToLocalTime():ddd d MMM}" + (pursuit is null ? "" : $" · {(ShortBuyer(pursuit.Buyer) is { Length: > 0 } b ? b : Short(pursuit.Project))}"),
                a.DueUtc < now ? Palette.Red : Palette.Muted));
        }

        People.Clear();
        foreach (var p in roster.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) People.Add(p);

        Team.Clear();
        var rank = 0;
        foreach (var p in people.Where(p => p.Live > 0).OrderByDescending(p => p.NoNextAction + p.OverdueNextAction).ThenByDescending(p => p.Live))
        {
            var card = new DirectorTeamCard(p, board) { IsSelf = p.OwnerKeys.Any(k => k.Equals(actor, StringComparison.OrdinalIgnoreCase)) };
            // Only the three with the most to clear get the highlighted button; colour everywhere is colour nowhere.
            card.IsTopNeed = card.NeedsHelp && !card.IsSelf && rank++ < 3;
            Team.Add(card);
        }
        var live = people.Sum(p => p.Live);
        var undated = people.Sum(p => p.NoNextAction);
        var overdue = people.Sum(p => p.OverdueNextAction);
        Nudge = Team.Where(t => !t.Person.OwnerKeys.Any(k => k.Equals(actor, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(t => t.Person.NoNextAction >= 5);

        var decisions = Decisions.Count(d => d.NeedsIan);
        var closing = board.Count(r => r.Stage == 1 && r.DeadlineUtc is { } d && d >= now && d <= now.AddDays(7));
        Subtitle = $"{local:dddd d MMMM}" + (decisions > 0 ? $" · {decisions} decision{(decisions == 1 ? "" : "s")} {(decisions == 1 ? "is" : "are")} yours" : " · nothing waiting on your call")
            + (closing > 0 ? $" · {closing} closing this week" : "");
        TeamSubtitle = live == 0 ? "Who is carrying what, and where a hand would help."
            : $"{live} live pursuits · {undated} without a next step · {overdue} overdue. The biggest gap is next steps that were never written down.";
        FillBoard();
        foreach (var name in new[] { nameof(Greeting), nameof(Subtitle), nameof(TeamSubtitle), nameof(Nudge), nameof(HasNudge),
                     nameof(HasDecisions), nameof(HasWeek), nameof(HasMyList), nameof(DecisionCount), nameof(WeekEmpty), nameof(ListEmpty) })
            Changed(name);
    }

    private void FillBoard()
    {
        var keep = _selected?.Row.EngagementId;
        Deciding.Clear();
        Submitted.Clear();
        foreach (var r in _board.Where(r => !_mineOnly || string.Equals(r.OwnerMailbox, _actor, StringComparison.OrdinalIgnoreCase)))
            (r.Stage == 3 ? Submitted : Deciding).Add(new DirectorCard(r, _actor, _now));
        var waiting = _board.Count(r => r.Stage == 1 && r.Recommendation is "decide" or "go" or "no-go");
        PursuitsSubtitle = $"{_board.Count(r => r.Stage == 1)} open tender{(_board.Count(r => r.Stage == 1) == 1 ? "" : "s")} · {_board.Count(r => r.Stage == 3)} submitted"
            + (waiting > 0 ? $" · {waiting} with Claude's read" : "");
        Changed(nameof(PursuitsSubtitle)); Changed(nameof(DecidingHeader)); Changed(nameof(SubmittedHeader));
        Selected = keep is { } id ? Deciding.Concat(Submitted).FirstOrDefault(c => c.Row.EngagementId == id) : null;
    }

    private bool IsMine(RfpAction a, TenderBoardRow r) =>
        PursuitRfpReview.ResolveOwner(a.Owner, r.OwnerMailbox).Equals(_actor, StringComparison.OrdinalIgnoreCase);

    private string WhoFor(RfpAction a, TenderBoardRow r)
    {
        var mailbox = PursuitRfpReview.ResolveOwner(a.Owner, r.OwnerMailbox);
        return mailbox.Equals(_actor, StringComparison.OrdinalIgnoreCase) ? "You"
            : string.Equals(mailbox, r.OwnerMailbox, StringComparison.OrdinalIgnoreCase) ? Owner(r) : mailbox.Split('@')[0];
    }

    private string Owner(TenderBoardRow r) =>
        string.Equals(r.OwnerMailbox, _actor, StringComparison.OrdinalIgnoreCase) ? "You" : First(r.OwnerName ?? r.OwnerMailbox?.Split('@')[0] ?? "Unowned");

    internal static string Verdict(string? recommendation) => recommendation switch
    {
        "go" => "Bid",
        "no-go" => "Don't bid",
        "decide" => "Your call",
        _ => "Not read yet",
    };

    internal static (string Text, ChipKind Kind) DeadlineTag(DateTimeOffset due, DateTimeOffset now)
    {
        var days = (due.ToLocalTime().Date - now.ToLocalTime().Date).Days;
        return days < 0 ? ("overdue", ChipKind.Red)
            : days == 0 ? ("today", ChipKind.Red)
            : days == 1 ? ("tomorrow", ChipKind.Amber)
            : days <= 3 ? ($"in {days} days", ChipKind.Amber)
            : ($"in {days} days", ChipKind.Grey);
    }

    /// <summary>A buyer as people say it: "Town of Peace River" is Peace River, "City of Leduc" is Leduc.</summary>
    internal static string ShortBuyer(string? buyer)
    {
        if (string.IsNullOrWhiteSpace(buyer)) return "";
        var b = Regex.Replace(buyer.Trim(), @"\s*\((APC|BC Bid)\)\s*$", "", RegexOptions.IgnoreCase);
        return Regex.Replace(b, @"^(The\s+)?(Town|City|County|Village|District|Municipal District|Municipality|Regional District|Government)\s+of\s+(the\s+)?", "",
            RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>A project without the procurement boilerplate ("Request for Proposals for …", "RFP # …"), at most 80 characters.</summary>
    internal static string ShortProject(string project)
    {
        var t = Regex.Replace(project, @"\s+", " ").Trim();
        t = Regex.Replace(t, @"^(Request\s+for\s+(Supplier\s+)?(Proposals?|Qualifications?|Quotations?|Expressions?\s+of\s+Interest)|RFP|RFQ|RFSQ|RFEOI)\b\s*(#\s*[\w-]+)?\s*(for|[-:–—])?\s*", "",
            RegexOptions.IgnoreCase);
        if (t.Length == 0) t = project.Trim();
        t = char.ToUpperInvariant(t[0]) + t[1..];
        if (t.Length <= 80) return t;
        var cut = t.LastIndexOf(' ', 78);
        return t[..(cut > 40 ? cut : 78)].TrimEnd(' ', '-', '–', ',') + "…";
    }

    /// <summary>A project name short enough for a card: the part before a long dash or the first 48 characters.</summary>
    internal static string Short(string project)
    {
        var t = Regex.Replace(project, @"\s+", " ").Trim();
        foreach (var cut in new[] { " - ", " – ", " — " })
        {
            var at = t.IndexOf(cut, StringComparison.Ordinal);
            if (at is > 12 and < 60) return t[..at];
        }
        return t.Length <= 48 ? t : t[..46].TrimEnd() + "…";
    }

    internal static string First(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    internal static string Initials(string? name, string? mailbox)
    {
        var parts = (name ?? mailbox?.Split('@')[0] ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
            : parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : "?";
    }
}

public enum ChipKind { Grey, Amber, Red, Green, Blue }

/// <summary>The KOR palette (Themes/KorTheme.xaml) as frozen brushes, for colours the view model decides.</summary>
public static class Palette
{
    public static readonly Brush Slate = Frozen("#3F5364");
    public static readonly Brush Orange = Frozen("#FF5B35");
    public static readonly Brush Green = Frozen("#166534");
    public static readonly Brush GreenBg = Frozen("#DCFCE7");
    public static readonly Brush Red = Frozen("#B91C1C");
    public static readonly Brush RedBg = Frozen("#FEE2E2");
    public static readonly Brush Amber = Frozen("#A16207");
    public static readonly Brush AmberBg = Frozen("#FEF3C7");
    public static readonly Brush Muted = Frozen("#6B7280");
    public static readonly Brush MutedBg = Frozen("#EEF0F2");
    public static readonly Brush Blue = Frozen("#2F5496");
    public static readonly Brush BlueBg = Frozen("#E6ECF6");
    public static readonly Brush Line = Frozen("#E3E6EA");

    private static readonly Brush[] Avatars = { Frozen("#3F5364"), Frozen("#2F5496"), Frozen("#5E7A8F"), Frozen("#7A6A58"), Frozen("#4B6B88"), Frozen("#6B7280") };

    /// <summary>A person keeps their colour across runs: a stable sum, not string.GetHashCode (randomised per process).</summary>
    public static Brush Avatar(string? key) => Avatars[(key ?? "").ToLowerInvariant().Sum(c => (int)c) % Avatars.Length];

    public static (Brush Fore, Brush Back) Chip(ChipKind kind) => kind switch
    {
        ChipKind.Red => (Red, RedBg),
        ChipKind.Amber => (Amber, AmberBg),
        ChipKind.Green => (Green, GreenBg),
        ChipKind.Blue => (Blue, BlueBg),
        _ => (Muted, MutedBg),
    };

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>One tender pursuit as a card: verdict, the reason that decides it, the next step, the countdown, the owner.</summary>
public sealed class DirectorCard : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public DirectorCard(TenderBoardRow row, string actor, DateTimeOffset now)
    {
        Row = row;
        var mine = string.Equals(row.OwnerMailbox, actor, StringComparison.OrdinalIgnoreCase);
        OwnerLine = $"{(mine ? "You" : BdDirectorHomeViewModel.First(row.OwnerName ?? row.OwnerMailbox?.Split('@')[0] ?? "Unowned"))}"
            + (row.DeadlineUtc is { } d ? $" · closes {d.ToLocalTime():ddd d MMM}" : "");
        // Who it is for leads; what it is follows, without the procurement boilerplate.
        var buyer = BdDirectorHomeViewModel.ShortBuyer(row.Buyer);
        Title = buyer.Length > 0 ? buyer : BdDirectorHomeViewModel.ShortProject(row.Project);
        Buyer = buyer.Length > 0 ? BdDirectorHomeViewModel.ShortProject(row.Project) : row.Buyer ?? "";
        Verdict = row.Stage == 3 ? "SUBMITTED" : BdDirectorHomeViewModel.Verdict(row.Recommendation).ToUpperInvariant();
        VerdictBrush = row.Stage == 3 ? Palette.Blue
            : row.Recommendation switch { "go" => Palette.Green, "no-go" => Palette.Muted, "decide" => Palette.Orange, _ => Palette.Line };
        VerdictFore = row.Stage != 3 && row.Recommendation is null ? Palette.Muted : Brushes.White;
        var reasons = (row.Reasons ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var deciding = Regex.Replace(reasons.FirstOrDefault() ?? "", @"^(decide|go|no-go|bid|don't bid)\s*[:\-–]\s*", "", RegexOptions.IgnoreCase);
        Deciding = deciding.Length > 0 ? char.ToUpperInvariant(deciding[0]) + deciding[1..] : (row.ReviewRunning ? "Claude is reading the RFP now." : "Claude has not read this RFP yet.");
        var next = row.Actions.FirstOrDefault();
        NextStep = next is not null ? $"→  {next.Action}  ·  {next.Due:ddd d MMM}"
            : !string.IsNullOrWhiteSpace(row.NextStep) ? $"→  {BdDirectorHomeViewModel.Short(row.NextStep!)}" + (row.NextStepDueUtc is { } n ? $"  ·  {n.ToLocalTime():ddd d MMM}" : "") : "";
        (string text, ChipKind kind) chip = row.Stage == 3 ? ("", ChipKind.Grey)
            : row.DeadlineUtc is { } due ? BdDirectorHomeViewModel.DeadlineTag(due, now) : ("no deadline on file", ChipKind.Grey);
        Chip = row.Stage == 3 ? (row.DeadlineUtc is { } closed ? $"closed {closed.ToLocalTime():d MMM}" : "awaiting the result")
            : row.DeadlineUtc is null ? chip.text : $"closes {chip.text}";
        (ChipFore, ChipBack) = Palette.Chip(chip.kind);
        Initials = BdDirectorHomeViewModel.Initials(row.OwnerName, row.OwnerMailbox);
        AvatarBrush = Palette.Avatar(row.OwnerMailbox);
        NeedsIan = row.Stage == 1 && row.Recommendation is "decide" or "no-go" or "go";
        PrimaryAction = row.Recommendation switch
        {
            "no-go" => "Archive it",
            _ when row.Questions.Count > 0 => $"Draft {row.Questions.Count} question{(row.Questions.Count == 1 ? "" : "s")}",
            _ => "Open review",
        };
        SecondaryAction = PrimaryAction == "Open review" ? "" : "Open review";
        HasSecondary = SecondaryAction.Length > 0;
        Steps = row.Actions.Take(3).Select((a, i) => new DirectorStep(i + 1, a.Action,
            a.Owner.Equals("owner", StringComparison.OrdinalIgnoreCase) ? "Owner" : a.Owner.Equals("Ian", StringComparison.OrdinalIgnoreCase) ? "Ian" : a.Owner.Split('@')[0],
            a.Due.ToString("ddd d MMM", CultureInfo.InvariantCulture))).ToList();
        More = row.Actions.Count > 3 ? $"+ {row.Actions.Count - 3} more steps" : "";
        Extras = string.Join(" · ", new[]
        {
            row.Questions.Count > 0 ? $"{row.Questions.Count} question{(row.Questions.Count == 1 ? "" : "s")} drafted" : "",
            row.QuestionsDeadline is { } q && row.Questions.Count > 0 ? $"due {q}" : "",
        }.Where(s => s.Length > 0));
    }

    public TenderBoardRow Row { get; }
    public string Title { get; }
    public string Buyer { get; }
    public string OwnerLine { get; }
    public string Verdict { get; }
    public Brush VerdictBrush { get; }
    public Brush VerdictFore { get; }
    public string Deciding { get; }
    public string NextStep { get; }
    public bool HasNextStep => NextStep.Length > 0;
    public string Chip { get; }
    public Brush ChipFore { get; }
    public Brush ChipBack { get; }
    public string Initials { get; }
    public Brush AvatarBrush { get; }
    public bool NeedsIan { get; }
    public string PrimaryAction { get; }
    public string SecondaryAction { get; }
    public bool HasSecondary { get; }
    public IReadOnlyList<DirectorStep> Steps { get; }
    public string More { get; }
    public string Extras { get; }
    public string Number => $"#{Row.EngagementId}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
}

public sealed record DirectorStep(int Number, string Action, string Who, string When);

/// <summary>A dated item on "This week and next".</summary>
public sealed class DirectorWeekItem
{
    public DirectorWeekItem(DateTimeOffset when, string title, string detail, (string Text, ChipKind Kind) tag, bool showTime)
    {
        When = when;
        var local = when.ToLocalTime();
        Day = local.ToString("ddd d", CultureInfo.InvariantCulture);
        Sub = showTime ? local.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant() : "";
        Title = title;
        Detail = detail;
        Tag = tag.Text;
        (TagFore, TagBack) = Palette.Chip(tag.Kind);
    }

    public DateTimeOffset When { get; }
    public string Day { get; }
    public string Sub { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Tag { get; }
    public Brush TagFore { get; }
    public Brush TagBack { get; }
}

/// <summary>One of the actor's open asks on Today, ticked off in place.</summary>
public sealed record DirectorListItem(long Id, string Text, string Sub, Brush SubBrush);

/// <summary>A person in the side panel's owner picker.</summary>
public sealed record DirectorPerson(string Name, string Mailbox, int Live)
{
    public string Initials => BdDirectorHomeViewModel.Initials(Name, Mailbox);
    public Brush AvatarBrush => Palette.Avatar(Mailbox);
    public string LiveText => Live == 0 ? "" : $"{Live} live";
}

/// <summary>A person on Team: what they carry, where a hand would help. No counts of app opens or emails.</summary>
public sealed class DirectorTeamCard
{
    public DirectorTeamCard(BdDirectorPersonRow person, IReadOnlyList<TenderBoardRow> board)
    {
        Person = person;
        Name = person.DisplayName;
        Initials = BdDirectorHomeViewModel.Initials(person.DisplayName, person.PersonKey);
        AvatarBrush = Palette.Avatar(person.OwnerKeys.FirstOrDefault() ?? person.PersonKey);
        var tenders = board.Count(r => r.Stage == 1 && person.OwnerKeys.Any(k => string.Equals(k, r.OwnerMailbox, StringComparison.OrdinalIgnoreCase)));
        Sub = $"{person.Live} pursuit{(person.Live == 1 ? "" : "s")}" + (tenders > 0 ? $" · {tenders} open tender{(tenders == 1 ? "" : "s")}" : "");
        var dated = Math.Max(0, person.Live - person.NoNextAction - person.OverdueNextAction);
        Overdue = person.OverdueNextAction;
        Dated = dated;
        Undated = person.NoNextAction;
        Summary = string.Join(" · ", new[]
        {
            person.OverdueNextAction > 0 ? $"{person.OverdueNextAction} overdue" : "",
            person.NoNextAction > 0 ? $"{person.NoNextAction} without a next step" : "",
            dated > 0 ? $"{dated} on track" : "",
        }.Where(s => s.Length > 0));
        (Help, NeedsHelp) =
            person.Live > 0 && person.NoNextAction == person.Live ? ("None of these pursuits has a next step yet.", true)
            : person.NoNextAction * 2 >= person.Live && person.NoNextAction >= 3 ? ("Most have no next step. A 20-minute pass would clear them.", true)
            : person.OverdueNextAction > 0 ? ($"{person.OverdueNextAction} next step{(person.OverdueNextAction == 1 ? " is" : "s are")} overdue.", true)
            : ("Every pursuit has a dated next step.", false);
        HelpFore = NeedsHelp ? Palette.Amber : Palette.Green;
        HelpBack = NeedsHelp ? Palette.AmberBg : Palette.GreenBg;
    }

    public BdDirectorPersonRow Person { get; }
    public string Name { get; }
    public string Initials { get; }
    public Brush AvatarBrush { get; }
    public string Sub { get; }
    public string Summary { get; }
    public int Overdue { get; }
    public int Dated { get; }
    public int Undated { get; }
    public System.Windows.GridLength OverdueWidth => new(Overdue, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DatedWidth => new(Dated, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength UndatedWidth => new(Undated, System.Windows.GridUnitType.Star);
    public string Help { get; }
    public bool NeedsHelp { get; }
    /// <summary>One of the three with the most to clear: their "Book 20 min" is the filled button.</summary>
    public bool IsTopNeed { get; set; }
    /// <summary>The person looking at the screen: no "Book 20 min" with yourself.</summary>
    public bool IsSelf { get; init; }
    public bool IsOtherNeed => NeedsHelp && !IsTopNeed && !IsSelf;
    public Brush HelpFore { get; }
    public Brush HelpBack { get; }
    public string NudgeText => $"{BdDirectorHomeViewModel.First(Name)} has {Person.NoNextAction} pursuits with no next step.";
}
