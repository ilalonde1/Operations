#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// BD Director v3 (21 Oct 2026): the rail and the three new screens. Today, Pursuits and Team read what the window already
/// loads plus one board read (SqlPursuitReviewStore.ListBoardAsync), on window load and Refresh only. Every action goes
/// through an existing path: the stored review, the logged reassignment and its automatic notice, the ask ledger, Outlook.
/// </summary>
public partial class BdDirectorWindow : IDirectorHomeHost
{
    private readonly BdDirectorHomeViewModel _home = new();

    /// <summary>The v3 screens' view model, for the render harness (which never fires Loaded and has no database).</summary>
    internal BdDirectorHomeViewModel Home => _home;

    private void WireHome()
    {
        TodayPanel.DataContext = _home;
        PursuitsPanel.DataContext = _home;
        TeamPanel.DataContext = _home;
    }

    /// <summary>Fills Today, Pursuits and Team after the Director's own load; a failure here leaves the rest of the window working.</summary>
    private async Task LoadHomeAsync(CancellationToken ct)
    {
        try
        {
            var reviews = AppServices.GetOptional<SqlPursuitReviewStore>();
            var board = reviews is null ? Array.Empty<TenderBoardRow>() : await reviews.ListBoardAsync(ct).ConfigureAwait(true);
            var asks = await AppServices.Get<IBdDirectorLedgerStore>().ListOpenAsksForAsync(_actor, ct).ConfigureAwait(true);
            var roster = _vm.People.Select(p => new DirectorPerson(p.DisplayName, p.OwnerKeys.FirstOrDefault(k => k.Contains('@')) ?? p.PersonKey, p.Live))
                .Where(p => p.Mailbox.Contains('@')).ToList();
            var me = _vm.People.FirstOrDefault(p => p.OwnerKeys.Any(k => k.Equals(_actor, StringComparison.OrdinalIgnoreCase)))?.DisplayName;
            _home.Load(board, asks, _vm.People.ToList(), roster, _actor, me, DateTimeOffset.UtcNow);
            if (_rhythm is { Hour: { } hour })
            {
                _home.HourTitle = hour.Title;
                _home.HourProgress = _rhythm.MovesProgressText;
            }
            TodayBadge.Visibility = _home.DecisionCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            TodayBadgeText.Text = _home.DecisionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: loading Today, Pursuits and Team failed.");
            _vm.Say("Today and Pursuits could not load; Refresh to try again.");
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string place } || TodayPanel is null) return;
        TodayPanel.Visibility = place == "today" ? Visibility.Visible : Visibility.Collapsed;
        PursuitsPanel.Visibility = place == "pursuits" ? Visibility.Visible : Visibility.Collapsed;
        TeamPanel.Visibility = place == "team" ? Visibility.Visible : Visibility.Collapsed;
        PlanPanel.Visibility = place == "plan" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility = place == "library" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Opens the Library; with <paramref name="byPerson"/> on the every-pursuit-by-person view.</summary>
    internal void ShowLibrary(bool byPerson = false, System.Windows.Controls.TabItem? tab = null)
    {
        NavLibrary.IsChecked = true;
        if (tab is not null) { Tabs.SelectedItem = tab; return; }
        if (!byPerson) return;
        Tabs.SelectedItem = DataTab;
        DataTabs.SelectedItem = DataTabs.Items.OfType<System.Windows.Controls.TabItem>().FirstOrDefault(t => Equals(t.Header, "By person"));
    }

    // ---- IDirectorHomeHost: what the screens ask for ----

    public async Task CardPrimaryAsync(DirectorCard card)
    {
        if (card.Row.Recommendation == "no-go")
        {
            await ArchiveAsync(card).ConfigureAwait(true);
            return;
        }
        if (card.Row.Questions.Count > 0 && await LatestReviewAsync(card) is { } review)
        {
            PursuitReviewWindow.DraftQuestions(this, review, card.Row.Project, card.Row.QuestionsTo);
            return;
        }
        await OpenReviewAsync(card).ConfigureAwait(true);
    }

    public async Task OpenReviewAsync(DirectorCard card)
    {
        if (await LatestReviewAsync(card) is not { } review)
        {
            MessageBox.Show(this, card.Row.ReviewRunning ? "Claude is reading this RFP now; the review appears when it reports."
                : "Claude has not read this RFP yet. It starts by itself once the documents are in.", "Claude's RFP review",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new PursuitReviewWindow(review, card.Row.Project, card.Row.Buyer, null, card.Row.DeadlineUtc,
            () => OpenChecklist(card), card.Row.OwnerMailbox) { Owner = this }.Show();
    }

    public async Task OpenPursuitAsync(DirectorCard card)
    {
        try
        {
            await global::Kor.Operations.KorDeepLink.OpenAsync($"kor://pursuit/{card.Row.EngagementId}").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: opening pursuit {Id} failed", card.Row.EngagementId);
            MessageBox.Show(this, "Open My Pursuits and select #" + card.Row.EngagementId + ".", "Open pursuit", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public async Task CompleteAskAsync(DirectorListItem item)
    {
        try
        {
            await AppServices.Get<IBdDirectorLedgerStore>().CloseAskAsync(item.Id, "Done", null, _actor, _lifetime.Token).ConfigureAwait(true);
            _home.RemoveFromList(item);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: closing ask {Id} failed", item.Id);
            MessageBox.Show(this, "That did not save. Please try again.", "Your list", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void BookTime(DirectorTeamCard person)
    {
        var mailbox = person.Person.OwnerKeys.FirstOrDefault(k => k.Contains('@')) ?? person.Person.PersonKey;
        var start = NextWeekday(DateTime.Today.AddDays(1)).AddHours(10);
        var body = $"Twenty minutes to put a dated next step on your live pursuits.\n\n{person.Summary}.";
        _ = OpenOutlookDraftAsync(new HourDraft(mailbox, $"20 min: next steps on your pursuits", body)
            { When = new DateTimeOffset(start).ToString("o", System.Globalization.CultureInfo.InvariantCulture), Minutes = 20 });
    }

    public void OpenHour() => ShowLibrary(tab: TodayTab);

    public void OpenPersonList(DirectorTeamCard person)
    {
        ShowLibrary(byPerson: true);
        var row = _vm.People.FirstOrDefault(p => p.PersonKey == person.Person.PersonKey);
        if (row is not null) _vm.SelectedPerson = row;
    }

    public async Task ReassignAsync(DirectorCard card, DirectorPerson to)
    {
        var r = card.Row;
        var from = r.OwnerMailbox ?? "";
        if (MessageBox.Show(this, $"Give \"{BdDirectorHomeViewModel.Short(r.Project)}\" to {to.Name}?\n\n{_home.ReassignPreview}", "Reassign",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var outcome = await AppServices.Get<IPursuitOverwatchStore>().ReassignAsync(r.EngagementId, r.OpportunityId, from, to.Mailbox, _actor,
                "Reassigned in BD Director.", _lifetime.Token).ConfigureAwait(true);
            if (outcome != ReassignOutcome.Reassigned)
            {
                MessageBox.Show(this, outcome switch
                {
                    ReassignOutcome.OwnerChanged => "Someone else moved it first. Refresh to see who owns it.",
                    ReassignOutcome.DuplicateForTarget => $"{to.Name} already holds a pursuit for this buyer and region; not moved.",
                    ReassignOutcome.PursuitClosed => "This pursuit has closed; not moved.",
                    _ => "Not moved.",
                }, "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var warning = await PursuitAssignmentNotice.SendAsync(
                new PursuitAssignment(r.EngagementId, null, r.Project, r.Buyer, r.DeadlineUtc, to.Mailbox, to.Name, _actor, "Reassigned in BD Director.", from),
                AppServices.Get<Kor.Operations.Graph.IGraphFacade>(), AppServices.Get<IBdDirectorLedgerStore>(),
                AppServices.GetOptional<SqlPursuitReviewStore>(), _lifetime.Token).ConfigureAwait(true);
            _vm.Say($"\"{BdDirectorHomeViewModel.Short(r.Project)}\" is now {BdDirectorHomeViewModel.First(to.Name)}'s; they have been told.");
            if (warning is not null) MessageBox.Show(this, "Reassigned. " + warning, "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: reassigning {Id} failed", r.EngagementId);
            MessageBox.Show(this, "Could not reassign. Refresh and try again.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ArchiveAsync(DirectorCard card)
    {
        var reason = card.Deciding;
        if (MessageBox.Show(this, $"Archive \"{BdDirectorHomeViewModel.Short(card.Row.Project)}\"?\n\nReason recorded: {reason}", "Archive",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var store = AppServices.Get<ICrmEngagementStore>();
            var current = await store.GetByIdAsync(card.Row.EngagementId, _lifetime.Token).ConfigureAwait(true)
                ?? throw new InvalidOperationException("The pursuit no longer exists.");
            await store.UpdateAsync(current with { Stage = CrmEngagementStage.Archived, OutcomeReason = reason.Length <= 400 ? reason : reason[..400] },
                _actor, _lifetime.Token).ConfigureAwait(true);
            _vm.Say($"Archived \"{BdDirectorHomeViewModel.Short(card.Row.Project)}\".");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: archiving {Id} failed", card.Row.EngagementId);
            MessageBox.Show(this, "Could not archive. Refresh and try again.", "Archive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<PursuitReviewRow?> LatestReviewAsync(DirectorCard card)
    {
        try
        {
            return AppServices.GetOptional<SqlPursuitReviewStore>() is { } reviews
                ? await reviews.LatestAsync(card.Row.EngagementId, _lifetime.Token).ConfigureAwait(true) : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director: reading the review for {Id} failed", card.Row.EngagementId);
            return null;
        }
    }

    private void OpenChecklist(DirectorCard card)
    {
        var checklist = new PursuitChecklistViewModel(AppServices.Get<IPursuitRequirementStore>(), AppServices.Get<IPursuitFileStore>(),
            card.Row.EngagementId, BdDirectorHomeViewModel.Short(card.Row.Project), _actor);
        new PursuitChecklistWindow(checklist) { Owner = this }.Show();
    }
}
