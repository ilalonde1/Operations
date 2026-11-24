using Kor.Operations.Framework.Whole;
using Xunit;

namespace Kor.Operations.Framework.Tests;

// Bounded replay follow-ups, still cases before their fixes. Covers the actual worker's review-list
// wording and audit reruns with changing log filenames, plus the minimum testdiff attestation.
// Does not infer item identities from arbitrary prose or follow aliases/filesystem links.
// Same-class miss: two audits deliberately reuse one bundle path for different content.
public class WholeGateRound5EvidenceTests
{
    private static WholeGate.Door Step() => WholeGate.DoorOf("Bash", "takeoff --deliver bundle", null, _ => null);
    private static void Run(WholeLedger l, int line, string cmd, string output) =>
        l.Add(new WholeEv(line, WholeKind.Call, "Bash", cmd, output, null, false, "C:/repo"));

    [Fact]
    public void TheWorkersFinalReviewRequiredListAccountsEachNamedItem()
    {
        var l = new WholeLedger();
        Run(l, 1, "takeoff model-audit bundle", "ACCEPTANCE 0 REVIEW REQUIRED: sample\nACCEPTANCE 8 REVIEW REQUIRED: CAD\nACCEPTANCE R REVIEW REQUIRED: release");
        var reply = new WholeGate.Door(WholeGate.DoorKind.Reply, "0, 8 and R still need review.", true, false, false);
        Assert.False(l.Decide(reply).Refuse);
        Assert.Contains("W3", l.Decide(Step()).Rules);
        Assert.Equal(3, l.Flags.Count);
    }

    [Fact]
    public void AuditIdentityUsesTheBundleNotTheChangingLogDestination()
    {
        var l = new WholeLedger();
        Run(l, 1, "cd C:/job && takeoff model-audit bundle > audit-9.txt", "ACCEPTANCE G FAIL: 2 of 2 component(s) not a measured success:");
        Run(l, 2, "cd C:/elsewhere && takeoff model-audit bundle > audit-10.txt", "ACCEPTANCE G PASS: 1 of 1 component(s) a measured success:");
        Assert.Contains("W3", l.Decide(Step()).Rules);
        Run(l, 3, "cd C:/job && takeoff model-audit bundle > audit-11.txt", "ACCEPTANCE G PASS: 1 of 1 component(s) a measured success:");
        Assert.False(l.Decide(Step()).Refuse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACompleteByNameAttestationDoesNotNeedTheOptionalResultStatistics(bool next)
    {
        const string verdict = "failures in both: 2; only in new: 0; failing in base, passing in new: 0\nVERDICT: NO NEW FAILURES - 2 of 20 test names failing in new, every one also failing in base (2 of 20)";
        const string run = "dotnet test tests.csproj --logger 'trx;LogFileName=C:/run/new.trx'";
        const string diff = "kor testdiff C:/run/base.trx C:/run/new.trx";
        var l = new WholeLedger();
        if (next) { Run(l, 1, run, "Failed: 2"); Run(l, 2, diff, verdict); }
        else Run(l, 1, run + "; " + diff, "Failed: 2\n" + verdict);
        Assert.False(l.Decide(Step()).Refuse);
        Run(l, 3, "python other.py", "2 failed");
        Assert.Contains("W3", l.Decide(Step()).Rules);
    }
}
