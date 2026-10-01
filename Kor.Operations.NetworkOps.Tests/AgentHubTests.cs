#nullable enable
using Kor.Operations.NetworkOps.Core.OnTarget;
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The agent's job broker: the rules that decide whether a script runs through the agent, through the network, or
// -- the one that must never happen -- through both, or for the wrong caller, or for a revoked key.
//
// WHAT IT COVERS: a job reaches a polling agent and comes back as the network route's OnTargetRun; no connected agent
// = the network route at once; a job not taken in time, OR whose caller cancels, is withdrawn so no later poll runs it
// (Codex audit 2026-09-30, finding 2); one PC cannot answer another's job; revoking a key cuts its held poll, hands it
// nothing, and marks the PC not connected (finding 6); the key a PC last authenticated with is what Status reports, so
// an install is confirmed by the new key only (finding 5); the work folder a poll may report, and that no quote of any
// kind can reach the script (work-directory finding); the agent's statuses map onto the network route's.
// WHAT IT DOES NOT: the wire, TLS and real PowerShell (AgentEndToEndTests), or the installer on a real PC (proven on
// KOR-104N). A SAME-CLASS FAULT IT WOULD NOT CATCH: a revocation that happens between a poll's authentication and its
// Seen call -- the hub cannot see what it was never told; only the gate's ordering (auth, then Seen) prevents it.
public sealed class AgentHubTests
{
    private const string Pc = "KOR-104N";
    private const string Work = @"C:\Program Files\KorOperations\Agent\data\work";
    private const string Ok = """{"Ok":true,"Output":["hello"]}""";
    private const string Key = "AAAA";

    private static AgentHub Connected(TimeProvider? clock = null, string key = Key)
    {
        var hub = new AgentHub(clock ?? TimeProvider.System);
        hub.Seen(Pc, "1.0.0", "192.168.1.79", key);
        return hub;
    }

    [Fact]
    public async Task A_job_reaches_the_polling_agent_and_comes_back_as_a_normal_run()
    {
        var hub = Connected();
        var run = hub.RunAsync(Pc, "'hello'", TimeSpan.FromSeconds(30), wantsIdle: true, default);
        var job = await hub.NextJobAsync(Pc, Key, Work, TimeSpan.FromSeconds(5), default);

        Assert.NotNull(job);
        Assert.True(job!.WantsIdle);
        Assert.Contains(Work + @"\" + job.JobId + ".json", job.Script);   // published where THIS poll said it looks
        Assert.True(hub.Complete(Pc, Key,job.JobId, new AgentJobOutcome("ok", Ok, null, 120)));
        var result = await run;
        Assert.Equal(OnTargetStatus.Ok, result!.Status);
        Assert.Equal("""["hello"]""", result.OutputJson);
        Assert.StartsWith("agent:", result.Stages);
    }

    [Fact]
    public async Task A_pc_whose_agent_is_not_connected_goes_to_the_network_route()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Assert.Null(await new AgentHub(clock).RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default));   // never seen

        var hub = Connected(clock);
        clock.Advance(AgentHub.Fresh + TimeSpan.FromSeconds(1));
        Assert.False(hub.IsConnected(Pc));
        Assert.Null(await hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default));   // seen, but silent too long
    }

    [Fact]
    public async Task A_job_not_taken_in_time_is_withdrawn_and_never_handed_out_later()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hub = Connected(clock);
        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        await Task.Yield();
        clock.Advance(AgentHub.PickupWait + TimeSpan.FromSeconds(1));
        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(5)));   // the caller falls back to the network route...
        Assert.Null(await hub.NextJobAsync(Pc, Key, Work, TimeSpan.FromMilliseconds(300), default));   // ...and the agent never runs it too
    }

    [Fact]
    public async Task A_job_whose_caller_cancels_before_pickup_is_never_handed_out()
    {
        var hub = Connected();
        using var cancel = new CancellationTokenSource();
        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Null(await hub.NextJobAsync(Pc, Key, Work, TimeSpan.FromMilliseconds(300), default));
    }

    [Fact]
    public async Task One_pc_cannot_answer_for_another_pcs_job()
    {
        var hub = Connected();
        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        var job = await hub.NextJobAsync(Pc, Key, Work, TimeSpan.FromSeconds(5), default);

        Assert.False(hub.Complete("KOR-216", Key, job!.JobId, new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.False(hub.Complete(Pc, "SOMEOTHERKEY", job.JobId, new AgentJobOutcome("ok", Ok, null, 1)));   // right PC, wrong key
        Assert.False(hub.Complete(Pc, Key,"not-a-job", new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.True(hub.Complete(Pc, Key,job.JobId, new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.Equal(OnTargetStatus.Ok, (await run)!.Status);
    }

    [Fact]
    public async Task Revoking_a_key_cuts_its_held_poll_and_the_pc_is_not_connected_until_a_new_key_polls()
    {
        var hub = Connected(key: "OLDKEY");
        var held = hub.NextJobAsync(Pc, "OLDKEY", Work, TimeSpan.FromSeconds(20), default);   // the old agent waiting for work
        await Task.Delay(100);

        hub.Revoke(Pc, "NEWKEY");
        Assert.Null(await held.WaitAsync(TimeSpan.FromSeconds(2)));   // cut at once, handed nothing
        Assert.False(hub.IsConnected(Pc));                            // and its ending does not make the PC look alive
        Assert.Equal("", hub.Status(Pc)!.KeyHash);

        Assert.NotNull(hub.Seen(Pc, "1.0.1", "192.168.1.79", "NEWKEY"));
        Assert.True(hub.IsConnected(Pc));
        Assert.Equal("NEWKEY", hub.Status(Pc)!.KeyHash);              // what the installer waits for
    }

    [Fact]
    public async Task A_request_that_authenticated_before_a_revocation_is_refused_after_it()
    {
        // Codex re-check: an old-key request passes SQL, stalls, and only reaches the hub after the key was replaced.
        var hub = Connected(key: "OLDKEY");
        hub.Revoke(Pc, "NEWKEY");

        Assert.Null(hub.Seen(Pc, "1.0.0", null, "OLDKEY"));                                           // its Seen is refused
        Assert.Null(await hub.NextJobAsync(Pc, "OLDKEY", Work, TimeSpan.FromMilliseconds(200), default)); // it is handed nothing
        Assert.False(hub.IsConnected(Pc));

        // A job queued meanwhile waits for the CURRENT key, untouched by the stale poll.
        hub.Seen(Pc, "1.0.1", null, "NEWKEY");
        var run = hub.RunAsync(Pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        Assert.Null(await hub.NextJobAsync(Pc, "OLDKEY", Work, TimeSpan.FromMilliseconds(200), default));
        var job = await hub.NextJobAsync(Pc, "NEWKEY", Work, TimeSpan.FromSeconds(5), default);
        Assert.NotNull(job);
        Assert.False(hub.Complete(Pc, "OLDKEY", job!.JobId, new AgentJobOutcome("ok", Ok, null, 1)));   // nor can it answer
        Assert.True(hub.Complete(Pc, "NEWKEY", job.JobId, new AgentJobOutcome("ok", Ok, null, 1)));
        Assert.Equal(OnTargetStatus.Ok, (await run)!.Status);
    }

    [Fact]
    public async Task A_removed_pc_accepts_no_key_at_all()
    {
        var hub = Connected();
        hub.Revoke(Pc, currentKeyHash: null);
        Assert.Null(hub.Seen(Pc, "1.0.1", null, Key));
        Assert.Null(await hub.NextJobAsync(Pc, Key, Work, TimeSpan.FromMilliseconds(200), default));
    }

    [Theory]
    [InlineData(@"C:\Program Files\KorOperations\Agent\data\work", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\kor-agent-e2e-1a2b3c4d\work", true)]
    [InlineData(@"C:\work'; Remove-Item C:\ -Recurse; '", false)]
    [InlineData("C:\\work\u2019; whoami; \u2019", false)]   // a typographic quote: PowerShell closes strings on it too
    [InlineData(@"\\server\share\work", false)]
    [InlineData(@"C:\a\..\Windows", false)]
    [InlineData("", false)]
    public void Only_a_plain_local_work_folder_is_accepted(string path, bool ok) => Assert.Equal(ok, AgentApi.IsSafeWorkDir(path));

    [Theory]
    [InlineData(@"C:\w'x\r.json")]
    [InlineData("C:\\w\u2018x\\r.json")]
    [InlineData("C:\\w\u2019x\\r.json")]
    public void The_payload_refuses_a_result_path_with_any_kind_of_quote(string path)
        => Assert.Throws<ArgumentException>(() => OnTargetPayload.Build("'x'", path));

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
