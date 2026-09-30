#nullable enable
using System.Data;

namespace Kor.Operations.NetworkOps.Service.Store;

// Rack devices live in NetworkOps.Devices beside the PCs (Source = 'Rack', InDirectory = 0, Kind = what they are),
// so facts, metrics, findings, history, notes, acknowledge and snooze all work on them unchanged. The census never
// touches them: it only marks Source = 'AD' rows.
internal sealed partial class NetworkOpsStore
{
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
