#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Kor.Operations.App.Services;
using Kor.Operations.Controls;
using Kor.Operations.Services;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// The "Approach" block: Claude drafts who-to-call, a call script, and a draft
/// email LIVE from the brief's current intel every time it's generated — nothing
/// is stored, so it is always exactly as fresh as the underlying data (which the
/// server refresh jobs keep current). Cost per generation is accepted; freshness
/// and richness are the point.
/// </summary>
public partial class PursuitBriefWindow
{
    private const string ApproachInstruction =
        "You are drafting a pursuit approach for KOR Structural (a structural engineering firm) to win " +
        "the STRUCTURAL seat on the project below. Use ONLY the intel provided — do not invent facts, " +
        "names, or emails not present. Write in Markdown with exactly these three sections:\n\n" +
        "## Who to call\n" +
        "Ranked list of the specific people to reach first. For each: name, role, and one line on why " +
        "them (their leverage over the structural sub decision). Prefer named contacts that have an email.\n\n" +
        "## Call script\n" +
        "A tight opener that names the project and KOR's angle for the procurement channel; 3-5 talking " +
        "points tuned to that channel and KOR's edge; and 2 likely objections with crisp responses.\n\n" +
        "## Draft email\n" +
        "A ready-to-send intro email to the single best contact — a subject line, then a 5-8 sentence body, " +
        "specific to THIS project and channel, professional and concrete, no filler.\n\n" +
        "Be specific to this pursuit. If the intel is thin on a point, say so briefly rather than inventing.";

    private bool _approachBusy;
    private CancellationTokenSource? _approachCts;

    private async void DraftApproach_Click(object sender, RoutedEventArgs e)
    {
        if (_approachBusy || _vm.Brief is null)
        {
            return;
        }

        var ai = AppServices.Get<AppAiService>();
        if (ai is null || !ai.IsConfigured)
        {
            ApproachStatus.Text = "AI is not configured (set McpServer in App.config).";
            return;
        }

        _approachBusy = true;
        _approachCts = new CancellationTokenSource();
        DraftApproachButton.IsEnabled = false;
        CancelApproachButton.Visibility = Visibility.Visible;
        CancelApproachButton.IsEnabled = true;
        ApproachHost.Children.Clear();
        ApproachStatus.Text = "Drafting who to call, script & email from the current intel…";

        try
        {
            var context = BuildApproachContext(_vm.Brief);
            var conversation = new[] { ("user", ApproachInstruction) };
            var result = await ai.AskAsync(conversation, localContext: context, ct: _approachCts.Token).ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                ApproachStatus.Text = result.ErrorMessage ?? "AI draft failed. Try again.";
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                ApproachStatus.Text = "No draft came back — try again.";
                return;
            }

            var ink = (Brush)(TryFindResource("Text.Primary") ?? Brushes.Black);
            var codeBg = (Brush)(TryFindResource("App.Background") ?? Brushes.WhiteSmoke);
            var codeBorder = (Brush)(TryFindResource("Panel.Border") ?? Brushes.LightGray);
            MarkdownPresenter.Render(result.Text, ApproachHost, ink, codeBg, codeBorder);
            ApproachStatus.Text = $"Drafted {DateTime.Now:HH:mm} from live intel — regenerate any time for the current picture.";
        }
        catch (Exception ex)
        {
            ApproachStatus.Text = $"Draft failed: {ex.Message}";
        }
        finally
        {
            _approachCts?.Dispose();
            _approachCts = null;
            _approachBusy = false;
            DraftApproachButton.IsEnabled = true;
            CancelApproachButton.Visibility = Visibility.Collapsed;
            CancelApproachButton.IsEnabled = false;
        }
    }

    private void CancelApproach_Click(object sender, RoutedEventArgs e)
    {
        CancelApproachButton.IsEnabled = false;
        ApproachStatus.Text = "Cancelling draft…";
        _approachCts?.Cancel();
    }

    /// <summary>Flattens the brief's live intel into a labelled block for the model.</summary>
    private static string BuildApproachContext(PursuitBrief b)
    {
        var sb = new StringBuilder();
        var p = b.Project;
        sb.AppendLine("PROJECT");
        sb.AppendLine($"- Name: {p.ProjectName}");
        sb.AppendLine($"- Owner/proponent: {p.Owner}");
        sb.AppendLine($"- Location: {p.City} ({p.Market}), sector {p.Sector}");
        sb.AppendLine($"- Stage: {p.Stage}; est. cost: {p.EstCostDisplay}");
        sb.AppendLine();

        var a = b.Architect;
        sb.AppendLine("ARCHITECT / STRUCTURAL SEAT");
        sb.AppendLine($"- Architect: {a.ArchitectName}");
        sb.AppendLine($"- Seat status: {a.SeatStatus}; priority: {a.SeatPriority}");
        if (!string.IsNullOrWhiteSpace(a.DisplacementRead)) sb.AppendLine($"- Displacement read: {a.DisplacementRead}");
        if (a.RecurringStructuralPartners.Count > 0)
            sb.AppendLine($"- Architect's recurring structural partners (incumbents to displace): {string.Join(", ", a.RecurringStructuralPartners)}");
        sb.AppendLine();

        var op = b.OwnerProcurement;
        if (!string.IsNullOrWhiteSpace(op.ProcurementMethod) || !string.IsNullOrWhiteSpace(op.RosterProgram))
        {
            sb.AppendLine("HOW IT'S BOUGHT");
            if (!string.IsNullOrWhiteSpace(op.ProcurementMethod)) sb.AppendLine($"- Method: {op.ProcurementMethod}");
            if (!string.IsNullOrWhiteSpace(op.RosterProgram)) sb.AppendLine($"- Roster: {op.RosterProgram}");
            if (!string.IsNullOrWhiteSpace(op.EvaluationCriteria)) sb.AppendLine($"- Evaluation: {op.EvaluationCriteria}");
            if (!string.IsNullOrWhiteSpace(op.BudgetCadence)) sb.AppendLine($"- Budget cadence: {op.BudgetCadence}");
            sb.AppendLine();
        }

        AppendPeople(sb, "ARCHITECT CONTACTS (who to reach at the architect)", b.ArchitectContacts);
        AppendPeople(sb, "OWNER CONTACTS", b.OwnerContacts);

        if (b.CompetitorNotes.Count > 0)
        {
            sb.AppendLine("COMPETITOR LOAD (structural firms in play)");
            foreach (var c in b.CompetitorNotes)
                sb.AppendLine($"- {c.Firm}: {c.CapacityRead}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(b.KorEdge)) { sb.AppendLine("KOR'S EDGE"); sb.AppendLine(b.KorEdge); sb.AppendLine(); }
        if (!string.IsNullOrWhiteSpace(b.ThePlay)) { sb.AppendLine("THE PLAY (current read)"); sb.AppendLine(b.ThePlay); sb.AppendLine(); }

        return sb.ToString();
    }

    private static void AppendPeople(StringBuilder sb, string label, System.Collections.Generic.IReadOnlyList<PursuitBriefPerson> people)
    {
        if (people.Count == 0) return;
        sb.AppendLine(label);
        foreach (var c in people.Take(8))
        {
            var email = string.IsNullOrWhiteSpace(c.Email) ? "(no email on file)" : c.Email;
            var role = string.Join(" / ", new[] { c.Title, c.Role }.Where(x => !string.IsNullOrWhiteSpace(x)));
            sb.AppendLine($"- {c.Name} — {role} — {email}");
        }
        sb.AppendLine();
    }
}
