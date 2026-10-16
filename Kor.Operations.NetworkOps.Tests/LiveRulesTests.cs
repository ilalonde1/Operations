#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The 5-minute live re-judge (Core/Health/LiveRules, run after each MeshCentral read): remote-control and agent findings
// that the live state proves gone are cleared between the weekday health checks.
//
// WHAT IT COVERS: each of the four rules clears on exactly its proof (node known, Mesh agent connected, agent calling in,
// agent not older than shipped); no read (null) clears nothing; it never raises and never touches another rule's finding.
// WHAT IT DOES NOT: the job wiring (MeshSweepJob reading the store and the hub) -- the deployed sweep's summary line says
// how many it cleared. A SAME-CLASS FAULT IT WOULD NOT CATCH: a MeshCentral read that says "connected" for a node that is
// not (a MeshCentral bug) clears mesh-silent here exactly as it would turn the Connect button green.
public sealed class LiveRulesTests
{
    private static OpenFinding F(string key) => new(1, key, Severity.Warning, new DateTime(2026, 10, 10), Severity.Warning);
    private static readonly IReadOnlyCollection<OpenFinding> All = [F("mesh-missing"), F("mesh-silent"), F("agent-silent"), F("agent-outdated"), F("low-disk:c")];
    private static AgentState Agent(bool connected, string version = "1.4.0") => new("1.4.0", version, connected, DateTime.UtcNow);

    private static string[] Cleared(MeshPresence? mesh, AgentState? agent, string? shipped = "1.4.0")
        => LiveRules.Clears(All, mesh, agent, shipped).Select(c => c.RuleKey).Order().ToArray();

    [Fact]
    public void Each_finding_clears_on_its_own_proof_and_nothing_else()
    {
        Assert.Equal(["agent-outdated", "agent-silent", "mesh-missing", "mesh-silent"], Cleared(new MeshPresence(true, true), Agent(true)));
        Assert.Equal(["mesh-missing"], Cleared(new MeshPresence(true, false), null));                  // known now, not connected
        Assert.Equal(["agent-outdated", "agent-silent"], Cleared(null, Agent(true)));                  // no fresh Mesh read
        Assert.Equal(["agent-silent"], Cleared(null, Agent(true, "1.3.0")));                           // calling in, still old
        Assert.Empty(Cleared(new MeshPresence(false, false), Agent(false)));                           // nothing proves anything gone
        Assert.Empty(Cleared(null, null));
        Assert.Equal(["agent-silent"], Cleared(null, Agent(true), shipped: null));                    // shipped version unreadable
    }

    [Fact]
    public void It_only_ever_clears()
        => Assert.All(LiveRules.Clears(All, new MeshPresence(true, true), Agent(true), "1.4.0"), c => Assert.Equal(ChangeKind.Cleared, c.Kind));
}
