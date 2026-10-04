#nullable enable
using Kor.Operations.NetworkOps.Service.Sweep;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// ActionRunner.DeployVerdict: the gate that stops a deploy payload's own failure from being recorded as a successful
// action (audit CODEX-KOROPS-MIGRATE-OR-UPDATE-AND-ADDIN-AUDIT finding #5). A deploy script runs as SYSTEM and returns
// an object; the dispatcher used to mark the action done on transport status alone, so a migration that ran but left a
// user's add-in unresolved -- or rolled back -- showed green over a broken box.
//
// WHAT THIS COVERS: the verdict read from the payload JSON -- Ok honoured, Done as a fallback, an unresolved AddinFailed
// forcing failure even past a true Ok, no-verdict -> null (so the caller keeps its transport-only behaviour for other
// deploy ops), stray leading output in an array, and malformed/empty input -> null. It uses the EXACT shapes
// migrate-to-korops.ps1 returns on its success, partial and catch paths.
// WHAT IT DOES NOT COVER: that the caller (ActionRunner deploy branch) combines (transport Ok && ok != false) correctly
// -- that is the one untested line at the call site; and it does not prove the PowerShell payload actually sets Ok to
// match reality on a real PC (the payload's own logic). A fault it would NOT catch: the payload computing Ok wrongly
// (e.g. reporting Ok=true while a user failed) -- that lives in the script, not here.
public sealed class DeployVerdictTests
{
    [Fact]
    public void Ok_true_is_success_and_carries_the_result_line()
    {
        var (ok, line) = ActionRunner.DeployVerdict("""{"Ok":true,"Done":true,"Result":"migrated/updated to 1.0.0.55; 1/1 signed-in users OK"}""");
        Assert.True(ok);
        Assert.Contains("migrated/updated", line);
    }

    [Fact]
    public void Ok_false_is_failure()
    {
        var (ok, line) = ActionRunner.DeployVerdict("""{"Ok":false,"Done":true,"Result":"INCOMPLETE: app=True; users still failing: jdesroches"}""");
        Assert.False(ok);
        Assert.Contains("INCOMPLETE", line);
    }

    [Fact]
    public void Unresolved_addin_user_forces_failure_even_past_a_true_ok()
    {
        // Defence in depth: AddinFailed present => not a success, whatever Ok claims.
        var (ok, line) = ActionRunner.DeployVerdict("""{"Ok":true,"Done":true,"AddinFailed":"jdesroches","Result":"ran"}""");
        Assert.False(ok);
        Assert.Contains("jdesroches", line);
    }

    [Fact]
    public void Empty_addin_failed_is_not_treated_as_a_failure()
    {
        var (ok, _) = ActionRunner.DeployVerdict("""{"Ok":true,"Done":true,"AddinFailed":"","Result":"ok"}""");
        Assert.True(ok);
    }

    [Fact]
    public void Catch_path_shape_is_failure()
    {
        // migrate-to-korops.ps1 catch block: Ok=false, Done=false, Error, Result.
        var (ok, line) = ActionRunner.DeployVerdict("""{"Ok":false,"Done":false,"Error":"V25 hash mismatch","Result":"FAILED: V25 hash mismatch"}""");
        Assert.False(ok);
        Assert.Contains("FAILED", line);
    }

    [Fact]
    public void Done_is_used_when_ok_is_absent()
    {
        var (okTrue, _) = ActionRunner.DeployVerdict("""{"Done":true,"Result":"ran"}""");
        var (okFalse, _) = ActionRunner.DeployVerdict("""{"Done":false,"Error":"boom"}""");
        Assert.True(okTrue);
        Assert.False(okFalse);
    }

    [Fact]
    public void No_verdict_returns_null_so_the_caller_keeps_transport_only_behaviour()
    {
        var (ok, line) = ActionRunner.DeployVerdict("""{"SomethingElse":1}""");
        Assert.Null(ok);
        Assert.Null(line);
    }

    [Fact]
    public void A_verdict_object_is_found_past_stray_leading_output()
    {
        // PowerShell can emit stray pipeline output before the result object; the verdict is still read.
        var (ok, line) = ActionRunner.DeployVerdict("""["some stray line",{"Ok":false,"Result":"INCOMPLETE"}]""");
        Assert.False(ok);
        Assert.Contains("INCOMPLETE", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all {")]
    public void Empty_or_malformed_input_returns_null(string? json)
    {
        var (ok, line) = ActionRunner.DeployVerdict(json);
        Assert.Null(ok);
        Assert.Null(line);
    }
}
