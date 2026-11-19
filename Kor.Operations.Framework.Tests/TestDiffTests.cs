using Kor.Operations.Framework.Proof;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// W2, kor testdiff. COVERS: equal runs -> NO NEW FAILURES exit 0; an aborted run (trx RunInfos) -> INCOMPLETE even when the
/// counts match (the 19 Nov PdfToSafe case, written small); a started-but-unfinished case from a blame sequence -> INCOMPLETE; a
/// failure only in new -> NEW FAILURES; a base test missing from new -> NEW FAILURES; a skipped (NotExecuted) test is counted, not
/// INCOMPLETE; new tests that pass do not count against; no sequence -> the verdict says hung cases are NOT MEASURED.
/// DOES NOT COVER: real trx files from dotnet test (replayed by hand 19 Nov on KOR.Drafter step5 vs portmerge: PdfToSafe
/// INCOMPLETE exit 1, Core NO NEW FAILURES exit 0 - see the commit message); a trx in another schema version.
/// Same-class fault it would miss: a test that passes by asserting nothing.
/// </summary>
public class TestDiffTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "testdiff-" + Guid.NewGuid().ToString("N"));
    public TestDiffTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Trx(string name, (string Test, string Outcome)[] results, string? abort = null)
    {
        var defs = string.Concat(results.Select(r => $"<UnitTest name=\"{r.Test}\" />"));
        var res = string.Concat(results.Select(r => $"<UnitTestResult testName=\"{r.Test}\" outcome=\"{r.Outcome}\" />"));
        var info = abort is null ? "" : $"<RunInfos><RunInfo outcome=\"Error\"><Text>{abort}</Text></RunInfo></RunInfos>";
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "<?xml version=\"1.0\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">"
            + $"<Results>{res}</Results><TestDefinitions>{defs}</TestDefinitions><ResultSummary outcome=\"Failed\">{info}</ResultSummary></TestRun>");
        return path;
    }

    private string Sequence(string name, params (string Test, bool Completed)[] tests)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "<?xml version=\"1.0\"?><TestSequence>" + string.Concat(tests.Select(t => $"<Test Name=\"{t.Test}\" Completed=\"{(t.Completed ? "True" : "False")}\" />")) + "</TestSequence>");
        return path;
    }

    private static readonly (string, string)[] Base = { ("A", "Passed"), ("B", "Failed"), ("C", "Passed") };

    [Fact]
    public void EqualCompleteRuns_AreNoNewFailures_Exit0()
    {
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", Base)), TestDiff.ReadTrx(Trx("n.trx", Base.Append(("D", "Passed")).ToArray())));
        Assert.Equal(0, r.Exit);
        Assert.StartsWith("NO NEW FAILURES - 1 of 4 test names failing in new", r.Verdict);
        Assert.Contains("hung cases NOT MEASURED", r.Verdict);
    }

    [Fact]
    public void AnAbortedRun_IsIncomplete_EvenWhenEveryCountMatches()   // 19 Nov: "same 6 of 591" while the host was killed
    {
        var r = TestDiff.Compare(
            TestDiff.ReadTrx(Trx("b.trx", Base, "The active test run was aborted. Reason: Test host process crashed")),
            TestDiff.ReadTrx(Trx("n.trx", Base, "The active test run was aborted. Reason: Test host process crashed")));
        Assert.Equal(1, r.Exit);
        Assert.StartsWith("INCOMPLETE", r.Verdict);
        Assert.Contains("do not call these runs the same", r.Verdict);
    }

    [Fact]
    public void AStartedUnfinishedCase_IsIncomplete()
    {
        var seq = Sequence("s.xml", ("A", true), ("B", true), ("C", true), ("Hung", false));
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", Base), seq), TestDiff.ReadTrx(Trx("n.trx", Base), seq));
        Assert.Equal(1, r.Exit);
        Assert.Contains("1 started and unfinished", r.Verdict);
        Assert.Contains(r.Lines, l => l.Contains("Hung"));
    }

    [Fact]
    public void AFailureOnlyInNew_IsNewFailures()
    {
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", Base)), TestDiff.ReadTrx(Trx("n.trx", new[] { ("A", "Failed"), ("B", "Failed"), ("C", "Passed") })));
        Assert.Equal(1, r.Exit);
        Assert.StartsWith("NEW FAILURES - 1 failing only in new, 0 base tests missing", r.Verdict);
        Assert.Contains("  NEW FAILURE: A", r.Lines);
    }

    [Fact]
    public void ABaseTestMissingFromNew_IsNewFailures()
    {
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", Base)), TestDiff.ReadTrx(Trx("n.trx", new[] { ("A", "Passed"), ("B", "Failed") })));
        Assert.Equal(1, r.Exit);
        Assert.Contains("1 base tests missing from new", r.Verdict);
        Assert.Contains("  MISSING FROM NEW: C", r.Lines);
    }

    [Fact]
    public void ARepeatedNameFailingOnceThenPassing_StillFails_AndEveryResultIsCounted()   // the kor suite has 3 such repeats
    {
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", Base)),
            TestDiff.ReadTrx(Trx("n.trx", new[] { ("A", "Failed"), ("A", "Passed"), ("B", "Failed"), ("C", "Passed") })));
        Assert.Equal(1, r.Exit);
        Assert.Contains("  NEW FAILURE: A", r.Lines);
        Assert.Contains(r.Lines, l => l.Contains("4 results for 4 defined tests") && l.Contains("2 passed, 2 failed, 0 skipped/not run, 0 other outcome"));
        Assert.Contains(r.Lines, l => l.Contains("3 distinct names, 1 with more than one result"));
    }

    [Fact]
    public void ASkippedTest_IsCounted_NotIncomplete()
    {
        var withSkip = Base.Append(("S", "NotExecuted")).ToArray();
        var r = TestDiff.Compare(TestDiff.ReadTrx(Trx("b.trx", withSkip)), TestDiff.ReadTrx(Trx("n.trx", withSkip)));
        Assert.Equal(0, r.Exit);
        Assert.Contains(r.Lines, l => l.Contains("1 skipped/not run"));
    }
}
