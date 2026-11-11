using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K4 - discoveries are captured (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md, the spec's 13 tests and more). LIVE on
/// KorStandards as korbrain_app, each test in its own <see cref="BrainDb.OpenRolledBack"/>; module zz-test-k4-&lt;time&gt;,
/// a session id of its own per test.
/// Covers: capture-check allows with no commit, blocks once per commit naming the sha and the command, never touches the
/// DB under stop_hook_active, ignores the reply text, ignores another session's commit and another module's sessionless
/// commit, asks about THIS session's commit in another module (the spec's same-class miss, closed), allows when the
/// marker cannot be written; kor record's git/run/query sources and refusals; same name supersedes; a record moves the
/// marker; capture-start and the K3 verbs carry Session.
/// Does NOT cover: the Stop hook's wiring in settings.json and Claude Code's own continuation (the live proof in Framework
/// does); a git hook run with a scrubbed environment (no CLAUDE_CODE_SESSION_ID).
/// </summary>
[Collection(LiveDb.Name)]
public class K4Tests
{
    private static readonly string Module = $"zz-test-k4-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    private static string NewSession() => "zz-k4-" + Guid.NewGuid().ToString("N");

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static string FrameworkDir() => RepoLocator.Find("Framework", RepoDir())!;

    private static (int Code, string Out, string Err) Run(BrainDb? db, string? stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, () => stdin, output, error, Path.Combine(Path.GetTempPath(), $"kor-k4-{Guid.NewGuid():N}.log"), db);
        return (code, output.ToString(), error.ToString());
    }

    private static string StartJson(string session) => JsonSerializer.Serialize(new
    {
        session_id = session, transcript_path = "C:/x/k4.jsonl", cwd = RepoDir(), hook_event_name = "SessionStart", source = "startup",
    });

    private static string StopJson(string session, bool active = false, string? reply = null) => JsonSerializer.Serialize(new
    {
        session_id = session, transcript_path = "C:/x/k4.jsonl", cwd = RepoDir(), hook_event_name = "Stop",
        stop_hook_active = active, last_assistant_message = reply ?? "done",
    });

    private static void Start(BrainDb db, string session, string? module = null)
    {
        var (code, _, e) = Run(db, StartJson(session), "capture-start", "--module", module ?? Module);
        Assert.True(code == 0, e);
    }

    /// <summary>A real post-commit event for this repo's HEAD, filed under a module and session of the test's choosing.</summary>
    private static int Commit(BrainDb db, string module, string? session)
    {
        var args = new List<string> { "capture-git", "post-commit", "--dir", RepoDir(), "--module", module };
        if (session is not null) { args.Add("--session"); args.Add(session); }
        var (code, o, e) = Run(db, null, args.ToArray());
        Assert.True(code == 0, e);
        var id = int.Parse(Regex.Match(o, @"brain\.Event (\d+)").Groups[1].Value);
        if (session is null)
        {
            // a commit outside any session: capture-git takes CLAUDE_CODE_SESSION_ID, which this test run inherits - so the
            // sessionless commit is written directly, the way a human's terminal commit lands
            var ev = BrainStore.EventById(id, db)!;
            return BrainStore.Insert(new BrainEvent(module, "commit", ev.Repo, ev.Commit, ev.Ref, null, ev.Detail, "a human", null), db);
        }
        return id;
    }

    private static string Check(BrainDb db, string session, bool hook = true, bool active = false, string? reply = null) =>
        Run(db, StopJson(session, active, reply), hook ? new[] { "capture-check", "--module", Module, "--hook" } : new[] { "capture-check", "--module", Module }).Out;

    private static string Head() => Git.Run(RepoDir(), "rev-parse", "HEAD")!.Trim();

    // 1
    [Fact]
    public void LiveNoCommitAllows()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Assert.Equal("", Check(db, s));
        Assert.StartsWith("allow: no commit to ask about", Check(db, s, hook: false));
        Assert.DoesNotContain(BrainStore.Last(Module, 50, "capture-ask", db), e => e.Session == s);
    }

    // 2
    [Fact]
    public void LiveACommitBlocksNamingTheShaAndTheRecordCommand()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Commit(db, Module, s);
        var o = Check(db, s);
        using var j = JsonDocument.Parse(o);
        Assert.Equal("block", j.RootElement.GetProperty("decision").GetString());
        var reason = j.RootElement.GetProperty("reason").GetString()!;
        Assert.Contains(Head()[..10], reason);
        Assert.Contains($"kor.exe record --session {s} --module {Module} --kind finding|decision|dead-end|lesson", reason);
        Assert.Single(BrainStore.Last(Module, 50, "capture-ask", db), e => e.Session == s);
    }

    // 3
    [Fact]
    public void LiveItAsksOncePerCommit()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Commit(db, Module, s);
        Assert.Contains("\"decision\":\"block\"", Check(db, s));
        Assert.Equal("", Check(db, s));                    // the capture-ask is the new marker
        Commit(db, Module, s);
        Assert.Contains("\"decision\":\"block\"", Check(db, s));   // a NEW commit asks again
    }

    // 4 - since row 1393 a continuation Stop IS judged when its turn can be read (TurnAskLiveTests); with no turn to read
    // (this transcript does not exist) it is still allowed without one database call
    [Fact]
    public void StopHookActiveTouchesNoDatabase()
    {
        var dead = BrainDb.OpenRolledBack();
        dead.Dispose();   // any database call on it throws - and without --hook a throw is exit 1
        var (code, o, e) = Run(dead, StopJson(NewSession(), active: true), "capture-check", "--module", Module);
        Assert.Equal((0, ""), (code, e));
        Assert.Equal("allow: stop_hook_active and no turn read - allowed", o.Trim());
    }

    // 5
    [Theory]
    [InlineData("rows 1182-1189 are closed")]
    [InlineData("I will render the drawing now")]
    [InlineData("done-when met: all green")]
    public void LiveTheReplyTextChangesNothing(string reply)
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Assert.Equal("", Check(db, s, reply: reply));                       // no commit: allowed whatever the reply says
        Commit(db, Module, s);
        var withReply = Check(db, s, reply: reply);
        Assert.Contains("\"decision\":\"block\"", withReply);               // a commit: blocked whatever the reply says
        Assert.DoesNotContain(reply, withReply);
    }

    // 6
    [Fact]
    public void LiveAnotherModulesSessionlessCommitAndAnotherSessionsCommitDoNotBlock()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Commit(db, Module + "-other", null);         // a human's commit in another module
        Commit(db, Module, NewSession());            // another Claude session's commit in this module: that session's to record
        Assert.Equal("", Check(db, s));
        Commit(db, Module, null);                    // a human's commit in THIS module: asked
        Assert.Contains("\"decision\":\"block\"", Check(db, s));
    }

    // 6b: the spec's same-class miss, CLOSED
    [Fact]
    public void LiveThisSessionsCommitInAnotherModuleIsAsked()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);                                // a Framework-like session (module = Module) ...
        Commit(db, Module + "-ops", s);              // ... that commits in another repo (Operations-like)
        var o = Check(db, s);
        Assert.Contains("\"decision\":\"block\"", o);
        Assert.Contains($"[{Module}-ops]", o);
        Assert.Contains($"--module {Module}-ops", o);
    }

    // 7
    [Fact]
    public void LiveAMarkerThatCannotBeWrittenAllows()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Commit(db, Module, s);
        var h = ClaudeCapture.Parse(StopJson(s));
        var d = SessionCapture.Check(db, h, Module, _ => throw new InvalidOperationException("db refused"));
        Assert.False(d.Block);
        Assert.Contains("could not be written", d.Why);
        Assert.Single(d.Commits);   // it saw the commit - and still did not block without its marker
    }

    // 8
    [Fact]
    public void LiveAGitSourceCarriesCommitPathAndBlob()
    {
        using var db = BrainDb.OpenRolledBack();
        var (code, o, e) = Run(db, null, "record", "--module", Module, "--session", NewSession(), "--kind", "finding",
                               "--name", "k4-git", "--says", "k4 git source", "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir());
        Assert.True(code == 0, e);
        var id = int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value);
        var r = RecordStore.Get(db, id)!;
        Assert.Equal(("git", "Framework", "docs/KNOWLEDGE-IDE.md"), (r.SourceKind, r.SourceRepo, r.SourcePath));
        Assert.Equal(Git.Run(FrameworkDir(), "rev-parse", "HEAD")!.Trim(), r.SourceCommit);
        Assert.Equal(Git.Run(FrameworkDir(), "rev-parse", $"{r.SourceCommit}:docs/KNOWLEDGE-IDE.md")!.Trim(), r.SourceBlob);
    }

    // 9
    [Fact]
    public void LiveAnUntrackedOrDirtyFileIsRefused()
    {
        using var repo = TempRepo.Create();
        using var db = BrainDb.OpenRolledBack();
        File.WriteAllText(Path.Combine(repo.Dir, "untracked.md"), "new");
        var (code, _, e) = Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-untracked", "--says", "x",
                               "--source", "git:untracked.md", "--dir", repo.Dir);
        Assert.Equal(1, code);
        Assert.Contains("is not tracked", e);
        File.AppendAllText(Path.Combine(repo.Dir, "a.md"), "\nchanged");
        (code, _, e) = Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-dirty", "--says", "x",
                           "--source", "git:a.md", "--dir", repo.Dir);
        Assert.Equal(1, code);
        Assert.Contains("uncommitted changes - commit it, then cite the commit", e);
        Assert.Null(RecordStore.LiveByName(db, Module, "k4-untracked"));
        Assert.Null(RecordStore.LiveByName(db, Module, "k4-dirty"));
    }

    // 10
    [Fact]
    public void LiveARunSourceMustBeAnExistingEvent()
    {
        using var db = BrainDb.OpenRolledBack();
        var (code, _, e) = Run(db, null, "record", "--module", Module, "--kind", "lesson", "--name", "k4-run-missing", "--says", "x", "--check", "row:1997",
                               "--source", "run:2000000000");
        Assert.Equal(1, code);
        Assert.Contains("there is no brain.Event #2000000000", e);
        var ev = BrainStore.Insert(new BrainEvent(Module, "test", "Operations", Head(), "dotnet test", "pass", null, "k4-test"), db);
        var (c2, o, e2) = Run(db, null, "record", "--module", Module, "--kind", "lesson", "--name", "k4-run", "--says", "x", "--check", "row:1997", "--source", $"run:{ev}");
        Assert.True(c2 == 0, e2);
        var r = RecordStore.Get(db, int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value))!;
        Assert.Equal(("run", $"brain.Event #{ev} (test pass)", Head()), (r.SourceKind, r.SourceRef, r.SourceCommit));
    }

    // 11
    [Fact]
    public void LiveAQuerySourceIsReadOnlyAndHashed()
    {
        using var db = BrainDb.OpenRolledBack();
        var (code, o, e) = Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-query", "--says", "x",
                               "--source", "query:SELECT 1 AS a, N'b' AS b");
        Assert.True(code == 0, e);
        var r = RecordStore.Get(db, int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value))!;
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1\u001fb\u001f\u001e"))).ToLowerInvariant();
        Assert.Equal(("query", "SELECT 1 AS a, N'b' AS b", expected), (r.SourceKind, r.SourceRef, r.SourceHash));
        foreach (var bad in new[] { "query:DELETE brain.Memory WHERE 1 = 0", "query:SELECT 1; DELETE brain.Memory WHERE 1 = 0", "query:UPDATE brain.Event SET Ref = Ref",
                                    // review row 165: a CTE that ends in a write, and SELECT ... INTO, start with WITH / SELECT
                                    "query:WITH x AS (SELECT Id FROM brain.Memory WHERE 1 = 0) UPDATE m SET Body = Body FROM brain.Memory m JOIN x ON x.Id = m.Id",
                                    "query:SELECT Id INTO #k4copy FROM brain.Memory WHERE 1 = 0",
                                    "query:WITH x AS (SELECT 1 AS a) INSERT brain.Event (Module) SELECT N'x' FROM x" })
        {
            var (c, _, err) = Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-bad-query", "--says", "x", "--source", bad);
            Assert.Equal(1, c);
            Assert.Contains("ONE SELECT or WITH statement", err);
        }
    }

    // 12
    [Fact]
    public void LiveTheSameNameSupersedes()
    {
        using var db = BrainDb.OpenRolledBack();
        string[] Args(string says) => new[] { "record", "--module", Module, "--kind", "decision", "--name", "k4-same", "--says", says,
                                              "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir() };
        var first = int.Parse(Regex.Match(Run(db, null, Args("first")).Out, @"brain\.Memory #(\d+)").Groups[1].Value);
        var (_, o, _) = Run(db, null, Args("second"));
        Assert.Contains($"(supersedes #{first})", o);
        var second = int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value);
        var old = RecordStore.Get(db, first)!;
        Assert.Equal(("superseded", (int?)second), (old.Status, old.SupersededBy));
        Assert.Equal("second", RecordStore.LiveByName(db, Module, "k4-same")!.Description);
    }

    // 13
    [Fact]
    public void LiveARecordMovesTheMarker()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        Commit(db, Module, s);
        var (code, o, e) = Run(db, null, "record", "--module", Module, "--session", s, "--kind", "lesson", "--name", "k4-marker",
                               "--says", "the commit taught this", "--check", "row:1997", "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir());
        Assert.True(code == 0, e);
        Assert.Contains("record event #", o);
        Assert.Equal("", Check(db, s));   // the record event is newer than the commit: nothing left to ask
    }

    // 13b - board row 1997, Office decision #131857: a lesson ships with its check, or with the board row that builds it.
    [Fact]
    public void LiveALessonWithoutItsCheckIsRefused()
    {
        using var db = BrainDb.OpenRolledBack();
        string[] Lesson(string name, params string[] check) => new[] { "record", "--module", Module, "--kind", "lesson", "--name", name,
            "--says", "x", "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir() }.Concat(check).ToArray();
        int Count() { using var c = db.Command("SELECT COUNT(*) FROM brain.Memory WHERE Lane = @m", 5); c.Parameters.AddWithValue("@m", Module); return (int)c.ExecuteScalar()!; }
        var before = Count();
        // refused, nothing written: no check at all; a board row that does not exist; a check id that is not live; a path that is not there
        foreach (var (check, says) in new[]
                 {
                     (Array.Empty<string>(), "a lesson ships with its check"),
                     (new[] { "--check", "row:2000000000" }, "there is no board row 2000000000"),
                     (new[] { "--check", "check:2000000000" }, "there is no live brain.Check #2000000000"),
                     (new[] { "--check", "tools/no_such_guard_k4.py" }, "is not a file"),
                 })
        {
            var (c, _, e) = Run(db, null, Lesson("k4-lesson-refused", check));
            Assert.Equal(1, c);
            Assert.Contains(says, e);
        }
        Assert.Equal(before, Count());
        // accepted: the board row that builds the check; a tracked hook file; each lands on the record, in Body and Description
        foreach (var (check, cites) in new[] { ("row:1997", "board row 1997"), ("board row 1997", "board row 1997"),
                                               ("tools/kor_review.py", "Framework:tools/kor_review.py"),
                                               (Path.Combine(FrameworkDir(), "tools", "kor_review.py"), "Framework:tools/kor_review.py") })
        {
            var (c, o, e) = Run(db, null, Lesson("k4-lesson-" + Count(), "--check", check));
            Assert.True(c == 0, e);
            var r = RecordStore.Get(db, int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value))!;
            Assert.Contains("CHECK: " + cites, r.Body);
            Assert.EndsWith($"[check: {cites}]", r.Description);
        }
        // a live brain.Check id is a check too
        using (var q = db.Command("SELECT TOP 1 Id FROM brain.vw_LiveCheck ORDER BY Id", 5))
        {
            var live = (int)q.ExecuteScalar()!;
            var (c, o, e) = Run(db, null, Lesson("k4-lesson-check-id", "--check", $"check:{live}"));
            Assert.True(c == 0, e);
            Assert.Contains($"CHECK: brain.Check #{live}", RecordStore.Get(db, int.Parse(Regex.Match(o, @"brain\.Memory #(\d+)").Groups[1].Value))!.Body);
        }
        // a finding is not a lesson: it records with no --check, as before
        var (fc, _, fe) = Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-finding-no-check", "--says", "x",
                              "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir());
        Assert.True(fc == 0, fe);
    }

    // 14
    [Theory]
    [InlineData("issue")]
    [InlineData("resolution")]
    public void IssueAndResolutionAreRefusedNamingKorIssue(string kind)
    {
        var (code, _, e) = Run(null, null, "record", "--module", Module, "--kind", kind, "--name", "x", "--says", "x", "--source", "git:a.md");
        Assert.Equal(1, code);
        Assert.Contains("`kor issue`", e);
    }

    // 15: capture-start and the K3 verbs carry the session
    [Fact]
    public void LiveCaptureStartAndTheK3VerbsCarryTheSession()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Start(db, s);
        var start = Assert.Single(BrainStore.Last(Module, 50, "session-start", db), x => x.Session == s);
        Assert.Equal(("startup", Git.Leaf(Git.TopLevel(RepoDir())), Head()), (start.Ref, start.Repo, start.Commit));
        var end = JsonSerializer.Serialize(new { session_id = s, transcript_path = "C:/x/k4.jsonl", cwd = RepoDir(), hook_event_name = "SessionEnd", reason = "other" });
        Assert.Equal(0, Run(db, end, "capture-end", "--module", Module).Code);
        Assert.Single(BrainStore.Last(Module, 50, "session-end", db), x => x.Session == s);
        var tool = JsonSerializer.Serialize(new
        {
            session_id = s, cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "dotnet build Kor.Operations.Framework" },
            tool_response = new { stdout = "Build succeeded.\n    0 Warning(s)\n    0 Error(s)", stderr = "" },
        });
        Assert.Equal(0, Run(db, tool, "capture-tool", "--module", Module).Code);
        Assert.Single(BrainStore.Last(Module, 50, "build", db), x => x.Session == s);
        var compact = JsonSerializer.Serialize(new { session_id = s, cwd = RepoDir(), hook_event_name = "PostCompact", trigger = "manual", compact_summary = "k4 resume" });
        var (_, co, _) = Run(db, compact, "capture-compact", "--module", Module);
        var lane = int.Parse(Regex.Match(co, @"brain\.LaneState (\d+)").Groups[1].Value);
        Assert.Equal(s, BrainStore.LaneStateById(lane, db)!.Session);
    }

    // 16: kor memory reads back the source
    [Fact]
    public void LiveKorMemoryShowsTheSource()
    {
        using var db = BrainDb.OpenRolledBack();
        Run(db, null, "record", "--module", Module, "--kind", "finding", "--name", "k4-memory", "--says", "memory read-back",
            "--source", "git:docs/KNOWLEDGE-IDE.md", "--dir", FrameworkDir());
        var (code, o, _) = Run(db, null, "memory", "--module", Module, "--last", "5");
        Assert.Equal(0, code);
        Assert.Contains("finding k4-memory [current]", o);
        Assert.Contains($"source=git Framework:docs/KNOWLEDGE-IDE.md commit={Git.Run(FrameworkDir(), "rev-parse", "HEAD")!.Trim()}", o);
    }
}

/// <summary>A throwaway git repo under %TEMP% with one committed file a.md (for dirty / untracked cases); deleted on Dispose.</summary>
public sealed class TempRepo : IDisposable
{
    public string Dir { get; }

    private TempRepo(string dir) => Dir = dir;

    public static TempRepo Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-zz-repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var args in new[] { new[] { "init", "-q" }, new[] { "config", "user.name", "k4 test" }, new[] { "config", "user.email", "k4@test.invalid" },
                                     new[] { "config", "core.hooksPath", "no-hooks" } })
            Git.Run(dir, args);
        File.WriteAllText(Path.Combine(dir, "a.md"), "a");
        Git.Run(dir, "add", "a.md");
        Git.Run(dir, "commit", "-q", "-m", "a");
        return new TempRepo(dir);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, true);
        }
        catch { /* a temp folder left behind is harmless */ }
    }
}
