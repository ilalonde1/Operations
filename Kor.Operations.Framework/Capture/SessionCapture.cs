using System.Text.Json;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K4 - discoveries are captured (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md). NEITHER verb reads the reply's prose: the
/// old Stop guards misfired on English ("rows 1182-1189 ... closed", "render", "done-when"); this one compares integer
/// event ids only. The hook input's last_assistant_message and transcript are never read.
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
/// Does not cover: a discovery with no commit (asked at the next commit); the quality of what is recorded; an ask lost
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

    public sealed record Decision(bool Block, string? Reason, int? AskEventId, IReadOnlyList<StoredEvent> Commits, string Why);

    /// <param name="insertMarker">writes the capture-ask event (tests inject a failing one).</param>
    public static Decision Check(BrainDb db, ClaudeCapture.HookInput h, string? moduleOverride, Func<BrainEvent, int>? insertMarker = null)
    {
        if (h.StopHookActive) return new Decision(false, null, null, Array.Empty<StoredEvent>(), "stop_hook_active - allowed, no DB call");
        var session = BrainStore.Clip(h.SessionId, 64);
        if (session is null) return new Decision(false, null, null, Array.Empty<StoredEvent>(), "no session_id - allowed");
        var module = ClaudeCapture.ModuleOf(h, moduleOverride);
        var marker = BrainStore.NewestOfSession(session, MarkerKinds, db);
        var commits = BrainStore.CommitsToAsk(session, module, marker?.Id ?? 0, db)
            // with no marker at all, only this session's own commits are known to be new
            .Where(c => marker is not null || c.Session == session).ToList();
        if (commits.Count == 0)
            return new Decision(false, null, null, commits, $"no commit to ask about since marker #{marker?.Id.ToString() ?? "none"}");
        var ask = new BrainEvent(module, "capture-ask", null, commits[^1].Commit,
            BrainStore.Clip("asked about " + string.Join(", ", commits.Select(c => $"#{c.Id} {Short(c.Commit)}")), 400), "asked",
            JsonSerializer.Serialize(new { commit_events = commits.Select(c => c.Id), marker = marker?.Id }, GitCapture.Json),
            ClaudeCapture.ActorOf(h), session);
        int askId;
        try { askId = (insertMarker ?? (e => BrainStore.Insert(e, db)))(ask); }
        catch (Exception e)
        {
            return new Decision(false, null, null, commits, $"the capture-ask marker could not be written ({e.Message}) - allowed, never blocked without it");
        }
        return new Decision(true, Reason(commits, session), askId, commits, $"{commits.Count} commit(s) since marker #{marker?.Id.ToString() ?? "none"}");
    }

    /// <summary>The block reason: each commit (sha10, module, subject, paths) and the exact command to record what it taught.</summary>
    public static string Reason(IReadOnlyList<StoredEvent> commits, string session)
    {
        var lines = new List<string>
        {
            $"kor capture-check: {commits.Count} commit(s) since you last recorded - record what each one taught you before you stop (asked once).",
        };
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
