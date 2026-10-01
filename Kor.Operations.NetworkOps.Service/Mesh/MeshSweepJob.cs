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
internal sealed class MeshSweepJob(NetworkOpsStore store, MeshState state, IOptions<NetworkOpsOptions> options, ILogger<MeshSweepJob> log) : INetworkOpsJob
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
        state.Update(linked);
        var stored = await store.SaveMeshNodesAsync(linked, DateTime.UtcNow, ct).ConfigureAwait(false);

        var summary = $"{nodes.Count} Mesh devices: {linked.Count} linked ({linked.Count(l => l.Node.AgentConnected)} connected)" +
                      (unmatched.Count > 0 ? $"; not a NetworkOps device: {string.Join(", ", unmatched.Select(n => n.Name))}" : "") +
                      (stored ? "" : "; NOT STORED (run db/KorNetworkOps/006_Mesh.sql)");
        log.LogInformation("Mesh sweep: {Summary}", summary);
        return summary;
    }
}
