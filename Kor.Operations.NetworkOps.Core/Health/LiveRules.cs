#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

// Remote control and the agent between health checks. Their findings are raised by the health check (hourly, weekdays
// 07-18), which knows the PC answered; but MeshCentral and the agent hub know within minutes when the problem has gone,
// and "Remote control is not connected" sat beside a Connect that worked all weekend (audit 2026-10-16, tier 2). So every
// 5 minutes, after the MeshCentral read, the live state may CLEAR what it proves gone -- and only that:
//
//   mesh-missing    cleared when MeshCentral now has a node for the device
//   mesh-silent     cleared when its Mesh agent is connected now
//   agent-silent    cleared when the NetworkOps agent is calling in now
//   agent-outdated  cleared when the agent calling in now is not older than the one APP01 ships
//
// It never RAISES: raising needs "the PC answered this check", which only the health check knows, and raising here would
// mail from a 5-minute job. A stale or missing read clears nothing (null mesh / null agent).
public static class LiveRules
{
    /// <summary>The findings this may clear: everything else is left exactly as it is.</summary>
    public static bool Owns(string ruleKey) => MeshRules.Owns(ruleKey) || ruleKey is "agent-silent" or "agent-outdated";

    /// <param name="mesh">The fresh MeshCentral read for this device; null when there is none (remote control off, stale read).</param>
    /// <param name="agent">The agent as the hub sees it now; null when the device has no agent.</param>
    /// <param name="packageVersion">The agent APP01 ships; null when it cannot be read (agent-outdated is then left alone).</param>
    public static IReadOnlyList<FindingChange> Clears(IReadOnlyCollection<OpenFinding> open, MeshPresence? mesh, AgentState? agent, string? packageVersion)
    {
        bool Gone(string key) => key switch
        {
            "mesh-missing" => mesh is { Known: true },
            "mesh-silent" => mesh is { Connected: true },
            "agent-silent" => agent is { Connected: true },
            "agent-outdated" => agent is { Connected: true, ReportedVersion: { } v } && packageVersion is not null && !AgentRules.IsOlder(v, packageVersion),
            _ => false,
        };
        return open.Where(o => Owns(o.RuleKey) && Gone(o.RuleKey)).Select(o => new FindingChange(ChangeKind.Cleared, o.RuleKey, null, o)).ToList();
    }
}
