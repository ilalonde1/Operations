#nullable enable
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Agents;

// Puts the agent on the fleet a batch at a time: the next AgentRolloutBatch PCs (default 5) that answered the
// network in the last two hours and have no agent or an older one, ONE AT A TIME, stopping at the first failure.
// Run it again for the next batch; the first run is the canary. Each PC's install is its own audited action row,
// exactly as if it had been asked for from that PC's window.
//
// On demand only (a JobTriggers row named AgentRollout, from the API or SQL): never scheduled, so the fleet is
// never changed without someone asking for it.
internal sealed class AgentRollout(NetworkOpsStore store, AgentInstaller installer, IOptions<NetworkOpsOptions> options, ILogger<AgentRollout> log)
{
    public const string JobName = "AgentRollout";
    public static readonly TimeSpan ReachableWithin = TimeSpan.FromHours(2);

    public async Task<string> RunAsync(string requestedBy, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.AgentsEnabled) return "agents are switched off on APP01 (AgentsEnabled = false): nothing installed";
        var shipped = AgentInstaller.PackageVersionOrNull() ?? throw new InvalidOperationException($"no agent package at {AgentInstaller.PackageDir}");

        var candidates = await store.AgentRolloutCandidatesAsync(DateTime.UtcNow - ReachableWithin, shipped, ct).ConfigureAwait(false);
        var batch = candidates.Take(Math.Max(1, o.AgentRolloutBatch)).ToList();
        if (batch.Count == 0) return $"nothing to do: every PC seen in the last {ReachableWithin.TotalHours:0} h has agent {shipped}";

        var done = new List<string>();
        foreach (var pc in batch)
        {
            var actionId = await store.StartActionAsync(pc.DeviceId, AgentInstaller.InstallKind, requestedBy,
                System.Text.Json.JsonSerializer.Serialize(new { action = "install", rollout = true, from = pc.AgentVersion }), ct).ConfigureAwait(false);
            bool ok;
            string detail;
            try { (ok, detail) = await installer.RunAsync(AgentInstaller.InstallKind, pc.DeviceId, pc.Name, requestedBy, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { (ok, detail) = (false, ex.Message); }
            await store.CompleteActionAsync(actionId, ok, detail, null).ConfigureAwait(false);
            log.LogWarning("ROLLOUT {Pc}: {Outcome} -- {Detail}", pc.Name, ok ? "done" : "FAILED", detail);
            if (!ok)
                return $"stopped at {pc.Name} (action {actionId}): {detail}. Done before it: {(done.Count == 0 ? "none" : string.Join(", ", done))}. " +
                       $"{candidates.Count - done.Count} PCs still to do.";
            done.Add(pc.Name);
        }
        return $"agent {shipped} on {done.Count} PCs ({string.Join(", ", done)}); {candidates.Count - done.Count} still to do";
    }
}
