using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1569, PURE: THE PAGES carry every row with its full description, the index, the section and the resume whole; each page
/// fits the hook; too few wired hook entries are said plainly on the last one and on the record part; the gate refuses until every
/// page and one subject's bodies are in the transcript; the cold-test grader fails a known-bad run.
/// Does NOT cover: the real lanes and settings (StartPagesLiveTests) nor a real session (kor start-proof). Same-class miss: a table
/// the start does not read at all.
/// </summary>
public class StartPagesTests
{
    private static StartRecord Row(int id, string name, string description, string? lane = "zz", string? sourceKind = "git", string status = "current", string? body = null) =>
        new(id, lane!, name, "finding", description, body, status, sourceKind, sourceKind is null ? null : "Framework", sourceKind is null ? null : new string('a', 40),
            sourceKind is null ? null : "docs/x.md", null, null, null, null);

    private static string Long(int id) => $"description of {id} " + string.Join(" ", Enumerable.Range(0, 90).Select(i => $"word{i}")) + $" tail{id}";

    private static StartSide Side(int rows, int imported, string? index = null)
    {
        var records = new List<StartRecord> { Row(1, ModuleState.FiveQuestionsName, "five") };
        records.AddRange(Enumerable.Range(1000, rows).Select(i => Row(i, $"sourced-{i}", Long(i), status: i % 7 == 0 ? "stale" : "current")));
        records.AddRange(Enumerable.Range(5000, imported).Select(i => Row(i, $"imported-{i}", Long(i), sourceKind: null)));
        if (index is not null) records.Add(Row(2, ModuleState.IndexName, "index", sourceKind: null, body: index));
        var section = "# STATE NOW\n" + string.Join("\n", Enumerable.Range(0, 300).Select(i => $"- state line {i:D3} " + new string('s', 60)));
        var handover = new HandoverState("Framework", "docs/HANDOVER.md", new string('c', 40), new string('d', 40), section, false, null);
        return new StartSide("zz", DateTimeOffset.Now, records, handover)
        {
            Office = Enumerable.Range(9000, 30).Select(i => Row(i, $"office-{i}", Long(i), lane: "Office", sourceKind: null)).ToList(),
        };
    }

    private static ResumeChoice Resume(int chars) =>
        new(new ResumeRow(9, "RESUME-START " + new string('r', chars) + " RESUME-END", "lead", DateTimeOffset.Now, "s1", false, true), "test", Array.Empty<ResumeRow>());

    [Fact]
    public void EveryRowTheIndexTheSectionAndTheResumeAreOnThePagesWhole()
    {
        var index = string.Join("\n", Enumerable.Range(0, 400).Select(i => $"- [line {i}](file_{i}.md) - index text {i}"));
        var side = Side(300, 500, index);
        var s = StartRender.BuildPages(side, Resume(16_000), null);
        var all = string.Join("\n", s.Pages);
        var rows = StartRender.PageRows(side).ToList();
        var present = rows.Count(r => all.Contains($"- record #{r.Id} [{r.Kind}] {r.Name} - {r.Description}", StringComparison.Ordinal));
        Assert.Equal(rows.Count, present);   // X of Y, and Y = 300 + 500 + 30
        Assert.Equal(830, rows.Count);
        foreach (var l in index.Split('\n')) Assert.Contains(l, all);
        Assert.Contains("- state line 299 ", all);
        Assert.Contains("RESUME-END", all);
        Assert.DoesNotContain("resume cut", all);
        Assert.Contains("[STALE]", all);
        Assert.All(s.Pages, p => Assert.True(p.Length <= StartRender.PageRoom, $"{p.Length}"));
    }

    [Fact]
    public void AResumeLongerThanAPageSpansPagesAndComesBackByteForByte()
    {
        var resume = "first\n" + string.Join("\n", Enumerable.Range(0, 900).Select(i => $"resume line {i}")) + "\nlast";
        var s = StartRender.BuildPages(Side(1, 0), new ResumeChoice(new ResumeRow(9, resume, "lead", DateTimeOffset.Now, "s1", false, true), "t", Array.Empty<ResumeRow>()), null);
        Assert.Contains(resume, string.Join("\n", s.Pages));
    }

    [Fact]
    public void EachPageFitsTheHookAndAPagePastTheContentIsEmpty()
    {
        var s = StartRender.BuildPages(Side(300, 300), Resume(100), null);
        Assert.True(s.Pages.Count > 4);
        var wired = s.Pages.Count + 3;
        for (var p = 1; p <= s.Pages.Count; p++)
        {
            var text = StartRender.Page(s, p, wired, "kor", "S");
            Assert.StartsWith($"{StartRender.PagesHead}{p} of {s.Pages.Count} - module 'zz'", text);
            Assert.True(text.Length <= StartRender.HookBudget, $"{text.Length}");
        }
        Assert.Equal("", StartRender.Page(s, s.Pages.Count + 1, wired, "kor", "S"));
    }

    [Fact]
    public void TooFewWiredPagesAreSaidPlainlyOnTheLastOneAndOnTheRecordPart()
    {
        var s = StartRender.BuildPages(Side(300, 300), Resume(100), null);
        var last = StartRender.Page(s, 4, 4, "kor", "S");
        Assert.Contains($"!! MEMORY LEFT OUT: {s.Pages.Count} pages, 4 wired - pages 5 to {s.Pages.Count} are NOT in your context", last);
        Assert.Contains("--part pages --page 5", last);
        Assert.True(last.Length <= StartRender.HookBudget, $"{last.Length}");
        var line = StartRender.PagesLine("zz", new PagesWiring(4, s.Pages.Count), "kor", "S");
        Assert.Contains("!! MEMORY LEFT OUT", line);
        Assert.DoesNotContain("LEFT OUT", StartRender.PagesLine("zz", new PagesWiring(s.Pages.Count, s.Pages.Count), "kor", "S"));
    }

    [Fact]
    public void TheRecordPartLeavesTheResumeToThePagesAndNeverCutsIt()
    {
        var fresh = new FreshnessReport(new List<Checked>(), new Dictionary<string, string?> { ["Framework"] = new string('c', 40) }, true);
        var state = new ModuleState("zz", DateTimeOffset.Now, fresh, new List<StartRecord>(), Array.Empty<StartRecord>(), Array.Empty<StartRecord>(), Resume(7000), null,
                                    Array.Empty<StoredEvent>(), Array.Empty<string>());
        var r = StartRender.Record(state, StartRender.HookBudget, split: true, new PagesWiring(40, 20), "kor", "S");
        Assert.DoesNotContain("RESUME-START", r);
        Assert.DoesNotContain("resume cut", r);
        Assert.Contains("whole on THE PAGES", r);
        Assert.Contains("'pages X of Y held, bodies A of B read for subject S'", r);
    }

    private static string Attachment(string text) => "{\"type\":\"attachment\",\"attachment\":{\"content\":[\"" + text + "\"]}}";
    private static string ToolResult(string text) => "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"" + text + "\"}]}}";
    private static string Page(int n, int m) => $"{StartRender.PagesHead}{n} of {m} - module 'zz'";
    private static string Bodies(string subject, int n, int m) => $"{BodyRead.Head}{subject}' - page {n} of {m} - 1 of 2 bodies";

    [Fact]
    public void TheGateRefusesUntilEveryPageAndOneSubjectsBodiesAreInTheTranscript()
    {
        var lines = new List<string> { Attachment(Page(1, 3)), Attachment(Page(3, 3)) };
        var d = StartGate.Decide(StartGate.Scan(lines), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("MEMORY LEFT OUT: you hold pages 2 of 3; missing 2", d.Reason);
        lines.Add(ToolResult(Page(2, 3)));
        d = StartGate.Decide(StartGate.Scan(lines), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("The bodies are not read yet", d.Reason);
        lines.Add(ToolResult(Bodies("peace river", 1, 2)));
        Assert.False(StartGate.Decide(StartGate.Scan(lines), "zz", "kor", "S").Allow);
        lines.Add(ToolResult(Bodies("peace river", 2, 2)));
        Assert.True(StartGate.Decide(StartGate.Scan(lines), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void TheGateCountsNothingTheAssistantWroteAndACompactionOwesTheBodiesAgain()
    {
        var mine = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + Page(1, 1) + Bodies("x", 1, 1) + "\"}]}}";
        Assert.Equal(0, StartGate.Scan(new[] { mine }).Total);
        Assert.False(StartGate.Decide(StartGate.Scan(new[] { mine }), "zz", "kor", "S").Allow);
        var lines = new[] { Attachment(Page(1, 1)), ToolResult(Bodies("x", 1, 1)), "{\"isCompactSummary\":true}" };
        var d = StartGate.Decide(StartGate.Scan(lines), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("The bodies are not read yet", d.Reason);
    }

    [Theory]
    [InlineData("Bash", "kor read --module X --subject \"peace river\"", true)]
    [InlineData("Bash", "\"C:/KOR-Operations/kor/kor.exe\" read --module \"X\" --subject a --page 2", true)]
    [InlineData("PowerShell", "& \"C:/KOR-Operations/kor/kor.exe\" start --module X --part pages --page 2 --pages 3 --text", true)]
    [InlineData("Bash", "kor read --module X --subject a; rm -rf /", false)]
    [InlineData("Bash", "kor read --module X --subject a && git push", false)]
    [InlineData("Bash", "kor start --module X --part board --text", false)]
    [InlineData("Bash", "git status", false)]
    [InlineData("Read", "kor read", false)]
    public void TheGatePassesOnlyTheReadAlone(string tool, string command, bool allowed) =>
        Assert.Equal(allowed, StartGate.IsReadCommand(tool, command));

    [Fact]
    public void ASubjectIsEveryWordFromAWordsStartNamesFirstThenBodies()
    {
        var rows = new[]
        {
            new BodyRead.Row(1, "zz", "peace-river-go-no-go", "finding", "tender closes 13 Nov", "body one"),
            new BodyRead.Row(2, "zz", "other", "finding", "a driver for peace", "body two"),
            new BodyRead.Row(3, "zz", "third", "finding", "nothing", "peace river is only in this body"),
        };
        Assert.Equal(new[] { 1 }, BodyRead.Match(rows, "peace river").Select(r => r.Id));
        Assert.Equal(new[] { 3 }, BodyRead.Match(rows, "this body").Select(r => r.Id));
        Assert.Empty(BodyRead.Match(rows, "zzz"));
    }

    [Fact]
    public void BodiesComeWholeAcrossPagesEachSayingXOfY()
    {
        var big = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"body line {i}"));
        var rows = new List<BodyRead.Row> { new(1, "zz", "peace-a", "finding", "d", big), new(2, "zz", "peace-b", "finding", "d", "short body") };
        var first = BodyRead.Pages(rows, "peace", "zz", "kor", 1, false, out var done);
        Assert.False(done);
        Assert.StartsWith($"{BodyRead.Head}peace' - page 1 of ", first);
        Assert.Contains("NEXT:", first);
        var pages = int.Parse(first.Split(" of ")[1].Split(' ')[0]);
        var all = string.Join("\n", Enumerable.Range(1, pages).Select(p => BodyRead.Pages(rows, "peace", "zz", "kor", p, false, out _)));
        Assert.Contains("body line 2999", all);
        Assert.Contains("short body", all);
        Assert.Contains("ALL 2 of 2 bodies", all);
    }

    [Fact]
    public void TooManyBodiesAreNotCutTheCostIsShownAndAllTakesThem()
    {
        var rows = Enumerable.Range(0, 20).Select(i => new BodyRead.Row(i, "zz", $"n{i}", "finding", "d", new string('b', 30_000))).ToList();
        var refused = BodyRead.Pages(rows, "n", "zz", "kor", 1, false, out var done);
        Assert.False(done);
        Assert.StartsWith("NOT READ: subject", refused);
        Assert.DoesNotContain(BodyRead.Head, refused);
        Assert.StartsWith(BodyRead.Head, BodyRead.Pages(rows, "n", "zz", "kor", 1, true, out _));
    }

    private static readonly List<ColdProof.Question> Qs = new()
    {
        new("q1", "ask", "the last six words here now"),
        new("q2", "ask", "another six words of the key"),
    };

    private const string Given = "KNOWN AT START - PAGES, page 1 of 2 - x\nthe last six words here now\nKNOWN AT START - PAGES, page 2 of 2 - x\nanother six words of the key";

    [Fact]
    public void TheGraderPassesARightRun()
    {
        var g = ColdProof.Grade(Qs, "{\"q1\":\"the last six words here now\",\"q2\":\"Another six words of the key.\",\"pages_held\":2,\"pages_total\":2}", Given);
        Assert.Equal(g.Of, g.Passed);
        Assert.Equal(0, g.Invented);
    }

    [Fact]
    public void TheGraderFailsAKnownBadRunInventedWrongOrNotGiven()
    {
        // wrong words, though given
        var wrong = ColdProof.Grade(Qs, "{\"q1\":\"something else entirely\",\"q2\":\"NOT IN CONTEXT\",\"pages_held\":2,\"pages_total\":2}", Given);
        Assert.Equal(1, wrong.Passed);
        // the right words with nothing delivered: invented, never passed
        var invented = ColdProof.Grade(Qs, "{\"q1\":\"the last six words here now\",\"q2\":\"another six words of the key\",\"pages_held\":2,\"pages_total\":2}", "no pages here");
        Assert.Equal(0, invented.Passed);
        Assert.Equal(2, invented.Invented);
        // a wrong count of the pages; and a reply that is no JSON
        Assert.False(ColdProof.Grade(Qs, "{\"q1\":\"the last six words here now\",\"q2\":\"another six words of the key\",\"pages_held\":1,\"pages_total\":2}", Given).PagesRight);
        Assert.Equal(0, ColdProof.Grade(Qs, "I could not do it", Given).Passed);
    }

    [Fact]
    public void TheKeyAndTheTokensComeFromTheTranscript()
    {
        var lines = new[]
        {
            "{\"type\":\"attachment\",\"attachment\":{\"type\":\"hook_additional_context\",\"hookEvent\":\"SessionStart\",\"content\":[\"page text\"]}}",
            "{\"type\":\"attachment\",\"attachment\":{\"type\":\"hook_additional_context\",\"hookEvent\":\"PostToolUse\",\"content\":[\"later\"]}}",
            "{\"type\":\"assistant\",\"message\":{\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":50}}}",
            "{\"type\":\"assistant\",\"message\":{\"usage\":{\"input_tokens\":9,\"cache_creation_input_tokens\":999,\"cache_read_input_tokens\":999}}}",
        };
        Assert.Equal("page text", ColdProof.Delivered(lines));
        Assert.Equal(152, ColdProof.TokensAtRequestOne(lines));
    }

    private const string Settings = "{\"hooks\":{\"SessionStart\":[{\"hooks\":[" +
        "{\"command\":\"K start --module \\\"M\\\" --part record --hook\"},{\"command\":\"K start --module \\\"M\\\" --part state --hook\"}," +
        "{\"command\":\"K start --module \\\"M\\\" --part memory --page 1 --pages 4 --hook\"},{\"command\":\"K start --module \\\"M\\\" --part records --page 1 --pages 4 --hook\"}," +
        "{\"command\":\"K start --module \\\"M\\\" --part board --owner M --hook\"},{\"command\":\"K capture-start --hook\"},{\"command\":\"K enroll-check --hook\"}]}]}}";

    [Fact]
    public void TheWiringReplacesTheOldPartsAndKeepsEveryOtherLine()
    {
        var w = ColdProof.Wiring(Settings, "M", "C:/x y/kor.exe", 3);
        Assert.Equal(new[]
        {
            "\"C:/x y/kor.exe\" start --module \"M\" --part record --pages-wired 3 --hook",
            "\"C:/x y/kor.exe\" start --module \"M\" --part pages --page 1 --pages 3 --hook",
            "\"C:/x y/kor.exe\" start --module \"M\" --part pages --page 2 --pages 3 --hook",
            "\"C:/x y/kor.exe\" start --module \"M\" --part pages --page 3 --pages 3 --hook",
            "K start --module \"M\" --part board --owner M --hook", "K capture-start --hook", "K enroll-check --hook",
        }, w);
    }

    private static string WiredSettings(int wired, bool gate, int? recordSays = null, int? skip = null)
    {
        var hooks = new List<string> { $"C:/KOR-Operations/kor/kor.exe start --module M --part record --pages-wired {recordSays ?? wired} --hook" };
        hooks.AddRange(Enumerable.Range(1, wired).Where(p => p != skip).Select(p => $"C:/KOR-Operations/kor/kor.exe start --module M --part pages --page {p} --pages {wired} --hook"));
        var start = string.Join(",", hooks.Select(h => "{\"type\":\"command\",\"command\":\"" + h + "\"}"));
        var pre = gate ? "\"PreToolUse\":[{\"matcher\":\"*\",\"hooks\":[{\"type\":\"command\",\"command\":\"C:/KOR-Operations/kor/kor.exe start-gate --module M --hook\"}]}]," : "";
        return "{\"hooks\":{" + pre + "\"SessionStart\":[{\"hooks\":[" + start + "]}]}}";
    }

    [Fact]
    public void EnrollSaysWhatTheWiringLacksOnce_OnThePages()
    {
        string[] Findings(string s) => EnrollCheck.SettingsFindings(s, "M").Where(f => f.Contains("pages", StringComparison.Ordinal) || f.Contains("start-gate", StringComparison.Ordinal) || f.Contains("--part state", StringComparison.Ordinal) || f.Contains("--part memory", StringComparison.Ordinal) || f.Contains("--part records", StringComparison.Ordinal)).ToArray();
        Assert.Empty(Findings(WiredSettings(EnrollCheck.PagesWired, gate: true)));
        Assert.Contains(Findings(WiredSettings(EnrollCheck.PagesWired, gate: false)), f => f.Contains("start-gate", StringComparison.Ordinal));
        Assert.Contains(Findings(WiredSettings(EnrollCheck.PagesWired - 1, gate: true)), f => f.Contains("under the", StringComparison.Ordinal));
        Assert.Contains(Findings(WiredSettings(EnrollCheck.PagesWired, gate: true, skip: 7)), f => f.Contains("--page 7 of", StringComparison.Ordinal));
        Assert.Contains(Findings(WiredSettings(EnrollCheck.PagesWired, gate: true, recordSays: 4)), f => f.Contains("--pages-wired 4", StringComparison.Ordinal));
    }
}

/// <summary>
/// Row 1569, LIVE on the real KorStandards (read only) and each repo's real settings.json on this PC: on each of the 6 lanes, X of Y rows on
/// the pages with Y = X; the pages needed are within what a repo wires; and a repo that has gone onto the pages wires enough. A repo still on the
/// old wiring (until Ian's go) is named on its own check, not failed. Does NOT cover: a real session (kor start-proof).
/// </summary>
public class StartPagesLiveTests
{
    public static readonly TheoryData<string, string> Repos = new()
    {
        { "Framework", "Framework" }, { "Operations", "Operations" }, { "NetworkOps", "NetworkOps" }, { "KOR.RevitTools", "KOR.RevitTools" },
        { "KOR Inspections Bookings", "KOR Inspections Bookings" }, { "KOR.Drafter", "KOR.Drafter" },
    };

    [Theory]
    [MemberData(nameof(Repos))]
    public void TheLanesWholeMemoryIsOnThePagesAndWithinTheWiring(string module, string folder)
    {
        var dir = Path.Combine(@"C:\VIsual Studio Projects", folder);
        var (side, resume, end) = BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadPages(d, module, new ModuleState.Start(null, "startup"), dir));
        var s = StartRender.BuildPages(side, resume, end);
        var all = string.Join("\n", s.Pages);
        var rows = StartRender.PageRows(side).ToList();
        var missing = rows.Where(r => !all.Contains($"- record #{r.Id} [{r.Kind}] ", StringComparison.Ordinal)
                                      || !all.Contains(SecretGuard.Mask(r.Description)!, StringComparison.Ordinal)).Select(r => r.Id).ToList();
        Assert.True(missing.Count == 0, $"{module}: {rows.Count - missing.Count} of {rows.Count} rows on the pages; left out: {string.Join(", ", missing.Take(20))}");
        Assert.True(rows.Count > 0, module);
        if (resume.Chosen is { } r0) Assert.Contains(SecretGuard.Mask(r0.ResumeBlock.Trim())!, all);
        Assert.All(s.Pages, p => Assert.True(p.Length <= StartRender.PageRoom));
        Assert.True(s.Pages.Count <= EnrollCheck.PagesWired, $"{module} needs {s.Pages.Count} pages, {EnrollCheck.PagesWired} are wired: bring EnrollCheck.PagesWired and every repo's settings up");
    }

    [Theory]
    [MemberData(nameof(Repos))]
    public void ARepoOnThePagesWiresEnough(string module, string folder)
    {
        var settings = File.ReadAllText(Path.Combine(@"C:\VIsual Studio Projects", folder, ".claude", "settings.json"));
        if (!settings.Contains("--part pages", StringComparison.Ordinal)) return;   // still on the old wiring: waits for Ian's go, said in docs/START-FULL-MEMORY.md
        var lapsed = EnrollCheck.SettingsFindings(settings, module).Where(f => f.Contains("pages", StringComparison.Ordinal) || f.Contains("start-gate", StringComparison.Ordinal)).ToList();
        Assert.True(lapsed.Count == 0, string.Join("; ", lapsed));
    }
}
