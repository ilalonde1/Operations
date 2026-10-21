#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.App.Options;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Core.Services;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Core.Models;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class BdDirectorWindow : Window
{
    private readonly BdDirectorViewModel _vm;
    private readonly IProposalStaffStore _staffStore;
    private readonly BdRhythmViewModel? _rhythm;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _initialized;
    private bool _reassignBusy;
    private bool _ledgerBusy;
    private string _actor = "";

    /// <summary>Version 2: Today, Actions, Meetings and References come first, from the operating rhythm store.</summary>
    public BdDirectorWindow(BdDirectorViewModel vm, IProposalStaffStore staffStore, BdRhythmViewModel rhythm) : this(vm, staffStore)
    {
        _rhythm = rhythm ?? throw new ArgumentNullException(nameof(rhythm));
        TodayTab.DataContext = rhythm;
        PeopleTab.DataContext = rhythm;
        ActionsTab.DataContext = rhythm;
        MeetingsTab.DataContext = rhythm;
        ReferencesTab.DataContext = rhythm;
        RhythmStatus.DataContext = rhythm;
        MeetingDate.SelectedDate = DateTime.Today.AddDays(1);
        PersonMeetingDate.SelectedDate = NextWeekday(DateTime.Today.AddDays(1));
    }

    /// <summary>With the consigliere: Ask Claude on Today, People and Pursuits, and the Doctrine screen.</summary>
    public BdDirectorWindow(BdDirectorViewModel vm, IProposalStaffStore staffStore, BdRhythmViewModel rhythm, BdDoctrineViewModel doctrine)
        : this(vm, staffStore, rhythm)
    {
        _doctrine = doctrine ?? throw new ArgumentNullException(nameof(doctrine));
        DoctrineTab.DataContext = doctrine;
        doctrine.PropertyChanged += DoctrineChanged;
        rhythm.SetDoctrineCards(doctrine.CitationCards);
        Closed += (_, _) => doctrine.PropertyChanged -= DoctrineChanged;
    }

    private void DoctrineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BdDoctrineViewModel.CitationCards) && _doctrine is not null)
            _rhythm?.SetDoctrineCards(_doctrine.CitationCards);
    }

    private readonly BdDoctrineViewModel? _doctrine;

    private static DateTime NextWeekday(DateTime day) =>
        day.DayOfWeek switch { DayOfWeek.Saturday => day.AddDays(2), DayOfWeek.Sunday => day.AddDays(1), _ => day };

    public BdDirectorWindow(BdDirectorViewModel vm, IProposalStaffStore staffStore)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _staffStore = staffStore ?? throw new ArgumentNullException(nameof(staffStore));
        InitializeComponent();
        DataContext = vm;
        Loaded += Window_Loaded;
        Closed += (_, _) => { _lifetime.Cancel(); _vm.Dispose(); _lifetime.Dispose(); };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        // Resolve once for both adoption and reassignment, in the existing board's
        // precedence: signed-in identity, configured override, Windows user.
        _actor = new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn,
                AppServices.Get<UserOptions>().UserUpnOverride, Environment.UserName }
            .First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
        _vm.CurrentActorUpn = _actor;
        _rhythm?.SetMe(_actor);
        try { AppServices.Get<IBdUiOpenStore>().RecordOpen("BdDirector", _actor); }
        catch (Exception ex) { Log.Warning(ex, "BD Director open instrumentation failed."); }
        await ReloadAsync().ConfigureAwait(true);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync().ConfigureAwait(true);

    private async Task ReloadAsync()
    {
        if (_ledgerBusy || _reassignBusy) return;
        try
        {
            var director = _vm.LoadAsync(_lifetime.Token);
            if (_rhythm is not null)
            {
                await _rhythm.LoadAsync(_lifetime.Token).ConfigureAwait(true);
                await LoadCoveredTouchstonesAsync().ConfigureAwait(true);
            }
            if (_doctrine is not null) await _doctrine.LoadAsync(_lifetime.Token).ConfigureAwait(true);
            await director.ConfigureAwait(true);
            // The plan reads the same stores; started, not awaited, so the other screens never wait on it.
            if (_rhythm is not null) _ = LoadPlanAsync();
        }
        catch (OperationCanceledException) { /* Window closed. */ }
    }

    /// <summary>Selects a view by the id a kor://director/{id} link carries: 1 Today, 2 Actions, 3 Meetings, 4 References.</summary>
    public void ShowView(long view)
    {
        System.Windows.Controls.TabItem? inData = view switch
        {
            Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.DirectorActions => ActionsTab,
            Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.DirectorMeetings => MeetingsTab,
            Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.DirectorReferences => ReferencesTab,
            _ => null,
        };
        if (inData is null) { Tabs.SelectedItem = TodayTab; return; }
        Tabs.SelectedItem = DataTab;
        DataTabs.SelectedItem = inData;
    }

    // ---- The three screens ----

    private async void MoveResult_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null || sender is not FrameworkElement { Tag: string result, DataContext: HourMoveView move }) return;
        if (!AskMoveReason(this, result, move.Rank, out var note)) return;
        await RhythmActionAsync(ct => _rhythm.RecordMoveAsync(move, result, _actor, ct, note)).ConfigureAwait(true);
        if (_doctrine is not null) await _doctrine.LoadAsync(_lifetime.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Not today and Not doing ask why, in one optional line: the reason reaches the cited cards' records and the weekly
    /// synthesis, where Ian's stated practice outranks the books. Done asks nothing. False when Ian cancels.
    /// </summary>
    internal static bool AskMoveReason(Window owner, string result, int rank, out string? note)
    {
        note = null;
        if (result is not ("NotToday" or "NotDoing")) return true;
        var dialog = new NoteDialog(result == "NotDoing" ? "Not doing" : "Not today",
            $"Move {rank}: why? One line is enough. It teaches the brief what you skip and why (optional).", 500, singleLine: true)
            { Owner = owner };
        if (dialog.ShowDialog() != true) return false;
        note = dialog.Note;
        return true;
    }

    /// <summary>
    /// A move answered from the 6 a.m. email (kor://move/{hour}/{rank}/{result}): ask why when it is skipped, record it as
    /// the signed-in user, then show Today with the answer in place.
    /// </summary>
    public async Task RecordMoveFromLinkAsync(long hourId, int rank, string result)
    {
        if (!AskMoveReason(this, result, rank, out var note)) return;
        var actor = _actor.Length > 0 ? _actor : new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn,
                AppServices.Get<UserOptions>().UserUpnOverride, Environment.UserName }
            .First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
        try
        {
            await AppServices.Get<IBdRhythmStore>().RecordMoveResultAsync(hourId, rank, result,
                string.IsNullOrWhiteSpace(note) ? null : note.Trim(), actor, _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Recording move {Rank} of hour {HourId} from a link failed.", rank, hourId);
            MessageBox.Show(this, "That answer could not be recorded. Open Today and mark it there.", "BD Director",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowView(Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.DirectorToday);
        if (_initialized) await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>A ticked action on Today or People closes as Done, with a one-line result; cancelling leaves it open.</summary>
    private async void ActionCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null || sender is not System.Windows.Controls.CheckBox { Tag: BdActionView action } box) return;
        if (!action.Row.IsOpen) { box.IsChecked = true; return; }
        var dialog = new NoteDialog("Mark done", "What came of it?\n\n" + action.Row.Action, 500) { Owner = this };
        if (dialog.ShowDialog() != true) { box.IsChecked = false; return; }
        await RhythmActionAsync(ct => _rhythm.CloseActionAsync(action, "Done", dialog.Note, _actor, ct)).ConfigureAwait(true);
    }

    private async void AddActionForPerson_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null) return;
        var person = _rhythm.SelectedPerson?.Key;
        await RhythmActionAsync(async ct =>
        {
            await ShowAskAsync(null, null, person, ct).ConfigureAwait(true);
            await _rhythm.LoadActionsAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void PlanPersonOneToOne_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedPerson is not { IsLead: true } person) return;
        if (PersonMeetingDate.SelectedDate is not { } date)
        {
            MessageBox.Show(this, "Choose the date of the one-to-one.", "Plan it", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RhythmActionAsync(async ct =>
        {
            await _rhythm.PlanOneToOneAsync(person.Key, person.Name, AtNine(date), _actor, ct).ConfigureAwait(true);
            SetRhythmStatus($"Planned the one-to-one with {person.Name} for {date:ddd d MMM}. Nothing was sent; draft the invite next.");
        }).ConfigureAwait(true);
    }

    private void CopyPersonAgenda_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is { PersonAgenda.Length: > 0 } r && TrySetClipboard(r.PersonAgenda)) SetRhythmStatus("Copied the agenda.");
    }

    /// <summary>The booked one-to-one opens as its Outlook invite; with none booked, the agenda opens as a message to the lead.</summary>
    private async void DraftPersonInvite_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedPerson is not { IsLead: true } person) return;
        var booked = _rhythm.Meetings.Select(m => m.Row).FirstOrDefault(m => m.Kind == "OneToOne" && m.Status == "Planned"
            && string.Equals(m.WithStaffKey, person.Key, StringComparison.OrdinalIgnoreCase) && m.ScheduledForUtc == person.NextMeetingUtc);
        await OpenOutlookDraftAsync(booked is null
            ? new HourDraft(person.Key, $"One-to-one: {person.Name}", _rhythm.PersonAgenda)
            : InviteFor(booked, _rhythm.PersonAgenda)).ConfigureAwait(true);
    }

    /// <summary>A stored meeting as an unsent invite: its people, time, length and agenda.</summary>
    private HourDraft InviteFor(BdMeetingRow meeting, string body) =>
        HourDraft.Invite(string.Join(";", MeetingInvite.Recipients(meeting, _rhythm?.PeopleCards.Select(p => p.Lead).ToList() ?? new List<BdLeadRow>())),
            meeting.Title, body, meeting.ScheduledForUtc, meeting.DurationMinutes);

    // ---- The consigliere: questions opened in Claude Code on this PC, and the doctrine ----

    private async void AskClaudeToday_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null) return;
        var (subject, context, facts) = _rhythm.TodayAskContext();
        await OpenQuestionInClaudeAsync(AskTodayBox, subject, context, facts).ConfigureAwait(true);
    }

    private async void AskClaudePerson_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.PersonAskContext() is not { } question) return;
        await OpenQuestionInClaudeAsync(AskPersonBox, question.Subject, question.Context, question.Facts).ConfigureAwait(true);
    }

    private async void AskClaudePursuit_Click(object sender, RoutedEventArgs e)
    {
        if (PursuitLinesList.SelectedItem is not BdPursuitLine line)
        {
            MessageBox.Show(this, "Pick a pursuit in the list first.", "Ask Claude", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (subject, context, facts) = BdPursuitAsk.For(line);
        await OpenQuestionInClaudeAsync(AskPursuitBox, subject, context, facts).ConfigureAwait(true);
    }

    /// <summary>
    /// Composes the question now, from live data and the doctrine that applies, saves it and opens Claude Code on this PC on it —
    /// the NetworkOps Prompt Library launcher. No model is called from the app; Claude reports back through the BD CLI.
    /// </summary>
    private async Task OpenQuestionInClaudeAsync(System.Windows.Controls.TextBox box, string subject, DoctrineContext context, IReadOnlyList<string> facts, string role = "BD")
    {
        if (_doctrine is null) return;
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            MessageBox.Show(this, "Type your question first.", "Ask Claude", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RhythmActionAsync(async ct =>
        {
            var prompt = await _doctrine.PrepareAskAsync(box.Text, subject, context, facts, _actor, ct, role).ConfigureAwait(true);
            Kor.Operations.App.NetworkOps.PromptLauncher.OpenInClaude(Kor.Operations.App.NetworkOps.PromptLauncher.Save(prompt.FileName, prompt.Markdown));
            box.Clear();
            SetRhythmStatus(_doctrine.Status);
        }).ConfigureAwait(true);
    }

    private async void Synthesize_Click(object sender, RoutedEventArgs e)
    {
        if (_doctrine is null) return;
        var books = _vm.KnowledgeBooks.Select(b => $"#{b.Source.Id} {b.Source.Title}{(b.Source.Author is { } a ? " (" + a + ")" : "")}: {b.Source.Claims} claims").ToList();
        await RhythmActionAsync(async ct =>
        {
            var prompt = await _doctrine.PrepareSynthesisAsync(books, SynthesisFocusBox.Text, _actor, ct).ConfigureAwait(true);
            Kor.Operations.App.NetworkOps.PromptLauncher.OpenInClaude(Kor.Operations.App.NetworkOps.PromptLauncher.Save(prompt.FileName, prompt.Markdown));
        }).ConfigureAwait(true);
    }

    private async void AcceptCard_Click(object sender, RoutedEventArgs e) => await DecideCardAsync(sender, true).ConfigureAwait(true);
    private async void RejectCard_Click(object sender, RoutedEventArgs e) => await DecideCardAsync(sender, false).ConfigureAwait(true);

    private async void DoctrineRole_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_doctrine is null || sender is not System.Windows.Controls.ComboBox { SelectedItem: string role } || role == _doctrine.Role) return;
        await RhythmActionAsync(ct => _doctrine.SelectRoleAsync(role, ct)).ConfigureAwait(true);
    }

    private async void OpenDoctrineCard_Click(object sender, RoutedEventArgs e)
    {
        if (_doctrine is null || sender is not FrameworkElement { Tag: long id }) return;
        if (_doctrine.CitationCards.FirstOrDefault(c => c.Id == id) is { } card && card.Role != _doctrine.Role)
            await RhythmActionAsync(ct => _doctrine.SelectRoleAsync(card.Role, ct)).ConfigureAwait(true);
        _doctrine.FocusCard(id);
        Tabs.SelectedItem = DoctrineTab;
        DoctrineTab.UpdateLayout();
        FocusedDoctrineCard.BringIntoView();
    }

    private async void RetireCard_Click(object sender, RoutedEventArgs e)
    {
        if (_doctrine is null || sender is not FrameworkElement { Tag: DoctrineCardView card }) return;
        if (MessageBox.Show(this, $"Retire “{card.Title}”? It will stop informing future suggestions. Its record stays available.",
            "Retire doctrine card", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RhythmActionAsync(ct => _doctrine.RetireAsync(card, ct)).ConfigureAwait(true);
    }

    private async void AmendCard_Click(object sender, RoutedEventArgs e)
    {
        if (_doctrine is null || sender is not FrameworkElement { Tag: DoctrineCardView card }) return;
        var dialog = new NoteDialog("Ask Claude to amend", "Review the question, then open it in Claude.", 2000,
            required: true, initialNote: BdDoctrineViewModel.AmendmentQuestion(card)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var question = new System.Windows.Controls.TextBox { Text = dialog.Note ?? "" };
        await OpenQuestionInClaudeAsync(question, $"doctrine card #{card.Id}: {card.Title}",
            new DoctrineContext("ask", Array.Empty<string>()), BdDoctrineViewModel.AmendmentFacts(card), card.Card.Role).ConfigureAwait(true);
    }

    private async void AcceptAllCards_Click(object sender, RoutedEventArgs e)
    {
        if (_doctrine is null || _doctrine.Proposed.Count == 0) return;
        var answer = MessageBox.Show(this, $"Accept all {_doctrine.Proposed.Count} card(s) waiting for you? Reject the ones you disagree with first.",
            "Accept all waiting", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        await RhythmActionAsync(ct => _doctrine.AcceptAllAsync(_actor, ct)).ConfigureAwait(true);
    }

    private async Task DecideCardAsync(object sender, bool accept)
    {
        if (_doctrine is null || sender is not FrameworkElement { Tag: DoctrineCardRow card }) return;
        await RhythmActionAsync(ct => _doctrine.DecideAsync(card, accept, _actor, ct)).ConfigureAwait(true);
    }

    private async void PursuitLine_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || System.Windows.Controls.ItemsControl.ContainerFromElement(PursuitLinesList, source) is not System.Windows.Controls.ListBoxItem { DataContext: BdPursuitLine line }) return;
        e.Handled = true;
        await OpenUrlAsync(BdDirectorRowLinks.For(line)).ConfigureAwait(true);
    }

    private async void OpenPursuitLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BdPursuitLine line }) await OpenUrlAsync(BdDirectorRowLinks.For(line)).ConfigureAwait(true);
    }

    private async Task OpenUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { await KorDeepLink.OpenAsync(url).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open {Url}.", url);
            MessageBox.Show(this, "Could not open that screen. Please try again.", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- Today ----

    private async void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        try { await KorDeepLink.OpenAsync(url).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open {Url}.", url);
            MessageBox.Show(this, "Could not open that screen. Please try again.", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>A double-clicked row opens the screen for what it is about (<see cref="BdDirectorRowLinks"/>).</summary>
    private async void Row_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid || e.OriginalSource is not DependencyObject source) return;
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(grid, source) is not System.Windows.Controls.DataGridRow row) return;
        if (BdDirectorRowLinks.For(row.Item) is not { } url) return;
        e.Handled = true;
        try { await KorDeepLink.OpenAsync(url).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open {Url}.", url);
            MessageBox.Show(this, "Could not open that screen. Please try again.", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HourDraft draft }) return;
        if (TrySetClipboard($"To: {draft.To}\r\nSubject: {draft.Subject}\r\n\r\n{draft.Body}") && _rhythm is not null)
            SetRhythmStatus("Copied the draft to the clipboard.");
    }

    private async void DraftInOutlook_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HourDraft draft }) await OpenOutlookDraftAsync(draft).ConfigureAwait(true);
    }

    /// <summary>A draft opened from the 6 a.m. email (kor://draft/{hour}/{rank}/{n}).</summary>
    public async Task OpenDraftFromLinkAsync(long hourId, int rank, int number)
    {
        HourDraft? draft = null;
        try
        {
            var row = await AppServices.Get<IBdRhythmStore>().GetHourByIdAsync(hourId, _lifetime.Token).ConfigureAwait(true);
            var content = row is null ? null : DirectorHourFile.Parse(row.ContentJson).Content;
            var move = content is null ? null : DirectorHourFile.AllMoves(content).FirstOrDefault(m => m.Rank == rank);
            draft = move is not null && number <= move.Drafts.Count ? move.Drafts[number - 1] : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Reading draft {Number} of move {Rank}, hour {HourId}, failed.", number, rank, hourId);
        }
        if (draft is null)
        {
            MessageBox.Show(this, "That draft could not be found. Open Today and use the draft there.", "Draft in Outlook",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await OpenOutlookDraftAsync(draft).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the draft in Outlook, unsent: a message, or an invite with its attendees and time. An invite for a recurring
    /// meeting also plans that occurrence (once), so the cadence knows it is booked. Nothing is sent from KOR Operations.
    /// </summary>
    private async Task OpenOutlookDraftAsync(HourDraft draft)
    {
        try
        {
            await Task.Run(() => OutlookDraft.Open(draft), _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Outlook could not open the draft {Subject}.", draft.Subject);
            if (!draft.IsInvite) { OpenMailtoDraft(draft.To, draft.Subject, draft.Body); return; }
            TrySetClipboard($"Invite: {draft.Subject}\r\nTo: {draft.To}\r\nWhen: {draft.When} ({draft.Minutes} min)\r\n\r\n{draft.Body}");
            MessageBox.Show(this, "Outlook could not open the invite, so it is on your clipboard.", "Invite in Outlook",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RecordTouchstonesAsync(draft).ConfigureAwait(true);
        if (draft is not { IsInvite: true, CadenceId: { } cadence, Start: { } start }) return;
        try
        {
            var store = AppServices.Get<IBdRhythmStore>();
            var meetings = await store.ListMeetingsAsync(_lifetime.Token).ConfigureAwait(true);
            if (meetings.Any(m => m.CadenceId == cadence && m.Status == "Planned" && m.ScheduledForUtc == start)) return;
            await store.PlanCadenceMeetingAsync(cadence, start, draft.Body, CurrentActor(), _lifetime.Token).ConfigureAwait(true);
            if (_rhythm is not null) await _rhythm.LoadMeetingsAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Planning cadence {Cadence} at {Start} failed.", cadence, start);
        }
    }

    /// <summary>An owner's roll-up, once opened, records the touchstones it covers so the next roll-up moves on.</summary>
    private async Task RecordTouchstonesAsync(HourDraft draft)
    {
        var keys = (draft.Touchstones ?? Array.Empty<string>())
            .Select(text => TouchstoneKey.TryParse(text, out var key) ? key : null).OfType<TouchstoneKey>().ToList();
        if (keys.Count == 0) return;
        try
        {
            await AppServices.Get<IPursuitTouchstoneStore>().RecordDraftedAsync(keys, CurrentActor(), _lifetime.Token).ConfigureAwait(true);
            await LoadCoveredTouchstonesAsync().ConfigureAwait(true);
            if (_rhythm is not null) SetRhythmStatus($"Roll-up to {draft.To} opened in Outlook; its {draft.RollupPursuits} pursuits are covered for this week.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Recording {Count} touchstones for {Subject} failed.", keys.Count, draft.Subject);
            if (_rhythm is not null) SetRhythmStatus("The roll-up opened, but its pursuits could not be marked covered; next week's roll-up may repeat them.");
        }
    }

    /// <summary>Shows which of today's roll-ups have been opened. A failure only leaves them unmarked.</summary>
    private async Task LoadCoveredTouchstonesAsync()
    {
        if (_rhythm is null || !_rhythm.Moves.Any(m => m.Drafts.Any(d => d.IsRollup))) return;
        try
        {
            var drafted = await AppServices.Get<IPursuitTouchstoneStore>().ListDraftsAsync(_lifetime.Token).ConfigureAwait(true);
            _rhythm.SetCoveredTouchstones(drafted.Select(d => d.Key.ToString()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Reading the recorded pursuit touchstones failed.");
        }
    }

    private string CurrentActor() => _actor.Length > 0 ? _actor : new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn,
            AppServices.Get<UserOptions>().UserUpnOverride, Environment.UserName }
        .First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    /// <summary>
    /// The fallback when Outlook cannot be reached: a new message in the default mail program for Ian to review and send.
    /// A body too long for a mailto address goes to the clipboard instead, with the subject and recipients prefilled.
    /// </summary>
    private void OpenMailtoDraft(string to, string subject, string body)
    {
        var recipients = string.Join(";", to.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var full = "mailto:" + recipients + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body.Replace("\n", "\r\n"));
        var bodyCopied = false;
        if (full.Length > 1900)
        {
            full = "mailto:" + recipients + "?subject=" + Uri.EscapeDataString(subject);
            bodyCopied = TrySetClipboard(body);
        }

        try
        {
            Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
            if (bodyCopied)
                MessageBox.Show(this, "The message is long, so its text is on your clipboard. Paste it into the new message.", "Draft in Outlook",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open a mail draft.");
            TrySetClipboard($"To: {to}\r\nSubject: {subject}\r\n\r\n{body}");
            MessageBox.Show(this, "Outlook could not be opened, so the draft is on your clipboard.", "Draft in Outlook", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not use the clipboard.");
            MessageBox.Show(this, "The clipboard is busy. Try again.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    private void SetRhythmStatus(string text) => _rhythm?.Say(text);

    // ---- Actions ----

    private async void ActionDone_Click(object sender, RoutedEventArgs e) => await CloseActionAsync("Done", "What came of it?").ConfigureAwait(true);
    private async void ActionDeclined_Click(object sender, RoutedEventArgs e) => await CloseActionAsync("Declined", "Why was it declined?").ConfigureAwait(true);
    private async void ActionWithdraw_Click(object sender, RoutedEventArgs e) => await CloseActionAsync("Withdrawn", "Why is it no longer needed?").ConfigureAwait(true);

    private async Task CloseActionAsync(string status, string prompt)
    {
        if (_rhythm?.SelectedAction is not { } action || !action.Row.IsOpen) return;
        var dialog = new NoteDialog("Close the action", prompt + "\n\n" + action.Row.Action, 500) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RhythmActionAsync(ct => _rhythm.CloseSelectedActionAsync(status, dialog.Note, _actor, ct)).ConfigureAwait(true);
    }

    private async void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null) return;
        await RhythmActionAsync(async ct =>
        {
            await ShowAskAsync(null, null, null, ct).ConfigureAwait(true);
            await _rhythm.LoadActionsAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    // ---- Meetings ----

    private async void PlanOneToOne_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null) return;
        if (LeadPicker.SelectedItem is not BdLeadOption lead || MeetingDate.SelectedDate is not { } date)
        {
            MessageBox.Show(this, "Choose who the one-to-one is with, and the date.", "Plan one-to-one", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RhythmActionAsync(ct => _rhythm.PlanOneToOneAsync(lead.Key, lead.Name, AtNine(date), _actor, ct)).ConfigureAwait(true);
    }

    private async void PlanAllHands_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm is null) return;
        if (MeetingDate.SelectedDate is not { } date)
        {
            MessageBox.Show(this, "Choose the date of the all-hands.", "Plan all-hands", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RhythmActionAsync(ct => _rhythm.PlanAllHandsAsync(AtNine(date), _actor, ct)).ConfigureAwait(true);
    }

    private static DateTimeOffset AtNine(DateTime date) => new DateTimeOffset(date.Date.AddHours(9), TimeZoneInfo.Local.GetUtcOffset(date.Date.AddHours(9)));

    private void CopyMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedMeeting is { } m && TrySetClipboard(m.Text)) SetRhythmStatus("Copied: " + m.Row.Title);
    }

    /// <summary>A planned meeting opens as its Outlook invite; a held one opens its recap as a message to the same people.</summary>
    private async void DraftMeetingInvite_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedMeeting is not { } m) return;
        var invite = InviteFor(m.Row, m.Text);
        await OpenOutlookDraftAsync(m.Row.Status == "Planned" ? invite : invite with { When = null, Minutes = null }).ConfigureAwait(true);
    }

    private async void MeetingHeld_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedMeeting is not { } m) return;
        var dialog = new NoteDialog("Record the meeting", "What was decided, and what each person will do next?", 4000) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RhythmActionAsync(ct => _rhythm.RecordSelectedMeetingAsync("Held", dialog.Note, ct)).ConfigureAwait(true);
    }

    private async void MeetingCancelled_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedMeeting is not { Row.Status: "Planned" }) return;
        await RhythmActionAsync(ct => _rhythm.RecordSelectedMeetingAsync("Cancelled", null, ct)).ConfigureAwait(true);
    }

    // ---- References ----

    private async void RecordReferenceCall_Click(object sender, RoutedEventArgs e)
    {
        if (_rhythm?.SelectedReference is not { } r) return;
        var dialog = new ReferenceCallDialog(r.Row) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RhythmActionAsync(ct => _rhythm.RecordSelectedReferenceCallAsync(dialog.Outcome, dialog.ContactName, dialog.Notes, _actor, ct)).ConfigureAwait(true);
    }

    private async Task RhythmActionAsync(Func<CancellationToken, Task> action)
    {
        try { await action(_lifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "BD Director", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director rhythm action failed.");
            MessageBox.Show(this, "That did not save. Please try again.", "BD Director", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Reassign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorPursuitRow row })
            await ReassignPursuitAsync(row).ConfigureAwait(true);
    }

    private async Task ReassignPursuitAsync(BdDirectorPursuitRow row)
    {
        if (_ledgerBusy || _reassignBusy || _vm.IsLoading) return;
        _reassignBusy = true;
        var ct = _lifetime.Token;
        try
        {
            var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            var dialog = new ReassignDialog(row.ProjectName, row.Pursuit.OwnerStaffId, people) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var outcome = await _vm.ReassignAsync(row, dialog.TargetOwner, _actor, dialog.Reason, ct).ConfigureAwait(true);
            if (outcome is not ReassignOutcome.Reassigned and not ReassignOutcome.Ignored)
                MessageBox.Show(this, _vm.StatusMessage, "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
            if (outcome == ReassignOutcome.Reassigned)
            {
                // Ian, 21 Oct 2026: the new owner is told automatically (email + a My day item), as from All Pursuits.
                var p = row.Pursuit;
                var warning = await Kor.Operations.App.Crm.PursuitAssignmentNotice.SendAsync(
                    new Kor.Operations.App.Crm.PursuitAssignment(p.EngagementId, null, row.ProjectName, p.Buyer, null,
                        dialog.TargetOwner, dialog.TargetDisplay, _actor, dialog.Reason, p.OwnerStaffId),
                    AppServices.Get<Kor.Operations.Graph.IGraphFacade>(), AppServices.Get<IBdDirectorLedgerStore>(),
                    AppServices.GetOptional<SqlPursuitReviewStore>(), ct).ConfigureAwait(true);
                if (warning is not null)
                    MessageBox.Show(this, "The pursuit was reassigned. " + warning, "Reassigned", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director reassignment failed.");
            MessageBox.Show(this, "Could not reassign the pursuit. Refresh and try again.", "Reassign failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _reassignBusy = false; }
    }

    private async void AssignBid_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } bid) return;
        if (_vm.PursuitForBid(bid) is { } row)
            await ReassignPursuitAsync(row).ConfigureAwait(true);
        else
            MessageBox.Show(this, "Could not find the owned pursuit. Refresh and try again.", "Assign",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void MarkSubmitted_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { CanSubmit: true } row) return;
        await LedgerActionAsync(ct => SubmitBidAsync(row, dialog => dialog.ShowDialog(), _actor, ct), showFailureDetail: true).ConfigureAwait(true);
    }

    // The same dialog-to-store path is exercised by the modal interaction gate.
    internal async Task SubmitBidAsync(BdDirectorPendingBidRow row, Func<SubmittedDialog, bool?> show,
        string actor, CancellationToken ct)
    {
        var dialog = new SubmittedDialog(row.Bid.SubmittedFileName, row.Bid.FolderLink, row.Bid.SubmittedFileId);
        if (IsVisible) dialog.Owner = this;
        if (show(dialog) == true)
            await _vm.MarkSubmittedAsync(row, dialog.Result, actor, ct).ConfigureAwait(true);
    }

    private void BuildFeeProposal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } row) return;
        AppServices.Get<FeeProposalLauncher>().Launch(row.Bid.OpportunityId, row.Bid.EngagementId,
            row.Bid.RfpNumber, row.Bid.Project, _actor, this, ReloadAsync,
            message => MessageBox.Show(this, message, "Fee proposal", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    private void Checklist_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } row) return;
        var checklist = new PursuitChecklistViewModel(AppServices.Get<IPursuitRequirementStore>(),
            AppServices.Get<IPursuitFileStore>(), row.Bid.EngagementId, row.Bid.RfpNumber, _actor);
        new PursuitChecklistWindow(checklist) { Owner = this }.Show();
    }

    private void OrgChart_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } row) return;
        var chart = new PursuitOrgChartViewModel(AppServices.Get<IPursuitFileStore>(),
            AppServices.Get<Kor.Opportunities.Data.Opportunities.IOpportunityFeeProposalLinkStore>(),
            AppServices.Get<IFeeProposalStore>(), _staffStore, row.Bid.EngagementId, row.Bid.OpportunityId,
            row.Bid.RfpNumber, row.Bid.Project, _actor);
        new PursuitOrgChartWindow(chart) { Owner = this }.Show();
    }

    private void BuildBrochure_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } row) return;
        AppServices.Get<Kor.Operations.App.Brochures.BrochurePursuitLauncher>().Launch(row.Bid.OpportunityId,
            row.Bid.EngagementId, row.Bid.RfpNumber, row.Bid.Project, _actor, this);
    }

    private async void SubmittedFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: BdDirectorPendingBidRow row } || !row.HasSubmittedFile) return;
        await LedgerActionAsync(async ct =>
        {
            var file = await _vm.GetSubmittedFileAsync(row, ct).ConfigureAwait(true);
            await AppServices.Get<IPursuitFileOpener>().OpenAsync(file, ct).ConfigureAwait(true);
        }, showFailureDetail: true).ConfigureAwait(true);
    }

    private async void EditOpportunity_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPendingBid is not { } row) return;
        await LedgerActionAsync(async ct =>
        {
            var opportunity = await _vm.GetBidOpportunityAsync(row, ct).ConfigureAwait(true)
                ?? throw new InvalidOperationException("The opportunity could not be found.");
            ct.ThrowIfCancellationRequested();
            var dialog = new OpportunityEntryDialog(opportunity) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is { } result)
                await _vm.SaveBidOpportunityAsync(result, _actor, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void FolderLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: BdDirectorPendingBidRow row } || !row.HasFolderLink) return;
        try { Process.Start(new ProcessStartInfo(row.Bid.FolderLink!) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open the folder link.");
            MessageBox.Show(this, "Could not open the link.", "Folder link", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenPursuit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long pursuitId }) return;
        try
        {
            var window = AppServices.Get<BdWorkspaceWindow>();
            window.Owner = this;
            window.NavigateToPursuit(pursuitId);
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open pursuit {PursuitId}.", pursuitId);
            MessageBox.Show(this, "Could not open the pursuit. Please try again.", "Open pursuit",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SetTarget_Click(object sender, RoutedEventArgs e) => await LedgerActionAsync(async ct =>
    {
        var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var dialog = new TargetDialog(people) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.SetTargetAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }).ConfigureAwait(true);

    private async void RetireTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTarget is not { } row) return;
        await LedgerActionAsync(ct => _vm.RetireTargetAsync(row.Target.Id, _actor, ct)).ConfigureAwait(true);
    }

    private async void Ask_Click(object sender, RoutedEventArgs e)
        => await LedgerActionAsync(ct => ShowAskAsync(null, null, null, ct)).ConfigureAwait(true);

    private async void AskPursuit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BdDirectorPursuitRow row }) return;
        // A legacy owner spelling resolves to the same mailbox used by the roster
        // and by the recipient's dashboard.
        var personKey = _vm.People.FirstOrDefault(p => p.OwnerKeys.Contains(row.Pursuit.OwnerStaffId, StringComparer.OrdinalIgnoreCase))?.PersonKey
            ?? row.Pursuit.OwnerStaffId;
        await LedgerActionAsync(ct => ShowAskAsync(row.PursuitId, null, personKey, ct)).ConfigureAwait(true);
    }

    private async void AskAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        await LedgerActionAsync(ct => ShowAskAsync(null, row.Account.CanonicalOrgId, null, ct)).ConfigureAwait(true);
    }

    private async Task ShowAskAsync(long? pursuitId, long? accountId, string? personKey, CancellationToken ct)
    {
        var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var dialog = new AskDialog(people, pursuitId, accountId, personKey) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.CreateAskAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }

    private async void WithdrawAsk_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAsk is not { IsOpen: true } row) return;
        await LedgerActionAsync(ct => _vm.WithdrawAskAsync(row.Record.Id, _actor, ct)).ConfigureAwait(true);
    }

    private async void Decide_Click(object sender, RoutedEventArgs e)
        => await LedgerActionAsync(ct => ShowDecisionAsync(null, ct)).ConfigureAwait(true);

    private async void DecideAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        await LedgerActionAsync(ct => ShowDecisionAsync(row.Account.CanonicalOrgId, ct)).ConfigureAwait(true);
    }

    private async Task ShowDecisionAsync(long? accountId, CancellationToken ct)
    {
        var dialog = new DecisionDialog(null, accountId) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.RecordDecisionAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }

    private async void ScoreDecision_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDecision is not { IsPending: true } row || sender is not FrameworkElement { Tag: string outcome }) return;
        await LedgerActionAsync(async ct =>
        {
            var dialog = new NoteDialog("Score decision", $"{outcome}: what happened? (optional)") { Owner = this };
            if (dialog.ShowDialog() == true)
                await _vm.ScoreDecisionAsync(row.Record.Id, outcome, dialog.Note, _actor, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void LogMyDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorMyDayRow row, Tag: string kind }
            && Enum.TryParse<CrmActivityType>(kind, out var type))
            await LedgerActionAsync(ct => _vm.LogMyDayAsync(row, type, ct), showFailureDetail: true).ConfigureAwait(true);
    }

    private async void ChooseEngineer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BdEngineerCandidate candidate }) return;
        var row = _vm.MyDay.FirstOrDefault(r => r.Item.Kind == CallSheetKind.Pursuit && r.Item.EngagementId == candidate.EngagementId);
        if (row is not null)
            await LedgerActionAsync(ct => _vm.ChooseEngineerAsync(row, candidate, ct), showFailureDetail: true).ConfigureAwait(true);
    }

    private async void StartTracking_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorMyDayRow row })
            await LedgerActionAsync(ct => _vm.StartTrackingAsync(row, ct), showFailureDetail: true).ConfigureAwait(true);
    }

    private async void JoinSecondSeat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorMyDayRow row })
            await LedgerActionAsync(ct => _vm.JoinSecondSeatAsync(row, ct), showFailureDetail: true).ConfigureAwait(true);
    }

    private void CopyMyDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BdDirectorMyDayRow row }) return;
        try { Clipboard.SetText(row.DraftedNote); }
        catch (Exception ex)
        {
            Log.Warning(ex, "My day note could not be copied.");
            MessageBox.Show(this, "Could not copy the note. Try Copy again.", "BD Director", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LedgerActionAsync(Func<CancellationToken, Task> action, bool showFailureDetail = false)
    {
        if (_ledgerBusy || _reassignBusy || _vm.IsLoading) return;
        _ledgerBusy = true;
        try { await action(_lifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not save a change.");
            MessageBox.Show(this, showFailureDetail ? ex.Message : "Could not save the change. Refresh and try again.", "BD Director", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _ledgerBusy = false; }
    }

    private async void SampleClaims_Click(object sender, RoutedEventArgs e)
        => await _vm.SampleKnowledgeAsync().ConfigureAwait(true);

    private void ClaimsByBook_Click(object sender, RoutedEventArgs e) => _vm.SelectedConcept = null;

    private async void KeepClaim_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClaim is not { CanReview: true } row) return;
        await LedgerActionAsync(ct => _vm.KeepClaimAsync(row, _actor, ct)).ConfigureAwait(true);
    }

    private async void RetireClaim_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClaim is not { CanReview: true } row) return;
        await LedgerActionAsync(async ct =>
        {
            var dialog = new NoteDialog("Retire claim", "Why does this claim not hold for KOR? A reason is required (1–200 characters).", 200, required: true,
                initialNote: row.RetirementReason) { Owner = this };
            if (dialog.ShowDialog() == true)
                await _vm.RetireClaimAsync(row, _actor, dialog.Note!, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async void AcknowledgeBrief_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorBriefRow row })
            await LedgerActionAsync(ct => _vm.AcknowledgeBriefAsync(row, _actor, ct)).ConfigureAwait(true);
    }
    private void OpenCitation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BdDirectorCitationRow row }) _vm.SelectedBriefCitation = row;
    }

    private void OpenDossier_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        try { new OrgDossierWindow(AppServices.Get<OrgDossierViewModel>(), row.Account.CanonicalOrgId) { Owner = this }.Show(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open dossier.");
            MessageBox.Show(this, "Could not open the account. Please try again.", "Open dossier", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
