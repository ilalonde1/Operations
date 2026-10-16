#nullable enable
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Mesh;

// Every 5 minutes: read KOR-MESH01's device list (read-only account, pinned certificate), link each node to its
// NetworkOps device, keep that live (MeshState) and stored (NetworkOps.MeshNodes). The Connect button and the
// mesh-missing / mesh-silent findings read what this writes. Also run by an install, to confirm the new agent.
// Then the live re-judge (Core/Health/LiveRules): remote-control and agent findings the live state proves gone are cleared
// now, not at the next weekday health check.
internal sealed class MeshSweepJob(NetworkOpsStore store, MeshState state, Agents.AgentHub agents, Alerting.IDigestSender digest,
    IOptions<NetworkOpsOptions> options, ILogger<MeshSweepJob> log) : INetworkOpsJob
{
    public const string JobName = "MeshSweep";
    public string Name => JobName;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.MeshEnabled) return "remote control off (MeshUrl, MeshCertSha256, MeshUser or KOR_NETWORKOPS_MESHPASSWORD not set)";
        IReadOnlyList<MeshNode> nodes;
        try { nodes = await new MeshCentralClient(new Uri(o.MeshUrl), o.MeshCertSha256, o.MeshUser, o.MeshPassword).ListNodesAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            state.Failed(ex.Message);
            throw new InvalidOperationException($"could not read MeshCentral at {o.MeshUrl}: {ex.Message}", ex);
        }

        var pcs = await store.DirectoryDevicesAsync(ct).ConfigureAwait(false);
        var rackIds = await store.RackDeviceIdsAsync(ct).ConfigureAwait(false);
        var rack = o.Rack.Where(r => rackIds.ContainsKey(r.Name)).Select(r => new RackIdentity(rackIds[r.Name], r.Name, r.Address, r.MeshName)).ToList();
        var (linked, unmatched) = MeshMap.Map(nodes, pcs, rack, o.MeshPcGroup, o.MeshServerGroup);
        state.Update(linked, nodes);
        // The live re-judge first: it reads only the live state, so a failing save can never hold it back.
        var cleared = await ClearWhatLiveStateProvesGoneAsync(ct).ConfigureAwait(false);
        var stored = await store.SaveMeshNodesAsync(state.Links, DateTime.UtcNow, ct).ConfigureAwait(false);

        var summary = $"{nodes.Count} Mesh devices: {linked.Count} linked ({linked.Count(l => l.Node.AgentConnected)} connected)" +
                      (unmatched.Count > 0 ? $"; not a NetworkOps device: {string.Join(", ", unmatched.Select(n => n.Name))}" : "") +
                      (stored ? "" : "; NOT STORED (run db/KorNetworkOps/006_Mesh.sql)") +
                      (cleared > 0 ? $"; cleared {cleared} remote-control/agent finding{(cleared == 1 ? "" : "s")} the live state shows are gone" : "");
        log.LogInformation("Mesh sweep: {Summary}", summary);
        return summary;
    }

    /// <summary>Clears (never raises) the remote-control and agent findings the live state proves gone (Core/Health/LiveRules),
    /// with a resolution like any clear, and a mail for any that were mailed when they were raised.</summary>
    private async Task<int> ClearWhatLiveStateProvesGoneAsync(CancellationToken ct)
    {
        var open = await store.OpenFindingsOfRulesAsync(["mesh-missing", "mesh-silent", "agent-silent", "agent-outdated"], ct).ConfigureAwait(false);
        if (open.Count == 0) return 0;
        var names = (await store.DirectoryDevicesAsync(ct).ConfigureAwait(false)).ToDictionary(kv => kv.Value, kv => kv.Key);
        foreach (var (name, id) in await store.RackDeviceIdsAsync(ct).ConfigureAwait(false)) names.TryAdd(id, name);
        var installed = await store.AgentRecordsAsync(ct).ConfigureAwait(false);
        var shipped = Agents.AgentInstaller.PackageVersionOrNull();
        var now = DateTime.UtcNow;
        var mail = new List<Core.Health.DeviceChanges>();
        var total = 0;
        foreach (var (deviceId, findings) in open)
        {
            if (!names.TryGetValue(deviceId, out var name)) continue;
            var changes = Core.Health.LiveRules.Clears(findings, state.PresenceOf(deviceId), Sweep.HealthSweeper.AgentStateOf(name, installed, agents), shipped);
            if (changes.Count == 0) continue;
            await store.ApplyChangesAsync(deviceId, changes, now, ct).ConfigureAwait(false);
            foreach (var c in changes)
            {
                var from = c.Previous!.LastSeenUtc ?? c.Previous.FirstSeenUtc;
                await store.InsertResolutionAsync(c.Previous.FindingId, deviceId, new Core.Learning.Resolution(c.RuleKey, now, false,
                    await store.FactChangesBetweenAsync(deviceId, from, now, ct).ConfigureAwait(false),
                    await store.ActionsBetweenAsync(deviceId, from, now, ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
            }
            total += changes.Count;
            if (changes.Where(Core.Health.FindingDiff.IsNotifiable).ToList() is { Count: > 0 } mailable) mail.Add(new Core.Health.DeviceChanges(name, mailable));
            log.LogInformation("Live re-judge cleared on {Device}: {Rules}", name, string.Join(", ", changes.Select(c => c.RuleKey)));
        }
        if (mail.Count > 0) await digest.SendAsync(mail, [], DateTime.Now, ct).ConfigureAwait(false);
        return total;
    }
}
