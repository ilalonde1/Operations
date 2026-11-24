using System.Text.Json;
using Kor.Operations.Framework.Whole;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Round 5 classes: item identities vs counts; attributed review clauses vs current findings;
/// complete testdiff attestations vs bare totals; stage-local recovery; executable reader dialects;
/// explicit missing values; exact accounting keys and population identity.
/// Checks ledger findings AND doors, with paired refusal controls. Does not interpret arbitrary shells,
/// verify a review/comparison's honesty, or establish that an uncut look contains the actual flagged objects.
/// Same-class misses: dynamically assembled awk commands, unmarked quotations, reused audit item IDs,
/// and a dishonest testdiff producer. Local fixtures never execute their command strings.
/// </summary>
public class WholeGateRound5Tests
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
        var l = new WholeLedger();
        foreach (var e in WholeTranscript.Parse(_lines, 1)) l.Add(e);
        return l;
    }
    private static WholeGate.Door Pre(string command) => WholeGate.DoorOf("Bash", command, null, _ => null);
    private static WholeGate.Door Reply(string text = "Done.") => new(WholeGate.DoorKind.Reply, text, true, false, false);
    private static WholeGate.Door Step() => Pre("pwsh run-on-210/send-set.ps1");
    private void Pads() => Call("python check.py", "4-sided 176 of 180");

    [Theory]
    [InlineData("3", "FAIL")]
    [InlineData("3a", "FAIL")]
    [InlineData("9", "FAIL")]
    [InlineData("0", "REVIEW REQUIRED")]
    [InlineData("R", "REVIEW REQUIRED")]
    public void AcceptanceLabelsAreOneItemAndCleanRerunsReplaceThatItem(string id, string state)
    {
        // 905b4bf0 L237/248: 3, 3a, 4, 5, 9 are audit identities, including the previously missed 3a.
        Call("takeoff model-audit bundle", $"ACCEPTANCE {id} {state}: inspect this item's evidence.");
        var l = Ledger();
        var f = Assert.Single(l.Flags);
        Assert.Equal(1, f.R.Flagged);
        Assert.Equal($"ACCEPTANCE {id} {state}", f.R.Key);
        Assert.Contains("W1", l.Decide(Reply()).Rules);
        Assert.False(l.Decide(Reply($"NOT OPENED: ACCEPTANCE {id} {state}")).Refuse);
        Assert.Contains("W3", l.Decide(Step()).Rules);
        Call("takeoff model-audit bundle", $"ACCEPTANCE {id} PASS: checked.");
        Assert.False(Ledger().Decide(Step()).Refuse);
    }

    [Fact]
    public void BareItemListsNeedListContextAndKeepRealCounts()
    {
        Call("python audit.py", "Items:\n2 PASS: done\n3 FAIL: loads absent\n3a REVIEW REQUIRED: mesh\n9 FAIL: README");
        var l = Ledger();
        Assert.Equal(3, l.Flags.Count);
        Assert.All(l.Flags, f => Assert.Equal(1, f.R.Flagged));
        Assert.Contains(l.Flags, f => f.R.Key == "3a REVIEW REQUIRED");
        Assert.Contains("W3", l.Decide(Step()).Rules);
        Assert.Equal(3, Assert.Single(WholeGate.Readings("3 FAIL")).Flagged);
        Assert.Equal(9, Assert.Single(WholeGate.Readings("9 failures")).Flagged);
    }

    [Fact]
    public void AnItemReportAccountsIdentitiesAndTheAuditSummaryWithoutOpeningThem()
    {
        Call("takeoff model-audit bundle", "ACCEPTANCE 3 FAIL: load omission\nACCEPTANCE 3a FAIL: mesh\nACCEPTANCE 4 FAIL: span\nACCEPTANCE 5 FAIL: loads\nACCEPTANCE 9 FAIL: notes\nACCEPTANCE 0 REVIEW REQUIRED: sample\nACCEPTANCE 8 REVIEW REQUIRED: CAD\nACCEPTANCE R REVIEW REQUIRED: release\nACCEPTANCE RESULT NOT ACCEPTED: 6 of 14 items PASS (FAIL 5, PASS 6, REVIEW REQUIRED 3)");
        var l = Ledger();
        Assert.False(l.Decide(Reply("NOT ACCEPTED, 6 of 14 PASS.\n- 3: loads are omitted\n- 3a: missing mesh evidence\n- 4: no span record\n- 5: no load transfer proof\n- 9: wrong notes\n- 0: sample still needs review\n- 8: CAD still needs review\n- R: release still needs review")).Refuse);
        Assert.Contains("W3", l.Decide(Step()).Rules);
        Assert.Contains("W1", Ledger().Decide(Reply("Widgets: 6 of 14 PASS.")).Rules);
    }

    [Fact]
    public void ReviewDispositionDetailIsAttributedButTheItemStillFails()
    {
        // 905b4bf0 L237 item 9: 91/92 are quoted COUNTS, not item labels as the brief claims.
        Call("takeoff model-audit bundle", "ACCEPTANCE 9 FAIL: rejected by Codex: README retains 91 DIFFERS; my count is 92 DIFFERS. Correct the notes.");
        var f = Assert.Single(Ledger().Flags);
        Assert.Equal("ACCEPTANCE 9 FAIL", f.R.Key);
        Assert.Equal(1, f.R.Flagged);
        Assert.Contains("W3", Ledger().Decide(Step()).Rules);
    }

    [Fact]
    public void LatestAuditStateReplacesEarlierNonpassStateOfTheSamePopulation()
    {
        Call("takeoff model-audit bundle", "ACCEPTANCE G FAIL: 2 of 2 component(s) not a measured success:\nACCEPTANCE RESULT NOT ACCEPTED: 5 of 14 items PASS (FAIL 1, PASS 5, REVIEW REQUIRED 8)");
        Call("takeoff model-audit bundle", "ACCEPTANCE G PASS: 1 of 1 component(s) a measured success:\nACCEPTANCE RESULT NOT ACCEPTED: 6 of 14 items PASS (PASS 6, REVIEW REQUIRED 8)");
        var l = Ledger();
        Assert.False(l.Decide(Reply("The bundle has 6 of 14 items PASS, 8 left for the reviewer.")).Refuse);
        Assert.Contains("W3", l.Decide(Step()).Rules);
    }

    private const string Comparison = "new : C:/run/new.trx\n  20 results for 20 defined tests (a theory's data rows each give a result): 17 passed, 3 failed, 0 skipped/not run, 0 other outcome\n  by name: 19 distinct names, 1 with more than one result (a name fails if any of its results failed)\nfailures in both: 2; only in new: 0; failing in base, passing in new: 0\ntests only in new: 1 (0 of them failing); base tests missing from new: 0\nVERDICT: NO NEW FAILURES - 2 of 19 test names failing in new, every one also failing in base (2 of 18); hung cases NOT MEASURED (no blame sequence on both sides)";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteTestdiffExplainsRunnerResultsInTheSameOrNextCompletedCall(bool next)
    {
        var run = "dotnet test tests.csproj --logger 'trx;LogFileName=C:/run/new.trx'";
        const string diff = "kor testdiff C:/run/base.trx C:/run/new.trx";
        if (next) { Call(run, "Failed: 3"); Call(diff, Comparison); }
        else Call(run + "; " + diff, "Failed: 3\n" + Comparison);
        Assert.False(Ledger().Decide(Step()).Refuse);
        // A complete comparison cannot explain an unrelated geometric deficit.
        Pads();
        Assert.Contains("W3", Ledger().Decide(Step()).Rules);
    }

    [Fact]
    public void TestdiffAttestationNeedsItsProtocolAndCannotOpenUnrelatedOrNonadjacentRuns()
    {
        Call("kor testdiff C:/run/base.trx C:/run/new.trx", Comparison);
        Assert.Empty(Ledger().Flags);
        foreach (var variant in new[]
        {
            Comparison.Replace("only in new: 0", "only in new: 1"),
            Comparison.Replace("failures in both: 2", "failures in both: 1"),
            Comparison.Replace("base tests missing from new: 0", "base tests missing from new: 1"),
            Comparison.Replace("VERDICT: NO NEW FAILURES", "VERDICT: INCOMPLETE"),
            "VERDICT: NO NEW FAILURES - 2 of 19 test names failing in new, every one also failing in base (2 of 18)",
        })
        {
            _lines.Clear();
            Call("dotnet test tests.csproj --logger 'trx;LogFileName=C:/run/new.trx'", "Failed: 3");
            Call("kor testdiff C:/run/base.trx C:/run/new.trx", variant);
            Assert.Contains("W3", Ledger().Decide(Step()).Rules);
        }
        foreach (var other in new[] { false, true })
        {
            _lines.Clear();
            Call("dotnet test tests.csproj --logger 'trx;LogFileName=C:/other/new.trx'", "Failed: 3");
            if (other) Call("cat note.txt", "note");
            Call("kor testdiff C:/run/base.trx C:/run/new.trx", Comparison);
            Assert.Contains("W3", Ledger().Decide(Step()).Rules);
        }
    }

    [Theory]
    [InlineData("python tools/kor_review.py review")]
    [InlineData("python tools/plan_review.py plan.md")]
    public void AttributedReviewExamplesAndHistoricalNumbersAreNotNewResults(string command)
    {
        // cc3b7be3 L3631/3636 and L4188/4198; same-line current findings must survive stripping.
        Call(command, "- MINOR: WholeGate.cs Readings - 'coverage: 71 missing 3 floors' reads as 3. '12 missing 2026-11-23' reads as 2026.\n- MINOR: docs/RESPONSE.md - The text says the first implementation passed 9 of 10 cases and exposed a fault.");
        Assert.Empty(Ledger().Flags);
        Call(command, "The brief says 6 of 14 PASS but my check shows 3 failed.");
        Assert.Contains(Ledger().Flags, f => f.R.Flagged == 3);
        Assert.Contains("W3", Ledger().Decide(Step()).Rules);
    }

    [Theory]
    [InlineData("The brief says 6 of 14 PASS but my check shows 3 failed.")]
    [InlineData("The document claims '6 of 14 PASS'; my check reports 3 failed.")]
    public void ReviewAttributionStopsBeforeTheReviewersCurrentFinding(string output)
    {
        Call("python tools/kor_review.py review", output);
        Assert.Equal(3, Assert.Single(Ledger().Flags).R.Flagged);
        Assert.Contains("W3", Ledger().Decide(Step()).Rules);
    }

    [Theory]
    [InlineData(";", "record --says 'saved'")]
    [InlineData("&&", "record --says 'saved'")]
    [InlineData("|", "record --says 'saved'")]
    [InlineData(";", "state --file state.md")]
    [InlineData("&&", "state --file state.md")]
    [InlineData("|", "state --file state.md")]
    public void SavingWorkDoesNotExemptAJoinedCommit(string join, string save)
    {
        Pads();
        var l = Ledger();
        var d = Pre($"kor {save} {join} git commit -m 'The 4 flagged pads are noise'");
        Assert.Equal(WholeGate.DoorKind.Commit, d.Kind);
        Assert.Contains("W2", l.Decide(d).Rules);
        Assert.False(l.Decide(Pre($"kor {save}")).Refuse);
        Assert.False(l.Decide(Pre("kor state --file state.md | Out-File saved.txt")).Refuse);
        Assert.Contains("W1", l.Decide(Pre($"kor {save} {join} python kor_task.py return 2139")).Rules);
    }

    [Theory]
    [InlineData("awk 'BEGIN{system(\"pwsh run-on-210/send-set.ps1\")}'")]
    [InlineData("awk 'BEGIN { system (\"takeoff --deliver bundle\") }'")]
    [InlineData("sed 's/x/pwsh run-on-210\\/send-set.ps1/e' input.txt")]
    [InlineData("sed 's@x@takeoff --deliver bundle@ge' input.txt")]
    [InlineData("sed -e 'e pwsh run-on-210/send-set.ps1' input.txt")]
    public void ExecutableReaderDialectsAreStepsAndTheirOutputsAreResults(string command)
    {
        Pads();
        var l = Ledger();
        Assert.Contains("W3", l.Decide(Pre(command)).Rules);
        Call(command, "3 failed");
        Assert.Contains(Ledger().Flags, f => f.R.Flagged == 3);
        foreach (var read in new[] { "awk '{print $1}' docs/run-on-210/README.md", "sed -n '1,25p' docs/run-on-210/README.md", "sed 's/example/else/g' docs/run-on-210/README.md" })
        {
            Assert.True(WholeGate.IsReader(read));
            Assert.False(l.Decide(Pre(read)).Refuse);
        }
    }

    [Theory]
    [InlineData("coverage: 71 missing 3 floors", 71)]
    [InlineData("12 missing 2026-11-23", 12)]
    [InlineData("71 missing 0.01 in2 pieces", 71)]
    public void AMissingCountIsNotReplacedByTheNextIncidentalNumber(string text, int expected)
    {
        Assert.Equal(expected, Assert.Single(WholeGate.Readings(text)).Flagged);
        Assert.Equal(0, Assert.Single(WholeGate.Readings("roles 20 files 63 missing []")).Flagged);
        Assert.Equal(0, Assert.Single(WholeGate.Readings("roles 22 files listed 65 missing 0 []")).Flagged);
        Assert.Equal(2, Assert.Single(WholeGate.Readings("roles 22 files listed 65 missing 2 ['a','b']")).Flagged);
    }

    [Fact]
    public void AnEmptyLabelCannotBorrowAnyOtherPopulationForNotListable()
    {
        // A deliberately constructed empty label: current parsed labels are nonempty; exercise the latent boundary.
        var l = new WholeLedger();
        ((List<WholeLedger.Flag>)l.Flags).Add(new WholeLedger.Flag { Line = 1, CallNo = 0, R = new WholeGate.Reading("unlabelled population", 3, 0, "", false) });
        l.Add(new WholeEv(2, WholeKind.Call, "Bash", "python inspect.py mesh.csv", "widgets: count 3; listable: 2\n1: A\n2: B", null, false));
        Assert.Contains("W3", l.Decide(Step() with { Text = "NOT LISTABLE: unlabelled population - inspected mesh.csv\n" + Step().Text }).Rules);
    }

    [Fact]
    public void AnExactRefusalKeyContainingDashesCanAlreadyBeOpened()
    {
        // Brief item 6 is a claim to test, not permission to manufacture a red result.
        Call("kor testdiff base.trx new.trx", "VERDICT: NO NEW FAILURES - 2 of 19 test names failing in new, every one also failing in base (2 of 18)");
        var l = Ledger();
        var keys = l.Flags.Select(f => f.R.Key).ToList();
        Assert.Contains(keys, k => k.Contains(" - ", StringComparison.Ordinal));
        Call("cat failures.txt", "case A\ncase B");
        foreach (var key in keys) Say($"OPENED 2 of 2: {key} - both case details read.");
        Assert.False(Ledger().Decide(Step()).Refuse);
    }
}
