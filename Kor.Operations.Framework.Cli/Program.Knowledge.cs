using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// K4 (capture-start, capture-check, record, memory) and K5b (issue, check, resolve) - Framework
/// docs/KNOWLEDGE-IDE-SPEC-K4-K7.md and KNOWLEDGE-IDE-SPEC-K5-K6.md. Every record write goes through RecordStore; every
/// write of a record also writes a 'record' event, which is the K4 marker capture-check measures from.
/// </summary>
public static partial class Program
{
    public static readonly string[] RecordKinds = { "finding", "decision", "dead-end", "lesson" };

    // ---- K4: capture-start / capture-check ---------------------------------------------------------------------------
    private static int CaptureStart(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-start reads the SessionStart JSON on stdin"));
        var e = SessionCapture.Start(h, o.Get("--module"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} session-start {e.Session} {e.Ref}");
        return 0;
    }

    /// <summary>
    /// Stop hook. stop_hook_active -> allow before ANY database call. Otherwise one unit of work: read the marker and the
    /// commits, write the capture-ask, commit; only then print the block. Any failure allows (hook mode exits 0 silent).
    /// </summary>
    private static int CaptureCheck(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-check reads the Stop JSON on stdin"));
        if (h.StopHookActive)
        {
            if (!hook) output.WriteLine("allow: stop_hook_active - no database call");
            return 0;
        }
        var d = BrainDb.Use(db, x => SessionCapture.Check(x, h, o.Get("--module")));
        if (d.Block) output.WriteLine(hook ? SessionCapture.BlockJson(d.Reason!) : $"block (capture-ask brain.Event {d.AskEventId}): {d.Reason}");
        else if (!hook) output.WriteLine($"allow: {d.Why}");
        return 0;
    }

    // ---- K4: kor record / kor memory --------------------------------------------------------------------------------
    /// <summary>
    /// kor record --module M --kind finding|decision|dead-end|lesson --name N --says D --source git:path[@rev] | run:&lt;event id&gt;
    /// | query:[brain:|board:]SELECT ... [--session S] [--body-file F] [--dir D]. The same live name is superseded. Writes a
    /// 'record' event (the K4 marker) in the same unit of work. Issues and resolutions are refused: they need a check.
    /// </summary>
    private static int Record(Options o, TextWriter output, BrainDb? db)
    {
        var lane = o.Get("--module") ?? throw new ArgumentException("record needs --module <lane>");
        var kind = o.Get("--kind") ?? throw new ArgumentException("record needs --kind finding|decision|dead-end|lesson");
        if (kind is "issue" or "resolution")
            throw new RecordRefusedException($"kind '{kind}' is not written by kor record: an issue is written by `kor issue` " +
                                             "(it needs its check: --check test|gate|query|source --check-ref ...), and closed only by evidence (`kor check`, `kor resolve`)");
        if (!RecordKinds.Contains(kind)) throw new ArgumentException($"record kind '{kind}' is not finding|decision|dead-end|lesson");
        var name = o.Get("--name") ?? throw new ArgumentException("record needs --name <slug>");
        var says = o.Get("--says") ?? throw new ArgumentException("record needs --says \"...\"");
        var body = o.Get("--body-file") is { } f ? File.ReadAllText(f) : says;
        var src = o.Get("--source") ?? throw new ArgumentException("record needs --source git:path[@rev] | run:<event id> | query:SELECT ...");
        var session = o.Get("--session") ?? BrainStore.CurrentSession();
        var by = Who(session);
        var where = o.Get("--dir") ?? Environment.CurrentDirectory;
        return BrainDb.Use(db, d =>
        {
            var s = SourceOf(d, src, where);
            var rec = new NewRecord(lane, name, kind, says, body, by, s.Git, s.Kind, s.Ref, s.Hash, SourceCommit: s.Commit);
            var live = RecordStore.LiveByName(d, lane, name);
            var id = live is null ? RecordStore.Insert(d, rec) : RecordStore.Supersede(d, live.Id, rec, by, "restated by kor record");
            var ev = RecordEvent(d, lane, id, $"{kind} {name}", session, by);
            output.WriteLine($"recorded brain.Memory #{id}: {lane} {kind} {name} - {Describe(rec)}" +
                             (live is null ? "" : $" (supersedes #{live.Id})") + $"; record event #{ev}");
            return 0;
        });
    }

    private sealed record Src(GitSource? Git, string? Kind, string? Ref, string? Hash, string? Commit);

    /// <summary>git:path[@rev] (verified, refused if untracked at that commit or dirty at HEAD) | run:&lt;id&gt; | query:...</summary>
    private static Src SourceOf(BrainDb db, string src, string where)
    {
        if (src.StartsWith("git:", StringComparison.Ordinal))
        {
            var (path, rev) = SplitGitSource(src[4..], TrackedIn(where));
            var g = RecordStore.At(where, path, rev);
            var head = RecordStore.Lower(Git.Run(g.RepoDir, "rev-parse", "HEAD")?.Trim());
            if (g.Commit == head && !string.IsNullOrWhiteSpace(Git.Run(g.RepoDir, "status", "--porcelain", "--", g.Path)))
                throw new RecordRefusedException($"{g.Path} has uncommitted changes - commit it, then cite the commit");
            return new Src(g, null, null, null, null);
        }
        if (src.StartsWith("run:", StringComparison.Ordinal))
        {
            if (!int.TryParse(src[4..].TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var ev))
                throw new RecordRefusedException("a run source is run:<brain.Event id>");
            var e = BrainStore.EventById(ev, db) ?? throw new RecordRefusedException($"run:{ev} - there is no brain.Event #{ev}");
            return new Src(null, "run", $"brain.Event #{ev} ({e.Kind} {e.Outcome})", null, e.Commit);
        }
        if (src.StartsWith("query:", StringComparison.Ordinal))
        {
            var text = src[6..].Trim();
            var (login, sql) = IssueCheck.ParseQueryRef(text.StartsWith("brain:", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("board:", StringComparison.OrdinalIgnoreCase) ? text : "brain:" + text);
            if (sql is null || sql.Contains(';')) throw new RecordRefusedException("a query source is ONE SELECT or WITH statement");
            if (text.Length > 400) throw new RecordRefusedException("a query source is at most 400 characters (it is stored as the record's SourceRef)");
            return new Src(null, "query", text, QueryHash(login, sql), null);
        }
        throw new RecordRefusedException($"source '{src}' is not git:path[@rev], run:<event id> or query:SELECT ...");
    }

    /// <summary>Runs the query in a transaction that is rolled back (10 s) and hashes its rows (SHA-256, lowercase hex).</summary>
    internal static string QueryHash(KorStandardsConnection.Login login, string sql)
    {
        using var q = BrainDb.OpenRolledBack(login);
        using var c = q.Command(sql, 10);
        using var r = c.ExecuteReader();
        var sb = new StringBuilder();
        while (r.Read())
        {
            for (var i = 0; i < r.FieldCount; i++)
                sb.Append(r.IsDBNull(i) ? "␀" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)).Append('\u001f');
            sb.Append('\u001e');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>The 'record' event: the K4 marker (capture-check asks only about commits after it).</summary>
    private static int RecordEvent(BrainDb db, string lane, int memoryId, string what, string? session, string by) =>
        BrainStore.Insert(new BrainEvent(lane, "record", null, null, BrainStore.Clip($"brain.Memory #{memoryId} {what}", 400), "recorded",
                                         JsonSerializer.Serialize(new { memory = memoryId }), by, session), db);

    private static string Who(string? session) =>
        session is null ? RecordStore.Actor() : BrainStore.Clip($"kor ({Environment.UserName}) session {session}", 100)!;

    private static string Describe(NewRecord r) => r.Git is { } g
        ? $"{g.Repo}:{g.Path}@{g.Commit[..7]} blob {g.Blob[..7]}"
        : $"{r.SourceKind}: {r.SourceRef}" + (r.SourceHash is null ? "" : $" sha256 {r.SourceHash[..12]}");

    /// <summary>kor memory --module M [--last N]: the lane's live records, newest first, each with its source.</summary>
    private static int Memory(Options o, TextWriter output, BrainDb? db)
    {
        var lane = o.Get("--module") ?? throw new ArgumentException("memory needs --module <lane>");
        var rows = BrainDb.Use(db, d => RecordStore.Recent(d, lane, o.Int("--last") ?? 20));
        output.WriteLine($"{rows.Count} live record(s) in {lane}, newest first");
        foreach (var r in rows)
        {
            var src = r.SourceKind switch
            {
                "git" => $"source=git {r.SourceRepo}:{r.SourcePath} commit={r.SourceCommit} blob={r.SourceBlob}",
                null => "source=none (imported)",
                _ => $"source={r.SourceKind} {r.SourceRef}" + (r.SourceCommit is null ? "" : $" commit={r.SourceCommit}"),
            };
            var check = r.CheckKind is null ? "" : $" check={r.CheckKind}:{r.CheckRef}";
            output.WriteLine($"#{r.Id} {r.Kind} {r.Name} [{r.Status}]{check}");
            output.WriteLine($"   {src}");
            output.WriteLine($"   {r.Description}");
        }
        return 0;
    }

    // ---- K5b: kor issue / kor check / kor resolve --------------------------------------------------------------------
    /// <summary>
    /// kor issue --module M --name N --description D --check test|gate|query|source --check-ref R [--source-path P]
    /// [--body-file F] [--restates ID] [--session S] [--dir D].
    /// </summary>
    private static int Issue(Options o, TextWriter output, BrainDb? db)
    {
        var lane = o.Get("--module") ?? throw new ArgumentException("issue needs --module <lane>");
        var name = o.Get("--name") ?? throw new ArgumentException("issue needs --name <slug>");
        var description = o.Get("--description") ?? throw new ArgumentException("issue needs --description \"...\"");
        var check = o.Get("--check") ?? throw new RecordRefusedException("an issue carries its check: --check test|gate|query|source");
        var checkRef = o.Get("--check-ref") ?? throw new RecordRefusedException("an issue carries its check: --check-ref <what proves it fixed>");
        var body = o.Get("--body-file") is { } f ? File.ReadAllText(f) : description;
        var session = o.Get("--session") ?? BrainStore.CurrentSession();
        var by = Who(session);
        var where = o.Get("--dir") ?? Environment.CurrentDirectory;
        GitSource? git = null;
        if (o.Get("--source-path") is { } p)
        {
            git = RecordStore.AtHead(where, p);
            if (!string.IsNullOrWhiteSpace(Git.Run(git.RepoDir, "status", "--porcelain", "--", git.Path)))
                throw new RecordRefusedException($"{git.Path} has uncommitted changes - commit it, then cite the commit");
        }
        var issue = new NewRecord(lane, name, "issue", description, body, by, git,
            git is null ? "session" : null, git is null ? BrainStore.Clip($"kor issue, {(session is null ? by : "session " + session)}", 400) : null,
            CheckKind: check, CheckRef: checkRef);
        return BrainDb.Use(db, d =>
        {
            var id = o.Int("--restates") is { } old ? RecordStore.RestateIssue(d, old, issue) : RecordStore.InsertIssue(d, issue);
            var ev = RecordEvent(d, lane, id, $"issue {name}", session, by);
            output.WriteLine($"recorded issue #{id}: {lane} {name} - check {check}: {checkRef}" +
                             (o.Int("--restates") is { } r ? $" (restates #{r})" : "") + $"; record event #{ev}");
            return 0;
        });
    }

    /// <summary>
    /// kor check --module M [--issue N] [--resolved] [--kinds test,gate,query] [--timeout S] [--hook]
    /// kor check --commit-hook [--dir D] [--hook]: the issues named "#issue N" in HEAD's message.
    /// Exit (without --hook): 0 when every check run passed, 1 when any did not.
    /// </summary>
    private static int Check(Options o, TextWriter output, bool hook, BrainDb? db)
    {
        var timeout = TimeSpan.FromSeconds(o.Int("--timeout") ?? 120);
        var where = o.Get("--dir") ?? Environment.CurrentDirectory;
        List<int> ids;
        if (o.Get("--commit-hook") is not null)
        {
            ids = IssuesNamedInHead(where);
            if (ids.Count == 0) { if (!hook) output.WriteLine("kor check: HEAD names no #issue"); return 0; }
        }
        else if (o.Int("--issue") is { } one) ids = new List<int> { one };
        else
        {
            var lane = o.Get("--module") ?? throw new ArgumentException("check needs --module <lane>, --issue N or --commit-hook");
            var kinds = (o.Get("--kinds") ?? "test,gate,query").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ids = BrainDb.Use(db, d => RecordStore.IssuesToCheck(d, lane, o.Get("--resolved") is not null, kinds).Select(i => i.Id).ToList());
            if (ids.Count == 0) { output.WriteLine($"kor check: {lane} has no issue to check"); return 0; }
        }
        var allPassed = true;
        foreach (var id in ids)
        {
            var issue = BrainDb.Use(db, d => RecordStore.Get(d, id));
            if (issue is null || issue.Kind != "issue") { output.WriteLine($"#{id}: not an issue"); allPassed = false; continue; }
            if (issue.CheckKind == "source") { output.WriteLine($"#{id} {issue.Name}: a source-checked issue - `kor resolve --issue {id}` once its file changed"); continue; }
            var r = IssueCheck.Run(issue, timeout, where);
            var session = o.Get("--session") ?? BrainStore.CurrentSession();
            var line = BrainDb.Use(db, d => IssueResolver.CheckAndRecord(d, issue, r, Who(session), session)).Line;
            output.WriteLine(line);
            if (r.Outcome != "pass") allPassed = false;
        }
        return hook || allPassed ? 0 : 1;
    }

    private static readonly Regex IssueTag = new(@"#issue\s+#?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The issue ids named "#issue N" in a commit message (any number of them, each once).</summary>
    public static List<int> IssueIds(string? message) =>
        message is null ? new List<int>() : IssueTag.Matches(message).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();

    private static List<int> IssuesNamedInHead(string dir) => IssueIds(Git.Run(dir, "log", "-1", "--format=%B", "HEAD"));

    /// <summary>kor resolve --issue N --because "..." [--session S] [--dir D]: a source-checked issue, by its changed file.</summary>
    private static int Resolve(Options o, TextWriter output, BrainDb? db)
    {
        var id = o.Int("--issue") ?? throw new ArgumentException("resolve needs --issue N");
        var because = o.Get("--because") ?? throw new ArgumentException("resolve needs --because \"...\"");
        var session = o.Get("--session") ?? BrainStore.CurrentSession();
        var by = Who(session);
        return BrainDb.Use(db, d =>
        {
            var issue = RecordStore.Get(d, id) ?? throw new RecordRefusedException($"no record #{id}");
            var res = RecordStore.ResolveBySource(d, id, by, because, o.Get("--dir"));
            var ev = RecordEvent(d, issue.Lane, res, $"resolution of #{id}", session, by);
            output.WriteLine($"#{id} {issue.Name} RESOLVED by #{res} (its file changed); record event #{ev}");
            return 0;
        });
    }

    /// <summary>post-commit: the issues named "#issue N" in the commit are checked now (capped), never failing the commit.</summary>
    internal static string? CommitHookCheck(string dir, BrainDb? db, TimeSpan cap)
    {
        if (IssuesNamedInHead(dir).Count == 0) return null;
        var output = new StringWriter();
        var o = Options.Parse(new[] { "check", "--commit-hook", "--dir", dir, "--timeout", ((int)cap.TotalSeconds).ToString(CultureInfo.InvariantCulture) });
        var run = Task.Run(() => Check(o, output, hook: true, db));
        return run.Wait(db is not null ? Timeout.InfiniteTimeSpan : cap + TimeSpan.FromSeconds(10))
            ? output.ToString().TrimEnd()
            : $"kor check --commit-hook: over {Secs(cap)} s - not waited for";
    }
}
