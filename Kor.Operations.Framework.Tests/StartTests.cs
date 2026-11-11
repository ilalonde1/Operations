using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K6 - known at start (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md), PURE: the board part against board_render.py's own
/// golden (its reads and its text, captured in one Python process on 3 Nov 01:47 - Fixtures/board-golden), the record
/// part's compaction floors, the resume choice, the watch list, the unreadable/late cache.
/// Covers: byte-identical board text for the same rows (live Framework / KOR.Drafter / Operations rows, and a synthetic
/// set with None, empty flags, astral characters cut at a width, U+001C-U+001F whitespace, :g hours, an owed overflow
/// and guards), the ONE deliberate difference being the COMPACT line's command; each part &lt;= 9,900 with 1,150 legacy
/// rows; every open-issue and stale id and the resume's first 1,500 characters surviving compaction.
/// Does NOT cover: the SQL behind the reads (StartLiveTests); whether a record is true. Same-class miss: a Python rule the
/// fixtures never exercise (e.g. a board row whose What is not a string) could still render differently.
/// </summary>
public class StartTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "board-golden", name + ".json");

    private static readonly Regex ReadAt = new(@"read (\d{4}-\d\d-\d\d \d\d:\d\d)", RegexOptions.CultureInvariant);

    private static DateTime ClockOf(string pythonText) =>
        DateTime.ParseExact(ReadAt.Match(pythonText).Groups[1].Value, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static List<BoardRow> Rows(JsonElement a) => a.EnumerateArray().Select(r => new BoardRow(r.GetProperty("Id").GetInt32(),
        Str(r, "Owner"), Str(r, "State"), Str(r, "Flag"), Str(r, "What"), Str(r, "DoneWhen"), Str(r, "DueLocal"), Str(r, "BlockedOn"),
        Str(r, "Budget"))).ToList();

    private static List<OwedRow> Owed(JsonElement a) => a.EnumerateArray().Select(r => new OwedRow(r.GetProperty("Id").GetInt32(),
        Str(r, "What"), Str(r, "State"), r.GetProperty("HoursOpen").ValueKind == JsonValueKind.Number ? r.GetProperty("HoursOpen").GetDouble() : null,
        Str(r, "Flag"))).ToList();

    private static List<GuardRow> Guards(JsonElement a) =>
        a.EnumerateArray().Select(r => new GuardRow(Str(r, "Guard"), Int(r, "Refusals"), Int(r, "JudgedWrong"))).ToList();

    [Theory]
    [InlineData("framework-text", "Framework")]
    [InlineData("framework-hook", "Framework")]
    [InlineData("drafter-hook", "KOR.Drafter")]
    [InlineData("drafter-text", "KOR.Drafter")]
    [InlineData("operations-hook", "Operations")]
    public void BoardTextEqualsBoardRendersGoldenForTheSameRows(string name, string module)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture(name)));
        var g = doc.RootElement;
        Assert.Equal("live", g.GetProperty("status").GetString());
        var given = g.GetProperty("given_owners").EnumerateArray().Select(x => x.GetString()!).ToList();
        var owners = g.GetProperty("owners").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(owners, BoardState.WithHelpers(given));   // the helper rule, too
        var state = new BoardState(owners, Rows(g.GetProperty("rows")), given.Contains("Framework"), Owed(g.GetProperty("owed")),
                                   g.GetProperty("owed_status").GetString()!, Guards(g.GetProperty("guards")), g.GetProperty("guards_status").GetString()!);
        int? budget = g.GetProperty("budget").ValueKind == JsonValueKind.Number ? g.GetProperty("budget").GetInt32() : null;
        var python = g.GetProperty("text").GetString()!;
        var kor = BoardRender.FullBoardCommand(module, given, noHelpers: false);
        // the ONE deliberate difference: the COMPACT line names kor's full-board command, not the Python script
        var expected = python.Replace(g.GetProperty("python_full_command").GetString()!, kor);
        Assert.Equal(expected, BoardRender.Render(state, ClockOf(python), budget, kor));
        if (name == "drafter-hook") Assert.Contains("COMPACT (", expected);   // the fixture really exercises compaction
    }

    [Theory]
    [InlineData("text", null)]
    [InlineData("hook", 9900)]
    [InlineData("tight", 3000)]
    public void BoardEdgeCasesEqualBoardRender(string key, int? budget)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("synthetic")));
        var g = doc.RootElement;
        var owners = g.GetProperty("owners").EnumerateArray().Select(x => x.GetString()!).ToList();
        var python = g.GetProperty("out").GetProperty(key).GetString()!;
        var kor = BoardRender.FullBoardCommand("Framework", new[] { "Framework" }, noHelpers: false);
        var expected = python.Replace(g.GetProperty("python_full_command").GetString()!, kor);
        var state = new BoardState(owners, Rows(g.GetProperty("rows")), true, Owed(g.GetProperty("owed")), "live", Guards(g.GetProperty("guards")), "live");
        var mine = BoardRender.Render(state, ClockOf(python), budget, kor);
        Assert.Equal(expected, mine);
        // the owed block keeps its room: it arrives whole, at the end, even when the rows compact around it
        var tail = BoardRender.OwedBlock(state.Owed, "live") + "\n" + BoardRender.GuardsLine(state.Guards, "live");
        Assert.EndsWith(tail, mine);
    }

    [Fact]
    public void OwedGuardsAndSingleOwnerEdgesEqualBoardRender()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("synthetic")));
        var o = doc.RootElement.GetProperty("out");
        Assert.Equal(o.GetProperty("owed_unreadable").GetString(), BoardRender.OwedBlock(Array.Empty<OwedRow>(), "UNREADABLE: login failed"));
        Assert.Equal(o.GetProperty("guards_unreadable").GetString(), BoardRender.GuardsLine(Array.Empty<GuardRow>(), "UNREADABLE: login failed"));
        Assert.Equal(o.GetProperty("owed_empty").GetString(), BoardRender.OwedBlock(Array.Empty<OwedRow>(), "live"));
        Assert.Equal("None", o.GetProperty("guards_empty").GetString());
        Assert.Null(BoardRender.GuardsLine(Array.Empty<GuardRow>(), "live"));
        var single = o.GetProperty("single").GetString()!;
        var rows = Rows(doc.RootElement.GetProperty("rows")).Take(3).ToList();
        Assert.Equal(single, BoardRender.Rows(new[] { "Operations" }, rows, "live", null, null, ClockOf(single), "unused"));
    }

    [Theory]
    [InlineData(25.0, "25")]
    [InlineData(0.05, "0.05")]
    [InlineData(1234567.0, "1.23457e+06")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(24.1, "24.1")]
    [InlineData(999999.5, "1e+06")]
    [InlineData(0.0, "0")]
    public void HoursFormatLikePythonsG(double v, string expected) => Assert.Equal(expected, PyText.G(v));

    // ---- the record part -------------------------------------------------------------------------------------------
    private static StartRecord Rec(int id, string kind, string name, string desc, string? sourceKind = "git", string status = "current",
                                   string? body = null, string lane = "zz") =>
        new(id, lane, name, kind, desc, body, status, sourceKind, sourceKind == "git" ? "Framework" : null,
            sourceKind == "git" ? new string('a', 40) : null, sourceKind == "git" ? $"docs/{name}.md" : null,
            sourceKind is null or "git" ? null : "brain.Event #1", kind == "issue" ? "test" : null,
            kind == "issue" ? "Operations/x.csproj|A.B" : null, null);

    private static ModuleState BigState(int issues, int stale, int legacy, int resumeChars)
    {
        var records = new List<StartRecord>
        {
            Rec(1, "reference", ModuleState.FiveQuestionsName, "five", body: string.Join("\n", Enumerable.Range(1, 5).Select(i => $"{i}. question {i} " + new string('q', 300)))),
        };
        for (var i = 0; i < 40; i++) records.Add(Rec(100 + i, new[] { "decision", "finding", "lesson", "dead-end" }[i % 4], $"rec-{i}", new string('d', 300)));
        for (var i = 0; i < legacy; i++) records.Add(Rec(10_000 + i, "feedback", $"legacy-{i}", new string('l', 150), sourceKind: null));
        var open = Enumerable.Range(0, issues).Select(i => Rec(50_000 + i, "issue", $"issue-{i}", new string('i', 200), sourceKind: "session")).ToList();
        var checkedRows = Enumerable.Range(0, stale).Select(i => new Checked(60_000 + i, "zz", $"stale-{i}", "finding", "git", "Framework",
            $"docs/s{i}.md", new string('a', 40), new string('b', 40), Verdict.Stale, "changed")).ToList();
        checkedRows.AddRange(Enumerable.Range(0, 300).Select(i => new Checked(70_000 + i, "zz", $"cur-{i}", "finding", "git", "Framework",
            $"docs/c{i}.md", new string('a', 40), new string('a', 40), Verdict.Current, "same")));
        var fresh = new FreshnessReport(checkedRows, new Dictionary<string, string?> { ["Framework"] = new string('c', 40) }, true,
                                        RepoDirs: new Dictionary<string, string> { ["Framework"] = "C:/VIsual Studio Projects/Framework" });
        var resume = new ResumeRow(9, "RESUME-START " + new string('r', resumeChars), "lead", DateTimeOffset.Now, "s1", false, false);
        var office = Enumerable.Range(0, 60).Select(i => Rec(80_000 + i, "feedback", $"office-{i}", new string('o', 120), sourceKind: null, lane: "Office")).ToList();
        var events = Enumerable.Range(0, 10).Select(i => new StoredEvent(90_000 + i, DateTimeOffset.Now, "zz", "commit", "Framework",
            new string('e', 40), new string('f', 300), "pass", null, "x", "sess")).ToList();
        return new ModuleState("zz", DateTimeOffset.Now, fresh, records, open, office,
            new ResumeChoice(resume, "test", Enumerable.Range(0, 5).Select(i => resume with { Id = 20 + i }).ToList()), null, events,
            Array.Empty<string>());
    }

    [Theory]
    [InlineData(0, 0, 1150, 3000)]
    [InlineData(25, 30, 1150, 12000)]
    [InlineData(150, 120, 1150, 12000)]
    [InlineData(400, 300, 1150, 12000)]
    public void TheRecordPartFitsAndKeepsEveryIssueStaleIdAndTheResumeHead(int issues, int stale, int legacy, int resumeChars)
    {
        var s = BigState(issues, stale, legacy, resumeChars);
        var text = StartRender.Record(s, StartRender.HookBudget, "kor");
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length} chars");
        foreach (var i in s.OpenIssues) Assert.Matches($@"#{i.Id}\b", text);
        foreach (var r in s.Fresh.Rows.Where(r => r.Verdict == Verdict.Stale)) Assert.Matches($@"#{r.Id}\b", text);
        Assert.Contains(s.Resume.Chosen!.ResumeBlock[..StartRender.ResumeKeep], text);
        // uncut, --text keeps everything
        var full = StartRender.Record(s, null, "kor");
        Assert.Contains("legacy-1149", full);
        Assert.True(full.Length > StartRender.HookBudget);
    }

    [Fact]
    public void LegacyIsCutFirstAndTheFiveQuestionsArriveWhole()
    {
        var s = BigState(3, 2, 1150, 2000);
        var text = StartRender.Record(s, StartRender.HookBudget, "kor");
        Assert.Contains(s.Records[0].Body!, text);                 // five questions whole
        Assert.Contains("rec-39", text);                           // the sourced records still listed
        Assert.Contains("LEGACY MEMORY: 1150 imported rows cut at the session-start limit", text);   // a section was cut: one line
        Assert.DoesNotContain("legacy-1149", text);
        // nothing above it cut: legacy fills exactly the room left, with its note
        var small = s with
        {
            Records = s.Records.Where(r => r.FromFileMemory || r.Name == ModuleState.FiveQuestionsName).ToList(),
            Office = Array.Empty<StartRecord>(),
            Events = Array.Empty<StoredEvent>(),
        };
        var fill = StartRender.Record(small, StartRender.HookBudget, "kor");
        Assert.Contains("legacy memory cut at the session-start limit", fill);
        Assert.Contains("legacy-0 ", fill);
        Assert.True(fill.Length <= StartRender.HookBudget && fill.Length > StartRender.HookBudget - 300, $"{fill.Length}");
    }

    [Theory]
    [InlineData(25, 30)]
    [InlineData(0, 0)]
    public void OnceAnySectionIsCutLegacyIsOneLineAndNeverRefillsTheRoom(int issues, int stale)
    {
        // seen live 3 Nov 02:24 on Framework: Office cut to its header while legacy rows still rendered below it
        var text = StartRender.Record(BigState(issues, stale, 1150, 12000), StartRender.HookBudget, "kor");
        // a section above legacy was cut: no record's full 300-char description renders, so the records section is below
        // its level 0 (40 records of ~330 chars are more than the budget). Not "[resume cut at 3000", which is the resume's
        // fixed cap and shows even when nothing was compacted (review row 202).
        Assert.Contains(new string('d', 300), StartRender.Record(BigState(issues, stale, 1150, 12000), null, "kor"));   // uncut: they render
        Assert.DoesNotContain(new string('d', 300), text);
        Assert.DoesNotContain("legacy-0 ", text);                             // ... so no legacy row renders
        Assert.Contains("LEGACY MEMORY: 1150 imported rows cut at the session-start limit", text);
    }

    [Fact]
    public void AHeadShorterThanSevenCharactersDoesNotBreakTheHeader()
    {
        var s = BigState(0, 0, 0, 10);
        var odd = s with { Fresh = s.Fresh with { RepoHeads = new Dictionary<string, string?> { ["X"] = "abc", ["Y"] = null } } };
        Assert.Contains("HEAD: X abc, Y UNREADABLE", StartRender.Record(odd, null, "kor"));
    }

    [Fact]
    public void ResolutionsAndLegacyRowsAreNotListedAsRecords()
    {
        var s = BigState(0, 0, 0, 100) with { Records = new List<StartRecord> { Rec(5, "resolution", "resolution of #4", "closed") } };
        var text = StartRender.Record(s, null, "kor");
        Assert.DoesNotContain("resolution of #4", text);
        Assert.Contains("resolutions in history: 1", text);
    }

    [Fact]
    public void WatchPathsAreAbsoluteCurrentAndAtMost200()
    {
        var s = BigState(0, 50, 0, 10);
        var w = StartRender.WatchPaths(s.Fresh);
        Assert.Equal(200, w.Count);
        Assert.All(w, p => Assert.True(Path.IsPathFullyQualified(p), p));
        Assert.DoesNotContain(w, p => p.Contains(Path.Combine("docs", "s1.md")));   // a stale record's file is not current
    }

    // ---- which resume ---------------------------------------------------------------------------------------------
    private static ResumeRow R(int id, int minutesAgo, string? session, bool compaction, bool work) =>
        new(id, $"block {id}", compaction ? $"compaction {session}" : "lead kor-brain", DateTimeOffset.Now.AddMinutes(-minutesAgo), session, compaction, work);

    [Fact]
    public void AStartupTakesTheNewestResumeOfASessionThatDidWork()
    {
        var rows = new[] { R(3, 1, "throwaway", true, false), R(2, 5, "worker", true, true), R(1, 9, null, false, false) };
        var c = ModuleState.ChooseResume(rows, new ModuleState.Start("someone-new", "startup"));
        Assert.Equal(2, c.Chosen!.Id);
        Assert.Contains(c.Others, o => o.Id == 3 && !o.Eligible);   // listed, never THE resume
        var fresh = ModuleState.ChooseResume(new[] { R(3, 1, "throwaway", true, false), R(1, 9, null, false, false) }, new ModuleState.Start(null, null));
        Assert.Equal(1, fresh.Chosen!.Id);                          // a state write counts as work
    }

    [Fact]
    public void ACompactedSessionGetsItsOwnSummaryBack()
    {
        var rows = new[] { R(3, 1, "other", true, true), R(2, 5, "me", true, false) };
        // resume: this session's own stored row
        Assert.Equal(2, ModuleState.ChooseResume(rows, new ModuleState.Start("me", "resume")).Chosen!.Id);
        Assert.Equal(3, ModuleState.ChooseResume(rows, new ModuleState.Start("me", "startup")).Chosen!.Id);
        var resumedNone = ModuleState.ChooseResume(rows, new ModuleState.Start("never-wrote", "resume"));
        Assert.Equal(3, resumedNone.Chosen!.Id);
        Assert.Contains("wrote none", resumedNone.Why);
        // compact (live 3 Nov 03:02: SessionStart:compact runs before PostCompact stores the summary): with no summary in
        // its transcript yet, THE resume is the summary already in its context - never another session's row, never its
        // own OLDER row - and the lane's rows are still listed
        foreach (var who in new[] { "me", "never-wrote" })
        {
            var c = ModuleState.ChooseResume(rows, new ModuleState.Start(who, "compact"));
            Assert.Null(c.Chosen);
            Assert.Contains("already at the top of your context", c.Why);
            Assert.Contains(c.Others, o => o.Id == 3);
            Assert.Contains("RESUME: this session was just compacted", StartRender.Record(BigState(0, 0, 0, 10) with { Resume = c }, null, "kor"));
        }
        // PostCompact already stored THIS compaction (an own compaction row written moments ago): that row
        var justStored = rows.Append(R(5, 0, "me", true, false)).ToArray();
        Assert.Equal(5, ModuleState.ChooseResume(justStored, new ModuleState.Start("me", "compact")).Chosen!.Id);
    }

    /// <summary>A JSONL line with the keys and shape of a real Claude Code 2.1.317 compaction entry (transcript
    /// aef01af3-..., line 62, 3 Nov 02:44): type user, isCompactSummary true, message.content a string.</summary>
    private static string SummaryLine(string text, string at) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["parentUuid"] = "p", ["isSidechain"] = false, ["type"] = "user", ["isCompactSummary"] = true, ["isVisibleInTranscriptOnly"] = true,
        ["message"] = new Dictionary<string, object> { ["role"] = "user", ["content"] = text }, ["timestamp"] = at, ["sessionId"] = "s",
    });

    [Fact]
    public void TheNewestRealCompactSummaryIsReadFromTheTranscript()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path, new[]
            {
                JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = "hello" } }),
                SummaryLine("OLD SUMMARY", "2026-11-03T09:00:00.000Z"),
                JsonSerializer.Serialize(new { type = "system", subtype = "compact_boundary" }),
                SummaryLine("NEW SUMMARY", "2026-11-03T09:44:58.348Z"),
                // later lines carrying the BARE key that are not a compaction entry: hand-written, so the key reaches the
                // file unescaped and passes the Contains filter (review row 205: a serialized mention never did)
                "{\"type\":\"user\",\"isCompactSummary\":false,\"message\":{\"role\":\"user\",\"content\":\"NOT A SUMMARY (false)\"},\"timestamp\":\"2026-11-03T10:00:00.000Z\"}",
                "{\"type\":\"user\",\"meta\":{\"isCompactSummary\":true},\"message\":{\"role\":\"user\",\"content\":\"NOT A SUMMARY (nested)\"},\"timestamp\":\"2026-11-03T10:01:00.000Z\"}",
                "{\"type\":\"user\",\"isCompactSummary\":true,\"message\":{\"role\":\"user\",\"content\":\"   \"},\"timestamp\":\"2026-11-03T10:02:00.000Z\"}",
                "{\"type\":\"user\",\"isCompactSummary\":tru",   // a torn last line (the file is being written)
            });
            Assert.Equal(6, File.ReadLines(path).Count(l => l.Contains("\"isCompactSummary\"")));   // every candidate reaches the parser
            var s = ModuleState.NewestCompactSummary(path)!;
            Assert.Equal("NEW SUMMARY", s.Text);
            Assert.Equal(DateTimeOffset.Parse("2026-11-03T09:44:58.348Z"), s.At);
            Assert.Null(ModuleState.NewestCompactSummary(path + ".missing"));
            Assert.Null(ModuleState.NewestCompactSummary(null));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnCompactTheSessionsOwnSummaryFromItsTranscriptIsTheResumeUntilItIsStored()
    {
        var rows = new[] { R(3, 1, "other", true, true), R(1, 9, null, false, false) };
        var t = new ModuleState.TranscriptSummary("MY SUMMARY", DateTimeOffset.Now, "x.jsonl");
        var c = ModuleState.ChooseResume(rows, new ModuleState.Start("me", "compact"), t);
        Assert.Equal(0, c.Chosen!.Id);
        Assert.Equal("MY SUMMARY", c.Chosen.ResumeBlock);
        Assert.Contains("RESUME - not yet a LaneState", StartRender.Record(BigState(0, 0, 0, 10) with { Resume = c }, null, "kor"));
        // once stored (a row of this session at least as new), the stored row is the resume
        var stored = rows.Append(R(4, 0, "me", true, false) with { WrittenAtUtc = t.At.AddSeconds(1) }).ToArray();
        Assert.Equal(4, ModuleState.ChooseResume(stored, new ModuleState.Start("me", "compact"), t).Chosen!.Id);
        // a startup never takes a transcript summary
        Assert.Equal(3, ModuleState.ChooseResume(rows, new ModuleState.Start("me", "startup"), t).Chosen!.Id);
    }

    // ---- unreadable / late -> the cache, loudly ---------------------------------------------------------------------
    [Fact]
    public void AnUnreadableDbGivesTheCachedRenderWithItsAgeLoudly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}");
        var path = StartCache.PathFor(dir, "zz mod", "record", hook: true);
        try
        {
            var ok = StartPart.Run(() => ("LIVE RENDER", new[] { "C:/x" }), path, 9900, TimeSpan.FromSeconds(30), "by hand", () => DateTime.Now);
            Assert.True(ok.Live);
            Assert.Equal("LIVE RENDER", File.ReadAllText(path));
            var bad = StartPart.Run(() => throw new InvalidOperationException("KORBRAIN_SQL is not set"), path, 9900, TimeSpan.FromSeconds(30),
                                    "by hand", () => DateTime.Now.AddMinutes(90));
            Assert.False(bad.Live);
            Assert.StartsWith("!! UNREADABLE: KORBRAIN_SQL is not set !!", bad.Text);
            Assert.Contains("(1 h 30 min old)", bad.Text);
            Assert.EndsWith("LIVE RENDER", bad.Text);
            Assert.Empty(bad.WatchPaths);
            var late = StartPart.Run(() => { Thread.Sleep(3000); return ("never", Array.Empty<string>()); }, path, 9900,
                                     TimeSpan.FromMilliseconds(200), "by hand", () => DateTime.Now);
            Assert.StartsWith("!! PART LATE: not back in 0.2 s !!", late.Text);
            Assert.EndsWith("LIVE RENDER", late.Text);
            var none = StartPart.Run(() => throw new TimeoutException("x"), StartCache.PathFor(dir, "other", "board", true), 9900,
                                     TimeSpan.FromSeconds(30), "read it by hand", () => DateTime.Now);
            Assert.Equal("!! UNREADABLE: x !! No cached render on this machine - read it by hand before any work.", none.Text);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ALatePartCanNeverCommitAfterTheSessionWasToldItIsLate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.txt");
        var committed = 0;
        bool? granted = null;
        var finished = new ManualResetEventSlim();
        var late = StartPart.Run(gate =>
        {
            Thread.Sleep(600);
            granted = gate.TryCommit(() => Interlocked.Increment(ref committed));
            finished.Set();
            return ("late render", Array.Empty<string>());
        }, path, 9900, TimeSpan.FromMilliseconds(100), "by hand", () => DateTime.Now);
        Assert.StartsWith("!! PART LATE", late.Text);
        Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(granted);
        Assert.Equal(0, committed);                       // refused: its unit of work rolls back
        Assert.False(File.Exists(path));                  // and nothing late is cached as live

        // committed at the deadline: waited for and shown live, never hidden behind a LATE banner
        var gateSeen = new StartPart.Gate();
        Assert.True(gateSeen.TryCommit(() => { }));
        Assert.False(gateSeen.MarkLate());
        var onTime = StartPart.Run(gate => (gate.TryCommit(() => committed++) ? "LIVE" : "?", Array.Empty<string>()), path, 9900,
                                   TimeSpan.FromSeconds(30), "by hand", () => DateTime.Now);
        Assert.Equal("LIVE", onTime.Text);
        Assert.Equal(1, committed);
        File.Delete(path);
    }

    [Fact]
    public void HeadFastAgreesWithGitOnALooseAPackedOnlyADetachedHeadAndAGitFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kor-k6-git-{Guid.NewGuid():N}");
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        // no user hooks or signing in a throwaway repo: a global core.hooksPath at the KOR hooks would write brain.Event rows
        var noHooks = Path.Combine(root, "no-hooks");
        Directory.CreateDirectory(noHooks);
        string G(string dir, params string[] a) =>
            Git.Run(dir, TimeSpan.FromSeconds(20), new[] { "-c", $"core.hooksPath={noHooks}", "-c", "commit.gpgsign=false" }.Concat(a).ToArray())
            ?? throw new InvalidOperationException($"git {string.Join(' ', a)} failed");
        try
        {
            G(repo, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "one");
            G(repo, "add", "a.txt");
            G(repo, "-c", "user.name=k6", "-c", "user.email=k6@test", "commit", "-q", "-m", "one");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "two");
            G(repo, "-c", "user.name=k6", "-c", "user.email=k6@test", "commit", "-q", "-am", "two");
            string Real(string d) => G(d, "rev-parse", "HEAD").Trim();
            var top = Git.TopLevelFast(repo)!;
            Assert.Equal(Git.TopLevel(repo), top);
            Assert.True(File.Exists(Path.Combine(repo, ".git", "refs", "heads", "main")));
            Assert.Equal(Real(repo), Git.HeadFast(top));                                 // loose ref
            G(repo, "pack-refs", "--all");
            Assert.False(File.Exists(Path.Combine(repo, ".git", "refs", "heads", "main")));
            Assert.Equal(Real(repo), Git.HeadFast(top));                                 // packed-only ref
            G(repo, "checkout", "-q", "--detach", "HEAD~1");
            Assert.Equal(Real(repo), Git.HeadFast(top));                                 // detached HEAD
            var wt = Path.Combine(root, "wt");
            G(repo, "worktree", "add", "-q", "--detach", wt, "main");
            Assert.True(File.Exists(Path.Combine(wt, ".git")));                          // a .git FILE: git is asked
            Assert.Equal(Git.TopLevel(wt), Git.TopLevelFast(Path.Combine(wt)));
            Assert.Equal(Real(wt), Git.HeadFast(Git.TopLevelFast(wt)!));
            Assert.NotEqual(Git.HeadFast(top), Git.HeadFast(Git.TopLevelFast(wt)!));
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void HookWithTextIsRefusedQuietlyAndExitsZero()
    {
        var output = new StringWriter();
        var log = Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log");
        try
        {
            Assert.Equal(0, Program.Run(new[] { "start", "--module", "zz", "--part", "record", "--hook", "--text" }, () => "{}", output,
                                        new StringWriter(), log));
            Assert.Equal("", output.ToString());
            Assert.Contains("--hook prints the SessionStart JSON and --text prints plain text: give one (review row 194)", File.ReadAllText(log));
        }
        finally { File.Delete(log); }
    }

    [Theory]
    // stdout: an unknown verb in hook mode prints one line naming it. stderr: a bad --part is refused before any database
    // is opened ("kor: --part is record, state, memory, records, board or all, not '...'", exit 1). Neither touches the database.
    [InlineData(false, "café-ó-→", "--hook", "unknown verb 'café-ó-→'")]
    [InlineData(true, "start", "--module zz --part café-ó-→", "--part is record, state, memory, records, pages, board or all, not 'café-ó-→'")]
    public async Task ARedirectedStdoutAndStderrAreUtf8(bool stderr, string verb, string args, string expected)
    {
        // The real assembly in its own process, both streams redirected as a hook or a pipe sees them. Needs `dotnet` on
        // PATH and kor.dll in the test output folder (the ProjectReference to the Cli project, AssemblyName kor) - review
        // row 209. Both streams are drained concurrently so neither can fill and block the child; it is killed if late.
        var dll = Path.Combine(AppContext.BaseDirectory, "kor.dll");
        Assert.True(File.Exists(dll), $"kor.dll is not in the test output folder: {dll}");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(verb);
        foreach (var a in args.Split(' ')) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        try
        {
            using MemoryStream outMs = new(), errMs = new();
            var outCopy = p.StandardOutput.BaseStream.CopyToAsync(outMs);
            var errCopy = p.StandardError.BaseStream.CopyToAsync(errMs);
            Assert.True(p.WaitForExit(30_000), "kor did not exit in 30 s");
            await Task.WhenAll(outCopy, errCopy);
            var strict = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);   // throws on OEM code page bytes
            var outText = strict.GetString(outMs.ToArray());
            var errText = strict.GetString(errMs.ToArray());
            Assert.Contains(expected, stderr ? errText : outText);
            Assert.Equal(stderr ? 1 : 0, p.ExitCode);
        }
        finally
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void HookAllPartsIsRefusedQuietlyAndExitsZero()
    {
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "start", "--module", "zz", "--part", "all", "--hook" }, () => "{}", output, new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log")));
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void OwnersRepeatAndKeepTheirOrder()
    {
        var o = Program.Options.Parse(new[] { "start", "--owner", "B C", "--module", "M", "--owner", "A", "--no-helpers", "--part", "board" });
        Assert.Equal(new[] { "B C", "A" }, o.Owners);
        Assert.Equal("board", o.Get("--part"));
        Assert.NotNull(o.Get("--no-helpers"));
        Assert.Equal(new[] { "start" }, o.Positional);
    }

    [Fact]
    public void GitFastReadsAgreeWithGit()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        Assert.Equal(Git.TopLevel(d.FullName), Git.TopLevelFast(d.FullName));
        var top = Git.TopLevelFast(d.FullName)!;
        Assert.Equal(Git.Run(top, "rev-parse", "HEAD")!.Trim(), Git.HeadFast(top));
        var fw = RepoLocator.Find("Framework", top)!;
        Assert.Equal(Git.Run(fw, "rev-parse", "HEAD")!.Trim(), Git.HeadFast(fw));
        Assert.Equal(Git.TopLevel(fw), fw);
    }
}

/// <summary>
/// K6 LIVE, on KorStandards, every test in its own rolled-back unit of work (lane zz-test-k6-&lt;time&gt;): a seeded open
/// issue, a stale finding and a resume appear; a superseded finding and a query issue the start's own check resolves do
/// not; resume choice by session against real LaneState/Event rows; the CLI hook JSON; garbage stdin; Framework's real
/// start within its step-0 budget; the board read live renders as the golden says.
/// Does NOT cover: the settings.json wiring (proved by real sessions, row 1188); the cold session's answers.
/// </summary>
[Collection(LiveDb.Name)]
public class StartLiveTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private static readonly string Lane = $"zz-test-k6-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Doc = "docs/KNOWLEDGE-IDE.md";

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static string FrameworkDir() => RepoLocator.Find("Framework", RepoDir())!;

    private static int Finding(BrainDb db, string name, GitSource g) =>
        RecordStore.Insert(db, new NewRecord(Lane, name, "finding", $"k6 {name}", $"k6 body {name}", "k6-test", g));

    private static string CacheDir() => Path.Combine(Path.GetTempPath(), $"kor-k6-cache-{Guid.NewGuid():N}");

    [Fact]
    public void LiveSeededRecordsShowAndSupersededOrResolvedDoNot()
    {
        using var db = BrainDb.OpenRolledBack();
        var issue = RecordStore.InsertIssue(db, new NewRecord(Lane, "k6-open", "issue", "k6 open issue", "b", "k6-test",
            SourceKind: "session", SourceRef: "k6 test", CheckKind: "test", CheckRef: "Operations/x.csproj|A.B"));
        var resolvable = RecordStore.InsertIssue(db, new NewRecord(Lane, "k6-query-passes", "issue", "k6 query issue", "b", "k6-test",
            SourceKind: "session", SourceRef: "k6 test", CheckKind: "query", CheckRef: "brain:SELECT CAST(1 AS bit) AS Pass"));
        var stale = Finding(db, "k6-stale", RecordStore.At(FrameworkDir(), Doc, "3b2ce76c"));
        var old = Finding(db, "k6-replaced", RecordStore.AtHead(FrameworkDir(), Doc));
        var nu = RecordStore.Supersede(db, old, new NewRecord(Lane, "k6-replaced", "finding", "k6 newer", "b", "k6-test",
            RecordStore.AtHead(FrameworkDir(), Doc)), "k6-test", "k6 test");
        var five = RecordStore.Insert(db, new NewRecord(Lane, ModuleState.FiveQuestionsName, "reference", "five", "1. Q one?\n2. Q two?",
            "k6-test", RecordStore.AtHead(FrameworkDir(), Doc)));
        var state = BrainStore.InsertLaneState(Lane, "K6 RESUME BODY", "lead kor-brain", db, "k6-session-lead");
        var s = ModuleState.Load(db, Lane, new ModuleState.Start(null, "startup"), RepoDir());
        var text = StartRender.Record(s, StartRender.HookBudget, "kor");
        log.WriteLine(text);
        Assert.Contains($"record #{issue} k6-open", text);
        Assert.Matches($@"record #{stale} \[finding\] k6-stale", text);
        Assert.Contains($"#{nu} [finding] k6-replaced", text);
        Assert.DoesNotContain($"#{old}", text);                       // superseded never renders
        // resolved at start by its own query check: named in the check line, never listed as open
        Assert.DoesNotContain(s.OpenIssues, i => i.Id == resolvable);
        Assert.DoesNotContain($"record #{resolvable} k6-query-passes", text);
        Assert.Contains($"#{resolvable} k6-query-passes: pass", string.Join("\n", s.CheckLines));
        Assert.Contains($"QUERY CHECKS AT START: #{resolvable} k6-query-passes: pass", text);
        Assert.Contains("resolutions in history: 1", text);
        Assert.Contains($"FIVE QUESTIONS (record #{five}", text);
        Assert.Contains("2. Q two?", text);
        Assert.Contains($"RESUME - LaneState #{state}", text);
        Assert.Contains("K6 RESUME BODY", text);
        Assert.Contains(Path.GetFullPath(Path.Combine(FrameworkDir(), Doc)), StartRender.WatchPaths(s.Fresh));
    }

    [Fact]
    public void LiveAHeadlessCompactionWithNoWorkDoesNotChangeAFreshStartAndACompactedSessionGetsItsOwn()
    {
        using var db = BrainDb.OpenRolledBack();
        var lead = BrainStore.InsertLaneState(Lane, "LEAD STATE", "lead kor-brain", db, "k6-lead");
        var worker = "k6-worker-" + Guid.NewGuid().ToString("N")[..6];
        BrainStore.Insert(new BrainEvent(Lane, "commit", "Operations", new string('a', 40), "x", null, null, "k6-test", worker), db);
        var workerSummary = BrainStore.InsertLaneState(Lane, "WORKER SUMMARY", $"compaction {worker}", db, worker);
        var before = ModuleState.Load(db, Lane, new ModuleState.Start(null, "startup"), RepoDir()).Resume.Chosen!.Id;
        Assert.Equal(workerSummary, before);
        // a headless session with no work events compacts in this module, through the real capture path
        var throwaway = "k6-throwaway-" + Guid.NewGuid().ToString("N")[..6];
        var input = JsonSerializer.Serialize(new { session_id = throwaway, hook_event_name = "PostCompact", trigger = "manual", compact_summary = "THROWAWAY SUMMARY" });
        Assert.Equal(0, Program.Run(new[] { "capture-compact", "--module", Lane, "--hook" }, () => input, new StringWriter(), new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log"), db));
        var after = ModuleState.Load(db, Lane, new ModuleState.Start("someone-else", "startup"), RepoDir());
        Assert.Equal(before, after.Resume.Chosen!.Id);                                            // unchanged
        Assert.Contains(after.Resume.Others, o => o.ResumeBlock == "THROWAWAY SUMMARY" && !o.Eligible);   // listed, not THE resume
        // that session resumed later: its own stored summary back
        var own = ModuleState.Load(db, Lane, new ModuleState.Start(throwaway, "resume"), RepoDir());
        Assert.Equal("THROWAWAY SUMMARY", own.Resume.Chosen!.ResumeBlock);
        Assert.NotEqual(lead, own.Resume.Chosen.Id);
        // a compact start that finds THIS compaction already stored (moments ago): that row, never the lead's
        var atCompact = ModuleState.Load(db, Lane, new ModuleState.Start(throwaway, "compact"), RepoDir());
        Assert.Equal("THROWAWAY SUMMARY", atCompact.Resume.Chosen!.ResumeBlock);
        Assert.Contains("just stored", atCompact.Resume.Why);
        // a compact start of a session with nothing stored: no other session's resume is shown as its own
        var unstored = ModuleState.Load(db, Lane, new ModuleState.Start("k6-not-stored-" + Guid.NewGuid().ToString("N")[..6], "compact"), RepoDir());
        Assert.Null(unstored.Resume.Chosen);
        Assert.Contains("already at the top of your context", unstored.Resume.Why);
    }

    /// <summary>Row 2059 HANDOVER-3: a kor_handover hand --test writes its state with kor state --test. Before, test LaneState
    /// #28209 (checkword included) was the session-start RESUME until the next real kor state.</summary>
    [Fact]
    public void LiveATestStateIsNeverTheResumeNorListedAsOne()
    {
        using var db = BrainDb.OpenRolledBack();
        string State(string body, bool test)
        {
            var f = Path.Combine(Path.GetTempPath(), $"kor-k6-state-{Guid.NewGuid():N}.txt");
            File.WriteAllText(f, body);
            var o = new StringWriter();
            var args = new List<string> { "state", "--module", Lane, "--file", f };
            if (test) args.Add("--test");
            Assert.Equal(0, Program.Run(args.ToArray(), () => null, o, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log"), db));
            File.Delete(f);
            return o.ToString();
        }
        var real = State("REAL STATE", false);
        var said = State("TEST STATE with its checkword", true);
        Assert.Contains("TEST (never a session-start resume)", said);   // = BrainStore.TestStateSaid (a literal: the test also runs on HEAD, red)
        Assert.DoesNotContain("TEST (never a session-start resume)", real);
        foreach (var source in new[] { "startup", "clear", "resume" })
        {
            var s = ModuleState.Load(db, Lane, new ModuleState.Start(BrainStore.CurrentSession(), source), RepoDir());
            Assert.Equal("REAL STATE", s.Resume.Chosen?.ResumeBlock);
            Assert.DoesNotContain(s.Resume.Others, x => x.ResumeBlock.Contains("TEST STATE"));
            Assert.DoesNotContain("TEST STATE", StartRender.Record(s, StartRender.HookBudget, "kor"));
        }
    }

    [Fact]
    public void ATestStateRowIsDroppedByTheChooserToo()
    {
        var now = DateTimeOffset.Now;
        var real = new ResumeRow(1, "REAL", "ian kor state", now.AddMinutes(-5), "s1", false, false);
        var test = new ResumeRow(2, "TEST", "ian kor state TEST", now, "s1", false, false);
        var c = ModuleState.ChooseResume(new[] { real, test }, new ModuleState.Start("s1", "resume"));
        Assert.Equal(1, c.Chosen!.Id);
        Assert.Empty(c.Others);
    }

    [Fact]
    public void LiveTheHookPrintsSessionStartJsonWithinBudgetAndAbsoluteWatchPaths()
    {
        using var db = BrainDb.OpenRolledBack();
        Finding(db, "k6-watched", RecordStore.AtHead(FrameworkDir(), Doc));
        var output = new StringWriter();
        var stdin = JsonSerializer.Serialize(new { session_id = "k6-hook", source = "startup", cwd = RepoDir() });
        Assert.Equal(0, Program.Run(new[] { "start", "--module", Lane, "--part", "record", "--hook", "--cache-dir", CacheDir() }, () => stdin,
                                    output, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log"), db));
        using var doc = JsonDocument.Parse(output.ToString());
        var h = doc.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", h.GetProperty("hookEventName").GetString());
        Assert.True(h.GetProperty("additionalContext").GetString()!.Length <= StartRender.HookBudget);
        var watch = h.GetProperty("watchPaths").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.InRange(watch.Count, 1, 200);
        Assert.All(watch, p => Assert.True(Path.IsPathFullyQualified(p)));
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void LiveGarbageOnStdinStillStartsAndExitsZero(string garbage)
    {
        using var db = BrainDb.OpenRolledBack();
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "start", "--module", Lane, "--part", "record", "--hook", "--cache-dir", CacheDir() }, () => garbage,
                                    output, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-k6-{Guid.NewGuid():N}.log"), db));
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.StartsWith(StartRender.NotReadLine + "\nKNOWN AT START", doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString());
    }

    [Fact]
    public void LiveFrameworksStartIsWithinTwiceItsStepZeroBudget()
    {
        // step 0 (3 Nov, median of 5): Framework board_render 1.41 s, kor_brain render 1.21 s -> 1.41 s. The budget itself
        // is proved by timing the INSTALLED kor.exe 5 times (row 1188); this in-process median of 5 (load + render on a
        // rolled-back unit of work, connection included) is a regression guard with a 2x margin, so a loaded machine or a
        // slow VPN does not turn the suite red without a code change (review row 193), while a 2x slowdown still does.
        var times = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var clock = Stopwatch.StartNew();
            using (var db = BrainDb.OpenRolledBack())
            {
                var s = ModuleState.Load(db, "Framework", new ModuleState.Start(null, "startup"), FrameworkDir(), TimeSpan.FromSeconds(5));
                StartRender.Record(s, StartRender.HookBudget, "kor");
            }
            times.Add(clock.Elapsed.TotalSeconds);
        }
        times.Sort();
        log.WriteLine(string.Join(", ", times.Select(t => t.ToString("0.00"))));
        Assert.True(times[2] < 2 * 1.41, $"median {times[2]:0.00} s");
    }

    [Fact]
    public void LiveTheBoardReadsAndRendersWithinBudget()
    {
        using var b = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var s = BoardState.Load(b, new[] { "Framework" }, withHelpers: true);
        Assert.Equal(new[] { "Framework", "Framework helper" }, s.Owners);
        Assert.Equal("live", s.OwedStatus);
        var text = BoardRender.Render(s, DateTime.Now, StartRender.HookBudget, "cmd");
        Assert.True(text.Length <= StartRender.HookBudget);
        Assert.StartsWith("THE BOARD (source of truth) - KorStandards oversight.vw_Board, Owners 'Framework', 'Framework helper'", text);
        Assert.Contains("IAN-CAUGHT MISTAKES OWED:", text);
    }
}
