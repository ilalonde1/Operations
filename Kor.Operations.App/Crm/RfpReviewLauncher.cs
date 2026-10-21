#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Prompts;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Opens Claude's RFP review of a pursuit in Claude Code on this PC: by itself when a grabbed tender's documents arrive, or when
/// a person asks from My Pursuits. The BD Director's method (rule 14): the prompt is composed from live data, the session reads
/// the files from the database through the BD CLI and reports back with a one-time token; nothing here calls a model. Only a
/// PC with the repo and Claude Code can run it; every other PC sees the progress and the review on the pursuit.
/// </summary>
public static class RfpReviewLauncher
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(2);
    private static DispatcherTimer? _timer;
    private static int _busy;

    /// <summary>Raised on the UI thread when a review opens: the pursuit id and its project name.</summary>
    public static event Action<long, string>? Opened;

    /// <summary>The KOR Claude account (Ian, 21 Oct 2026): a review runs only in Claude Code signed in with it, never a personal one.</summary>
    public const string KorClaudeAccount = "ilalonde@korstructural.com";

    /// <summary>The repo (the BD CLI runs from it), Claude Code on PATH, and Claude Code signed in with the KOR account.</summary>
    public static bool CanRunHere => Directory.Exists(PromptComposer.RepoPath) && ClaudeOnPath()
        && string.Equals(ClaudeAccount(), KorClaudeAccount, StringComparison.OrdinalIgnoreCase);

    /// <summary>The account Claude Code on this PC is signed in with (oauthAccount.emailAddress in ~/.claude.json), or null.</summary>
    public static string? ClaudeAccount()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            if (!File.Exists(path)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("oauthAccount", out var account) && account.TryGetProperty("emailAddress", out var email)
                ? email.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool ClaudeOnPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => new[] { "claude.cmd", "claude.exe" }.Any(name => File.Exists(Path.Combine(dir.Trim(), name))));

    /// <summary>Starts watching for pursuits whose documents are in and that have no review yet. Does nothing on a PC that cannot run Claude.</summary>
    public static void StartWatching(SqlPursuitReviewStore reviews, IBdDoctrineStore runs, Func<string> actor)
    {
        if (_timer is not null) return;
        if (!CanRunHere)
        {
            Log.Information("RFP review: not on this PC (needs the repo and Claude Code signed in as {Account}; Claude Code here: {Here}).",
                KorClaudeAccount, ClaudeAccount() ?? "not signed in or not installed");
            return;
        }

        _timer = new DispatcherTimer { Interval = Every };
        _timer.Tick += async (_, _) => await OpenNextDueAsync(reviews, runs, actor()).ConfigureAwait(true);
        _timer.Start();
        Log.Information("RFP review: watching for grabbed tenders whose documents have arrived, every {Minutes} min.", Every.TotalMinutes);
    }

    /// <summary>One due pursuit per tick, so a backlog opens one terminal at a time rather than a stack of them.</summary>
    private static async Task OpenNextDueAsync(SqlPursuitReviewStore reviews, IBdDoctrineStore runs, string actor)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var due = await reviews.ListDueAsync(CancellationToken.None).ConfigureAwait(true);
            if (due.Count > 0) await OpenAsync(reviews, runs, due[0], actor, onlyIfNone: true, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RFP review: checking for due reviews failed");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// Records the run, composes the prompt and opens Claude Code on it. With <paramref name="onlyIfNone"/> the run row is the
    /// claim, so a pursuit another PC already started is left alone (returns false).
    /// </summary>
    public static async Task<bool> OpenAsync(SqlPursuitReviewStore reviews, IBdDoctrineStore runs, long engagementId, string actor, bool onlyIfNone, CancellationToken ct)
    {
        if (!CanRunHere)
            throw new InvalidOperationException($"Claude's RFP review runs on a PC with Claude Code signed in as {KorClaudeAccount}. "
                + $"Claude Code here: {ClaudeAccount() ?? "not signed in or not installed"}.");
        var now = DateTimeOffset.UtcNow;
        var input = await reviews.LoadInputAsync(engagementId, actor, now, ct).ConfigureAwait(true)
            ?? throw new InvalidOperationException($"Pursuit {engagementId} does not exist.");
        var (token, tokenSha) = BdDoctrine.NewToken();
        var runId = await reviews.StartRunAsync(engagementId, PursuitRfpReview.Subject(engagementId, input.Project), actor, tokenSha, onlyIfNone, ct).ConfigureAwait(true);
        if (runId is null) return false;

        var markdown = PursuitRfpReview.Prompt(input, new BdPromptHandle(runId.Value, token));
        await runs.SetPromptShaAsync(runId.Value, BdDoctrine.Sha(markdown), ct).ConfigureAwait(true);
        // One-shot: Claude reads, reports back and exits, and the terminal closes itself (Ian, 21 Oct: "can it close automatically").
        PromptLauncher.RunInClaude(PromptLauncher.Save($"rfp-review-{runId.Value}.md", markdown), $"RFP review #{engagementId}");
        Log.Information("RFP review: opened run {RunId} for pursuit {EngagementId} ({Files} files)", runId.Value, engagementId, input.Files.Count);
        Opened?.Invoke(engagementId, input.Project);
        return true;
    }
}
