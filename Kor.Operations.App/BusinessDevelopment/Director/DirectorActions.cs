#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Crm;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// What a card's buttons do, the same in BD Director and the BD Workspace (21 Oct 2026: "that same look and function"):
/// open the stored review, draft the buyer questions (unsent), archive a no-go, tick off an ask. Each goes through an existing
/// store; a failure is shown, never thrown.
/// </summary>
public static class DirectorActions
{
    /// <summary>The card's main button: archive a no-go, draft the questions when there are some, otherwise open the review.</summary>
    public static async Task CardPrimaryAsync(Window owner, DirectorCard card, string actor, Func<Task> reload, CancellationToken ct)
    {
        if (card.Row.Recommendation == "no-go")
        {
            await ArchiveAsync(owner, card, actor, reload, ct).ConfigureAwait(true);
            return;
        }
        if (card.Row.Questions.Count > 0 && await LatestReviewAsync(card, ct).ConfigureAwait(true) is { } review)
        {
            PursuitReviewWindow.DraftQuestions(owner, review, card.Row.Project, card.Row.QuestionsTo);
            return;
        }
        await OpenReviewAsync(owner, card, actor, ct).ConfigureAwait(true);
    }

    public static async Task OpenReviewAsync(Window owner, DirectorCard card, string actor, CancellationToken ct)
    {
        if (await LatestReviewAsync(card, ct).ConfigureAwait(true) is not { } review)
        {
            MessageBox.Show(owner, card.Row.ReviewRunning ? "Claude is reading this RFP now; the review appears when it reports."
                : "Claude has not read this RFP yet. It starts by itself once the documents are in.", "Claude's RFP review",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new PursuitReviewWindow(review, card.Row.Project, card.Row.Buyer, null, card.Row.DeadlineUtc,
            () => OpenChecklist(owner, card, actor), card.Row.OwnerMailbox) { Owner = owner }.Show();
    }

    public static async Task ArchiveAsync(Window owner, DirectorCard card, string actor, Func<Task> reload, CancellationToken ct)
    {
        var reason = card.Deciding;
        if (MessageBox.Show(owner, $"Archive \"{card.Title}: {card.Buyer}\"?\n\nReason recorded: {reason}", "Archive",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var store = AppServices.Get<ICrmEngagementStore>();
            var current = await store.GetByIdAsync(card.Row.EngagementId, ct).ConfigureAwait(true)
                ?? throw new InvalidOperationException("The pursuit no longer exists.");
            await store.UpdateAsync(current with { Stage = CrmEngagementStage.Archived, OutcomeReason = reason.Length <= 400 ? reason : reason[..400] },
                actor, ct).ConfigureAwait(true);
            await reload().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Archiving pursuit {Id} failed", card.Row.EngagementId);
            MessageBox.Show(owner, "Could not archive. Refresh and try again.", "Archive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static async Task CompleteAskAsync(Window owner, DirectorListItem item, string actor, BdDirectorHomeViewModel home, CancellationToken ct)
    {
        try
        {
            await AppServices.Get<IBdDirectorLedgerStore>().CloseAskAsync(item.Id, "Done", null, actor, ct).ConfigureAwait(true);
            home.RemoveFromList(item);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Closing ask {Id} failed", item.Id);
            MessageBox.Show(owner, "That did not save. Please try again.", "Your list", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenChecklist(Window owner, DirectorCard card, string actor)
    {
        var checklist = new PursuitChecklistViewModel(AppServices.Get<IPursuitRequirementStore>(), AppServices.Get<IPursuitFileStore>(),
            card.Row.EngagementId, $"{card.Title}: {card.Buyer}", actor);
        new PursuitChecklistWindow(checklist) { Owner = owner }.Show();
    }

    private static async Task<PursuitReviewRow?> LatestReviewAsync(DirectorCard card, CancellationToken ct)
    {
        try
        {
            return AppServices.GetOptional<SqlPursuitReviewStore>() is { } reviews
                ? await reviews.LatestAsync(card.Row.EngagementId, ct).ConfigureAwait(true) : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading the review for {Id} failed", card.Row.EngagementId);
            return null;
        }
    }
}
