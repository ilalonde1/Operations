#nullable enable
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The agent's job broker: the rules that decide whether a script runs through the agent, through the network, or
// -- the one that must never happen -- through both.
//
// WHAT IT COVERS: a job reaches a polling agent and its result comes back as the network route's OnTargetRun; a
// PC with no connected agent goes to the network route at once; a job the agent does not take in time is
// withdrawn so a late poll can never run it as well; one PC cannot answer for another's job; the agent's
// statuses map onto the network route's.
// WHAT IT DOES NOT: the wire (JSON both ways, TLS, the key) and real PowerShell -- AgentEndToEndTests does that
// against the real agent exe. A SAME-CLASS FAULT IT WOULD NOT CATCH: a job taken by the agent and then lost
// (the agent crashes mid-script) is reported as a timeout after the script's limit plus grace, which is right,
// but nothing here proves the timing on a real PC.
public sealed class AgentHubTests
{
    private const string Pc = "KOR-104N";
    private const string Ok = """{"Ok":true,"Output":["hello"]}""";

    [Fact]
    public async Task A_job_reaches_the_polling_agent_and_comes_back_as_a_normal_run()
    {
        var hub = new AgentHub(TimeProvider.System);
        hub.Seen(Pc, "1.0.0", @"C:\ProgramData\KorOperations\Agent\work", "192.168.1.79");

        var run = hub.RunAsync(Pc, "'hello'", TimeSpan.FromSeconds(30), wantsIdle: true, default);
        var job = await hub.NextJobAsync(Pc, TimeSpan.FromSeconds(5), default);

        Assert.NotNull(job);
        Assert.True(job!.WantsIdle);
        Assert.Contains(@"C:\ProgramData\KorOperations\Agent\work\" + job.JobId + ".json", job.Script);   // published where the agent looks
        Assert.True(hub.Complete(Pc, job.JobId, new AgentJobOutcome("ok", Ok, null, 120)));
        var result = await run;
        Assert.NotNull(result);
        Assert.Equal(OnTargetStatus.Ok, result!.Status);
        Assert.Equal("""["hello"]""", result.OutputJson);
        Assert.StartsWith("agent:", result.Stages);
    }

    [Fact]
    public async Task A_pc_whose_agent_is_not_connected_goes_to_the_network_route()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hub = new AgentHub(clock);
        Assert.Null(await hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default));   // never seen

        hub.Seen(Pc, "1.0.0", @"C:\w", null);
        clock.Advance(AgentHub.Fresh + TimeSpan.FromSeconds(1));
        Assert.False(hub.IsConnected(Pc));
        Assert.Null(await hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default));   // seen, but silent too long
    }

    [Fact]
    public async Task A_job_not_taken_in_time_is_withdrawn_and_never_handed_out_later()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hub = new AgentHub(clock);
        hub.Seen(Pc, "1.0.0", @"C:\w", null);

        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        await Task.Yield();
        clock.Advance(AgentHub.PickupWait + TimeSpan.FromSeconds(1));
        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(5)));   // the caller falls back to the network route...

        hub.Seen(Pc, "1.0.0", @"C:\w", null);
        Assert.Null(await hub.NextJobAsync(Pc, TimeSpan.FromMilliseconds(300), default));   // ...and the agent never runs it too
    }

    [Fact]
    public async Task One_pc_cannot_answer_for_another_pcs_job()
    {
        var hub = new AgentHub(TimeProvider.System);
        hub.Seen(Pc, "1.0.0", @"C:\w", null);
        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        var job = await hub.NextJobAsync(Pc, TimeSpan.FromSeconds(5), default);

        Assert.False(hub.Complete("KOR-216", job!.JobId, new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.False(hub.Complete(Pc, "not-a-job", new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.True(hub.Complete(Pc, job.JobId, new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.Equal(OnTargetStatus.Ok, (await run)!.Status);
    }

    [Theory]
    [InlineData("ok", """{"Ok":false,"Error":"Access is denied","Line":12}""", null, OnTargetStatus.ScriptError, "line 12: Access is denied")]
    [InlineData("timeout", null, "no result after 600 s", OnTargetStatus.Timeout, "no result after 600 s")]
    [InlineData("error", null, "powershell exited 1 without publishing a result", OnTargetStatus.ScriptError, "powershell exited 1")]
    [InlineData("ok", "not json", null, OnTargetStatus.ScriptError, "not JSON")]
    public void The_agents_statuses_mean_what_the_network_routes_do(string status, string? result, string? error, OnTargetStatus expected, string text)
    {
        var run = AgentHub.ToRun(Pc, new AgentJobOutcome(status, result, error, 5), TimeSpan.FromSeconds(1));
        Assert.Equal(expected, run.Status);
        Assert.Contains(text, run.Error);
    }

    [Fact]
    public void A_key_matches_only_its_own_hash()
    {
        var key = AgentApi.NewKey();
        Assert.Equal(43, key.Length);                        // 256 bits, base64url, no padding
        Assert.DoesNotContain('+', key);
        Assert.True(AgentApi.Matches(key, AgentApi.Hash(key)));
        Assert.False(AgentApi.Matches(AgentApi.NewKey(), AgentApi.Hash(key)));
    }
}
