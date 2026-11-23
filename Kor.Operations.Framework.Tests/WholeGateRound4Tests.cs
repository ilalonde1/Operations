using System.Text.Json;
using Kor.Operations.Framework.Whole;
using Xunit;

namespace Kor.Operations.Framework.Tests;

// Round 4: C = cc3b7be3, F = 80383e99, physical JSONL lines. Short source extracts and
// explicitly constructed boundary pairs; no database, installed hook, or remote execution.
public class WholeGateRound4Tests
{
    private readonly List<string> _lines = new();
    private int _id;

    private void Say(string text) => _lines.Add(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "text", text } } } }));
    private void Call(string command, string output, string tool = "Bash")
    {
        var id = $"t{++_id}";
        _lines.Add(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "tool_use", id, name = tool, input = new { command } } } } }));
        _lines.Add(JsonSerializer.Serialize(new { type = "user", message = new { content = new[] { new { type = "tool_result", tool_use_id = id, content = output } } } }));
    }
    private WholeLedger Ledger()
    {
        var ledger = new WholeLedger();
        foreach (var e in WholeTranscript.Parse(_lines, 1)) ledger.Add(e);
        return ledger;
    }
    private static WholeGate.Door Pre(string command, string tool = "Bash") => WholeGate.DoorOf(tool, command, null, _ => null);
    private static WholeGate.Door Reply(string text) => new(WholeGate.DoorKind.Reply, text, true, false, false);
    private void Flag() => Call("python check.py", "4-sided 176 of 180");

    [Theory]
    [InlineData("Bash", "cd 'C:/repo/docs/run-on-210' && ls; sed -n 1,25p send-set.ps1; cat holder.txt | tail -3")]
    [InlineData("PowerShell", "Get-Content '\\\\KOR-210\\results\\coverage.txt'")]
    [InlineData("Bash", "git show HEAD:docs/run-on-210/README.md")]
    [InlineData("Bash", "git -C C:/repo/docs/run-on-210 --no-pager show HEAD:README.md")]
    [InlineData("Bash", "python -I -c \"print(open('docs/run-on-210/README.md', encoding='utf-8').read())\"")]
    [InlineData("Bash", "kor record --help | head -30")]
    [InlineData("PowerShell", "& 'C:/kor/kor.exe' record --help | Select-Object -First 25")]
    [InlineData("Bash", "kor state --module Framework --file state.md")]
    [InlineData("Bash", "kor record --says 'The 4 flagged pads are expected; work saved.'")]
    public void RecoveryCallsCannotBeDoors(string tool, string command)
    {
        // C L2272 / #10721, L2387 / #10723; F L608,620,626,656 / #10729-10732.
        Flag();
        var l = Ledger();
        Assert.False(l.Decide(Pre(command, tool)).Refuse);
        Assert.Contains("W3", l.Decide(Pre("pwsh run-on-210/send-set.ps1")).Rules);
    }

    [Theory]
    [InlineData("cat docs/run-on-210/README.md; pwsh run-on-210/send-set.ps1")]
    [InlineData("kor record --says 'saved'; takeoff --deliver bundle")]
    [InlineData("git show HEAD:README.md; kor-runner submit bundle")]
    [InlineData("kor state --file state.md; kor_task.py accept 2139")]
    [InlineData("echo $(pwsh run-on-210/send-set.ps1)")]
    public void ARecoveryPrefixDoesNotHideAnExpensiveStep(string command)
    {
        Flag();
        Assert.Contains("W3", Ledger().Decide(Pre(command)).Rules);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AccountingCountsMidTurnInTextAndToolInput(bool opened, bool toolInput)
    {
        // F L705 -> L706 actually DID pass mid-turn. Preserve that boundary.
        Flag();
        Call("cat pads.txt", "pad A\npad B\npad C\npad D");
        var text = opened ? "OPENED 4 of 4: 4-sided 176 of 180 - all four pads" : "NOT OPENED: 4-sided 176 of 180";
        if (toolInput) Call("printf '%s' '" + text + "'", text);
        else Say(text);
        var l = Ledger();
        Assert.False(l.Decide(Reply("Continuing work.")).Refuse);
        Assert.Equal(!opened, l.Decide(Pre("takeoff --deliver bundle")).Refuse);
    }

    [Fact]
    public void CurrentToolCarriesItsOwnAccountingBeforeItRuns()
    {
        Flag();
        Call("cat pads.txt", "pad A\npad B\npad C\npad D");
        Assert.False(Ledger().Decide(Pre("# OPENED 4 of 4: 4-sided 176 of 180 - pads.txt\ntakeoff --deliver bundle")).Refuse);
    }

    [Theory]
    [InlineData("(c) expected FAIL states printed PASS: 0 of 215")]
    [InlineData("roles 20 files 63 missing []")]
    [InlineData("roles 22 files listed 65 missing 0 []")]
    public void ZeroDeficitIsNotAFlag(string output)
    {
        // C L924, L1003, L2319; F L335. #10704,10707,10722,10734-10745.
        Call("python check.py", output);
        Assert.Empty(Ledger().Flags);
    }

    [Theory]
    [InlineData("(c) expected FAIL states printed PASS: 2 of 215", 2)]
    [InlineData("roles 20 files 63 missing 2 ['a', 'b']", 2)]
    [InlineData("45 of 46 checks passed", 1)]
    public void NonzeroDeficitsStillRefuse(string output, int count)
    {
        Call("python check.py", output);
        var l = Ledger();
        Assert.Equal(count, Assert.Single(l.Flags).R.Flagged);
        Assert.Contains("W3", l.Decide(Pre("takeoff --deliver bundle")).Rules);
    }

    [Fact]
    public void TheSelfTestDisclosureAccountsForItsOneFailure()
    {
        // C L1710 -> L1714 / #10713: the count is real; the refusal of its explicit disclosure is wrong.
        Call("python selftest.py", "45 of 46 checks passed");
        var l = Ledger();
        Assert.False(l.Decide(Reply("The self-test passed 45 of 46 checks. The one failure is folder:IanBrain:memory.")).Refuse);
        Assert.Contains("W3", l.Decide(Pre("takeoff --deliver bundle")).Rules);
    }

    [Theory]
    [InlineData("Failed: 45\nBaseline comparison: 45 of 45 failing names also in base; 0 only in new")]
    [InlineData("Failed! - Failed: 49, Passed: 2576, Skipped: 0, Total: 2625\n49\n49\nin mine not base:\nin base not mine:\nMon Nov 23 12:40:30 BCDT 2026")]
    public void SameOutputBaselineNameComparisonExplainsTheFailures(string output)
    {
        // F L315/325: same names established by comm, not just equal failure totals.
        Call("dotnet test tests.csproj; comm -23 mine.fail base.fail; comm -13 mine.fail base.fail", output);
        Assert.Empty(Ledger().Flags);
    }

    [Theory]
    [InlineData("Failed: 45\nBaseline failed: 45")]
    [InlineData("Failed: 45\nBaseline comparison: 44 of 45 failing names also in base; 1 only in new")]
    [InlineData("Failed: 49\n49\n49\nin mine not base:\nNew.Fault\nin base not mine:\nOld.Fault")]
    public void EqualTotalsOrANewFailureAreNotABaselineExplanation(string output)
    {
        Call("dotnet test tests.csproj; comm -23 mine.fail base.fail; comm -13 mine.fail base.fail", output);
        Assert.Contains("W3", Ledger().Decide(Pre("takeoff --deliver bundle")).Rules);
    }

    [Theory]
    [InlineData("git show 4e2df0d --format= -- docs/HANDOVER.md | grep '^+' | cut -c1-260; python count_rulings.py", "+ VERIFIED: 23 fail/59 pass\n+ pass (23 of 23 failures in SafeMeshCoverageExceptionTests), 49 failures\n188")]
    [InlineData("python -I -c \"print(open('CODEX-RESPONSE.md', encoding='utf-8').read())\"", "23 fail; 49 failures")]
    [InlineData("python tools/kor_review.py review", "The brief says the bundle has 6 of 14 PASS. I did not open the bundle, so I could not check its numbers (65 files, 22 roles, 6 of 14 PASS).")]
    [InlineData("python tools/kor_review.py review", "- MINOR: docs/HANDOVER.md:326 - The block cites many counts (23 of 23 failures, 49 = 49 baseline, 0 of 174, 82/82) with no command or log path. These are the coordinator's claims, not reviewer-verified.")]
    public void AttributedTextIsNotThisSessionsMeasurement(string command, string output)
    {
        // C L3189/3194 (#10742/10743), L1301/1305 (#10711/10712), F L677/681 (#10733).
        Call(command, output);
        Assert.Empty(Ledger().Flags);
    }

    [Theory]
    [InlineData("git show HEAD:HANDOVER.md; python check.py", "+ previous run: 23 fail\n4-sided 176 of 180")]
    [InlineData("python -I -c \"print(open('CODEX-RESPONSE.md').read())\"; python check.py", "4-sided 176 of 180")]
    [InlineData("python tools/kor_review.py review", "The brief says 6 of 14 PASS.\nMy coverage check: 4-sided 176 of 180")]
    public void AQuotedHistoryDoesNotHideACurrentCheck(string command, string output)
    {
        Call(command, output);
        var l = Ledger();
        Assert.Contains(l.Flags, f => f.R.Flagged == 4);
        Assert.Contains("W3", l.Decide(Pre("takeoff --deliver bundle")).Rules);
    }

    private static string Pieces(int total = 71, int listed = 60) =>
        $"missing: count {total}; pieces > 1e-9 {listed}; pieces <= 1e-9 sum 7.65e-12\n" +
        string.Join("\n", Enumerable.Range(1, listed).Select(i => $"{i} {{\"area\": 0.003, \"floors\": [\"A1\"], \"id\": {i}}}"));
    private const string NotListable = "NOT LISTABLE: 71 missing - inspected measurement.json: 60 pieces listed; the remaining 11 fragments have no individual records below 1e-9.";

    [Theory]
    [InlineData("reply")]
    [InlineData("tool")]
    [InlineData("step")]
    public void NotListableAfterAnUncutPopulationLookDischargesW3(string where)
    {
        // Constructed correction of C L2319/2326/2333 -> L2387: one look prints both total and listable part.
        Call("python coverage.py", "71 missing");
        Call("python inspect.py measurement.json", Pieces());
        if (where == "reply") Say(NotListable);
        if (where == "tool") Call("printf '%s' '" + NotListable + "'", "saved");
        var l = Ledger();
        var d = Pre("takeoff --deliver bundle");
        if (where == "step") d = d with { Text = NotListable + "\n" + d.Text };
        Assert.False(l.Decide(d).Refuse);
    }

    [Theory]
    [InlineData("no-look")]
    [InlineData("before-flag")]
    [InlineData("wrong-total")]
    [InlineData("wrong-population")]
    [InlineData("wrong-source")]
    [InlineData("count-only")]
    [InlineData("short-list")]
    [InlineData("duplicate-items")]
    [InlineData("cut")]
    [InlineData("refused-look")]
    [InlineData("no-reason")]
    [InlineData("not-opened")]
    public void NotListableCannotSkipThePopulationLook(string fault)
    {
        if (fault == "before-flag") Call("python inspect.py measurement.json", Pieces());
        Call("python coverage.py", "71 missing");
        var output = fault switch
        {
            "wrong-total" => Pieces(72),
            "wrong-population" => Pieces().Replace("missing:", "extra:"),
            "count-only" => "missing: count 71; pieces > 1e-9 60",
            "short-list" => Pieces().Replace("60 {", "not-an-item {"),
            "duplicate-items" => Pieces().Replace("60 {", "59 {"),
            "refused-look" => "PreToolUse:Bash hook error: denied\n" + Pieces(),
            _ => Pieces(),
        };
        if (fault is not ("no-look" or "before-flag"))
            Call("python inspect.py " + (fault == "wrong-source" ? "other.json" : "measurement.json") + (fault == "cut" ? " | head -61" : ""), output);
        Say(fault switch { "no-reason" => "NOT LISTABLE: 71 missing", "not-opened" => "NOT OPENED: 71 missing", _ => NotListable });
        Assert.Contains("W3", Ledger().Decide(Pre("takeoff --deliver bundle")).Rules);
    }

    [Fact]
    public void HeldOutRightRefusalStillStopsTheUnopenedPadSend()
    {
        // Round 1 A1 L6434/6444 -> L6516, independent of the new false-refusal fixtures.
        Flag();
        Say("NOT OPENED: 4-sided 176 of 180");
        Assert.Contains("W3", Ledger().Decide(Pre("pwsh run-on-210/send-set.ps1")).Rules);
    }

    [Fact]
    public void RealSelfTestReplyKeepsTheWholeSentence()
    {
        // The full-prefix comparison found that the short fixture omitted the trailing counts.
        Call("python selftest.py", "45 of 46 checks passed; 22 claude -p sessions; $2.4586");
        Assert.False(Ledger().Decide(Reply("The self-test passed 45 of 46 checks: 22 sessions, about $2.46. The one failure, `folder:IanBrain:memory` answering NOT IN CONTEXT, is the same one already open as board row 2189 from 2.1.329.")).Refuse);
    }

    [Fact]
    public void CompletedLookCreditsAccountingCarriedByThatCall()
    {
        Flag();
        Call("# OPENED 4 of 4: 4-sided 176 of 180 - pads.txt\ncat pads.txt", "pad A\npad B\npad C\npad D");
        var l = Ledger();
        Assert.False(l.Decide(Reply("Continuing work.")).Refuse);
        Assert.False(l.Decide(Pre("takeoff --deliver bundle")).Refuse);
    }

    [Fact]
    public void DeniedToolInputDoesNotAccountForAnything()
    {
        Flag();
        Call("# NOT OPENED: 4-sided 176 of 180\npython helper.py", "PreToolUse:Bash hook error: denied");
        Assert.Contains("W1", Ledger().Decide(Reply("Everything passed.")).Rules);
    }

    [Fact]
    public void SavedStateThroughAnOutputFileCannotBeRefused()
    {
        Flag();
        Assert.False(Ledger().Decide(Pre("kor state --file state.md | Out-File saved.txt", "PowerShell")).Refuse);
    }

    [Fact]
    public void NotListableAlsoWorksForNumberedTextItems()
    {
        Call("python coverage.py", "3 missing");
        Call("python inspect.py mesh.csv", "missing: count 3; listable: 2\n1: A1 hole at (2,3)\n2: A18 hole at (4,5)");
        Say("NOT LISTABLE: 3 missing - inspected mesh.csv: two named holes; the third has no stored identity.");
        Assert.False(Ledger().Decide(Pre("takeoff --deliver bundle")).Refuse);
    }

    [Fact]
    public void PythonTextReaderStillReportsThisSessionsOwnResult()
    {
        Call("dotnet test tests.csproj > C:/run/result.log", "exit 1");
        Call("python -I -c \"print(open('C:/run/result.log', encoding='utf-8').read())\"", "Failed: 4");
        Assert.Contains("W3", Ledger().Decide(Pre("takeoff --deliver bundle")).Rules);
    }
}
