using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K4 - discoveries are captured (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md), and since board row 1393 (3 Nov) not only
/// after a commit: the Stop check reads the ENDING TURN off transcript_path (<see cref="TurnCapture"/>, whose summary is
/// the rule-11 header for what a turn asks) and asks ONCE per turn that produced something unrecorded. The commit ask
/// below is unchanged and merged into the same single ask. (Until row 1393 neither verb read prose - the old Stop guards
/// had misfired on English; the turn rules were measured instead: a 30-day dry run with every ask judged by name,
/// `kor capture-check --replay`.)
/// ONCE PER TURN, never a loop: stop_hook_active allows with no DB call; and a capture-ask of this session written at or
/// after the turn's prompt (minus <see cref="ClockSlack"/>) allows whatever else holds - Stop-hook feedback is not a new
/// turn. A marker that cannot be written allows. A transcript that cannot be read leaves only the commit ask.
/// Background runs (row 1393 part 1) whose task-notification starts the ending turn are recorded here as events, once per
/// task (their Detail carries "task"), before anything is decided.
/// <list type="bullet">
/// <item><see cref="Start"/> (SessionStart): a 'session-start' event - Session, Repo/Commit = HEAD of cwd, Ref = source.
/// It is the session's first MARKER.</item>
/// <item><see cref="Check"/> (Stop): W = this session's newest marker (session-start, capture-ask or record, in ANY module).
/// C = the commit events after W that this session must be asked about: commits whose Session is this session in ANY
/// module (a git hook inherits CLAUDE_CODE_SESSION_ID - so a Framework session committing in Operations IS asked, the
/// spec's same-class miss closed), plus commits in this session's module with no session (a human's). None -> allow.
/// Else a 'capture-ask' event is written FIRST (it is the new marker: once per commit); if that write fails -> allow
/// (never block without the marker, or the session would be blocked forever). Then block, naming each commit and the
/// exact `kor record` command.</item>
/// </list>
/// Does not cover: a discovery with no commit that none of the turn rules recognises; the quality of what is recorded; an ask lost
/// to Claude Code's cap on Stop continuations; a session killed between commit and Stop; a commit made outside any
/// Claude session in ANOTHER module (it is asked of that module's next session only). Same-class miss: a git commit run
/// by a tool that scrubs the environment (no CLAUDE_CODE_SESSION_ID) in another repo is not tied to the session.
/// </summary>
public static class SessionCapture
{
    public static readonly string[] MarkerKinds = { "session-start", "capture-ask", "record" };

    public static BrainEvent Start(ClaudeCapture.HookInput h, string? moduleOverride)
    {
        var module = ClaudeCapture.ModuleOf(h, moduleOverride);
        string? repo = null, commit = null;
        if (!string.IsNullOrWhiteSpace(h.Cwd) && Directory.Exists(h.Cwd))
        {
            var lines = Git.Run(h.Cwd, TimeSpan.FromSeconds(3), "rev-parse", "--show-toplevel", "HEAD")?.Split('\n');
            if (lines is { Length: >= 2 }) { repo = Git.Leaf(lines[0].Trim()); commit = lines[1].Trim().ToLowerInvariant(); }
        }
        var detail = JsonSerializer.Serialize(new { source = h.Source, cwd = h.Cwd, transcript = h.TranscriptPath }, GitCapture.Json);
        return new BrainEvent(module, "session-start", repo, commit, h.Source ?? "unknown", null, detail,
                              ClaudeCapture.ActorOf(h), h.SessionId);
    }

    public sealed record Decision(bool Block, string? Reason, int? AskEventId, IReadOnlyList<StoredEvent> Commits, string Why,
                                  IReadOnlyList<TurnCapture.Reason>? TurnReasons = null, IReadOnlyList<int>? BackgroundEvents = null);

    /// <summary>Row 1393: transcript time vs brain.Event's AtUtc (the DB server's clock). Measured 3 Nov 16:23: under 1 s
    /// apart (3 of 3 reads), so 10 s covers it without reaching back into the previous turn of a normal session.</summary>
    public static readonly TimeSpan ClockSlack = TimeSpan.FromSeconds(10);

    /// <summary>Row 1393: on a continuation Stop, a capture-ask of this session this recent allows, whatever turn was read.</summary>
    public static readonly TimeSpan ContinuationWindow = TimeSpan.FromMinutes(10);

    private static readonly Regex TaskId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    /// <param name="insertMarker">writes the capture-ask event (tests inject a failing one).</param>
    /// <param name="readTurn">reads the turn that is ending from transcript_path (tests inject one); null = the transcript.</param>
    /// <param name="findLaunches">finds a background run's launch by tool_use id (tests inject); null = the transcript.</param>
    public static Decision Check(BrainDb db, ClaudeCapture.HookInput h, string? moduleOverride, Func<BrainEvent, int>? insertMarker = null,
                                 Func<string?, TurnCapture.Turn?>? readTurn = null,
                                 Func<IReadOnlyCollection<string>, Dictionary<string, TurnCapture.ToolCall>>? findLaunches = null,
                                 Func<string, string?>? readFile = null)
    {
        // A continuation Stop (stop_hook_active: some Stop hook blocked this turn already) is judged too - the work done in a
        // continuation after ANOTHER hook's block was never asked before (row 1393 dry run: 8 real failures, a migration
        // among them, were in such continuations). It can never loop: below, any capture-ask of this session in the turn -
        // or, on a continuation, in the last ContinuationWindow - allows at once. With no transcript to read, a continuation
        // is allowed with no DB call, as before row 1393.
        if (h.StopHookActive && string.IsNullOrWhiteSpace(h.TranscriptPath))
            return new Decision(false, null, null, Array.Empty<StoredEvent>(), "stop_hook_active - allowed, no DB call");
        var session = BrainStore.Clip(h.SessionId, 64);
        if (session is null) return new Decision(false, null, null, Array.Empty<StoredEvent>(), "no session_id - allowed");
        var module = ClaudeCapture.ModuleOf(h, moduleOverride);

        // ---- row 1393: the turn that is ending (a transcript that cannot be read leaves only the commit ask) ----
        TurnCapture.Turn? turn = null;
        try { turn = (readTurn ?? (p => TurnCapture.LastTurn(p)))(h.TranscriptPath); } catch (Exception) { turn = null; }
        if (turn is not null && h.LastAssistantMessage is { Length: > 0 } last) turn = turn with { FinalReply = last };
        if (h.StopHookActive && turn?.Start.At is null)
            return new Decision(false, null, null, Array.Empty<StoredEvent>(), "stop_hook_active and no turn read - allowed");
        var since = turn?.Start.At is { } at ? at - ClockSlack : (DateTimeOffset?)null;
        // background runs whose result arrived in this turn: recorded now (part 1), once per task
        var bgRuns = new List<TurnCapture.Run>();
        var bgEvents = new List<int>();
        if (turn is { Notifications.Count: > 0 })
        {
            var known = turn.Tools.ToDictionary(t => t.Id, t => t);
            var missing = turn.Notifications.Select(n => n.ToolUseId).OfType<string>().Where(id => !known.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                foreach (var (id, call) in (findLaunches ?? (ids => TurnCapture.FindLaunches(h.TranscriptPath!, ids)))(missing)) known[id] = call;
            bgRuns = TurnCapture.BackgroundRuns(turn, known, readFile);
            foreach (var r in bgRuns)
            {
                if (r.Note is null || !TaskId.IsMatch(r.Note.TaskId) || BrainStore.BackgroundRunRecorded(session, r.Note.TaskId, db)) continue;
                if (BackgroundEvent(h, r, moduleOverride) is { } e) bgEvents.Add(BrainStore.Insert(e, db));
            }
        }
        // asked already in this turn (the once-per-turn marker): never twice, whatever else holds. On a continuation the window
        // also reaches back ContinuationWindow, so even a turn misread as new can be asked at most once in it.
        var askedSince = since is { } s && h.StopHookActive && DateTimeOffset.UtcNow - ContinuationWindow < s ? DateTimeOffset.UtcNow - ContinuationWindow : since;
        if (askedSince is { } s0 && BrainStore.SessionEventsSince(session, new[] { "capture-ask" }, s0, db) is { Count: > 0 } asked)
            return new Decision(false, null, null, Array.Empty<StoredEvent>(), $"already asked in this turn (capture-ask #{asked[^1].Id}) - allowed",
                                null, bgEvents);
        var reasons = turn is null ? new List<TurnCapture.Reason>() : TurnCapture.Evaluate(turn, TurnCapture.ForegroundRuns(turn).Concat(bgRuns).ToList());
        var recorded = since is { } s1 && reasons.Count > 0 && BrainStore.SessionEventsSince(session, new[] { "record" }, s1, db).Count > 0;
        if (recorded) reasons.Clear();
        var runEvents = since is { } s2 && reasons.Count > 0
            ? BrainStore.SessionEventsSince(session, new[] { "test", "build", "migration", "gate" }, s2, db) : Array.Empty<StoredEvent>();

        var marker = BrainStore.NewestOfSession(session, MarkerKinds, db);
        var commits = BrainStore.CommitsToAsk(session, module, marker?.Id ?? 0, db)
            // with no marker at all, only this session's own commits are known to be new
            .Where(c => marker is not null || c.Session == session).ToList();
        if (commits.Count == 0 && reasons.Count == 0)
            return new Decision(false, null, null, commits,
                $"no commit to ask about since marker #{marker?.Id.ToString() ?? "none"}" +
                (turn is null ? "" : recorded ? "; the turn recorded something" : "; the turn produced nothing to ask about"), reasons, bgEvents);
        var refText = commits.Count > 0 ? "asked about " + string.Join(", ", commits.Select(c => $"#{c.Id} {Short(c.Commit)}")) : "";
        if (reasons.Count > 0) refText = (refText.Length > 0 ? refText + "; " : "") + "turn: " + string.Join(", ", reasons.Select(r => r.Kind).Distinct());
        var ask = new BrainEvent(module, "capture-ask", null, commits.Count > 0 ? commits[^1].Commit : null, BrainStore.Clip(refText, 400), "asked",
            JsonSerializer.Serialize(new
            {
                commit_events = commits.Select(c => c.Id), marker = marker?.Id,
                // Ian's words and the reply's diagnosis, verbatim (credential values are masked by BrainStore.Fit): a
                // decision record cites THIS event as its source (run:<id>)
                turn = turn is null ? null : new
                {
                    prompt_line = turn.Start.Line, prompt_at = turn.Start.At, origin = turn.Start.Origin,
                    reasons = reasons.Select(r => new { kind = r.Kind, line = r.Line, text = r.Text }),
                    run_events = runEvents.Select(e => e.Id), background_events = bgEvents,
                },
            }, GitCapture.Json),
            ClaudeCapture.ActorOf(h), session);
        int askId;
        try { askId = (insertMarker ?? (e => BrainStore.Insert(e, db)))(ask); }
        catch (Exception e)
        {
            return new Decision(false, null, null, commits, $"the capture-ask marker could not be written ({e.Message}) - allowed, never blocked without it", reasons, bgEvents);
        }
        return new Decision(true, Reason(commits, session, reasons, runEvents, askId, module), askId, commits,
                            $"{commits.Count} commit(s) since marker #{marker?.Id.ToString() ?? "none"}, {reasons.Count} turn reason(s)", reasons, bgEvents);
    }

    /// <summary>A background run's event: the K3 event for its command and output, plus the task it came from.</summary>
    private static BrainEvent? BackgroundEvent(ClaudeCapture.HookInput h, TurnCapture.Run r, string? moduleOverride)
    {
        var exit = r.Note!.ExitCode ?? (r.Note.Status == "completed" ? 0 : 1);
        var synthetic = h with
        {
            ToolName = r.Call.Name, Command = r.Call.Command, Cwd = r.Call.Cwd ?? h.Cwd,
            // Tool() is used for WHERE the run is cited (source, repo, commit, module) - kind comes from the command alone;
            // the outcome and the output's lines are the classifier's reading of the full output (r.Event), copied below
            Output = "", Error = exit != 0 ? $"Exit code {exit}" : null, BackgroundTaskId = null,
        };
        var e = ClaudeCapture.Tool(synthetic, moduleOverride);
        if (e is null) return null;
        var d = System.Text.Json.Nodes.JsonNode.Parse(e.Detail ?? "{}")!.AsObject();
        var full = System.Text.Json.Nodes.JsonNode.Parse(r.Event.Detail)!.AsObject();
        foreach (var k in full.Select(p => p.Key).ToList()) d[k] = full[k]!.DeepClone();
        d["task"] = r.Note.TaskId;
        d["background"] = true;
        d["notification_status"] = r.Note.Status;
        d["exit_code"] = exit;
        return e with { Outcome = r.Event.Outcome, Ref = r.Event.Ref, Detail = d.ToJsonString(GitCapture.Json) };
    }

    /// <summary>The block reason: each commit (sha10, module, subject, paths) and the exact command to record what it taught.</summary>
    public static string Reason(IReadOnlyList<StoredEvent> commits, string session) =>
        Reason(commits, session, Array.Empty<TurnCapture.Reason>(), Array.Empty<StoredEvent>(), null, null);

    /// <summary>The ONE ask (row 1393): what the turn produced (failed runs, pipeline outcomes, Ian's decision quoted, the
    /// reply's diagnosis), then the commits as before, then the exact commands.</summary>
    public static string Reason(IReadOnlyList<StoredEvent> commits, string session, IReadOnlyList<TurnCapture.Reason> turn,
                                IReadOnlyList<StoredEvent> runEvents, int? askId, string? module)
    {
        var lines = new List<string>();
        if (turn.Count > 0)
        {
            lines.Add("kor capture-check: this turn produced something the brain does not have - record what it found (kor record ...), " +
                      "or say in one line why there is nothing, then stop (asked once per turn; stopping again is allowed).");
            foreach (var r0 in turn)
            {
                // a credential value in his words or the reply is masked here as in the stored event (mistake 138)
                var r = r0 with { Text = SecretGuard.Mask(r0.Text) ?? "" };
                lines.Add(r.Kind switch
                {
                    "failed-run" => $"- a failed run: {r.Text}",
                    "run-outcome" => $"- a pipeline outcome: {r.Text}",
                    "ian-decision" => $"- Ian decided: \"{r.Text}\"",
                    "diagnosis" => $"- your reply diagnoses: \"{r.Text}\"",
                    _ => $"- {r.Kind}: {r.Text}",
                });
            }
            if (runEvents.Count > 0)
                lines.Add("  recorded runs this turn (cite one as --source run:<id>): " +
                          string.Join(", ", runEvents.Take(12).Select(e => $"#{e.Id} {e.Kind} {e.Outcome ?? "-"} {BrainStore.Clip(e.Ref, 60) ?? ""}")));
            var m = module ?? "<module>";
            lines.Add($"  C:/KOR-Operations/kor/kor.exe record --session {session} --module \"{m}\" --kind finding|dead-end|lesson " +
                      "--name <short-slug> --says \"<what it found>\" --source run:<event id>|git:<path>@<sha>|query:SELECT ...");
            if (turn.Any(r => r.Kind == "ian-decision"))
                lines.Add($"  Ian's decision, his words verbatim: C:/KOR-Operations/kor/kor.exe record --session {session} --module \"{m}\" " +
                          $"--kind decision --name <short-slug> --says \"Ian: <his words>\" --source run:{askId?.ToString(CultureInfo.InvariantCulture) ?? "<this ask's event id>"}" +
                          " (that event holds his words as he wrote them)");
        }
        if (commits.Count == 0) return string.Join("\n", lines);
        lines.Add($"kor capture-check: {commits.Count} commit(s) since you last recorded - record what each one taught you before you stop (asked once).");
        foreach (var c in commits)
        {
            var (subject, paths) = SubjectAndPaths(c);
            lines.Add($"- {Short(c.Commit, 10)} [{c.Module}] {subject}");
            if (paths.Count > 0) lines.Add($"    paths: {string.Join(", ", paths.Take(8))}{(paths.Count > 8 ? $" ... ({paths.Count})" : "")}");
        }
        var first = commits[^1];
        var path = SubjectAndPaths(first).Paths.FirstOrDefault() ?? "<path>";
        lines.Add("Record each finding, decision, dead end or lesson (the source is the file at that commit):");
        lines.Add($"  C:/KOR-Operations/kor/kor.exe record --session {session} --module {first.Module} --kind finding|decision|dead-end|lesson " +
                  $"--name <short-slug> --says \"<what it taught>\" --source git:{path}@{Short(first.Commit, 10)} --dir <the {first.Repo ?? first.Module} checkout>");
        lines.Add("An issue is written with `kor issue` (it needs its check). If the commit taught nothing new, stopping again is allowed.");
        return string.Join("\n", lines);
    }

    private static (string Subject, List<string> Paths) SubjectAndPaths(StoredEvent c)
    {
        string subject = "", paths = c.Ref ?? "";
        try
        {
            if (c.Detail is not null)
            {
                using var d = JsonDocument.Parse(c.Detail);
                if (d.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    subject = (m.GetString() ?? "").Split('\n')[0];
                if (d.RootElement.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array)
                    return (Clip(subject), f.EnumerateArray().Select(x => x.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "")
                                             .Where(p => p.Length > 0).ToList());
            }
        }
        catch (JsonException) { /* an old or foreign detail: fall back to Ref */ }
        return (Clip(subject), paths.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    private static string Clip(string s) => s.Length <= 120 ? s : s[..117] + "...";

    private static string Short(string? sha, int n = 7) => sha is null ? "-" : sha.Length > n ? sha[..n] : sha;

    /// <summary>The Stop hook's block JSON.</summary>
    public static string BlockJson(string reason) =>
        JsonSerializer.Serialize(new { decision = "block", reason }, GitCapture.Json);
}
