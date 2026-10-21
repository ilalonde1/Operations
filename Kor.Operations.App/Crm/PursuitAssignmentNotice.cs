#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Graph;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>What the new owner is told, gathered once by whichever screen moved the pursuit.</summary>
public sealed record PursuitAssignment(long EngagementId, long? BuyerOrgId, string Project, string? Buyer, DateTimeOffset? DeadlineUtc,
    string ToOwner, string? ToDisplay, string ByUpn, string? Reason);

/// <summary>
/// Tells a person a pursuit is now theirs, the same way from every place an owner changes (BD Director Reassign and Assign,
/// All Pursuits Reassign, My Pursuits Edit). Ian, 21 Oct 2026: the email goes automatically, sent as the person who moved it
/// ("I don't want to have to dig into my outlook and send"). It carries Claude's RFP verdict and the owner's actions when the
/// RFP has been read. A "take it over" ask also lands on their My day, so it is waiting in the app even if the email is missed.
/// The move has already been saved; a failure here is returned as a warning, never thrown.
/// </summary>
public static class PursuitAssignmentNotice
{
    public static async Task<string?> SendAsync(PursuitAssignment a, IGraphFacade graph, IBdDirectorLedgerStore ledger,
        SqlPursuitReviewStore? reviews, CancellationToken ct)
    {
        var problems = new StringBuilder();
        PursuitReviewRow? review = null;
        try
        {
            if (reviews is not null)
            {
                review = await reviews.LatestAsync(a.EngagementId, ct).ConfigureAwait(true);
                // A board row may not carry the deadline; the pursuit's own record does.
                if (a.DeadlineUtc is null && await reviews.LoadInputAsync(a.EngagementId, a.ByUpn, DateTimeOffset.UtcNow, ct).ConfigureAwait(true) is { } input)
                    a = a with { DeadlineUtc = input.SubmissionDeadlineUtc };
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Assignment notice: reading the RFP review failed for {EngagementId}", a.EngagementId); }

        var name = string.IsNullOrWhiteSpace(a.ToDisplay) ? a.ToOwner : a.ToDisplay!;
        if (a.ToOwner.Contains('@'))
        {
            try
            {
                await graph.SendSimpleMailAsync(a.ByUpn, new[] { a.ToOwner }, $"Pursuit assigned to you: {a.Project}",
                    global::Kor.Operations.KorEmailTemplate.Shell("PURSUIT ASSIGNED TO YOU", Body(a, name, review)), ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Assignment notice: email to {To} failed", a.ToOwner);
                problems.Append($"The email to {name} could not be sent: {ex.Message} ");
            }
        }
        else
        {
            problems.Append($"No email was sent: the new owner \"{a.ToOwner}\" is not an email address. ");
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var due = review?.Actions.Where(x => PursuitRfpReview.ResolveOwner(x.Owner, a.ToOwner).Equals(a.ToOwner, StringComparison.OrdinalIgnoreCase))
                .Select(x => (DateTimeOffset?)new DateTimeOffset(x.Due.ToDateTime(new TimeOnly(17, 0)), TimeSpan.FromHours(-6))).Min()
                ?? now.AddDays(2);
            var ask = $"Take over {a.Project}" + (review is null ? "" : ": read Claude's RFP review and act on it");
            await ledger.CreateAskAsync(new BdAsk(0, a.ToOwner, a.EngagementId, a.BuyerOrgId, ask.Length <= 500 ? ask : ask[..500],
                string.IsNullOrWhiteSpace(a.Reason) ? $"Assigned to you by {a.ByUpn}." : a.Reason, 30, due, a.ByUpn, now, "Open", null, null, null), ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Assignment notice: the My day ask for {To} failed", a.ToOwner);
            problems.Append("The \"take it over\" item could not be added to their My day. ");
        }

        return problems.Length == 0 ? null : problems.ToString().Trim();
    }

    /// <summary>The email: what, why, the deadline, and, when Claude has read the RFP, the verdict and this person's actions.</summary>
    internal static string Body(PursuitAssignment a, string name, PursuitReviewRow? review)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append($"<p>Hi {H(name)},</p>");
        sb.Append($"<p><b>{H(a.Project)}</b>{(string.IsNullOrWhiteSpace(a.Buyer) ? "" : $" ({H(a.Buyer)})")} is now yours.</p>");
        if (!string.IsNullOrWhiteSpace(a.Reason)) sb.Append($"<p><b>Why:</b> {H(a.Reason)}</p>");
        if (a.DeadlineUtc is { } d) sb.Append($"<p><b>Proposal due:</b> {H(d.ToLocalTime().ToString("dddd d MMMM, h:mm tt"))}</p>");
        if (review is not null)
        {
            var verdict = review.Recommendation switch { "go" => "Bid", "no-go" => "Don't bid", _ => "Your call" };
            var deciding = review.Reasons.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            sb.Append($"<p><b>Claude read the RFP: {H(verdict)}.</b> {H(deciding)}</p>");
            var mine = review.Actions.Where(x => PursuitRfpReview.ResolveOwner(x.Owner, a.ToOwner).Equals(a.ToOwner, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count > 0)
            {
                sb.Append("<p><b>Your actions:</b></p><ol>");
                foreach (var x in mine) sb.Append($"<li>{H(x.Action)} <b>by {H(x.Due.ToString("ddd d MMM"))}</b> — {H(x.Why)}</li>");
                sb.Append("</ol>");
            }
            if (review.Questions.Count > 0)
                sb.Append($"<p>{review.Questions.Count} question{(review.Questions.Count == 1 ? "" : "s")} for the buyer{(review.QuestionsDeadline is { } q ? $", due {H(q)}" : "")}, are drafted in the review.</p>");
        }
        sb.Append("<p>Open <b>KOR Operations → Business Development → My Pursuits</b> and select the pursuit: the review, the documents and the checklist are on it.</p>");
        sb.Append($"<p style=\"color:#6B7280;font-size:12px\">Assigned by {H(a.ByUpn)} in KOR Operations.</p>");
        return sb.ToString();
    }
}
