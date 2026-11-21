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
}
