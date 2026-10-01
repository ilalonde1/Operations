#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Service.Api;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The agent's own health, the kill switch, and how the fleet page learns which PCs have an agent.
//
// WHAT IT COVERS: agent-silent only when the PC answered over the network AND the agent has been quiet past the
// grace period; agent-outdated only for a connected agent older than what APP01 ships; no agent = no findings;
// AgentsEnabled=false sends a connected PC's job to the network route and hands the agent nothing; the fleet rows
// carry installed (SQL) and live (hub) agent state, live winning.
// WHAT IT DOES NOT: the sweep wiring that feeds these (HealthSweeper is proven on a real PC), or the rollout's SQL
// candidate query. A SAME-CLASS FAULT IT WOULD NOT CATCH: a sweep that passed answeredOverNetwork = true for an
// agent-run check would raise agent-silent for a working agent -- only the live 104N run shows that it does not.
public sealed class AgentRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_quiet_agent_on_a_pc_that_answered_the_network_is_a_warning()
    {
        var f = Assert.Single(AgentRules.Evaluate(new AgentState("1.0.0", "1.0.0", false, Now.AddHours(-3)), answeredOverNetwork: true, "1.0.0", Now));
        Assert.Equal("agent-silent", f.RuleKey);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("last heard 3 h ago", f.Evidence);
    }

    [Theory]
    [InlineData(false, 120, false)]    // the PC did not answer either: it is off, not the agent's fault
    [InlineData(true, 5, false)]       // quiet for 5 minutes: a restart or a blip, inside the grace period
    [InlineData(true, 120, true)]
    public void Silent_needs_the_pc_on_and_the_agent_quiet_past_the_grace_period(bool answered, int quietMinutes, bool expected)
        => Assert.Equal(expected, AgentRules.Evaluate(new AgentState("1.0.0", "1.0.0", false, Now.AddMinutes(-quietMinutes)), answered, "1.0.0", Now)
            .Any(f => f.RuleKey == "agent-silent"));

    [Fact]
    public void A_connected_agent_is_never_silent_and_is_outdated_only_when_older()
    {
        Assert.Empty(AgentRules.Evaluate(new AgentState("1.0.0", "1.0.0", true, Now), true, "1.0.0", Now));
        Assert.Empty(AgentRules.Evaluate(new AgentState("1.1.0", "1.1.0", true, Now), true, "1.0.0", Now));   // newer than shipped: not "outdated"
        Assert.Empty(AgentRules.Evaluate(new AgentState("1.0.0", "1.0.0", true, Now), true, null, Now));      // package unreadable: no claim
        var f = Assert.Single(AgentRules.Evaluate(new AgentState("1.0.0", "1.0.0", true, Now), false, "1.2.0", Now));
        Assert.Equal("agent-outdated", f.RuleKey);
        Assert.Contains("running 1.0.0; APP01 ships 1.2.0", f.Evidence);
    }

    [Fact]
    public void A_pc_without_an_agent_has_no_agent_findings()
        => Assert.Empty(AgentRules.Evaluate(null, true, "1.0.0", Now));

    [Fact]
    public void Both_agent_findings_have_knowledge()
    {
        Assert.NotNull(Knowledge.For("agent-silent"));
        Assert.NotNull(Knowledge.For("agent-outdated"));
    }

    [Fact]
    public async Task The_kill_switch_sends_a_connected_pc_to_the_network_route_and_gives_the_agent_nothing()
    {
        // A name that cannot resolve: the network route answers Offline at once, which is how we know it was taken.
        const string pc = "KOR-NO-SUCH-PC-0001";
        var hub = new AgentHub(TimeProvider.System);
        hub.Seen(pc, "1.0.0", @"C:\w", null);
        var runner = new MachineRunner(hub, Options.Create(new NetworkOpsOptions { AgentsEnabled = false }), NullLogger<MachineRunner>.Instance);

        Assert.False(runner.ViaAgent(pc));
        var run = await runner.RunAsync(pc, "'x'", TimeSpan.FromSeconds(30), false, default);
        Assert.Equal(OnTargetStatus.Offline, run.Status);
        Assert.Null(await hub.NextJobAsync(pc, TimeSpan.FromMilliseconds(200), default));   // nothing was queued for the agent
    }

    [Fact]
    public void Fleet_rows_carry_the_agent_installed_and_live()
    {
        var fleet = new FleetSnapshot(
            [new DeviceRow(1, "KOR-104N", Now, Now), new DeviceRow(2, "KOR-216", Now, Now), new DeviceRow(3, "KOR-101", Now, Now)],
            new Dictionary<string, IReadOnlyDictionary<string, string>>(), [], [], null);
        var installed = new Dictionary<string, NetworkOpsStore.AgentRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["KOR-104N"] = new("KOR-104N", "1.0.0", "1.0.0", Now.AddMinutes(-4)),
            ["KOR-216"] = new("KOR-216", "1.0.0", "0.9.0", Now.AddHours(-5)),
        };
        var hub = new AgentHub(TimeProvider.System);
        hub.Seen("KOR-104N", "1.0.1", @"C:\w", null);

        var rows = ApiHost.WithAgents(fleet, installed, hub).Devices.ToDictionary(d => d.Name);

        Assert.True(rows["KOR-104N"].AgentConnected);
        Assert.Equal("1.0.1", rows["KOR-104N"].AgentVersion);                 // live beats the record
        Assert.False(rows["KOR-216"].AgentConnected);
        Assert.Equal("0.9.0", rows["KOR-216"].AgentVersion);                  // what it last reported, not what was installed
        Assert.Equal(Now.AddHours(-5), rows["KOR-216"].AgentLastContactUtc);
        Assert.Null(rows["KOR-101"].AgentVersion);                            // no agent
    }
}
