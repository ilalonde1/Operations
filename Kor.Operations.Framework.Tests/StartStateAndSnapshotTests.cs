using System.Diagnostics;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K6 follow-up (3 Nov, done-when 1 failed), PURE: the handover's STATE NOW in the record part, the page's first line, and
/// the read policy of kor start (SNAPSHOT, READ COMMITTED when the database refuses it, once more on a deadlock).
/// Covers: the STATE NOW section found in both header forms the 6 KOR handovers use ("## STATE NOW" ended by the next
/// ##, "# STATE NOW" ended by the next ## with a ### inside kept), CRLF, no section; the cap cuts whole lines; the section
/// sits right after FIVE QUESTIONS, carries its repo:path@commit blob citation and the uncommitted-changes line, and is
/// cut only after every other section is at its floor; SQL 3952 falls back, 1205 retries, anything else is thrown.
/// Does NOT cover: whether the section is true or recent; a heading inside a code fence. Same-class miss: a handover
/// whose current state is under another heading ("## Where things stand") shows "not read", not its state.
/// </summary>
public class StartStateTests
{
    private const string FrameworkStyle =
        "# The Framework - HANDOVER\n\n## STATE NOW - 2 Nov 22:18. Read first.\n- one\n- two\n\n## STATE at 20:45 (history)\nold\n## Five questions\n1. q";

    private const string DrafterStyle =
        "# STATE NOW - 2 Nov 23:49\nline a\n### a sub-part\nline b\n## 2 Nov log (history)\nold\n# Virtual Drafter LEAD handover\n";

    [Fact]
    public void TheStateNowSectionIsFoundInBothHeaderForms()
    {
        Assert.Equal("## STATE NOW - 2 Nov 22:18. Read first.\n- one\n- two", HandoverState.StateNow(FrameworkStyle));
        Assert.Equal("# STATE NOW - 2 Nov 23:49\nline a\n### a sub-part\nline b", HandoverState.StateNow(DrafterStyle));
        Assert.Equal("## STATE NOW x\nbody", HandoverState.StateNow("# H\r\n## STATE NOW x\r\nbody\r\n## next\r\n"));
        Assert.Null(HandoverState.StateNow("# H\n## Where things stand\nx\n## STATE at 20:45\ny"));
        Assert.Equal("### STATE NOW\na", HandoverState.StateNow("## Part\n### STATE NOW\na\n### Other\nb"));   // a ### ends at the next ###
    }

    [Fact]
    public void TheCapCutsWholeLinesAndSaysWhereTheRestIs()
    {
        var section = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"line {i:D3} " + new string('x', 50)));
        var cut = HandoverState.CapLines(section, HandoverState.Cap, "docs/HANDOVER.md");
        Assert.True(cut.Length <= HandoverState.Cap, $"{cut.Length}");
        var lines = cut.Split('\n');
        Assert.All(lines[..^1], l => Assert.Matches(@"^line \d{3} x{50}$", l));     // every kept line whole
        Assert.StartsWith($"... [STATE NOW cut at {HandoverState.Cap} of {section.Length} chars - the rest: git show HEAD:docs/HANDOVER.md]", lines[^1]);
        Assert.Equal("short", HandoverState.CapLines("short", HandoverState.Cap, "p"));
    }

    private static HandoverState Handover(string section, bool dirty = false) =>
        new("Framework", "docs/HANDOVER.md", new string('c', 40), "6a28024" + new string('0', 33), section, dirty, null);

    private static ModuleState State(int issues, int stale, HandoverState? h)
    {
        var records = new List<StartRecord>
        {
            new(1, "zz", ModuleState.FiveQuestionsName, "reference", "five", "1. Where do things stand? " + new string('q', 4000), "current",
                "git", "Framework", new string('a', 40), "docs/HANDOVER.md", null, null, null, null),
        };
        for (var i = 0; i < 40; i++)
            records.Add(new(100 + i, "zz", $"rec-{i}", "finding", new string('d', 300), null, "current", "git", "Framework", new string('a', 40),
                            $"docs/r{i}.md", null, null, null, null));
        var open = Enumerable.Range(0, issues).Select(i => new StartRecord(50_000 + i, "zz", $"issue-{i}", "issue", new string('i', 200), null,
            "current", "session", null, null, null, "t", "test", "x|y", null)).ToList();
        var rows = Enumerable.Range(0, stale).Select(i => new Checked(60_000 + i, "zz", $"stale-{i}", "finding", "git", "Framework",
            $"docs/s{i}.md", new string('a', 40), new string('b', 40), Verdict.Stale, "changed")).ToList();
        var fresh = new FreshnessReport(rows, new Dictionary<string, string?> { ["Framework"] = new string('c', 40) }, true);
        var resume = new ResumeRow(9, "RESUME-START " + new string('r', 6000), "lead", DateTimeOffset.Now, "s1", false, false);
        var office = Enumerable.Range(0, 60).Select(i => new StartRecord(80_000 + i, "Office", $"office-{i}", "feedback", new string('o', 120),
            null, "current", null, null, null, null, null, null, null, null)).ToList();
        return new ModuleState("zz", DateTimeOffset.Now, fresh, records, open, office, new ResumeChoice(resume, "test", Array.Empty<ResumeRow>()),
                               null, Array.Empty<StoredEvent>(), Array.Empty<string>()) { Handover = h };
    }

    [Fact]
    public void TheFirstLineSaysWhatIsNotOnThePageWasNotRead()
    {
        var text = StartRender.Record(State(0, 0, null), StartRender.HookBudget);
        Assert.Equal(StartRender.NotReadLine, text.Split('\n')[0]);
        Assert.DoesNotContain('\n', StartRender.NotReadLine);
        Assert.Contains("answer 'not checked', never a value", StartRender.NotReadLine);
        Assert.EndsWith("A command written as text runs nothing and returns nothing.", StartRender.NotReadLine);
    }

    [Fact]
    public void StateNowFollowsTheFiveQuestionsWithItsCitationAndTheUncommittedLine()
    {
        var text = StartRender.Record(State(0, 0, Handover("## STATE NOW - x\n- the state", dirty: true)), null);
        var five = text.IndexOf("FIVE QUESTIONS (record #1", StringComparison.Ordinal);
        var state = text.IndexOf("STATE NOW - the module's current state, from its handover at HEAD (Framework:docs/HANDOVER.md@ccccccc blob 6a28024):", StringComparison.Ordinal);
        var resume = text.IndexOf("RESUME - LaneState #9", StringComparison.Ordinal);
        Assert.True(five >= 0 && five < state && state < resume, $"five {five}, state {state}, resume {resume}");
        Assert.Contains("blob 6a28024):\nthe handover has uncommitted changes - commit them to show them here\n## STATE NOW - x\n- the state", text);
        Assert.DoesNotContain("uncommitted changes", StartRender.Record(State(0, 0, Handover("## STATE NOW - x")), null));
    }

    [Fact]
    public void AHandoverWithNoStateNowSaysNotRead()
    {
        var h = new HandoverState("zz", "docs/HANDOVER.md", null, null, null, false, "repo zz is not on this machine beside this folder");
        Assert.Contains("STATE NOW: not read - repo zz is not on this machine beside this folder. Answer anything that needs the module's current state 'not checked'.",
                        StartRender.Record(State(0, 0, h), null));
    }

    [Theory]
    [InlineData(25, 30)]
    [InlineData(60, 60)]
    public void StateNowSurvivesCompactionAheadOfEverySectionButTheFloors(int issues, int stale)
    {
        var section = "## STATE NOW - x\n" + string.Join("\n", Enumerable.Range(0, 40).Select(i => $"- state line {i:D2} " + new string('s', 50)));
        Assert.True(section.Length <= HandoverState.Cap);
        var s = State(issues, stale, Handover(section));
        var text = StartRender.Record(s, StartRender.HookBudget);
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
        Assert.Contains(section, text);                                               // whole
        Assert.DoesNotContain(new string('q', 4000), text);                           // while the five questions' body was cut
        Assert.DoesNotContain(new string('d', 300), text);                            // and the records
        foreach (var i in s.OpenIssues) Assert.Matches($@"#{i.Id}\b", text);
        foreach (var r in s.Fresh.Rows) Assert.Matches($@"#{r.Id}\b", text);
        Assert.Contains(s.Resume.Chosen!.ResumeBlock[..StartRender.ResumeKeep], text);
    }

    // ---- the read policy -------------------------------------------------------------------------------------------
    [Fact]
    public void ARefusedSnapshotIsReadAgainUnderReadCommitted()
    {
        var asked = new List<bool>();
        var r = BrainDb.ReadWithFallback(snap =>
        {
            asked.Add(snap);
            if (snap) throw DeadlockRetryTests.SqlError(3952, "Snapshot isolation transaction failed accessing database 'KorStandards' - injected");
            return "read committed";
        }, BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, _ => { });
        Assert.Equal("read committed", r);
        Assert.Equal(new[] { true, false }, asked);
    }

    [Fact]
    public void ADeadlockedReadIsRunOnceMoreAndAnyOtherErrorIsThrown()
    {
        var n = 0;
        Assert.Equal(2, BrainDb.ReadWithFallback(snap => ++n == 1 ? throw DeadlockRetryTests.SqlError(1205) : n,
                                                 BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, _ => { }));
        var asked = new List<bool>();
        var e = Assert.Throws<SqlException>(() => BrainDb.ReadWithFallback<int>(snap => { asked.Add(snap); throw DeadlockRetryTests.SqlError(208, "invalid object"); },
                                                                               BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, _ => { }));
        Assert.Equal(208, e.Number);
        Assert.Equal(new[] { true }, asked);                                           // never fell back, never retried
        Assert.True(BrainDb.IsBlockedWrite(DeadlockRetryTests.SqlError(1222, "Lock request time out period exceeded.")));
        Assert.False(BrainDb.IsBlockedWrite(DeadlockRetryTests.SqlError(208, "invalid object")));
    }
}

/// <summary>
/// K6 follow-up, LIVE (KorStandards, every write rolled back): kor start's reads never wait on another session's open
/// transaction. A "holder" unit of work inserts a zz-test record and event and keeps its transaction open - as a kor test
/// suite does. Covers: the holder really blocks a READ COMMITTED read (so the next assertion means something); the start's
/// SNAPSHOT read finishes and does not see the uncommitted row; the session's isolation level is what was asked for, and a
/// pooled connection does not carry SNAPSHOT into the next unit of work; the INSTALLED path (no caller unit of work) for a
/// zz-test lane - which has nothing committed, so its write phase writes nothing - shows the page live, never PART LATE or
/// UNREADABLE; a write phase that meets a held lock gives up (1222) and the page says nothing was recorded.
/// Does NOT cover: the 3952 fallback against a real database without snapshot (StartStateTests injects it); a writer that
/// holds a lock on a row the start must UPDATE for longer than 2 s still costs the start those 2 s.
/// </summary>
[Collection(LiveDb.Name)]
public class StartSnapshotLiveTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private static readonly string Lane = $"zz-test-k6s-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Doc = "docs/KNOWLEDGE-IDE.md";

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static string FrameworkDir() => RepoLocator.Find("Framework", RepoDir())!;

    private static int IsolationLevel(BrainDb db)
    {
        using var c = db.Command("SELECT CAST(transaction_isolation_level AS int) FROM sys.dm_exec_sessions WHERE session_id = @@SPID");
        return (int)c.ExecuteScalar()!;
    }

    /// <summary>A unit of work holding an uncommitted zz-test record and event in <see cref="Lane"/>.</summary>
    private static (BrainDb Holder, int Record) Hold()
    {
        var holder = BrainDb.OpenRolledBack();
        var id = RecordStore.Insert(holder, new NewRecord(Lane, "k6s-held", "finding", "k6s held", "b", "k6s-test", RecordStore.AtHead(FrameworkDir(), Doc)));
        BrainStore.Insert(new BrainEvent(Lane, "commit", "Operations", new string('a', 40), "k6s", null, null, "k6s-test", "k6s-holder"), holder);
        return (holder, id);
    }

    [Fact]
    public void LiveTheIsolationIsWhatWasAskedAndDoesNotLeakIntoTheNextUnitOfWork()
    {
        using (var snap = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: true))
        {
            Assert.Equal("SNAPSHOT", snap.Isolation);
            Assert.Equal(5, IsolationLevel(snap));                                    // 5 = SNAPSHOT
        }
        using (var rc = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: false))
            Assert.Equal(2, IsolationLevel(rc));                                      // 2 = READ COMMITTED
        using (var snap = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: true)) Assert.Equal(5, IsolationLevel(snap));
        using (var next = BrainDb.OpenRolledBack()) Assert.Equal(2, IsolationLevel(next));   // named: never the pool's last level
        using (var snap = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: true)) Assert.Equal(5, IsolationLevel(snap));
        using (var write = BrainDb.Open()) Assert.Equal(2, IsolationLevel(write));           // kor start's write unit of work (never committed here)
        Assert.Equal("SNAPSHOT", BrainDb.Read(KorStandardsConnection.Login.Board, b => IsolationLevel(b) == 5 ? b.Isolation : "not 5"));
    }

    [Fact]
    public void LiveAnOpenTransactionBlocksReadCommittedButNotTheStartsSnapshotRead()
    {
        var (holder, held) = Hold();
        using (holder)
        {
            // the holder really blocks a READ COMMITTED read of the lane
            using (var rc = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: false))
            {
                using var c = rc.Command("SELECT COUNT(*) FROM brain.Memory WHERE Lane = @l", 2);
                c.Parameters.AddWithValue("@l", Lane);
                var e = Assert.Throws<SqlException>(() => c.ExecuteScalar());
                Assert.Equal(-2, e.Number);                                           // the 2 s command timeout
            }
            var clock = Stopwatch.StartNew();
            var s = BrainDb.Read(KorStandardsConnection.Login.Brain,
                                 d => ModuleState.Read(d, Lane, new ModuleState.Start(null, "startup"), RepoDir(), TimeSpan.FromSeconds(5)));
            log.WriteLine($"snapshot read {clock.Elapsed.TotalSeconds:0.00} s");
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed.TotalSeconds:0.00} s");
            Assert.DoesNotContain(s.Records, r => r.Id == held);                    // the last COMMITTED version: none
            Assert.DoesNotContain(s.Events, e => e.Session == "k6s-holder");
        }
    }

    [Fact]
    public void LiveTheInstalledStartPathIsLiveWhileAnotherTransactionHoldsTheLane()
    {
        var (holder, _) = Hold();
        using (holder)
        {
            var output = new StringWriter();
            var errors = Path.Combine(Path.GetTempPath(), $"kor-k6s-{Guid.NewGuid():N}.log");
            var cache = Path.Combine(Path.GetTempPath(), $"kor-k6s-cache-{Guid.NewGuid():N}");
            var clock = Stopwatch.StartNew();
            // db: null = the installed path: BrainDb.Read (SNAPSHOT), then its own write unit of work, committed. The lane
            // is a zz-test lane with nothing committed, so that unit of work writes nothing.
            Assert.Equal(0, Program.Run(new[] { "start", "--module", Lane, "--owner", Lane, "--part", "all", "--text", "--dir", RepoDir(), "--cache-dir", cache },
                                        () => null, output, new StringWriter(), errors));
            var text = output.ToString();
            log.WriteLine($"{clock.Elapsed.TotalSeconds:0.00} s\n{text}");
            Assert.StartsWith(StartRender.NotReadLine + "\nKNOWN AT START", text);
            Assert.DoesNotContain("PART LATE", text);
            Assert.DoesNotContain("UNREADABLE", text);
            Assert.Contains("THE BOARD (source of truth)", text);
            Assert.DoesNotContain("k6s-held", text);
            try { Directory.Delete(cache, true); } catch (IOException) { }
            File.Delete(errors);
        }
    }

    [Fact]
    public void LiveAWritePhaseThatMeetsAHeldLockRecordsNothingAndSaysSo()
    {
        var read = BrainDb.Read(KorStandardsConnection.Login.Brain,
                                d => ModuleState.Read(d, "Framework", new ModuleState.Start(null, "startup"), FrameworkDir(), TimeSpan.FromSeconds(5)));
        var target = read.Fresh.Rows.First(r => r.Verdict != Verdict.Unverifiable).Id;
        using var holder = BrainDb.OpenRolledBack();
        using (var c = holder.Command("SELECT Id FROM brain.Memory WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id"))
        {
            c.Parameters.AddWithValue("@id", target);
            Assert.Equal(target, (int)c.ExecuteScalar()!);                          // a U lock, held: no row is changed
        }
        using var w = BrainDb.OpenRolledBack();
        w.SetLockTimeout(500);
        var clock = Stopwatch.StartNew();
        var e = Assert.ThrowsAny<Exception>(() => ModuleState.Record(w, read, null));
        Assert.True(BrainDb.IsBlockedWrite(e), e.ToString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed.TotalSeconds:0.00} s");
        var shown = StartRender.Record(ModuleState.NotRecorded(read, "SQL 1222"), null);
        Assert.Contains("freshness verdicts NOT recorded (SQL 1222) - kor fresh --module \"Framework\" records them", shown);
    }
}
