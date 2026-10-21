#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Claude's review of a pursuit's RFP, as stored (migration 377). The decision first: the verdict and the reason that decides
/// it, the dates, the questions for the buyer, then what the tender is. The questions email opens unsent in the person's
/// Outlook; nothing is sent from here.
/// </summary>
public partial class PursuitReviewWindow : Window
{
    private readonly PursuitReviewRow _review;
    private readonly string _project;
    private readonly string? _sendTo;
    private readonly Action _openChecklist;

    /// <summary>A question as the window shows it: numbered, with why it matters and where it comes from.</summary>
    public sealed record QuestionLine(int Number, string Question, string Why, string Ref);

    /// <summary>An action as the window shows it: numbered, who and when on the right, red when overdue, amber within two days.</summary>
    public sealed record ActionLine(int Number, string Action, string Why, string Who, string When, System.Windows.Media.Brush WhenBrush);

    public PursuitReviewWindow(PursuitReviewRow review, string project, string? buyer, string? buyerEmail,
        DateTimeOffset? submissionDeadlineUtc, Action openChecklist, string? pursuitOwner = null)
    {
        InitializeComponent();
        _review = review;
        _project = project;
        // questionsTo is meant to be an address; a session may write a sentence there (Leduc, run 17: "Through the bids&tenders
        // 'Submit a Question' feature only…"), which must never land in an email's To line.
        _sendTo = IsAddress(review.QuestionsTo) ? review.QuestionsTo!.Trim() : IsAddress(buyerEmail) ? buyerEmail!.Trim() : null;
        _openChecklist = openChecklist;

        Heading.Text = project;
        Byline.Text = $"Pursuit #{review.EngagementId}{(buyer is { } b ? " · " + b : "")} · read by {review.ReportedBy}, {review.ReportedAtUtc.ToLocalTime():ddd d MMM h:mm tt}";

        Verdict.Text = CrmViewModel.RecommendationLabel(review.Recommendation).ToUpperInvariant();
        VerdictPill.Background = CrmViewModel.RecommendationBrush(review.Recommendation);
        var reasons = ReasonLines(review.Reasons);
        // The pill already says the verdict; "Decide: …" beside it says it twice.
        var deciding = System.Text.RegularExpressions.Regex.Replace(reasons.FirstOrDefault() ?? "", @"^(decide|go|no-go|bid|don't bid)\s*[:\-–]\s*", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Deciding.Text = deciding.Length > 0 ? char.ToUpperInvariant(deciding[0]) + deciding[1..] : deciding;
        Reasons.ItemsSource = reasons.Skip(1).ToList();
        WhyHeading.Visibility = reasons.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        var dates = new StringBuilder();
        if (review.Questions.Count > 0 && review.QuestionsDeadline is { } q) dates.Append($"Questions due {q}{(_sendTo is { } to ? " to " + to : "")}");
        if (submissionDeadlineUtc is { } s) dates.Append(dates.Length > 0 ? "     ·     " : "").Append($"Proposal due {s.ToLocalTime():ddd d MMM yyyy, h:mm tt}");
        Dates.Text = dates.ToString();
        Dates.Visibility = dates.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var today = DateOnly.FromDateTime(DateTime.Today);
        Actions.ItemsSource = review.Actions.Select((a, i) => new ActionLine(i + 1, a.Action, a.Why,
            PersonName(PursuitRfpReview.ResolveOwner(a.Owner, pursuitOwner)),
            a.Due < today ? $"Overdue · {a.Due:ddd d MMM}" : a.Due == today ? "Today" : a.Due == today.AddDays(1) ? $"Tomorrow · {a.Due:ddd d MMM}" : $"By {a.Due:ddd d MMM}",
            a.Due < today ? Overdue : a.Due <= today.AddDays(1) ? CrmViewModel.RecommendationBrush("decide") : (System.Windows.Media.Brush)FindResource("Text.Secondary")))
            .ToList();
        ActionsHeading.Visibility = Actions.Visibility = review.Actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        QuestionsHeading.Text = review.Questions.Count == 0 ? "No questions for the buyer" : $"Questions for the buyer ({review.Questions.Count})";
        Questions.ItemsSource = review.Questions.Select((x, i) => new QuestionLine(i + 1, x.Question, x.Why, x.Ref is { } r ? "From " + r : "")).ToList();
        DraftButton.Content = review.Questions.Count == 1 ? "Draft the question as an email" : $"Draft the {review.Questions.Count} questions as an email";
        DraftButton.IsEnabled = review.Questions.Count > 0;
        ChecklistButton.Content = "Checklist";
        Summary.Text = review.Summary;
        Footnote.Text = string.Join("  ", new[] { review.FilesRead, review.ChecklistNote }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static readonly System.Windows.Media.Brush Overdue = Frozen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B)));

    private static System.Windows.Media.Brush Frozen(System.Windows.Media.SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>"ishabana@korstructural.com" reads as "ishabana"; Ian's own mailbox as "Ian".</summary>
    internal static string PersonName(string mailbox) =>
        mailbox.Equals(PursuitRfpReview.IanMailbox, StringComparison.OrdinalIgnoreCase) ? "Ian"
        : mailbox.IndexOf('@') is var at and > 0 ? mailbox[..at] : mailbox;

    internal static bool IsAddress(string? s) =>
        s is { Length: > 3 } && !s.Trim().Contains(' ') && s.IndexOf('@') > 0 && s.IndexOf('@') == s.LastIndexOf('@');

    /// <summary>The reasons, one point per line, the deciding one first (as the review prompt asks for them).</summary>
    internal static System.Collections.Generic.List<string> ReasonLines(string reasons) =>
        reasons.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimStart('-', '•', '*', ' ')).Where(l => l.Length > 0).ToList();

    private async void DraftEmail_Click(object sender, RoutedEventArgs e)
    {
        var body = new StringBuilder();
        body.AppendLine("Hello,");
        body.AppendLine();
        body.AppendLine($"KOR Structural has the following questions on {_project}:");
        body.AppendLine();
        for (var i = 0; i < _review.Questions.Count; i++)
        {
            var q = _review.Questions[i];
            body.AppendLine($"{i + 1}. {q.Question}{(q.Ref is { } r ? $" ({r})" : "")}");
            body.AppendLine();
        }
        body.AppendLine("Thank you,");
        var draft = new HourDraft(_sendTo ?? "", $"Questions: {_project}", body.ToString());
        try
        {
            await Task.Run(() => OutlookDraft.Open(draft)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RFP review: opening the questions draft failed");
            MessageBox.Show(this, "Outlook could not open the draft: " + ex.Message, "Questions email", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Checklist_Click(object sender, RoutedEventArgs e) => _openChecklist();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Heading.Text).AppendLine(Byline.Text).AppendLine();
        sb.AppendLine($"{Verdict.Text}: {_review.Reasons}").AppendLine();
        if (Dates.Text.Length > 0) sb.AppendLine(Dates.Text).AppendLine();
        sb.AppendLine(QuestionsHeading.Text);
        foreach (var (q, i) in _review.Questions.Select((q, i) => (q, i)))
            sb.AppendLine($"{i + 1}. {q.Question}").AppendLine($"   Why it matters: {q.Why}").AppendLine(q.Ref is { } r ? $"   From {r}" : "");
        sb.AppendLine().AppendLine("What it is").AppendLine(_review.Summary);
        Clipboard.SetText(sb.ToString());
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
