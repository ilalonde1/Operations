#nullable enable
using System.Collections.Concurrent;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Transport;

namespace Kor.Operations.NetworkOps.Service.Mesh;

/// <summary>One NetworkOps device and its MeshCentral node.</summary>
internal sealed record MeshLink(int DeviceId, string DeviceName, MeshNode Node, string Group);

/// <summary>A NetworkOps rack device as MeshCentral may know it: by its MeshName, else by its Address.</summary>
internal sealed record RackIdentity(int DeviceId, string Name, string Address, string MeshName);

// Which MeshCentral node is which NetworkOps device. Pure, so the matching can be tested without either system.
// A PC matches by name (MeshCentral uses the computer name); a rack server by its configured MeshName, else its Address
// (KOR-APP01 is configured with Address "KOR-APP01" and appears in Mesh as "Kor-APP01"). Case never matters.
internal static class MeshMap
{
    public static (IReadOnlyList<MeshLink> Linked, IReadOnlyList<MeshNode> Unmatched) Map(
        IReadOnlyList<MeshNode> nodes, IReadOnlyDictionary<string, int> pcs, IReadOnlyList<RackIdentity> rack, string pcGroup, string serverGroup)
    {
        var linked = new List<MeshLink>();
        var unmatched = new List<MeshNode>();
        foreach (var n in nodes)
        {
            var group = n.MeshId == pcGroup ? "KOR PCs" : n.MeshId == serverGroup ? "KOR Servers" : n.MeshId;
            if (pcs.TryGetValue(n.Name, out var pcId)) { linked.Add(new MeshLink(pcId, n.Name, n, group)); continue; }
            var server = rack.FirstOrDefault(r => (r.MeshName.Length > 0 ? r.MeshName : r.Address).Equals(n.Name, StringComparison.OrdinalIgnoreCase));
            if (server is not null) { linked.Add(new MeshLink(server.DeviceId, server.Name, n, group)); continue; }
            unmatched.Add(n);
        }
        return (linked, unmatched);
    }
}

// The last MeshCentral read, live in the service (the table MeshNodes is what survives a restart). Fresh = read within
// 15 minutes (the sweep runs every 5); a stale read claims nothing, so a MeshCentral outage never turns into findings
// on every PC.
internal sealed class MeshState(TimeProvider clock)
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);
    private ConcurrentDictionary<int, MeshLink> _links = new();
    private IReadOnlyList<MeshNode> _nodes = [];

    public DateTime LastReadUtc { get; private set; }
    public string? LastError { get; private set; }

    public bool Fresh => LastReadUtc != default && clock.GetUtcNow().UtcDateTime - LastReadUtc < FreshFor;

    /// <summary>This process has tried to read MeshCentral at least once (it succeeded or failed). Until then nothing that is
    /// judged from MeshCentral can be judged: just after a service start, "not read yet" is not "not answering".</summary>
    public bool Attempted => LastReadUtc != default || LastError is not null;

    /// <summary>Every node at the last read, linked or not (the server collector counts them; remote-only servers find theirs).</summary>
    public IReadOnlyList<MeshNode> Nodes => _nodes;

    public void Update(IReadOnlyList<MeshLink> links, IReadOnlyList<MeshNode>? allNodes = null)
    {
        // A device can match more than one node when a stale/renamed node keeps the old computer name (BK01 after its NIC
        // swap). Never guess which to run a fix on: take the single CONNECTED node when there is exactly one; if two are both
        // connected, link NONE (a fix then refuses rather than hit the wrong machine). (Audit 2026-10-06, finding #3.)
        _links = new ConcurrentDictionary<int, MeshLink>(
            links.GroupBy(l => l.DeviceId)
                 .Select(g => Disambiguate(g.ToList()))
                 .Where(l => l is not null)
                 .ToDictionary(l => l!.DeviceId, l => l!));
        _nodes = allNodes ?? links.Select(l => l.Node).ToList();
        LastReadUtc = clock.GetUtcNow().UtcDateTime;
        LastError = null;
    }

    private static MeshLink? Disambiguate(IReadOnlyList<MeshLink> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        var connected = candidates.Where(l => l.Node.AgentConnected).ToList();
        if (connected.Count == 1) return connected[0];   // the live node wins over a stale duplicate of the same name
        if (connected.Count > 1) return null;            // two live nodes with this name: ambiguous -- link none, do not guess
        return candidates[0];                            // none connected: a fix refuses on AgentConnected anyway
    }

    /// <summary>The node MeshCentral calls <paramref name="name"/>, at the last fresh read.</summary>
    public MeshNode? NodeNamed(string name) => !Fresh ? null : _nodes.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Failed(string error) => LastError = error;

    public MeshLink? For(int deviceId) => _links.TryGetValue(deviceId, out var l) ? l : null;

    /// <summary>What the rules see: null when there is nothing trustworthy to say (no read yet, or a stale one).</summary>
    public MeshPresence? PresenceOf(int deviceId) => !Fresh ? null : For(deviceId) is { } l ? new MeshPresence(true, l.Node.AgentConnected) : new MeshPresence(false, false);
}
