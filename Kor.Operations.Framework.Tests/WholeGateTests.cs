using System.Text.Json;
using Kor.Operations.Framework.Whole;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 2139, the whole-source ledger gate (<see cref="WholeGate"/>, <see cref="WholeLedger"/>). Covers, on transcripts built in
/// the SHAPE of the three real instances (Framework e7287342 L6444-L6516, Operations 6b3b880f L10557-L10873, Framework 4f85ab91
/// L2099-L2763): what a check's output reads as (a ratio with its nearest polarity word, a count form, a hole; the dry-run misfire
/// classes - grep -n hits, diff lines, "not checked: 0", a quoted mention, ids and the Y of a ratio as counts, X/Y ids); each rule
/// refusing its case and allowing its correction (W1 NOT OPENED / NOT CHECKED, W2 artifact, W3 run-on-210 with NOT OPENED still
/// refused and OPENED after an uncut look allowed, W4 "all 37 ... 3.1-3.4" from head -5 + grep -c, allowed after sort | uniq -c of the
/// same file); review 2583's findings (a long cell's key can be copied; a cut tail is numbered as the file; a refused door's own
/// accounting does not stick); a reader's output and another hook's refusal hold no flag; KOR_WHOLE_GATE=off is not read here.
/// Does NOT cover: the hook wiring inside kor context (proven on real transcripts through kor.exe, row 2139 Evidence); the real
/// transcripts themselves (kor whole --replay); GuardRefusal logging.
/// </summary>
public class WholeGateTests
{
    private int _n;
    private readonly List<string> _lines = new();

    private void Prompt(string text) => _lines.Add(JsonSerializer.Serialize(new { type = "user", message = new { content = text } }));
    private void Say(string text) => _lines.Add(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new object[] { new { type = "text", text } } } }));

    private void Call(string tool, object input, string output)
    {
        var id = $"t{++_n}";
        _lines.Add(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new object[] { new { type = "tool_use", id, name = tool, input } } } }));
        _lines.Add(JsonSerializer.Serialize(new { type = "user", message = new { content = new object[] { new { type = "tool_result", tool_use_id = id, content = output } } } }));
    }

    private void Bash(string command, string output) => Call("Bash", new { command }, output);

    private WholeLedger Ledger()
    {
        var l = new WholeLedger();
        foreach (var e in WholeTranscript.Parse(_lines, 1)) l.Add(e);
        return l;
    }

    private static WholeGate.Door Reply(WholeLedger l, string extra = "") => new(WholeGate.DoorKind.Reply, l.TurnText + "\n" + extra, true, false, false);

    private static WholeGate.Door Pre(WholeLedger l, string tool, string input, string? file = null)
    {
        var d = WholeGate.DoorOf(tool, input, file, _ => null);
        return d with { Text = l.Narration + "\n" + d.Text };
    }

    // ---- readings ----

    [Fact]
    public void TheMergedPadCheckFlagsFourOf180()
    {
        var r = WholeGate.Readings("B1_L02 pads 100 -> 180 | pad-pad overlaps after 0 | 4-sided 176 of 180 | stiff union diff 0.025200 in2");
        var f = Assert.Single(r, x => x.Flagged > 0);
        Assert.Equal(4, f.Flagged);
        Assert.Equal(180, f.Of);
        Assert.Contains("176 of 180", f.Key);
    }

    [Fact]
    public void TheNearestPolarityWordDecides()
    {
        Assert.Equal(8, Assert.Single(WholeGate.Readings("ACCEPTANCE RESULT NOT ACCEPTED: 6 of 14 items PASS"), x => x.Of == 14).Flagged);
        Assert.Equal(4, Assert.Single(WholeGate.Readings("4 of 180 merged pads were not 4-sided"), x => x.Of == 180).Flagged);
        Assert.DoesNotContain(WholeGate.Readings("page 2 of 5"), x => x.Flagged > 0);        // no polarity word: not a result
        Assert.DoesNotContain(WholeGate.Readings("Opened: 119 of 119 pages, 41 of 41 pictures"), x => x.Flagged > 0);
    }

    [Fact]
    public void CountFormsFlagButIdsOrdinalsAndARatiosYDoNot()
    {
        Assert.Equal(new[] { 2, 8 }, WholeGate.Readings("It found 2 WRONG and 8 MISLEADING items.").Where(x => x.Flagged > 0).Select(x => x.Flagged).OrderBy(x => x));
        Assert.Equal(9, Assert.Single(WholeGate.Readings("Failed!  - Failed:     9, Passed:  1102"), x => x.Flagged > 0).Flagged);
        var ratio = WholeGate.Readings("VERDICT: NO NEW FAILURES - 45 of 2714 failing in new").Where(x => x.Flagged > 0).ToList();
        Assert.Equal(45, Assert.Single(ratio).Flagged);                                      // not also "2714 failing"
        Assert.DoesNotContain(WholeGate.Readings("after Codex pass 3 rejected 3a/4/9"), x => x.Flagged > 0);
        Assert.DoesNotContain(WholeGate.Readings("GuardRefusal 9119 refused at 18:20"), x => x.Flagged > 0);
        Assert.DoesNotContain(WholeGate.Readings("Fixes the review 2527/2528 minors (passes)"), x => x.Flagged > 0);   // X/Y ids
        Assert.Equal(0, Assert.Single(WholeGate.Readings("Passed!  - Failed:     0, Passed:    34")).Flagged);    // a clean reading
    }

    [Fact]
    public void QuotedLinesAndEmptyHolesAreNoFlags()
    {
        Assert.Empty(WholeGate.Readings("93:2. Walls | NOT CHECKED: no wall thickness compared"));
        Assert.Empty(WholeGate.Readings("letter.txt:585:modulus are not checked against it"));
        Assert.Empty(WholeGate.Readings("< the 81 of L2. Why SAFE did not calculate those 9 is not checked"));
        Assert.Empty(WholeGate.Readings("- record #135770 [lesson, Framework, 20 Nov] a check said 4 of 180 merged pads were not 4-sided"));
        Assert.Empty(WholeGate.Readings("ledger rows 3877 | not checked: 0"));
        Assert.Empty(WholeGate.Readings("HARD RULES: every fact from a read, else \"not checked\""));
        Assert.True(Assert.Single(WholeGate.Readings("| C-ext-B1_L02-east.png (not looked at) |")).Hole);
    }

    [Fact]
    public void AReaderPrintsNoFlags()
    {
        Assert.True(WholeGate.IsReader("cd \"C:/x\" && grep -n FAIL report.txt | head -5"));
        Assert.True(WholeGate.IsReader("C:/KOR-Operations/kor/kor.exe recall --module Framework \"pads\""));
        Assert.False(WholeGate.IsReader("cd C:/x && python -I check.py | tail -2"));
        Bash("cat CHECK.md", "4-sided 176 of 180");
        Assert.Empty(Ledger().Flags);
    }

    [Fact]
    public void ADryRunOrThisGatesOwnTextIsNoCheckOutput()
    {
        // the row-2139 close was refused on what this session's dry runs and proofs PRINTED about other sessions
        Assert.True(WholeGate.IsReader("C:/KOR-Operations/kor/kor.exe whole --replay C:/t/dry.txt"));
        Assert.True(WholeGate.IsReader("python -I locate.py C:/u/.claude/projects/x/e7287342.jsonl \"4 of 180\""));
        Assert.Empty(WholeGate.Readings("c1-L6516-run-on-210  DENY kor whole (board row 2139, the whole-source ledger gate): refused - W3."));
        Assert.Empty(WholeGate.Readings("  - transcript line 4601: 4 flagged - key `4 FAIL`"));
        Assert.Empty(WholeGate.Readings("W3 - a board close while 6 flagged item(s) are not opened"));
        Assert.Empty(WholeGate.Readings("139b7ae2 L116 LOOK 235 of 239 shipped file(s) looked at"));
        Assert.Empty(WholeGate.Readings("REFUSE e7287342-6067-4ac2-818a-e923a9744797.jsonl L6470 Report (Write) W2: L6434 `4-sided 176 of 180`"));
        Assert.NotEmpty(WholeGate.Readings("B1_L02 pads 100 -> 180 | 4-sided 176 of 180"));   // a check's own line still reads
    }

    [Fact]
    public void AnotherHooksRefusalIsNoCheckOutput()
    {
        Bash("python -I run.py", "PreToolUse:Bash hook error: kor recall ... record #135770 4 of 180 merged pads were not 4-sided");
        Assert.Empty(Ledger().Flags);
    }

    // ---- case 1: 20 Nov, 4 of 180 pads, "a measuring artifact", sent to KOR-210 ----

    private void Case1()
    {
        Prompt("merge the wall pads and test B1 L02");
        Bash("cd x && python -I merge_check.py", "B1_L02 pads 100 -> 180 | pad-pad overlaps after 0 | 4-sided 176 of 180 | stiff union diff 0.025200 in2");
        Bash("cd x && python -I -c \"print(nonquads)\"", "out []\nout-merged2 []");
    }

    [Fact]
    public void Case1_TheArtifactSentenceIsRefusedW2()
    {
        Case1();
        Say("All 180 pads are four-sided in the file itself. The 4 my first check flagged were an artifact of how it measured the shapes.");
        var l = Ledger();
        var v = l.Decide(Pre(l, "Write", "# state\nmerge built", "C:/t/state-1735.md"));
        Assert.True(v.Refuse);
        Assert.Contains("W2", v.Rules);
    }

    [Fact]
    public void Case1_TheReplyHidingTheFlagIsRefusedW1_SayingItPlainlyIsAllowed()
    {
        Case1();
        Say("The merge is built: all 180 pads are four-sided.");
        var l = Ledger();
        var v = l.Decide(Reply(l));
        Assert.True(v.Refuse);
        Assert.Contains("W1", v.Rules);
        Assert.Contains("176 of 180", v.Reason);
        var key = Assert.Single(v.Named).R.Key;
        Assert.False(l.Decide(Reply(l, $"NOT OPENED: {key}")).Refuse);
    }

    [Fact]
    public void Case1_TheKor210SendIsRefusedW3_NotOpenedDoesNotLiftIt()
    {
        Case1();
        Say($"NOT OPENED: 4-sided 176 of 180");
        var l = Ledger();
        var v = l.Decide(Pre(l, "Bash", "cd \"C:/x/docs/one-tool/run-on-210\" && pwsh -NoProfile -File send.ps1 -Set step5-2249-merged"));
        Assert.True(v.Refuse);
        Assert.Contains("W3", v.Rules);
    }

    [Fact]
    public void Case1_Corrected_OpenedAfterAnUncutLookOfTheFourIsAllowedEverywhere()
    {
        Case1();
        Bash("cd x && python -I list_nonquads.py", "P12 3 vertices 0 in wide\nP13 3 vertices 0 in wide\nP40 5 vertices 0.0018 in wide\nP41 5 vertices 0.0018 in wide");
        var l = Ledger();
        var key = Assert.Single(l.Flags, f => f.R.Of == 180).R.Key;
        var reply = Reply(l, $"OPENED 4 of 4: {key} - P12/P13 0 in, P40/P41 0.0018 in slivers; not sending until fixed");
        Assert.False(l.Decide(reply).Refuse);
        Assert.False(l.Decide(Pre(l, "Bash", "cd \"C:/x/run-on-210\" && pwsh -File send.ps1")).Refuse);
    }

    [Fact]
    public void OpenedWithoutALookBigEnoughDoesNotCount()
    {
        Case1();                                                                   // the only look printed 2 lines, 0 pads
        var l = Ledger();
        var key = Assert.Single(l.Flags, f => f.R.Of == 180).R.Key;
        var v = l.Decide(Reply(l, $"OPENED 4 of 4: {key}"));
        Assert.True(v.Refuse);
        Assert.Contains("W1", v.Rules);
    }

    [Fact]
    public void ALaterCleanRunOfTheSameCheckSupersedesTheFlag()
    {
        Case1();
        Bash("cd x && python -I merge_check.py", "B1_L02 pads 100 -> 184 | pad-pad overlaps after 0 | 4-sided 180 of 180 | stiff union diff 0");
        var l = Ledger();
        Assert.False(l.Decide(Pre(l, "Bash", "pwsh -File C:/x/run-on-210/send.ps1")).Refuse);
    }

    // ---- case 2: rule 12, "all 37 ... 3.1-3.4 in" from head -5 ----

    private void Case2()
    {
        Prompt("does the report already tell her about these?");
        Bash("grep -oE \"JBP_V-WALL[^:]*: outline\" \"$SP/31168-FROM-DRAWINGS-report.txt\" | head -5; echo \"--- count ---\"; grep -c \"could not be resolved\" \"$SP/31168-FROM-DRAWINGS-report.txt\"",
             "JBP_V-WALL: outline 256x70 [implied thickness 3.4 in]\nJBP_V-WALL: outline 164x3 [implied thickness 3.1 in]\nJBP_V-WALL: outline 136x29 [implied thickness 3.2 in]\n" +
             "JBP_V-WALL: outline 114x3 [implied thickness 3.1 in]\nJBP_V-WALL: outline 69x3 [implied thickness 3.1 in]\n--- count ---\n37");
    }

    [Fact]
    public void Case2_AllThirtySevenFromFiveIsRefusedW4()
    {
        Case2();
        Say("The report already names all 37, each with implied thickness - consistently 3.1-3.4 in, just under the 4 in minimum.");
        var l = Ledger();
        var v = l.Decide(Reply(l));
        Assert.True(v.Refuse);
        Assert.Contains("W4", v.Rules);
    }

    [Fact]
    public void Case2_Corrected_CountedWholeOverTheSameFileIsAllowed()
    {
        Case2();
        Bash("A=\"//fs01/x\"; grep -oE \"implied thickness [0-9.]+ in\" \"$A/31168-FROM-DRAWINGS-report.txt\" | sort | uniq -c | sort -rn",
             "12 implied thickness 3.1 in\n8 implied thickness 3.4 in\n6 implied thickness 3.2 in\n5 implied thickness 4.3 in\n4 implied thickness 2.1 in\n2 implied thickness 2.7 in");
        Say("Counted on the landed file, all 37 lie between 3.1-3.4 in or below, five at 4.3 in.");   // the SAME range ends as the sample
        var l = Ledger();
        Assert.DoesNotContain("W4", l.Decide(Reply(l)).Rules);
    }

    [Fact]
    public void Case2_TheSameClaimWithoutTheRecountIsStillRefused()
    {
        Case2();                                                                                   // review 2584: the pair proves the lift
        Say("Counted on the landed file, all 37 lie between 3.1-3.4 in or below, five at 4.3 in.");
        var l = Ledger();
        Assert.Contains("W4", l.Decide(Reply(l)).Rules);
    }

    [Fact]
    public void ABareCountedClaimRestsOnTheCountNotTheCut()
    {
        Bash("find deliver -type f | wc -l; sha256sum x | cut -c1-64", "84\nabc");
        Say("84 of 84 files hash OK.");
        var l = Ledger();
        Assert.False(l.Decide(Reply(l)).Refuse);
    }

    // ---- case 3: 16 Nov, the brief put 03 Models / 04 Results out of scope, then "DONE" ----

    private void Case3()
    {
        Prompt("run step 1");
        Call("Edit", new { file_path = "C:/F/lanes/2026-11-16/brief-jm-v9-step1b-look.txt", old_string = "4. Grade", new_string = "OUT OF SCOPE (Codex and step 1a cover them as text): \"03 Models\", \"04 Results\" and the 3 CSVs.\n\n4. Grade" }, "ok");
        Bash("cat out-jm-1b.txt", "Opened: 119 of 119 pages, 41 of 41 pictures, and the viewer. Found: 2 WRONG and 8 MISLEADING.");
        Prompt("status?");
    }

    [Fact]
    public void Case3_DoneOverAScopedOutSourceIsRefusedW1()
    {
        Case3();
        var l = Ledger();
        var v = l.Decide(Pre(l, "SendMessage", "Steps 0 and 1 are DONE. 32 faults are confirmed."));
        Assert.True(v.Refuse);
        Assert.Contains("W1", v.Rules);
        Assert.True(Assert.Single(v.Named).R.Hole);
    }

    [Fact]
    public void Case3_Corrected_NotCheckedNamingTheExcludedPartIsAllowed()
    {
        Case3();
        var l = Ledger();
        Assert.False(l.Decide(Pre(l, "SendMessage", "Steps 0 and 1 are DONE. 32 faults are confirmed.\nNOT CHECKED: 03 Models, 04 Results and the 3 CSVs (out of look 1b's scope).")).Refuse);
    }

    // ---- review 2583 ----

    [Fact]
    public void ALongCellsKeyIsShortEnoughToCopy()
    {
        var cell = string.Join(" ", Enumerable.Repeat("context words", 150)) + " 4-sided 176 of 180 " + string.Join(" ", Enumerable.Repeat("more", 150));
        var r = Assert.Single(WholeGate.Readings(cell), x => x.Flagged > 0);
        Assert.True(r.Key.Length <= 140, $"key is {r.Key.Length} chars");   // the refusal shows 140: the whole key
        Assert.Contains("176 of 180", r.Key);
    }

    [Fact]
    public void ACutTailIsNumberedAsTheFileNumbersIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wholegate-{Guid.NewGuid():N}.jsonl");
        try
        {
            Prompt("go");
            for (var i = 0; i < 400; i++) Bash($"echo {i}", new string('x', 200));
            Bash("python -I check.py", "4-sided 176 of 180");
            File.WriteAllLines(path, _lines);
            var evs = WholeTranscript.ReadTail(path, 20_000);
            var last = evs.Last(e => e.Kind == WholeKind.Call);
            Assert.Equal(_lines.Count - 1, last.Line);   // the tool_use line, 1-based, as in the file
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ARefusedDoorsOwnAccountingDoesNotStick()
    {
        Case1();
        Say("The 4 my first check flagged were an artifact.");
        var l = Ledger();
        var v = l.Decide(Reply(l, "NOT OPENED: 4-sided 176 of 180"));   // W2 refuses this reply, so its NOT OPENED line was never sent
        Assert.True(v.Refuse);
        Assert.False(Assert.Single(l.Flags, f => f.R.Of == 180).Accounted);
    }

    [Fact]
    public void EveryHoleCanBeAccountedFor()
    {
        Prompt("go");
        Bash("python -I look.py", "- .rvt 5868cb27 (not opened)\nNot checked");      // 4f85ab91 L457's two unnameable keys
        var l = Ledger();
        var v = l.Decide(Reply(l));
        Assert.Equal(2, v.Named.Count);
        var acct = string.Join("\n", v.Named.Select(f => $"NOT CHECKED: {f.R.Key}"));
        Assert.False(l.Decide(Reply(l, acct)).Refuse);
    }

    [Fact]
    public void ACommitMessageNamingKor210IsNotTheSend()
    {
        var d = WholeGate.DoorOf("Bash", "git commit -m \"send to \\\\KOR-210 next\"", null, _ => null);
        Assert.Equal(WholeGate.DoorKind.Commit, d.Kind);
        Assert.False(d.Step);
    }

    // Codex narrowing cases, 21 Nov 2026. Short, credential-free extracts; commands/paths shortened where irrelevant.
    // A1=e7287342, A2=6b3b880f, A3=4f85ab91, B1=b6568d08, B2=ee8754e5 (full paths in the response).
    // These compare decisions and retained findings, not merely fewer refusals. They do not prove source identity,
    // the truth of a helper's report, or completeness with no machine-readable count ("all pads look fine" still escapes).
    private const string RealPadOutput = "B1_L02 pads 100 -> 180 | pad-pad overlaps after 0 | 4-sided 176 of 180 | stiff union diff 0.025200 in2\n" +
        "B1_L03 pads 89 -> 163 | pad-pad overlaps after 0 | 4-sided 159 of 163 | stiff union diff 0.025200 in2\n" +
        "B1_L09 pads 52 -> 112 | pad-pad overlaps after 0 | 4-sided 108 of 112 | stiff union diff 0.025200 in2";

    private void RealPads()
    {
        // A1 L6434 -> L6444; L6462 -> L6464. The empty raw-coordinate listing did NOT open the repaired polygons.
        Bash("python -I -c \"four=sum(1 for p in sb if len(p.exterior.coords)-1==4); print(st, '4-sided', four, 'of', len(sb))\"", RealPadOutput);
        Bash("python -I -c \"print(d,[(n,len(p)) for n,p,s in M['floors'] if s=='Col_Stiff' and len(p)!=4])\"", "out []\nout-merged2 []");
    }

    [Theory]
    [InlineData("report", "W2")] // A1 L6469 narration, L6470 Write.
    [InlineData("reply", "W1")]  // A1 L6490.
    [InlineData("send", "W3")]   // A1 L6516.
    public void Narrow_A1_RealPadFaultsStillRefuse(string moment, string rule)
    {
        RealPads();
        if (moment == "report") Say("All 180 pads are four-sided in the file itself. The 4 my first check flagged were an artifact of how it measured the shapes.");
        if (moment == "reply") Say("The merge is built and checked offline. All 180 pads are four-sided, the only shape SAFE keeps unchanged.");
        var l = Ledger();
        var door = moment switch
        {
            "report" => Pre(l, "Write", "Offline: B1_L02 merged, 180/180 4-sided", "state-1735.md"),
            "send" => Pre(l, "Bash", "cd docs/one-tool/run-on-210 && pwsh -File send-set.ps1 -Tag step5-2249-merged -SafeModels B1_L02"),
            _ => Reply(l),
        };
        var v = l.Decide(door);
        Assert.Contains(rule, v.Rules);
        // Two further faults omitted by the brief: the SAME check found four non-quads on L03 and L09 too.
        Assert.Equal(new[] { 112, 163, 180 }, v.Named.Where(f => !f.R.Hole).Select(f => f.R.Of).OrderBy(n => n));
    }

    [Fact]
    public void Narrow_A1_CorrectionStatesEveryUnopenedStorey()
    {
        RealPads();
        var l = Ledger();
        // Constructed correction of L6490. No assertion that a look or fix ran.
        Assert.False(l.Decide(Reply(l, "Not opened: 4-sided 176 of 180\nNot opened: 4-sided 159 of 163\nNot opened: 4-sided 108 of 112")).Refuse);
        Assert.Contains("W3", l.Decide(Pre(l, "Bash", "pwsh -File run-on-210/send-set.ps1")).Rules);
    }

    private void RealThicknessSample()
    {
        // A2 L10557 -> L10558. Exactly five values and a separate full count.
        Bash("grep -oE 'JBP_V-WALL[^:]*: outline [0-9]+x[0-9]+.*' 31168-FROM-DRAWINGS-report.txt | head -5; grep -c 'could not be resolved into wall panels' 31168-FROM-DRAWINGS-report.txt",
            "JBP_V-WALL: outline 256x70 with 6 vertices [implied thickness 3.4 in]\n" +
            "JBP_V-WALL: outline 164x3 with 4 vertices [implied thickness 3.1 in]\n" +
            "JBP_V-WALL: outline 136x29 with 6 vertices [implied thickness 3.2 in]\n" +
            "JBP_V-WALL: outline 114x3 with 4 vertices [implied thickness 3.1 in]\n" +
            "JBP_V-WALL: outline 69x3 with 4 vertices [implied thickness 3.1 in]\n37");
    }

    [Fact]
    public void Narrow_A2_RealMarkdownRangeRefuses()
    {
        RealThicknessSample();
        Say("The report **already** names all 37, each with implied thickness — consistently **3.1–3.4 in**, just under the 4 in minimum."); // L10567
        var l = Ledger();
        Assert.Contains("W4", l.Decide(Reply(l)).Rules);
    }

    [Theory]
    [InlineData(false)] // A2 L10760/10761 -> L10764, before the divisor fix.
    [InlineData(true)]  // A2 L10873, after the fix; corrected distribution, not the earlier one.
    public void Narrow_A2_RealRecountCorrectionsPass(bool final)
    {
        RealThicknessSample();
        Bash("grep -oE 'implied thickness [0-9.]+ in' 31168-FROM-DRAWINGS-report.txt | sort | uniq -c | sort -rn",
            "12 implied thickness 3.1 in\n8 implied thickness 3.4 in\n6 implied thickness 3.2 in\n5 implied thickness 4.3 in\n4 implied thickness 2.1 in\n2 implied thickness 2.7 in");
        if (final)
        {
            // L10845/10846: the actual reply also owes this current test result, which its "817 of 819 green" discloses.
            Bash("dotnet test Kor.Operations.EngineeringTools.Core.Tests | head -8",
                "Failed!  - Failed: 2, Passed: 817, Skipped: 0, Total: 819");
            // L10867/10868, the recount after the divisor was fixed.
            Bash("grep -oE 'implied thickness [0-9.]+ in' 31168-FROM-DRAWINGS-report.txt | sort -t' ' -k3 -n | uniq -c",
                "2 implied thickness 1.9 in\n4 implied thickness 2.1 in\n2 implied thickness 2.9 in\n10 implied thickness 3.0 in\n10 implied thickness 3.1 in\n9 implied thickness 3.4 in");
        }
        Say(final
            ? "Counted on the landed file, all 37 — `sort | uniq -c`, no `head`: 2 at 1.9, 4 at 2.1, 2 at 2.9, 10 at 3.0, 10 at 3.1, 9 at 3.4.\n817 of 819 green."
            : "Codex is right and I was wrong. My \"all 37 are 3.1–3.4 in\" came from reading the first five lines and generalising. The real distribution has 5 at 4.3 in, ABOVE the 4 in minimum. B2 is not closed.");
        var l = Ledger();
        Assert.False(l.Decide(Reply(l)).Refuse);
        if (final) Assert.Contains("W3", l.Decide(Pre(l, "Bash", "pwsh -File run-on-210/send-set.ps1")).Rules);
    }

    private const string RealExclusion = "OUT OF SCOPE (Codex and step 1a cover them as text): \"03 Models\", \"04 Results\" and the 3 CSVs. List them once as out of scope, not as \"not checked\".";
    private const string HistoryParagraph = "3. Framework 4f85ab91 (16 Nov, #133870). L2099: the session's own brief edit put `\"03 Models\", \"04 Results\" and the 3\n" +
        "   CSVs` OUT OF SCOPE, \"not as 'not checked'\". L2483: helper \"Opened: 119 of 119 pages, 41 of 41 pictures\" (X of X of\n" +
        "   a population already cut). L2763 SendMessage: \"Steps 0 and 1 are DONE. 32 faults are confirmed\".";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Narrow_A3_RealExclusionNeedsDisclosureEvenBesideQuotedHistory(bool besideHistory)
    {
        // A3 L2099, L2483, L2763. A corrected full count of the reduced population cannot erase the exclusion.
        Call("Edit", new { file_path = "brief-jm-v9-step1b-look.txt", new_string = (besideHistory ? HistoryParagraph + "\n\n" : "") + RealExclusion }, "ok");
        Bash("cat out-jm-1b.txt", "Opened: 119 of 119 pages, 41 of 41 pictures, and the viewer. Found: 2 WRONG and 8 MISLEADING.");
        Prompt("status?");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Pre(l, "SendMessage", "Steps 0 and 1 are DONE. 32 faults are confirmed on frozen pkg-v9-63c74822.")).Rules);
    }

    [Fact]
    public void Narrow_A3_PlainScopeDisclosureAccountsButDoesNotOpen()
    {
        Call("Edit", new { file_path = "brief-jm-v9-step1b-look.txt", new_string = RealExclusion }, "ok");
        var l = Ledger();
        // Constructed correction of A3 L2763, using the brief's words rather than mandatory uppercase keys.
        Assert.False(l.Decide(Pre(l, "SendMessage", "32 faults are confirmed. 03 Models, 04 Results and the 3 CSVs are out of scope of this look.")).Refuse);
        Assert.Contains("W3", l.Decide(Pre(l, "Bash", "kor_task.py accept 1982")).Rules);
    }

    [Fact]
    public void Narrow_B9150_QuotedHistoryIsNotTheNewBriefsScope()
    {
        // B1 L678 and L1474: historical example and definition in the gate's plan, not a new source exclusion.
        Call("Write", new { file_path = "PLAN-2139-WHOLE-SOURCE-GATE.md", content = HistoryParagraph + "\n\n- HOLE: OUT OF SCOPE / NOT CHECKED / not graded in a check's output.\n\n" +
            "- Flags narrowed after reading all 563 distinct flags: brief holes only for out of scope / not in scope / excluded from." }, "ok");
        Bash("python -I compare-flags.py flags-v2.txt flags.txt", "8b81c215 L343 Not proven (2 of 14), each with its reason (Appendix F.1 gives each member)"); // L1353/1354
        var l = Ledger();
        Assert.Empty(l.Flags);
        Assert.False(l.Decide(Pre(l, "PowerShell", "UPDATE oversight.Commitment SET State = N'done' WHERE Id=2139")).Refuse);
    }

    [Fact]
    public void Narrow_B9150_CurrentUnopenedFailuresStillPreventTheFirstClose()
    {
        // B1 L2457/2458 -> L2601/2602 (#9150). The historical W2 grounds are wrong; these current failures remain real.
        Bash("kor testdiff base.trx new.trx | tail -25", "VERDICT: NO NEW FAILURES - 42 of 1155 test names failing in new, every one also failing in base (42 of 1133)");
        var l = Ledger();
        Assert.Contains("W3", l.Decide(Pre(l, "PowerShell", "UPDATE oversight.Commitment SET State = N'done', Evidence=N'NO NEW FAILURES (42 names fail on both, 22 new pass)' WHERE Id=2139")).Rules);
    }

    [Fact]
    public void Narrow_B9150_DocumentationPrintedByPythonIsNotACheck()
    {
        // B1 L715/718: ast.get_docstring reads source documentation; blind_look_check was not executed.
        Bash("python -I -c \"import ast;print(ast.get_docstring(ast.parse(open('tools/blind_look_check.py',encoding='utf-8').read())))\" | head -80; wc -l tools/blind_look_check.py",
            "blind_look_check - a drawing package goes out only behind a BLIND LOOK RECORD.\nExit: 0 PASS, 1 REFUSED (every reason printed, not the first), 2 usage error.");
        Assert.Empty(Ledger().Flags);
    }

    private const string StashFinding = "8 dc8752a4 2026-10-19 covered 0 of 2 On develop: PARKED 2026-10-19: ReclassifyByColor moved App->";
    private const string Coordination = "- ME: coordination, board, row 1975 cleanup (parked by Ian until later; branches only - I will not touch KOR.Drafter-step5, Operations row-2139-whole-gate, or port worktrees/branches), then plan step 3 (noisy guards) READ-ONLY until step 5 closes - no guard edits while you run.";

    [Theory]
    [InlineData(9198)] // B2 L3412/3415
    [InlineData(9199)] // B2 L3428/3438
    [InlineData(9200)] // B2 L3445
    public void Narrow_B9198To9200_CoordinationDoesNotDismissAStash(int refusal)
    {
        Bash("bash stash-class.sh Operations", StashFinding); // B2 L2255/2257: a REAL coverage check, not quoted history.
        Prompt("I need you to connect now"); // L3384: W1's previous-turn flags are not owed here; W2 still sees them.
        if (refusal != 9198) Bash("git diff --stat dc8752a4^1 dc8752a4; git ls-tree -r --name-only dc8752a4^3",
            "PdfGeometryExtractor.cs | 588 +---\n1 file changed, 3 insertions(+), 585 deletions(-)\nKor.Operations.EngineeringTools.Core/PdfToSafe/MarkupReclassifier.cs"); // L3422/3423
        var l = Ledger();
        Assert.Equal(2, Assert.Single(l.Flags).R.Flagged); // Do not "fix" W2 by throwing away the check.
        Assert.False(l.Decide(Pre(l, "SendMessage", Coordination + (refusal == 9198 ? "" : "\nFYI your kor whole gate fired over an old stash line; I opened the item."))).Refuse);
    }

    [Fact]
    public void Narrow_StashCoverageStillRefusesAnActualDismissal()
    {
        Bash("bash stash-class.sh Operations", StashFinding);
        Prompt("status?");
        var l = Ledger();
        Assert.Contains("W2", l.Decide(Pre(l, "SendMessage", "The 2 files the check flagged are harmless.")).Rules);
    }

    [Fact]
    public void Narrow_B9201_ReadSqlFromTheTranscriptIsQuotedLaneState()
    {
        // B1 L3372/3374/3375 -> L3434/3438. Query text was in a Write, not in the shell command.
        Call("Write", new { file_path = "C:/scratch/ls28721.sql", content = "SET NOCOUNT ON; WITH n AS (SELECT 1 AS p) SELECT p, SUBSTRING(l.ResumeBlock,p,200) FROM n CROSS JOIN KorStandards.brain.LaneState l WHERE l.Id=28720;" }, "ok");
        Call("PowerShell", new { command = "python -I tools/SqlQuery/oq.py -h -1 -i \"C:/scratch/ls28721.sql\"" },
            "1401|frozen bad B1_L04 case of its own. Manifest mentions / B1_L04 241 times (B1_L02 243) - possibly only inside whole-set snapshots. NOT checked.");
        var l = Ledger();
        Assert.Empty(l.Flags);
        Assert.False(l.Decide(Pre(l, "PowerShell", "UPDATE oversight.Commitment SET Evidence=N'OPEN QUESTION of LaneState #28720 ANSWERED: YES, B1_L04 is a valid twin; 158 of 158 fixtures read: C25 historical is scoped B1 L04/L05.' WHERE Id=2248")).Refuse);
    }

    private const string ReviewQuote = "- MINOR: docs/THE-PRIZE-AGENTIC-DEV-ISSUES-2026-11-19.md (summary) - 'covered 0 of 15' is a judgement call, since no refusal was judged right. The classification rule is not stated, so the 13/2 split cannot be reproduced.";

    [Theory]
    [InlineData(9202)] // B2 L3832/3835 Reply
    [InlineData(9203)] // B2 L3843/3846 Record
    [InlineData(9204)] // B2 L3863/3866 Record
    [InlineData(9205)] // B2 L3884/3887 Record
    [InlineData(9206)] // B2 L3893 Record
    public void Narrow_B9202To9206_ReviewQuotesASummaryItDoesNotMeasureCoverage(int refusal)
    {
        Bash("python -I tools/kor_review.py review --shas 2681572 | tail -5", ReviewQuote); // L3806/3808
        var l = Ledger();
        var text = refusal == 9202
            ? "Covered | **0 of 15** | No mechanism here is both wired and shown to catch what it's meant to catch. Partly 13 of 15, nothing 2 of 15."
            : "kor record --says \"Row 2219 map: 15 of 15 prize issues mapped; covered 0, partly 13, nothing 2. No guard's catch rate is measured.\"";
        Assert.False(l.Decide(refusal == 9202 ? Reply(l, text) : Pre(l, "Bash", text)).Refuse);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("Agent")]
    public void Narrow_AReviewOrHelperCanStillReportARealDeficit(string tool)
    {
        // Boundary of B2 L3808: a current finding, including one beside that quotation, remains a finding.
        Call(tool, tool == "Bash" ? new { command = "python -I tools/kor_review.py review --shas 2681572" } : (object)new { prompt = "Check coverage" },
            ReviewQuote + "\nCoverage check: covered 0 of 15");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Reply(l, "All issues handled.")).Rules);
        Assert.Contains(l.Flags, f => f.R.Flagged == 15 && f.R.Key.StartsWith("Coverage check", StringComparison.Ordinal));
    }

    [Fact]
    public void Narrow_UnknownSqlScriptIsNotAssumedToBeARecordRead()
    {
        Call("PowerShell", new { command = "python -I tools/SqlQuery/oq.py -i C:/scratch/check.sql" }, "4-sided 176 of 180");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Reply(l)).Rules);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("docstring")]
    public void Narrow_ReadingQuotedTextDoesNotHideACheckInTheSameCommand(string source)
    {
        Call("Write", new { file_path = "C:/scratch/ls.sql", content = "SELECT ResumeBlock FROM KorStandards.brain.LaneState WHERE Id=28720" }, "ok");
        var read = source == "sql" ? "python -I tools/SqlQuery/oq.py -i C:/scratch/ls.sql"
            : "python -I -c \"import ast;print(ast.get_docstring(ast.parse(open('check.py',encoding='utf-8').read())))\"";
        Bash(read + "; python -I check.py", "4-sided 176 of 180");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Reply(l)).Rules);
    }

    [Fact]
    public void Narrow_ARefusedSqlWriteCannotSupplyReadProvenance()
    {
        Call("Write", new { file_path = "C:/scratch/ls.sql", content = "SELECT ResumeBlock FROM KorStandards.brain.LaneState WHERE Id=28720" }, "PreToolUse:Write hook error: denied");
        Bash("python -I tools/SqlQuery/oq.py -i C:/scratch/ls.sql", "4-sided 176 of 180");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Reply(l)).Rules);
    }

    [Fact]
    public void Narrow_QuotedCleanResultCannotSupersedeALiveFailure()
    {
        Bash("python -I check.py", "Coverage check: covered 0 of 15");
        Bash("python -I tools/kor_review.py review", "- MINOR: docs/map.md (summary) - 'Coverage check: covered 15 of 15' is a judgement call.");
        var l = Ledger();
        Assert.Contains("W1", l.Decide(Reply(l)).Rules);
    }

    // B1 L2754/2763 and L2769/2772: every printed method/case, not invented filler to meet a line count.
    private const string BaselineMethods = """
        BOTH Kor.Operations.Framework.Tests.BrainRecallTests.TheHookBringsAWorkedJobsNamingRowsAndTheStandingAsksAsOneLine
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.ABoardEntryNeedsAnOwnerOrATag
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.ADroppedHookFlagFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.AGitHookWithoutItsCaptureLineFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.ANarrowedMatcherFailsNamingTheToolLeftOut
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.APrePushThatDropsAGuardFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.APrePushThatNoLongerExitsWithItsGuardsResultFails
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.APrePushThatThrowsAwayItsGuardsResultAfterExitRcFails
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.AStopCheckTimeoutUnder30SecondsFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.AWrongModuleFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.NoSettingsFileFails
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.TheRealGitHooksPass
        BOTH Kor.Operations.Framework.Tests.EnrollCheckTests.TheRealSettingsPass
        BOTH Kor.Operations.Framework.Tests.FileMemoryLiveTests.LiveEveryImportedMemoryIsOnTheMemoryPartOnceAndGoesStaleWhenItsFileChanges
        BOTH Kor.Operations.Framework.Tests.StartPagesLiveTests.TheLanesWholeMemoryIsOnThePagesAndWithinTheWiring
        BOTH Kor.Operations.Framework.Tests.StartSplitLiveTests.LiveTheSidePartsPrintSessionStartJsonWithinBudgetAndWriteNothing
        17 failing names in new; 17 also failing in base; 0 only in new
        """;

    private const string BaselineCases = """
        BOTH BrainRecallTests.TheHookBringsAWorkedJobsNamingRowsAndTheStandingAsksAsOneLine
        BOTH EnrollCheckTests.ABoardEntryNeedsAnOwnerOrATag(replacement: " --tag \"Revit Tools\"", fails: False)
        BOTH EnrollCheckTests.ABoardEntryNeedsAnOwnerOrATag(replacement: "", fails: True)
        BOTH EnrollCheckTests.ADroppedHookFlagFailsNamingIt
        BOTH EnrollCheckTests.AGitHookWithoutItsCaptureLineFailsNamingIt(hook: "post-checkout")
        BOTH EnrollCheckTests.AGitHookWithoutItsCaptureLineFailsNamingIt(hook: "post-commit")
        BOTH EnrollCheckTests.AGitHookWithoutItsCaptureLineFailsNamingIt(hook: "post-merge")
        BOTH EnrollCheckTests.AGitHookWithoutItsCaptureLineFailsNamingIt(hook: "pre-push")
        BOTH EnrollCheckTests.ANarrowedMatcherFailsNamingTheToolLeftOut
        BOTH EnrollCheckTests.APrePushThatDropsAGuardFailsNamingIt
        BOTH EnrollCheckTests.APrePushThatNoLongerExitsWithItsGuardsResultFails
        BOTH EnrollCheckTests.APrePushThatThrowsAwayItsGuardsResultAfterExitRcFails
        BOTH EnrollCheckTests.AStopCheckTimeoutUnder30SecondsFailsNamingIt(timeout: 15, fails: True)
        BOTH EnrollCheckTests.AStopCheckTimeoutUnder30SecondsFailsNamingIt(timeout: 29, fails: True)
        BOTH EnrollCheckTests.AStopCheckTimeoutUnder30SecondsFailsNamingIt(timeout: 30, fails: False)
        BOTH EnrollCheckTests.AStopCheckTimeoutUnder30SecondsFailsNamingIt(timeout: null, fails: False)
        BOTH EnrollCheckTests.AWrongModuleFailsNamingIt
        BOTH EnrollCheckTests.NoSettingsFileFails
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "FileChanged: fresh --file-changed --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PostCompact: capture-compact --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PostToolUse: capture-tool --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PostToolUseFailure: capture-tool --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PreToolUse: context --module \"Operations\" --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"comm
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PreToolUse: enforce --module \"Operations\" --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"comm
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PreToolUse: recall --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PreToolUse: stall --module \"Operations\" --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"comman
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "PreToolUse: start-docs --doc \"docs/HANDOVER.md#ST"···, settings: "{\"statusLine\":{\"type\":\"command\",\"c
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "SessionEnd: capture-end --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "SessionStart: capture-start --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "SessionStart: enroll-check --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "SessionStart: start --module \"Operations\" --part"···, settings: "{\"statusLine\":{\"type\":\"command\",\"c
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "Stop: capture-check --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "Stop: context --module \"Operations\" --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "Stop: recall --hook --push-back", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "UserPromptSubmit: context --module \"Operations\" "···, settings: "{\"statusLine\":{\"type\":\"command\",\"c
        BOTH EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt(removed: "UserPromptSubmit: recall --hook", settings: "{\"statusLine\":{\"type\":\"command\",\"command\":"···)
        BOTH EnrollCheckTests.TheRealGitHooksPass
        BOTH EnrollCheckTests.TheRealSettingsPass
        BOTH FileMemoryLiveTests.LiveEveryImportedMemoryIsOnTheMemoryPartOnceAndGoesStaleWhenItsFileChanges
        BOTH StartPagesLiveTests.TheLanesWholeMemoryIsOnThePagesAndWithinTheWiring(module: "Framework", folder: "Framework")
        BOTH StartPagesLiveTests.TheLanesWholeMemoryIsOnThePagesAndWithinTheWiring(module: "KOR.Drafter", folder: "KOR.Drafter")
        BOTH StartSplitLiveTests.LiveTheSidePartsPrintSessionStartJsonWithinBudgetAndWriteNothing
        42 failing cases in new; 42 also failing in base; 0 only in new
        """;

    [Fact]
    public void Narrow_B9195_EveryBaselineFailureWasActuallyListed()
    {
        // Real later close L2826/2830/2837. The first close (9150) did not yet have these lists.
        Bash("kor testdiff base.trx new.trx | tail -25", "VERDICT: NO NEW FAILURES - 42 of 1155 test names failing in new, every one also failing in base (42 of 1133)");
        Bash("python -I compare-methods.py base.trx new.trx", BaselineMethods);
        Say("My list strips theory parameters, so it shows 17 base methods; testdiff's 42 counts each theory case separately. Printing every case whole so the 42 are actually shown."); // L2768
        Bash("python -I compare-cases.py base.trx new.trx", BaselineCases);
        var l = Ledger();
        Assert.False(l.Decide(Pre(l, "PowerShell", "UPDATE oversight.Commitment SET State=N'done', Evidence=N'NO NEW FAILURES. OPENED 42 of 42: every one also failing in base (42 of 1133) - the same 42 cases.' WHERE Id=2139")).Refuse);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Narrow_BaselineSummaryCannotSubstituteForOpeningEveryFailure(bool cut)
    {
        // Counterexamples to 9195: keep the real summary but withhold cases, or cut the listing.
        var output = cut ? BaselineCases : string.Join("\n", BaselineCases.Split('\n').Skip(1));
        Bash("python -I compare-cases.py base.trx new.trx" + (cut ? " | head -43" : ""), output);
        var l = Ledger();
        Assert.Contains("W3", l.Decide(Pre(l, "PowerShell", "UPDATE oversight.Commitment SET State=N'done' WHERE Id=2139")).Rules);
    }
}
