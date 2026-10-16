#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Api;

/// <summary>
/// The ONE assembly of the fleet that anyone reads: the app (/api/fleet, /api/rack, /api/to-clear, /api/updates), the CLI
/// (netops, through /api/fleet and /api/rack), Claude (the MCP tools) and the prompt library. SQL, plus the agents' live
/// state, plus remote control, plus the live UPS reading -- and the answer says when it was read (ReadUtc) and how old its
/// remote-control state is (MeshReadUtc).
/// <para>Before 2026-10-16 the fleet was assembled five different ways: the MCP tools served the raw SQL (agentConnected
/// false on every PC, no remote control), To clear and Updates served it raw too, and the rack carried no agents. One
/// question about the fleet had up to three answers (audit 2026-10-16, tier 2). <c>FleetAssemblyTests</c> fails if anything
/// outside this class assembles it again.</para>
/// </summary>
internal sealed class FleetView(NetworkOpsStore store, Agents.AgentHub agents, Mesh.MeshState mesh, Power.PowerState power,
    IOptions<NetworkOpsOptions> options, TimeProvider clock)
{
    /// <summary>The PCs (the directory fleet).</summary>
    public async Task<FleetSnapshot> PcsAsync(CancellationToken ct)
    {
        var meshRecords = await store.MeshRecordsAsync(ct).ConfigureAwait(false);
        var sql = await store.FleetSnapshotAsync(ct).ConfigureAwait(false);
        return Stamp(WithMesh(WithAgents(sql, await store.AgentRecordsAsync(ct).ConfigureAwait(false), agents), mesh, meshRecords));
    }

    /// <summary>The rack (hosts, storage, UPSes, backup, network, internet, servers, printers): served apart from the PCs so a
    /// page that predates the rack never lists a host or a UPS as a PC.</summary>
    public async Task<FleetSnapshot> RackAsync(CancellationToken ct)
    {
        var meshRecords = await store.MeshRecordsAsync(ct).ConfigureAwait(false);
        var sql = await store.FleetSnapshotAsync(ct, rack: true).ConfigureAwait(false);
        return Stamp(WithUps(WithMesh(sql, mesh, meshRecords), power.Snapshot(false, []).Ups, options.Value.Rack));
    }

    public async Task<(FleetSnapshot Pcs, FleetSnapshot Rack)> BothAsync(CancellationToken ct)
        => (await PcsAsync(ct).ConfigureAwait(false), await RackAsync(ct).ConfigureAwait(false));

    private FleetSnapshot Stamp(FleetSnapshot s) => s with { ReadUtc = clock.GetUtcNow().UtcDateTime };

    /// <summary>Each PC's agent: installed (SQL) and live (the hub, which knows within seconds; SQL is written every few minutes).</summary>
    internal static FleetSnapshot WithAgents(FleetSnapshot fleet, IReadOnlyDictionary<string, NetworkOpsStore.AgentRecord> installed, Agents.AgentHub hub)
        => fleet with
        {
            Devices = fleet.Devices.Select(d =>
            {
                if (!installed.TryGetValue(d.Name, out var rec)) return d;
                var live = hub.Status(d.Name);
                return d with
                {
                    AgentVersion = live?.Version is { Length: > 0 } v ? v : rec.LastVersion ?? rec.Version,
                    AgentConnected = live?.Connected == true,
                    AgentLastContactUtc = new[] { live is { LastPollUtc: var p } && p != default ? p : (DateTime?)null, rec.LastContactUtc }.Max(),
                };
            }).ToList(),
        };

    /// <summary>
    /// Each device's remote control: the live read while it is fresh, else what was stored (just after a restart). Either way
    /// the snapshot says how old it is (MeshReadUtc): the stored picture used to be served as if it were live.
    /// </summary>
    internal static FleetSnapshot WithMesh(FleetSnapshot fleet, Mesh.MeshState live, IReadOnlyDictionary<int, NetworkOpsStore.MeshRecord> stored)
        => fleet with
        {
            Devices = fleet.Devices.Select(d =>
                live.Fresh
                    ? live.For(d.DeviceId) is { } l ? d with { MeshNodeId = l.Node.Id, MeshConnected = l.Node.AgentConnected } : d
                    : stored.TryGetValue(d.DeviceId, out var r) ? d with { MeshNodeId = r.NodeId, MeshConnected = r.Connected } : d).ToList(),
            MeshReadUtc = live.Fresh ? live.LastReadUtc : stored.Count > 0 ? stored.Values.Max(r => r.LastReadUtc) : null,
        };

    /// <summary>
    /// A UPS's rack row from the SAME reading as the Power panel: the power watcher reads the UPSes every few seconds, the rack
    /// sweep every 5 minutes, and one screen showed both ("52 min / 23 %" beside "48 min / 24 %"). The fresher reading wins
    /// and carries its own time. A rack UPS is tied to its watcher reading by its UpsName (appsettings Rack[].UpsName).
    /// </summary>
    internal static FleetSnapshot WithUps(FleetSnapshot rack, IReadOnlyList<UpsRow> live, IReadOnlyList<RackDevice> config)
    {
        if (live.Count == 0) return rack;
        var upsNameOf = config.Where(c => c.UpsName.Length > 0).ToDictionary(c => c.Name, c => c.UpsName, StringComparer.OrdinalIgnoreCase);
        return rack with
        {
            Devices = rack.Devices.Select(d =>
                d.Kind == Core.Rack.RackKinds.Ups && upsNameOf.TryGetValue(d.Name, out var upsName)
                && live.FirstOrDefault(u => u.Name.Equals(upsName, StringComparison.OrdinalIgnoreCase)) is { Reachable: true } u
                && (d.LastCheckedUtc is not { } lc || u.AtUtc > lc)
                    ? d with { Summary = Core.Rack.UpsRules.Line(u.Source, u.MinutesRemaining, u.LoadPercent), LastCheckedUtc = u.AtUtc, LastReachableUtc = u.AtUtc }
                    : d).ToList(),
        };
    }
}
