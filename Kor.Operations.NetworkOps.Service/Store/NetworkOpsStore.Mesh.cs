#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Service.Mesh;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// Remote control (db/KorNetworkOps/006_Mesh.sql): which device is which MeshCentral node, written by MeshSweepJob.
internal sealed partial class NetworkOpsStore
{
    /// <summary>Rack devices that are live (not retired), for matching MeshCentral nodes to servers.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RackDeviceIdsAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT Name, DeviceId FROM NetworkOps.Devices WHERE Source = 'Rack' AND RetiredUtc IS NULL;");
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) map[r.GetString(0)] = r.GetInt32(1);
        return map;
    }

    /// <summary>
    /// Replaces the stored picture with this read: a row per linked device (insert or update), and rows for devices
    /// MeshCentral no longer lists are removed. False before 006 has run (the live state still works).
    /// </summary>
    public async Task<bool> SaveMeshNodesAsync(IReadOnlyList<MeshLink> links, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var l in links)
            {
                await using var up = Cmd(c, """
                    MERGE NetworkOps.MeshNodes WITH (HOLDLOCK) AS t USING (SELECT @d AS DeviceId) AS s ON t.DeviceId = s.DeviceId
                    WHEN MATCHED THEN UPDATE SET NodeId = @n, MeshName = @m, MeshGroup = @g, Connected = @c, LastReadUtc = @now,
                                                 LastConnectedUtc = CASE WHEN @c = 1 THEN @now ELSE t.LastConnectedUtc END
                    WHEN NOT MATCHED THEN INSERT (DeviceId, NodeId, MeshName, MeshGroup, Connected, LastConnectedUtc, LastReadUtc)
                                          VALUES (@d, @n, @m, @g, @c, CASE WHEN @c = 1 THEN @now END, @now);
                    """, tx);
                up.Parameters.Add("@d", SqlDbType.Int).Value = l.DeviceId;
                up.Parameters.Add("@n", SqlDbType.VarChar, 100).Value = l.Node.Id;
                up.Parameters.Add("@m", SqlDbType.NVarChar, 64).Value = Truncate(l.Node.Name, 64)!;
                up.Parameters.Add("@g", SqlDbType.NVarChar, 64).Value = Truncate(l.Group, 64)!;
                up.Parameters.Add("@c", SqlDbType.Bit).Value = l.Node.AgentConnected;
                up.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            var keep = links.Select(l => l.DeviceId).Distinct().ToList();
            var inList = keep.Count == 0 ? "NULL" : string.Join(",", keep.Select((_, i) => "@k" + i));
            await using (var gone = Cmd(c, $"DELETE FROM NetworkOps.MeshNodes WHERE DeviceId NOT IN ({inList});", tx))
            {
                for (var i = 0; i < keep.Count; i++) gone.Parameters.Add("@k" + i, SqlDbType.Int).Value = keep[i];
                await gone.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 208) { return false; }   // 006 not run yet
    }

    public sealed record MeshRecord(string NodeId, bool Connected, DateTime? LastConnectedUtc, DateTime LastReadUtc);

    /// <summary>The stored picture, for the Command Center while the live state is empty (just after a restart). Empty before 006.</summary>
    public async Task<IReadOnlyDictionary<int, MeshRecord>> MeshRecordsAsync(CancellationToken ct)
    {
        var map = new Dictionary<int, MeshRecord>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, "SELECT DeviceId, NodeId, Connected, LastConnectedUtc, LastReadUtc FROM NetworkOps.MeshNodes;");
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                map[r.GetInt32(0)] = new MeshRecord(r.GetString(1), r.GetBoolean(2), Utc(r, 3), Utc(r, 4)!.Value);
        }
        catch (SqlException ex) when (ex.Number == 208) { }
        return map;
    }
}
