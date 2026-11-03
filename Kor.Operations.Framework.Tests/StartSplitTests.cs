using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1198 (3 Nov), PURE: the hook's start split into parts, each under Claude Code's per-hook 10,000-char cap
/// (each hook's additionalContext is measured on its own - hooks docs, read 3 Nov). The record part leaves STATE NOW,
/// OFFICE LESSONS and LEGACY MEMORY to parts of their own and names them; the STATE NOW part carries the section whole
/// (KOR.Drafter's section, 7,330 chars - 7,465 with its citation line - cut at 3,000 before); the MEMORY part's pages carry office + legacy, every line once, in
/// order, split at whole lines; a page past the content is empty; content past the last wired page is cut with a note;
/// a credential value in the legacy index is masked.
/// Does NOT cover: the SQL read (StartSplitLiveTests); whether Claude Code orders the parts as wired (it need not - each
/// part names itself); the settings.json wiring (proved by real sessions). Same-class miss: a part whose hook entry is
/// missing from a repo's settings.json is silently absent - the record part's line names the parts, but cannot see them.
/// </summary>
public class StartSplitTests
{
    private static HandoverState Handover(string section) =>
        new("Framework", "docs/HANDOVER.md", new string('c', 40), "6a28024" + new string('0', 33), section, false, null);

    private static string LongSection(int lines) =>
        "# STATE NOW - x\n" + string.Join("\n", Enumerable.Range(0, lines).Select(i => $"- state line {i:D3} " + new string('s', 60)));

    private static StartRecord Legacy(int id, string name, string? body = null) =>
        new(id, "zz", name, "reference", $"legacy {name} " + new string('l', 100), body, "current", null, null, null, null, null, null, null, null);

    private static List<StartRecord> Office() => Enumerable.Range(0, 30).Select(i => new StartRecord(80_000 + i, "Office", $"office-{i}",
        "feedback", $"office lesson {i} " + new string('o', 80), null, "current", null, null, null, null, null, null, null, null)).ToList();

    private static string Index(int lines) =>
        "# Memory Index\n" + string.Join("\n", Enumerable.Range(0, lines).Select(i => $"- [entry {i:D4}](feedback_entry_{i}.md) - " + new string('m', 120)));

    private static StartSide Side(string? section, IReadOnlyList<StartRecord> records, IReadOnlyList<StartRecord>? office = null) =>
        new("zz", DateTimeOffset.Now, records, section is null ? null : Handover(section)) { Office = office ?? Office() };

    private static ModuleState State(HandoverState h, IReadOnlyList<StartRecord> legacy)
    {
        var records = new List<StartRecord>
        {
            new(1, "zz", ModuleState.FiveQuestionsName, "reference", "five", "1. Where do things stand?", "current",
                "git", "Framework", new string('a', 40), "docs/HANDOVER.md", null, null, null, null),
        };
        records.AddRange(legacy);
        var fresh = new FreshnessReport(new List<Checked>(), new Dictionary<string, string?> { ["Framework"] = new string('c', 40) }, true);
        var resume = new ResumeRow(9, "RESUME-START the resume", "lead", DateTimeOffset.Now, "s1", false, false);
        return new ModuleState("zz", DateTimeOffset.Now, fresh, records, Array.Empty<StartRecord>(), Office(),
                               new ResumeChoice(resume, "test", Array.Empty<ResumeRow>()), null, Array.Empty<StoredEvent>(), Array.Empty<string>())
        { Handover = h };
    }

    [Fact]
    public void TheSplitRecordPartLeavesStateOfficeAndLegacyToTheirPartsAndNamesThem()
    {
        var section = LongSection(100);
        var s = State(Handover(section), new[] { Legacy(7, ModuleState.IndexName, Index(150)) });
        var split = StartRender.Record(s, StartRender.HookBudget, split: true);
        Assert.Equal(StartRender.NotReadLine, split.Split('\n')[0]);
        Assert.Contains(StartRender.PartsLine("zz"), split);
        Assert.DoesNotContain("state line 000", split);
        Assert.DoesNotContain("OFFICE LESSONS (", split);
        Assert.DoesNotContain("LEGACY MEMORY (", split);
        Assert.Contains("RESUME-START the resume", split);
        Assert.True(split.Length <= StartRender.HookBudget, $"{split.Length}");
        // the one page (--text) still carries every section, STATE NOW whole
        var page = StartRender.Record(s, null);
        Assert.Contains(section, page);
        Assert.Contains("OFFICE LESSONS (", page);
        Assert.Contains("LEGACY MEMORY (", page);
        // the one page under a budget still caps STATE NOW at Cap (3,000)
        Assert.Contains($"... [STATE NOW cut at {HandoverState.Cap} of {section.Length} chars", StartRender.Record(s, StartRender.HookBudget));
    }

    [Fact]
    public void TheStatePartCarriesAStateNowOverTheOldCapWhole()
    {
        var section = LongSection(95);   // ~7,300 chars, KOR.Drafter's size on 3 Nov
        Assert.InRange(section.Length, 7000, 8000);
        var text = StartRender.State(Side(section, Array.Empty<StartRecord>()), StartRender.HookBudget);
        Assert.StartsWith("KNOWN AT START - STATE NOW - module 'zz'", text);
        Assert.Contains("STATE NOW - the module's current state, from its handover at HEAD (Framework:docs/HANDOVER.md@ccccccc blob 6a28024):\n" + section, text);
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
    }

    [Fact]
    public void AStatePartOverTheBudgetIsCutAtWholeLinesWithWhereTheRestIs()
    {
        var section = LongSection(200);   // ~15,000
        var text = StartRender.State(Side(section, Array.Empty<StartRecord>()), StartRender.HookBudget);
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
        var lines = text.Split('\n');
        Assert.StartsWith($"... [STATE NOW cut at ", lines[^1]);
        Assert.Contains("the rest: git show HEAD:docs/HANDOVER.md]", lines[^1]);
        Assert.All(lines.Where(l => l.StartsWith("- state line", StringComparison.Ordinal)), l => Assert.Matches(@"^- state line \d{3} s{60}$", l));
    }

    [Fact]
    public void AStatePartWithNoHandoverSectionSaysNotRead()
    {
        var side = new StartSide("zz", DateTimeOffset.Now, Array.Empty<StartRecord>(),
                                 new HandoverState("zz", "docs/HANDOVER.md", null, null, null, false, "repo zz is not on this machine beside this folder"));
        Assert.Contains("STATE NOW: not read - repo zz is not on this machine", StartRender.State(side, StartRender.HookBudget));
    }

    private static string Body(string page) => page.Length == 0 ? "" : page[(page.IndexOf('\n') + 1)..];

    [Fact]
    public void MemoryPagesCarryEveryLineOnceInOrderEachWithinTheBudget()
    {
        var side = Side(null, new[] { Legacy(7, ModuleState.IndexName, Index(120)), Legacy(8, "feedback_one") });   // ~23,500 with office
        var whole = StartRender.Memory(side, 1, 1, null);
        var wholeBody = Body(whole).Split('\n');
        Assert.True(Body(whole).Length > 2 * StartRender.HookBudget, $"{Body(whole).Length}");
        var pages = Enumerable.Range(1, 4).Select(p => StartRender.Memory(side, p, 4, StartRender.HookBudget)).ToList();
        Assert.All(pages, p => Assert.True(p.Length <= StartRender.HookBudget, $"{p.Length}"));
        Assert.Equal("", pages[3]);                                                   // past the content: nothing
        Assert.StartsWith("KNOWN AT START - MEMORY, page 1 of 3 - module 'zz'", pages[0]);
        Assert.StartsWith("KNOWN AT START - MEMORY, page 3 of 3 - module 'zz'", pages[2]);
        var joined = pages.Where(p => p.Length > 0).SelectMany(p => Body(p).Split('\n')).ToList();
        Assert.Equal(wholeBody, joined);                                              // every line, once, in order
        Assert.Contains("OFFICE LESSONS (", pages[0]);
        Assert.Contains("never into a memory file", pages[0]);
        Assert.Contains(joined, l => l.StartsWith("- [entry 0119]", StringComparison.Ordinal));
    }

    [Fact]
    public void MemoryPastTheLastWiredPageIsCutThereWithANote()
    {
        var side = Side(null, new[] { Legacy(7, ModuleState.IndexName, Index(400)) });   // ~55,000
        var last = StartRender.Memory(side, 2, 2, StartRender.HookBudget);
        Assert.True(last.Length <= StartRender.HookBudget, $"{last.Length}");
        Assert.StartsWith("KNOWN AT START - MEMORY, page 2 of 2", last);
        Assert.Matches(@"\.\.\. \[memory continues past page 2 of the session start \(\d+ more page\(s\)\) - kor start --module ""zz"" --part memory --text for all\]$", last);
        var lines = last.Split('\n');
        Assert.Matches(@"^- \[entry \d{4}\]\(feedback_entry_\d+\.md\) - m{120}$", lines[^2]);   // the cut is at a whole line (review row 247)
    }

    [Fact]
    public void ALaneWithNoLegacySaysSoOnPageOneAndPrintsNothingAfter()
    {
        var side = Side(null, Array.Empty<StartRecord>(), Array.Empty<StartRecord>());
        Assert.Contains("LEGACY MEMORY: none - this lane has no imported file memory.", StartRender.Memory(side, 1, 4, StartRender.HookBudget));
        Assert.Equal("", StartRender.Memory(side, 2, 4, StartRender.HookBudget));
        Assert.Throws<ArgumentException>(() => StartRender.Memory(side, 5, 4, StartRender.HookBudget));
    }

    [Fact]
    public void ACredentialValueInTheLegacyIndexIsMaskedOnTheMemoryPart()
    {
        // synthetic value (SecretGuardTests' own sample), never a real one
        var side = Side(null, new[] { Legacy(7, ModuleState.IndexName, "# Memory Index\n- the db: Server=srv;Database=db;User Id=u;Password=Zx9vQ2mT;") });
        var page = StartRender.Memory(side, 1, 4, StartRender.HookBudget);
        Assert.DoesNotContain("Zx9vQ2mT", page);
        Assert.Contains("[REDACTED len=8]", page);
    }

    [Fact]
    public void ChunksAreWholeLinesAndNeverOverTheMax()
    {
        var text = "a\n" + new string('b', 25) + "\nccc\n" + string.Join("\n", Enumerable.Range(0, 30).Select(i => $"line {i}"));
        var chunks = StartRender.Chunk(text, 10);
        Assert.All(chunks, c => Assert.True(c.Length <= 10, c));
        Assert.Equal(text.Replace("\n", ""), string.Concat(chunks).Replace("\n", ""));
        Assert.Contains("line 29", chunks);
    }
}

/// <summary>
/// Board row 1198, LIVE (rolled back): the side parts through the CLI on a real module (KOR.RevitTools: a legacy index
/// over one page, a STATE NOW). Covers: the JSON is SessionStart's, each part within 9,900; a page past the content prints
/// nothing; --page outside --pages and --page on another part are refused; the parts write nothing (the module's
/// brain.Memory and brain.Event counts equal before and after, read on their own connection).
/// Does NOT cover: another module's content (the 6 modules are measured by the cold test).
/// </summary>
[Collection(LiveDb.Name)]
public class StartSplitLiveTests
{
    private const string Module = "KOR.RevitTools";

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static (long Memory, long Events) Counts() => BrainDb.Read(KorStandardsConnection.Login.Brain, d =>
    {
        using var c = d.Command("SELECT (SELECT COUNT_BIG(*) FROM brain.Memory WHERE Lane = @m), (SELECT COUNT_BIG(*) FROM brain.Event WHERE Module = @m);", 30);
        c.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = Module;
        using var r = c.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
    });

    private static (int Exit, string Out) Run(BrainDb db, params string[] args)
    {
        var output = new StringWriter();
        var cache = Path.Combine(Path.GetTempPath(), $"kor-1198-cache-{Guid.NewGuid():N}");
        var stdin = JsonSerializer.Serialize(new { session_id = "k1198", source = "startup", cwd = RepoDir() });
        var exit = Program.Run(new[] { "start", "--module", Module }.Concat(args).Concat(new[] { "--hook", "--cache-dir", cache }).ToArray(),
                               () => stdin, output, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-1198-{Guid.NewGuid():N}.log"), db);
        return (exit, output.ToString());
    }

    private static string Context(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var h = doc.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", h.GetProperty("hookEventName").GetString());
        return h.GetProperty("additionalContext").GetString()!;
    }

    [Fact]
    public void LiveTheSidePartsPrintSessionStartJsonWithinBudgetAndWriteNothing()
    {
        var before = Counts();
        using (var db = BrainDb.OpenRolledBack())
        {
            var state = Run(db, "--part", "state");
            Assert.Equal(0, state.Exit);
            var st = Context(state.Out);
            Assert.StartsWith($"KNOWN AT START - STATE NOW - module '{Module}'", st);
            Assert.Contains("STATE NOW - the module's current state, from its handover at HEAD (KOR.RevitTools:docs/HANDOVER.md@", st);
            Assert.True(st.Length <= StartRender.HookBudget);
            var p1 = Run(db, "--part", "memory", "--page", "1", "--pages", "4");
            var p2 = Run(db, "--part", "memory", "--page", "2", "--pages", "4");
            var p3 = Run(db, "--part", "memory", "--page", "3", "--pages", "4");
            // 3 Nov 12:53-12:55: 41 Office lessons were added and the lane's memory grew past 4 pages, so where the legacy
            // heading lands and whether page 4 is empty are the live data's, not the code's. The heading is on one of the
            // wired pages; "past the content prints nothing" is proved on a page no live lane reaches (20 of 20).
            var p4 = Run(db, "--part", "memory", "--page", "4", "--pages", "4");
            var past = Run(db, "--part", "memory", "--page", "20", "--pages", "20");
            Assert.Equal((0, 0, 0, 0, 0), (p1.Exit, p2.Exit, p3.Exit, p4.Exit, past.Exit));
            var c1 = Context(p1.Out);
            Assert.StartsWith($"KNOWN AT START - MEMORY, page 1 of ", c1);
            var wired = new[] { p1, p2, p3, p4 }.Where(p => p.Out.Length > 0).Select(p => Context(p.Out)).ToList();
            Assert.Contains("LEGACY MEMORY (", string.Concat(wired));
            Assert.All(wired, c => Assert.True(c.Length <= StartRender.HookBudget));
            Assert.Equal("", past.Out);                                                // past the content: nothing printed
            var rec = Context(Run(db, "--part", "record").Out);
            Assert.Contains(StartRender.PartsLine(Module), rec);
            Assert.DoesNotContain("LEGACY MEMORY (", rec);
        }
        Assert.Equal(before, Counts());
    }

    /// <summary>
    /// The red test for row 1198 on the real handovers: the STATE NOW part holds the section as `git show HEAD:path` gives
    /// it (read here with git itself, not through kor), whole, for each KOR module. Before 1198 KOR.Drafter's was cut at
    /// 3,000 of 7,330 chars and this failed.
    /// </summary>
    [Theory]
    [InlineData("KOR.Drafter")]
    [InlineData("Framework")]
    [InlineData("Operations")]
    [InlineData("NetworkOps")]
    [InlineData("KOR.RevitTools")]
    [InlineData("KOR Inspections Bookings")]
    public void LiveTheStatePartHoldsTheHandoversStateNowWholeAsGitShowsIt(string module)
    {
        var dir = RepoLocator.Find(module, RepoDir());
        Assert.NotNull(dir);
        using var db = BrainDb.OpenRolledBack();
        var output = new StringWriter();
        var stdin = JsonSerializer.Serialize(new { session_id = "k1198", source = "startup", cwd = dir });
        Assert.Equal(0, Program.Run(new[] { "start", "--module", module, "--part", "state", "--hook", "--cache-dir",
                                            Path.Combine(Path.GetTempPath(), $"kor-1198-cache-{Guid.NewGuid():N}") },
                                    () => stdin, output, new StringWriter(), Path.Combine(Path.GetTempPath(), $"kor-1198-{Guid.NewGuid():N}.log"), db));
        var text = Context(output.ToString());
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
        var cite = System.Text.RegularExpressions.Regex.Match(text, $@"from its handover at HEAD \({System.Text.RegularExpressions.Regex.Escape(module)}:(?<p>[^@]+)@");
        Assert.True(cite.Success, text[..Math.Min(400, text.Length)]);
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"show HEAD:{cite.Groups["p"].Value}")
        {
            WorkingDirectory = dir!, RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var file = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var section = HandoverState.StateNow(file);
        Assert.NotNull(section);
        Assert.DoesNotContain("[STATE NOW cut at", text);
        Assert.Contains(SecretGuard.Mask(section)!, text);
    }

    /// <summary>Review row 247: the board part through the CLI hook path (an empty-page check placed before the board
    /// block threw on it, and a hook failure is silent). THE BOARD must arrive, within budget.</summary>
    [Fact]
    public void LiveTheBoardHookStillPrintsTheBoard()
    {
        using var db = BrainDb.OpenRolledBack();
        var r = Run(db, "--part", "board", "--owner", "Revit Tools");
        Assert.Equal(0, r.Exit);
        var text = Context(r.Out);
        Assert.StartsWith("THE BOARD", text);
        Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
    }

    [Theory]
    [InlineData("--part", "memory", "--page", "3", "--pages", "2")]
    [InlineData("--part", "state", "--page", "1")]
    [InlineData("--part", "memory", "--page", "x")]
    [InlineData("--part", "nonsense")]
    public void LiveABadPartOrPageIsRefused(params string[] args)
    {
        // without --hook (a hook-mode failure is logged and exits 0 by design, review row 146)
        using var db = BrainDb.OpenRolledBack();
        var error = new StringWriter();
        Assert.Equal(1, Program.Run(new[] { "start", "--module", Module, "--text" }.Concat(args).ToArray(), () => null, new StringWriter(), error,
                                    Path.Combine(Path.GetTempPath(), $"kor-1198-{Guid.NewGuid():N}.log"), db));
        Assert.StartsWith("kor: ", error.ToString());
    }
}
