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

    // Only a real UPN is kept: a bare Windows login used while sign-in was still resolving stuck for the whole session and
    // matched none of the stored owners (audit C13, 2026-10-21). The fallback is used for that one load and asked again next.
    private string Actor()
    {
        if (_actor.Length > 0) return _actor;
        var actor = new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn, _services.GetService<UserOptions>()?.UserUpnOverride, Environment.UserName }
            .First(v => !string.IsNullOrWhiteSpace(v))!.Trim();
        if (actor.Contains('@')) _actor = actor;
        return actor;
    }

    // Each load has a number; only the newest applies what it read, and it applies nothing until every read has succeeded,
    // so Today is never half old and half new, and a failure says so on the screen (audit C14, 2026-10-21).
    private int _todayGeneration;

    private async Task LoadTodayAsync()
    {
        var actor = Actor();
        var generation = ++_todayGeneration;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var reviews = _services.GetService<SqlPursuitReviewStore>();
            var board = reviews is null ? Array.Empty<TenderBoardRow>() : await reviews.ListBoardAsync(CancellationToken.None).ConfigureAwait(true);
            var asks = await _services.GetRequiredService<IBdDirectorLedgerStore>().ListOpenAsksForAsync(actor, CancellationToken.None).ConfigureAwait(true);
            var joins = await _services.GetRequiredService<IPursuitGrabStore>().PendingJoinsAsync(actor, CancellationToken.None).ConfigureAwait(true);
            // Their live pursuits with no next step, or an overdue one: names from the board of every pursuit, steps from the record.
            var rows = await _services.GetRequiredService<IPursuitOverwatchStore>().ListAsync(CancellationToken.None).ConfigureAwait(true);
            var records = (await _services.GetRequiredService<ICrmEngagementStore>().ListAsync(CancellationToken.None).ConfigureAwait(true))
                .ToDictionary(r => r.Id);
            if (generation != _todayGeneration) return;

            var name = string.IsNullOrWhiteSpace(HeaderBar.UserDisplayName) ? null : HeaderBar.UserDisplayName;
            _today.Load(board, asks, Array.Empty<BdDirectorPersonRow>(), Array.Empty<DirectorPerson>(), actor, name, now, ownOnly: true);
            _today.LoadJoins(joins);
            _today.LoadProblem = "";
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
            if (generation == _todayGeneration)
                _today.LoadProblem = "Today could not be brought up to date, so what is below may be out of date. Click Today again to retry.";
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

    public Task AcceptJoinAsync(DirectorJoin join) => DirectorActions.AcceptJoinAsync(this, join, Actor(), _today, CancellationToken.None);

    public Task RejectJoinAsync(DirectorJoin join) => DirectorActions.RejectJoinAsync(this, join, Actor(), _today, LoadTodayAsync, CancellationToken.None);

    // Director-only: the prepared hour, team time and reassigning live in BD Director.
    public void BookTime(DirectorTeamCard person) { }
    public void OpenHour() { }
    public void OpenPersonList(DirectorTeamCard person) { }
    public Task ReassignAsync(DirectorCard card, DirectorPerson to) => Task.CompletedTask;
}
