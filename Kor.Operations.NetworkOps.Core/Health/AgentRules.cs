#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

/// <param name="InstalledVersion">What the installer put on the PC.</param>
/// <param name="ReportedVersion">What the agent itself last said it is (null: never heard from).</param>
/// <param name="Connected">Calling in right now.</param>
/// <param name="LastContactUtc">When it was last heard from.</param>
/// <param name="InstalledUtc">When it was (re)installed: a fresh install is given the same grace as a fresh restart.</param>
public sealed record AgentState(string InstalledVersion, string? ReportedVersion, bool Connected, DateTime? LastContactUtc, DateTime? InstalledUtc = null);

// The endpoint agent's own health, judged on each health check of a PC that has one. Pure: the sweep supplies
// what it knows (the agent record, whether the hub hears it, whether the PC answered this check over the network).
//
//   agent-silent    the PC answered the check over the network, so it is on -- but its agent has not called in.
//                   The agent is stopped, crashed or blocked; checks still work (the network route), but a PC
//                   that is off the network at sweep time is no longer covered, and idle time is not read.
//   agent-outdated  the agent is older than the one APP01 ships: it works, but misses whatever the newer one fixed.
public static class AgentRules
{
    /// <summary>A freshly started or briefly interrupted agent is not "silent": it reconnects within seconds.</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromMinutes(15);

    /// <param name="answeredOverNetwork">This check reached the PC by the network route (so the PC is on and reachable).</param>
    /// <param name="packageVersion">The agent version APP01 ships; null when it cannot be read (no outdated finding then).</param>
    public static IReadOnlyList<Finding> Evaluate(AgentState? agent, bool answeredOverNetwork, string? packageVersion, DateTime nowUtc)
    {
        if (agent is null) return [];
        var found = new List<Finding>();
        // Quiet since the later of its last contact and its (re)install: a just-installed agent is not "silent".
        var since = new[] { agent.LastContactUtc, agent.InstalledUtc }.Max();
        if (!agent.Connected && answeredOverNetwork && (since is not { } s || nowUtc - s > SilentAfter))
            found.Add(new("agent-silent", Severity.Warning, "The NetworkOps agent is not calling in",
                $"agent {agent.InstalledVersion} is installed, last heard {(agent.LastContactUtc is { } t ? Ago(nowUtc - t) + " ago" : "never")}, " +
                "but the PC answered this check over the network"));
        if (agent.Connected && packageVersion is not null && agent.ReportedVersion is { } reported && IsOlder(reported, packageVersion))
            found.Add(new("agent-outdated", Severity.Info, "The NetworkOps agent is out of date",
                $"running {reported}; APP01 ships {packageVersion}"));
        return found;
    }

    /// <summary>
    /// The one version comparison (the rollout uses it too): older than <paramref name="shipped"/>. A version that does
    /// not parse is treated as older -- reinstalling fixes it -- rather than silently passing as current.
    /// </summary>
    public static bool IsOlder(string? have, string shipped)
        => !Version.TryParse(shipped, out var ship) ? false : !Version.TryParse(have, out var v) || v < ship;

    private static string Ago(TimeSpan d) => d < TimeSpan.FromHours(1) ? $"{(int)d.TotalMinutes} min" : d < TimeSpan.FromDays(2) ? $"{(int)d.TotalHours} h" : $"{(int)d.TotalDays} days";
}
