#nullable enable
using System.Data;

namespace Kor.Operations.NetworkOps.Service.Store;

// Rack devices live in NetworkOps.Devices beside the PCs (Source = 'Rack', InDirectory = 0, Kind = what they are),
// so facts, metrics, findings, history, notes, acknowledge and snooze all work on them unchanged. The census never
// touches them: it only marks Source = 'AD' rows.
internal sealed partial class NetworkOpsStore
{
    /// <summary>The version-security baseline (013): product -> minimum secure build, end-of-support, last reviewed. Empty
    /// (never null) when the table is missing -- VersionBaselineRules then raises baseline.stale "run 013".</summary>
    public async Task<IReadOnlyList<Core.Rack.VersionBaselineRow>> VersionBaselineAsync(CancellationToken ct)
    {
        var rows = new List<Core.Rack.VersionBaselineRow>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, "SELECT Product, MinSecureBuild, EndOfSupportUtc, LastReviewedUtc, Reason, Source FROM NetworkOps.VersionBaseline;");
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new Core.Rack.VersionBaselineRow(r.GetString(0),
                    r.IsDBNull(1) ? null : r.GetString(1), Utc(r, 2), Utc(r, 3)!.Value,
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (MissingObject(ex, "VersionBaseline (013)")) { }
        return rows;
    }

    /// <summary>The rack inventory (014): which infrastructure to read and how to reach each, the source the sweep walks.
    /// Ordered by SortOrder (the Command Center lists it in that order). Empty (never null) when the table is missing --
    /// the service then falls back to the appsettings "Rack" list, so a deploy before 014 runs never blanks the sweep.
    /// A NULL Address/MeshName/UpsName/CertSha256 reads back as the empty string the appsettings model used.</summary>
    public async Task<IReadOnlyList<RackDevice>> RackInventoryAsync(CancellationToken ct)
    {
        var byId = new Dictionary<int, RackDevice>();
        var ordered = new List<RackDevice>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using (var cmd = Cmd(c, "SELECT RackDeviceId, Name, Kind, Collector, Address, MeshName, UpsName, CertSha256, VolumeFreeWarnPct FROM NetworkOps.RackInventory WHERE Enabled = 1 ORDER BY SortOrder, Name;"))
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    var d = new RackDevice
                    {
                        Name = r.GetString(1),
                        Kind = r.GetString(2),
                        Collector = r.GetString(3),
                        Address = r.IsDBNull(4) ? "" : r.GetString(4),
                        MeshName = r.IsDBNull(5) ? "" : r.GetString(5),
                        UpsName = r.IsDBNull(6) ? "" : r.GetString(6),
                        CertSha256 = r.IsDBNull(7) ? "" : r.GetString(7).Trim(),   // char(64): trim any pad
                        VolumeFreeWarnPct = r.GetInt32(8),
                    };
                    byId[r.GetInt32(0)] = d;
                    ordered.Add(d);
                }
            if (byId.Count > 0)
            {
                await using var cmd2 = Cmd(c, "SELECT RackDeviceId, HostKey FROM NetworkOps.RackInventoryHostKey ORDER BY RackDeviceId, HostKey;");
                await using var r2 = await cmd2.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r2.ReadAsync(ct).ConfigureAwait(false))
                    if (byId.TryGetValue(r2.GetInt32(0), out var d)) d.HostKeys.Add(r2.GetString(1));
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (MissingObject(ex, "RackInventory (014)")) { return []; }
        return ordered;
    }

    /// <summary>The device's id, creating it on first sight; records when it was last read and whether it answered.</summary>
    public async Task<int> UpsertRackDeviceAsync(string name, string kind, bool reachable, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            IF NOT EXISTS (SELECT 1 FROM NetworkOps.Devices WHERE Name = @n)
                INSERT NetworkOps.Devices (Name, Kind, Source, InDirectory) VALUES (@n, @k, 'Rack', 0);
            UPDATE NetworkOps.Devices SET Kind = @k, LastCensusUtc = @now,
                   LastReachableUtc = CASE WHEN @reach = 1 THEN @now ELSE LastReachableUtc END
            OUTPUT inserted.DeviceId
            WHERE Name = @n AND Source = 'Rack';
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = name;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 24).Value = kind;
        cmd.Parameters.Add("@reach", SqlDbType.Bit).Value = reachable;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id
            ? id
            : throw new InvalidOperationException($"'{name}' exists but is not a rack device (a directory PC has that name)");
    }

    /// <summary>Rack devices no longer in the configuration are retired (kept, with their history), not deleted.</summary>
    public async Task RetireRackDevicesExceptAsync(IReadOnlyCollection<string> names, DateTime nowUtc, CancellationToken ct)
    {
        if (names.Count == 0) return;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var inList = string.Join(",", names.Select((_, i) => "@p" + i));
        await using var cmd = Cmd(c, $"""
            UPDATE NetworkOps.Devices SET RetiredUtc = @now WHERE Source = 'Rack' AND RetiredUtc IS NULL AND Name NOT IN ({inList});
            UPDATE NetworkOps.Devices SET RetiredUtc = NULL WHERE Source = 'Rack' AND RetiredUtc IS NOT NULL AND Name IN ({inList});
            """);
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        var i = 0;
        foreach (var n in names) cmd.Parameters.Add("@p" + i++, SqlDbType.NVarChar, 64).Value = n;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>True when the name is a rack device (a "check now" on it runs the rack sweep for it, not a PC probe).</summary>
    public async Task<bool> IsRackDeviceAsync(string name, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT COUNT(*) FROM NetworkOps.Devices WHERE Name = @n AND Source = 'Rack' AND RetiredUtc IS NULL;");
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = name;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<long> QueueRackCheckAsync(string name, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "INSERT NetworkOps.JobTriggers (JobName, DeviceName, RequestedBy) OUTPUT inserted.TriggerId VALUES ('RackSweep', @n, @by);");
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = name;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
