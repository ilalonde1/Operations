#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

/// <param name="Known">MeshCentral has a node for this device.</param>
/// <param name="Connected">Its Mesh agent was connected at the last read.</param>
public sealed record MeshPresence(bool Known, bool Connected);

// Remote control (MeshCentral on KOR-MESH01), judged with each health check of a PC. Pure: the sweep supplies what the
// last MeshCentral read said (null when remote control is off or that read is stale -- then nothing is claimed).
//
//   mesh-missing   the PC answered this check, but MeshCentral has never seen it: remote control is not installed.
//   mesh-silent    MeshCentral knows it, the PC answered this check, but its Mesh agent is not connected: Connect fails.
public static class MeshRules
{
    public static IReadOnlyList<Finding> Evaluate(MeshPresence? mesh, bool answered)
    {
        if (mesh is null || !answered) return [];
        if (!mesh.Known)
            return [new("mesh-missing", Severity.Info, "Remote control is not installed", "MeshCentral has no Mesh agent for this PC; the Connect button cannot reach it")];
        if (!mesh.Connected)
            return [new("mesh-silent", Severity.Warning, "Remote control is not connected", "the PC answered this check, but its Mesh agent is not connected to KOR-MESH01")];
        return [];
    }
}
