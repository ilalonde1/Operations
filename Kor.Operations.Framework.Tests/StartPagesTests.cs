using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1569, PURE: THE PAGES carry every row with its full description, the index, the section and the resume whole; each page
/// fits the hook; too few wired hook entries are said plainly on the last one and on the record part; the gate refuses until every
/// page and one subject's bodies (or a read that found the subject in no body) are in the transcript and the session has said so with
/// those numbers, and takes the read only from the kor it runs as; the cold-test grader fails a known-bad run.
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
    private static string Bodies(string subject, int n, int m, string lane = "zz") => $"{BodyRead.Head}{subject}' - page {n} of {m} - 2 of 2 bodies read through this page (lanes {lane} and Office).";
    private static string Said(string text) => "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}}";

    [Fact]
    public void ARealReadOfZeroBodiesDoesNotOpenTheGate()
    {
        var zero = BodyRead.Pages(new List<BodyRead.Row>(), "zzz", "zz", "kor", 1, false, out _).Replace("\n", " ", StringComparison.Ordinal);
        Assert.False(StartGate.Decide(StartGate.Scan(new[] { Attachment(Page(1, 1)), ToolResult(zero) }, "zz"), "zz", "kor", "S").Allow, zero);
    }

    [Fact]
    public void AHeadSayingZeroOfZeroIsNoRead()
    {
        var old = $"{BodyRead.Head}zzz' - page 1 of 1 - 0 of 0 bodies read through this page (lanes zz and Office).";
        Assert.False(StartGate.Decide(StartGate.Scan(new[] { Attachment(Page(1, 1)), ToolResult(old) }, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void ReadingIsNotEnoughTheSessionMustSayItBeforeItsFirstWork()
    {
        var lines = new List<string> { Attachment(Page(1, 1)), ToolResult(Bodies("peace river", 1, 1)) };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("pages 1 of 1 held, bodies 2 of 2 read for subject peace river", d.Reason);   // the exact words to say
        lines.Add(Said("pages 1 of 1 held, bodies 9 of 9 read for subject peace river"));              // wrong count
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
        lines.Add(Said("Pages 1 of 1 held, bodies 2 of 2 read for subject **Peace River**."));
        Assert.True(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void WhatTheSessionSaidBeforeTheReadDoesNotCount()
    {
        var lines = new[] { Attachment(Page(1, 1)), Said("pages 1 of 1 held, bodies 2 of 2 read for subject x"), ToolResult(Bodies("x", 1, 1)) };
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void AnotherLanesBodiesAreNotThisSessionsRead()
    {
        var lines = new[] { Attachment(Page(1, 1)), ToolResult(Bodies("x", 1, 1, "another lane")), Said("pages 1 of 1 held, bodies 2 of 2 read for subject x") };
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void OnlyTheKorTheGateItselfIsPassesAsTheRead()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var transcript = Path.Combine(dir, "t.jsonl");
            File.WriteAllLines(transcript, new[] { Attachment(Page(1, 1)) });
            string Gate(string command)
            {
                var o = new StringWriter();
                Assert.Equal(0, Program.Run(new[] { "start-gate", "--module", "zz", "--cache-dir", dir, "--hook" }, () => GateInput(transcript, "Bash", command), o,
                                            new StringWriter(), Path.Combine(dir, "log.txt")));
                return o.ToString();
            }
            var self = Environment.ProcessPath!.Replace('\\', '/');
            Assert.Equal("", Gate($"\"{self}\" read --module zz --subject x"));
            foreach (var fake in new[] { "./kor", "kor", "C:/Temp/kor.exe", "/tmp/kor", Path.GetDirectoryName(self)!.Replace('\\', '/') + "/kor.exe" })
                Assert.Contains("\"permissionDecision\":\"deny\"", Gate($"\"{fake}\" read --module zz --subject x"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheGateRefusesUntilEveryPageAndOneSubjectsBodiesAreInTheTranscript()
    {
        var lines = new List<string> { Attachment(Page(1, 3)), Attachment(Page(3, 3)) };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("MEMORY LEFT OUT: you hold pages 2 of 3; missing 2", d.Reason);
        lines.Add(ToolResult(Page(2, 3)));
        d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("The bodies are not read yet", d.Reason);
        lines.Add(ToolResult(Bodies("peace river", 1, 2)));
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
        lines.Add(ToolResult(Bodies("peace river", 2, 2)));
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
        lines.Add(Said("pages 3 of 3 held, bodies 2 of 2 read for subject peace river"));
        Assert.True(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    private static readonly BodyRead.Row[] Lane =
    {
        new(1, "zz", "peace-river", "finding", "tender closes", "body one"),
        new(2, "zz", "other", "finding", "something else", "river in the body"),
    };

    [Fact]
    public void WhatARealReadPrintsIsWhatTheGateCounts()
    {
        var read = BodyRead.Read(Lane, "Peace River", "zz", "kor", 1, false);
        var lines = new List<string> { Attachment(Page(1, 1)), ToolResult(read.Replace("\n", " ", StringComparison.Ordinal)) };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("'pages 1 of 1 held, bodies 1 of 1 read for subject peace river'", d.Reason);
        lines.Add(Said("pages 1 of 1 held, bodies 1 of 1 read for subject peace river"));
        Assert.True(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void ASubjectInNoBodyIsToldSoAndGoesOnOnlyBySayingSo()
    {
        var none = BodyRead.Read(Lane, "zzz qqq", "zz", "kor", 1, false);
        Assert.StartsWith(BodyRead.NothingHead + "zzz qqq' - 0 of 0 bodies", none);
        Assert.Contains("NOTHING WAS READ", none);
        var lines = new List<string> { Attachment(Page(1, 1)), ToolResult(none) };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("You read 0 of 0 bodies for subject 'zzz qqq': NOTHING WAS READ", d.Reason);
        Assert.Contains("pages 1 of 1 held, bodies 0 of 0 for subject zzz qqq: it truly matches nothing", d.Reason);
        lines.Add(Said("pages 1 of 1 held, bodies 0 of 0 for subject aaa: it truly matches nothing"));   // a subject never read
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
        lines.Add(Said("I read it."));
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
        lines.Add(Said("Pages 1 of 1 held, bodies 0 of 0 for subject 'zzz qqq': it truly matches nothing."));
        d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.True(d.Allow);
        Assert.Contains("truly matches nothing", d.Reason);
        lines.Add("{\"isCompactSummary\":true}");                                                         // compaction owes the sentence again
        lines.Add(Attachment(Page(1, 1)));
        Assert.False(StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Allow);
    }

    [Fact]
    public void ASubjectWithAWordInSomeBodyIsNotNothingAndNamesTheReadToTry()
    {
        const string exe = "C:/KOR-Operations/kor/kor.exe";
        var partial = BodyRead.Read(Lane, "river zzz", "zz", exe, 1, false);
        Assert.StartsWith("NOT READ: subject 'river zzz' matches 0 of 2 bodies", partial);
        Assert.Contains("river 2, zzz 0", partial);
        Assert.DoesNotContain(BodyRead.NothingHead, partial);
        Assert.DoesNotContain(BodyRead.Head, partial);
        Assert.True(StartGate.IsReadCommand("Bash", partial[partial.IndexOf('"')..], exe), partial);
        var lines = new[]
        {
            Attachment(Page(1, 1)), ToolResult(partial.Replace("\"", "'", StringComparison.Ordinal)),
            Said("pages 1 of 1 held, bodies 0 of 0 for subject river zzz: it truly matches nothing"),
        };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", exe, "S");
        Assert.False(d.Allow);
        Assert.Contains("The bodies are not read yet", d.Reason);
    }

    [Theory]
    [InlineData("C:/KOR-Operations/kor/kor.exe", true)]
    [InlineData("\"C:/KOR-Operations/kor/kor.exe\"", true)]
    [InlineData("C:\\KOR-Operations\\kor\\kor.exe", true)]
    [InlineData("/c/KOR-Operations/kor/kor.exe", true)]
    [InlineData("c:/kor-operations/kor/kor", true)]
    [InlineData("C:/KOR-Operations/kor/../kor/kor.exe", false)]
    [InlineData("C:/KOR-Operations/kor2/kor.exe", false)]
    [InlineData("D:/KOR-Operations/kor/kor.exe", false)]
    [InlineData("/d/KOR-Operations/kor/kor.exe", false)]
    [InlineData("./kor.exe", false)]
    [InlineData("kor", false)]
    public void TheReadIsThatOneKorByItsPath(string path, bool allowed) =>
        Assert.Equal(allowed, StartGate.IsReadCommand("Bash", $"{path} read --module zz --subject x", "C:/KOR-Operations/kor/kor.exe"));

    [Fact]
    public void TheGateCountsNothingTheAssistantWroteAndACompactionOwesEverythingAgain()
    {
        var mine = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + Page(1, 1) + Bodies("x", 1, 1) + "\"}]}}";
        Assert.Equal(0, StartGate.Scan(new[] { mine }, "zz").Total);
        Assert.False(StartGate.Decide(StartGate.Scan(new[] { mine }, "zz"), "zz", "kor", "S").Allow);
        var lines = new List<string> { Attachment(Page(1, 2)), ToolResult(Page(2, 2)), ToolResult(Bodies("x", 1, 1)), "{\"isCompactSummary\":true}" };
        var d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("not in your context", d.Reason);
        // the compact start re-delivers page 1; page 2, read by hand before it, is gone from context and is named
        lines.Add(Attachment(Page(1, 2)));
        d = StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S");
        Assert.False(d.Allow);
        Assert.Contains("missing 2", d.Reason);
        lines.Add(Attachment(Page(2, 2)));
        Assert.Contains("The bodies are not read yet", StartGate.Decide(StartGate.Scan(lines, "zz"), "zz", "kor", "S").Reason);
    }

    [Fact]
    public void AQuotedHeadOrAnotherLanesPageRaisesNothing()
    {
        var lines = new[]
        {
            Attachment(Page(1, 1)), ToolResult(Bodies("x", 1, 1)),
            ToolResult($"{StartRender.PagesHead}3 of 20\""),                         // a comment quoting the head, as StartPages.cs does
            ToolResult($"{StartRender.PagesHead}1 of 99 - module 'another lane'"),   // another lane's page
            Said("pages 1 of 1 held, bodies 2 of 2 read for subject x"),
        };
        var seen = StartGate.Scan(lines, "zz");
        Assert.Equal(1, seen.Total);
        Assert.True(StartGate.Decide(seen, "zz", "kor", "S").Allow);
    }

    [Fact]
    public void ParallelCallsOfOneTurnAreOneRefusal()
    {
        var now = new DateTime(2026, 11, 5, 22, 0, 0);
        Assert.Equal(1, StartGate.NextDenied(0, null, now));
        Assert.Equal(1, StartGate.NextDenied(1, now, now.AddSeconds(1)));
        Assert.Equal(2, StartGate.NextDenied(1, now, now + StartGate.SameRefusal));
    }

    private static string GateInput(string transcript, string tool, string command) =>
        System.Text.Json.JsonSerializer.Serialize(new { session_id = "gate-test", transcript_path = transcript, tool_name = tool, tool_input = new { command } });

    [Fact]
    public void TheVerbRefusesThenOpensThenStaysOpenAndGivesUpAfterTheLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var transcript = Path.Combine(dir, "t.jsonl");
            File.WriteAllLines(transcript, new[] { Attachment(Page(1, 1)) });
            string Gate(string tool, string command, string? cache = null)
            {
                var o = new StringWriter();
                Assert.Equal(0, Program.Run(new[] { "start-gate", "--module", "zz", "--cache-dir", cache ?? dir, "--hook" }, () => GateInput(transcript, tool, command), o,
                                            new StringWriter(), Path.Combine(dir, "log.txt")));
                return o.ToString();
            }
            Assert.Contains("\"permissionDecision\":\"deny\"", Gate("Bash", "git status"));
            Assert.Equal("", Gate("Bash", $"\"{Environment.ProcessPath!.Replace('\\', '/')}\" read --module zz --subject x"));   // the read the refusal asks for passes
            File.AppendAllLines(transcript, new[] { ToolResult(Bodies("x", 1, 1)) });
            Assert.Contains("\"permissionDecision\":\"deny\"", Gate("Bash", "git status"));   // read, not yet said
            File.AppendAllLines(transcript, new[] { Said("pages 1 of 1 held, bodies 2 of 2 read for subject x") });
            Assert.Equal("", Gate("Bash", "git status"));
            Assert.True(File.Exists(StartGate.OpenMarker(dir, "gate-test")));
            File.WriteAllLines(transcript, new[] { "nothing delivered" });
            Assert.Equal("", Gate("Read", "anything"));   // open stays open, with no transcript read
            // never wedged: at the limit it opens and says what is not read
            var other = Path.Combine(dir, "other");
            Directory.CreateDirectory(Path.Combine(other, "gate"));
            File.WriteAllText(StartGate.DeniedMarker(other, "gate-test"), StartGate.MaxDenials.ToString());
            var gaveUp = Gate("Read", "x", other);
            Assert.DoesNotContain("\"permissionDecision\":\"deny\"", gaveUp);
            Assert.Contains("WITHOUT the read", gaveUp);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void KorStartGateOffLiftsItForASessionWithNoBash()
    {
        Assert.True(StartGate.Lifted(_ => "off"));
        Assert.True(StartGate.Lifted(_ => "OFF"));
        Assert.False(StartGate.Lifted(_ => null));
        Assert.False(StartGate.Lifted(_ => "on"));
    }

    [Fact]
    public void AFullLastWiredPageWithTheLongestRealPathsStaysUnderTheBudget()
    {
        const string exe = "C:/VIsual Studio Projects/Operations/Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.exe";
        var side = Side(300, 300) with { Module = "KOR Inspections Bookings" };
        var s = StartRender.BuildPages(side, Resume(100), null);
        Assert.Contains(s.Pages.Take(4), p => p.Length > StartRender.PageRoom - 300);   // a full page, or the test proves nothing
        for (var p = 1; p <= 4; p++)
            Assert.True(StartRender.Page(s, p, p, exe, Guid.NewGuid().ToString()).Length <= StartRender.HookBudget, $"page {p}");
    }

    [Theory]
    [InlineData("Bash", "kor read --module X --subject a\ngit push", false)]
    [InlineData("Bash", "rm -rf x kor read --subject a", false)]
    [InlineData("Bash", "git push origin kor read", false)]
    [InlineData("Bash", "kor read --subject $(git push)", false)]
    [InlineData("Bash", "kor read --subject `git push`", false)]
    [InlineData("Bash", "kor read --subject a > out.txt", false)]
    [InlineData("Bash", "notkor read --subject a", false)]
    [InlineData("PowerShell", "kor read --subject (git push)", false)]
    [InlineData("PowerShell", "kor read --subject (Remove-Item x)", false)]
    [InlineData("PowerShell", "(git push)kor read --subject a", false)]
    [InlineData("PowerShell", "(git)kor read --subject a", false)]
    [InlineData("PowerShell", "& \"(git)/kor.exe\" read --subject a", false)]
    [InlineData("PowerShell", "kor\nstart notepad --part pages", false)]
    [InlineData("PowerShell", "kor\nRead-Host", false)]
    [InlineData("Bash", "kor\nread --subject a", false)]
    [InlineData("Bash", "kor start --part\npages x", false)]
    [InlineData("PowerShell", "kor read-host", false)]
    [InlineData("Bash", "not-kor read --subject a", false)]
    [InlineData("Bash", "kor read --subject {a}", false)]
    [InlineData("Bash", "kor read --subject \"Peace River #1163, a=b\"", true)]
    [InlineData("Bash", "kor read --subject a=b", true)]
    [InlineData("Bash", "kor start --module X --part board --text # --part pages", false)]
    [InlineData("Bash", "kor read --subject a # b", false)]
    [InlineData("Bash", "kor read --subject a#b", false)]
    [InlineData("PowerShell", "&\"C:/KOR-Operations/kor/kor.exe\" read --subject a", true)]
    [InlineData("Bash", "kor read --subject \"it's\"", false)]
    [InlineData("Bash", "kor read --subject 'a' \"b c\" --page 2", true)]
    [InlineData("Bash", "kor start --module X --part pages --page 2 --pages 3 --text --session 1a-2b", true)]
    [InlineData("PowerShell", "C:\\KOR-Operations\\kor\\kor.exe read --module \"KOR Inspections Bookings\" --subject \"a b\"", true)]
    [InlineData("PowerShell", "& \"C:/KOR-Operations/kor/kor.exe\" read --module X --subject \"(git push)\"", false)]
    [InlineData("PowerShell", "& \"C:/KOR-Operations/kor/kor.exe\" read --module X --subject \"peace river\"", true)]
    [InlineData("Bash", "C:/KOR-Operations/kor/kor.exe read --module X --subject a\n", true)]
    [InlineData("Bash", "kor read --module X --subject \"peace river\"", true)]
    [InlineData("Bash", "\"C:/KOR-Operations/kor/kor.exe\" read --module \"X\" --subject a --page 2", true)]
    [InlineData("PowerShell", "& \"C:/KOR-Operations/kor/kor.exe\" start --module X --part pages --page 2 --pages 3 --text", true)]
    [InlineData("Bash", "kor read --module X --subject a; rm -rf /", false)]
    [InlineData("Bash", "kor read --module X --subject a && git push", false)]
    [InlineData("Bash", "kor start --module X --part board --text", false)]
    [InlineData("Bash", "git status", false)]
    [InlineData("Read", "kor read", false)]
    public void TheGatePassesOnlyTheReadAlone(string tool, string command, bool allowed) =>
        Assert.Equal(allowed, StartGate.IsReadCommand(tool, command, command.Contains("KOR-Operations", StringComparison.Ordinal) ? "C:/KOR-Operations/kor/kor.exe" : "kor"));

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

    [Theory]
    [InlineData("Peace River #1163, a=b")]
    [InlineData("it's O&M (phase 2); done")]
    [InlineData("$(git push) `x` | y")]
    public void TheNextLineAPagePrintsIsAlwaysTheReadTheGateLetsThrough(string subject)
    {
        var rows = Enumerable.Range(0, 3).Select(i => new BodyRead.Row(i, "zz", $"n{i}", "finding", "d", new string('b', 20_000))).ToList();
        var page = BodyRead.Pages(rows, subject, "zz", "C:/KOR-Operations/kor/kor.exe", 1, false, out _);
        var next = page.Split('\n').Last(l => l.StartsWith("NEXT: ", StringComparison.Ordinal))["NEXT: ".Length..];
        Assert.True(StartGate.IsReadCommand("Bash", next, "C:/KOR-Operations/kor/kor.exe"), next);
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
        // a wrong lane on either new entry leaves every page unseen by the gate
        Assert.Contains(EnrollCheck.SettingsFindings(WiredSettings(EnrollCheck.PagesWired, gate: true).Replace("start-gate --module M", "start-gate --module Other", StringComparison.Ordinal), "M"),
                        f => f.Contains("names --module Other, not M", StringComparison.Ordinal));
        Assert.Contains(EnrollCheck.SettingsFindings(WiredSettings(EnrollCheck.PagesWired, gate: true).Replace("--module M --part pages --page 3 ", "--module Other --part pages --page 3 ", StringComparison.Ordinal), "M"),
                        f => f.Contains("names --module Other, not M", StringComparison.Ordinal));
        Assert.DoesNotContain(EnrollCheck.SettingsFindings(WiredSettings(EnrollCheck.PagesWired, gate: true), "M"), f => f.Contains("names --module", StringComparison.Ordinal));
        // half-migrated: the old 4 + 4 pages beside the new ones
        var both = WiredSettings(EnrollCheck.PagesWired, gate: true).Replace("\"SessionStart\":[{\"hooks\":[", "\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"C:/KOR-Operations/kor/kor.exe start --module M --part memory --page 1 --pages 4 --hook\"},", StringComparison.Ordinal);
        Assert.Contains(Findings(both), f => f.Contains("left beside THE PAGES: --part memory", StringComparison.Ordinal));
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

    /// <summary>
    /// Row 1861: the failure paths of the verb itself, on the real Framework lane and settings, with no cold session spent: claude cannot start; claude
    /// starts and answers nonsense. Each leaves no settings file behind. Does NOT cover the 10-minute timeout path (same finally, not run: it takes ten minutes).
    /// </summary>
    [Theory]
    [InlineData(@"C:\no-such-folder\claude.exe")]
    [InlineData(@"C:\Windows\System32\hostname.exe")]
    public void StartProofLeavesNoSettingsFileBehindWhenTheSessionFails(string claude)
    {
        var temp = Path.Combine(Path.GetTempPath(), "kor-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var error = new StringWriter();
            var code = Program.Run(new[] { "start-proof", "--module", "Framework", "--dir", @"C:\VIsual Studio Projects\Framework", "--temp-dir", temp, "--claude", claude, "--model", "haiku" },
                                   new StringWriter(), error);
            Assert.NotEqual(0, code);
            Assert.False(string.IsNullOrWhiteSpace(error.ToString()));
            Assert.Empty(Directory.GetFiles(temp));
        }
        finally { Directory.Delete(temp, true); }
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
