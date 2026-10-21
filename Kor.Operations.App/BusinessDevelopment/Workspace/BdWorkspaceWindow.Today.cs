#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// The BD Workspace's Today (21 Oct 2026, Ian: "that same look and function … to the BD interface the users use"): the BD
/// Director's Today screen, scoped to the signed-in lead. Their tenders with Claude's read, what is coming up, their list,
/// and their pursuits waiting for a next step. Loaded each time Today opens; nothing here watches anyone.
/// </summary>
public partial class BdWorkspaceWindow : IDirectorHomeHost
{
    private readonly BdDirectorHomeViewModel _today = new();
    private DirectorTodayView? _todayView;
    private string _actor = "";

    private void Today_Click(object sender, RoutedEventArgs e) => NavigateToToday();

    public void NavigateToToday()
    {
        SetActiveNav(TodayButton);
        _todayView ??= new DirectorTodayView { DataContext = _today };
        ContentHost.Content = _todayView;
        _ = LoadTodayAsync();
    }

    private string Actor()
    {
        if (_actor.Length > 0) return _actor;
        _actor = new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn, _services.GetService<UserOptions>()?.UserUpnOverride, Environment.UserName }
            .First(v => !string.IsNullOrWhiteSpace(v))!.Trim();
        return _actor;
    }

    private async Task LoadTodayAsync()
    {
        var actor = Actor();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var reviews = _services.GetService<SqlPursuitReviewStore>();
            var board = reviews is null ? Array.Empty<TenderBoardRow>() : await reviews.ListBoardAsync(CancellationToken.None).ConfigureAwait(true);
            var asks = await _services.GetRequiredService<IBdDirectorLedgerStore>().ListOpenAsksForAsync(actor, CancellationToken.None).ConfigureAwait(true);
            var name = string.IsNullOrWhiteSpace(HeaderBar.UserDisplayName) ? null : HeaderBar.UserDisplayName;
            _today.Load(board, asks, Array.Empty<BdDirectorPersonRow>(), Array.Empty<DirectorPerson>(), actor, name, now, ownOnly: true);

            // Their live pursuits with no next step, or an overdue one: names from the board of every pursuit, steps from the record.
            var rows = await _services.GetRequiredService<IPursuitOverwatchStore>().ListAsync(CancellationToken.None).ConfigureAwait(true);
            var records = (await _services.GetRequiredService<ICrmEngagementStore>().ListAsync(CancellationToken.None).ConfigureAwait(true))
                .ToDictionary(r => r.Id);
            _today.LoadFollowUps(rows
                .Where(r => string.Equals(r.OwnerStaffId, actor, StringComparison.OrdinalIgnoreCase) && records.ContainsKey(r.EngagementId))
                .Select(r => (row: r, rec: records[r.EngagementId]))
                .Where(x => x.rec.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted
                    && (string.IsNullOrWhiteSpace(x.rec.NextActionNote) || x.rec.NextActionDueUtc < now))
                .Select(x => new DirectorFollowUp(x.row.EngagementId, x.row.ProjectName, x.row.Buyer,
                    !string.IsNullOrWhiteSpace(x.rec.NextActionNote) && x.rec.NextActionDueUtc < now, x.rec.NextActionDueUtc, x.row.LastActivityUtc)));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Workspace: loading Today failed for {Actor}", actor);
        }
    }

    // ---- IDirectorHomeHost: the same buttons as BD Director, routed inside this window ----

    public Task CardPrimaryAsync(DirectorCard card) => DirectorActions.CardPrimaryAsync(this, card, Actor(), LoadTodayAsync, CancellationToken.None);

    public Task OpenReviewAsync(DirectorCard card) => DirectorActions.OpenReviewAsync(this, card, Actor(), CancellationToken.None);

    public Task CompleteAskAsync(DirectorListItem item) => DirectorActions.CompleteAskAsync(this, item, Actor(), _today, CancellationToken.None);

    public Task OpenPursuitAsync(DirectorCard card)
    {
        NavigateToPursuit(card.Row.EngagementId);
        return Task.CompletedTask;
    }

    public void OpenFollowUp(DirectorFollowUp pursuit) => NavigateToPursuit(pursuit.EngagementId);

    // Director-only: the prepared hour, team time and reassigning live in BD Director.
    public void BookTime(DirectorTeamCard person) { }
    public void OpenHour() { }
    public void OpenPersonList(DirectorTeamCard person) { }
    public Task ReassignAsync(DirectorCard card, DirectorPerson to) => Task.CompletedTask;
}
