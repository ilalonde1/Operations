#nullable enable
using System.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// The endpoint agents (db/KorNetworkOps/005_Agents.sql): which PCs have one, the hash of each one's key, and
// when it last called in. Installs and removals are recorded in NetworkOps.Actions like every fix.
internal sealed partial class NetworkOpsStore : Agents.IAgentDirectory
{
    public sealed record AgentCredential(int DeviceId, string DeviceName, byte[] SecretSha256, bool Removed);

    /// <summary>The key hash for a PC's agent; null when the PC has never had one (or 005 has not run).</summary>
    public async Task<AgentCredential?> AgentCredentialAsync(string deviceName, CancellationToken ct)
    {
        try { return await AgentCredentialCoreAsync(deviceName, ct).ConfigureAwait(false); }
        catch (SqlException ex) when (MissingObject(ex, "Agents (005)")) { return null; }
    }

    private async Task<AgentCredential?> AgentCredentialCoreAsync(string deviceName, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT d.DeviceId, d.Name, a.SecretSha256, CASE WHEN a.RemovedUtc IS NULL THEN 0 ELSE 1 END
            FROM NetworkOps.Agents a JOIN NetworkOps.Devices d ON d.DeviceId = a.DeviceId
            WHERE d.Name = @n AND d.RetiredUtc IS NULL;
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = deviceName;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)
            ? new AgentCredential(r.GetInt32(0), r.GetString(1), (byte[])r.GetValue(2), r.GetInt32(3) == 1)
            : null;
    }

    /// <summary>
    /// Records an install or upgrade: the new key's hash replaces any old one, so the old key stops working. Contact is
    /// cleared: until the installer confirms a poll with the new key, this install is unconfirmed, and the rollout
    /// retries it (Codex audit 2026-09-30, finding 7).
    /// </summary>
    /// <summary>
    /// A PC that is not in the domain, added by hand (Ian, 2026-10-02: the Boardroom PC). Source 'Manual' -- the directory
    /// sync only ever retires Source 'AD' rows, so it stays. Returns its id; null when a domain PC already has that name
    /// (it is enrolled the domain way, from its own window).
    /// </summary>
    public async Task<int?> AddManualPcAsync(string name, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            DECLARE @id int, @src varchar(16);
            SELECT @id = DeviceId, @src = Source FROM NetworkOps.Devices WHERE Name = @n;
            IF @id IS NULL
            BEGIN
                INSERT NetworkOps.Devices (Name, Kind, Source, InDirectory) VALUES (@n, 'Workstation', 'Manual', 1);
                SET @id = SCOPE_IDENTITY(); SET @src = 'Manual';
            END
            ELSE IF @src = 'Manual' UPDATE NetworkOps.Devices SET InDirectory = 1, RetiredUtc = NULL WHERE DeviceId = @id;
            SELECT CASE WHEN @src = 'Manual' THEN @id END;
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = name;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int id ? id : null;
    }

    public async Task SaveAgentAsync(int deviceId, byte[] secretSha256, string version, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            MERGE NetworkOps.Agents WITH (HOLDLOCK) AS t
            USING (SELECT @d AS DeviceId) AS s ON t.DeviceId = s.DeviceId
            WHEN MATCHED THEN UPDATE SET SecretSha256 = @h, Version = @v, InstalledUtc = SYSUTCDATETIME(), InstalledBy = @by, RemovedUtc = NULL,
                                         LastContactUtc = NULL, LastVersion = NULL, LastAddress = NULL
            WHEN NOT MATCHED THEN INSERT (DeviceId, SecretSha256, Version, InstalledUtc, InstalledBy) VALUES (@d, @h, @v, SYSUTCDATETIME(), @by);
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@h", SqlDbType.Binary, 32).Value = secretSha256;
        cmd.Parameters.Add("@v", SqlDbType.VarChar, 32).Value = version;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Contact, recorded only against the key it came with (Codex re-check 2026-09-30): a request made with a key that
    /// has since been replaced updates nothing, so it can never make a failed reinstall look confirmed.</summary>
    public async Task TouchAgentAsync(int deviceId, byte[] secretSha256, string version, string? address, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.Agents SET LastContactUtc = @now, LastVersion = @v, LastAddress = @a WHERE DeviceId = @d AND SecretSha256 = @h AND RemovedUtc IS NULL;");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@h", SqlDbType.Binary, 32).Value = secretSha256;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        cmd.Parameters.Add("@v", SqlDbType.VarChar, 32).Value = version;
        cmd.Parameters.Add("@a", SqlDbType.VarChar, 45).Value = (object?)address ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An install is starting: until it confirms, the record says "not confirmed", so a failure part-way -- even a
    /// same-version repair that dies during the copy -- is retried by the rollout rather than looking done (Codex re-check).
    /// </summary>
    public async Task MarkAgentInstallPendingAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.Agents SET LastContactUtc = NULL, InstalledUtc = SYSUTCDATETIME() WHERE DeviceId = @d;");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The row stays (the history of who had an agent), but its key is refused from now on.</summary>
    public async Task RemoveAgentAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.Agents SET RemovedUtc = SYSUTCDATETIME() WHERE DeviceId = @d;");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public sealed record AgentRecord(string DeviceName, string Version, string? LastVersion, DateTime? LastContactUtc, DateTime? InstalledUtc = null);

    /// <summary>Every PC with an agent, for the Command Center. Empty before 005 has run, never an error.</summary>
    public async Task<IReadOnlyDictionary<string, AgentRecord>> AgentRecordsAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, AgentRecord>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, """
                SELECT d.Name, a.Version, a.LastVersion, a.LastContactUtc, a.InstalledUtc
                FROM NetworkOps.Agents a JOIN NetworkOps.Devices d ON d.DeviceId = a.DeviceId
                WHERE a.RemovedUtc IS NULL;
                """);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                map[r.GetString(0)] = new AgentRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), Utc(r, 3), Utc(r, 4));
        }
        catch (SqlException ex) when (MissingObject(ex, "Agents (005)"))
        {
        }
        return map;
    }

    public sealed record RolloutCandidate(int DeviceId, string Name, string? AgentVersion);

    /// <summary>
    /// The PCs a rollout would touch next, in name order: in the directory, answering the network recently, and one of
    /// never had an agent / has one older than <paramref name="shippedVersion"/> / was installed but never confirmed
    /// (no contact since that install). A PC whose agent was REMOVED is left alone: removal is a decision, and only an
    /// install from that PC's own window undoes it (Codex audit 2026-09-30, finding 11).
    /// </summary>
    public async Task<IReadOnlyList<RolloutCandidate>> AgentRolloutCandidatesAsync(DateTime reachableSinceUtc, string shippedVersion, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT d.DeviceId, d.Name, a.Version,
                   CASE WHEN a.DeviceId IS NOT NULL AND (a.LastContactUtc IS NULL OR a.LastContactUtc < a.InstalledUtc) THEN 1 ELSE 0 END AS Unconfirmed
            FROM NetworkOps.Devices d LEFT JOIN NetworkOps.Agents a ON a.DeviceId = d.DeviceId
            WHERE d.InDirectory = 1 AND d.RetiredUtc IS NULL AND d.Source = 'AD' AND d.LastReachableUtc >= @since
              AND a.RemovedUtc IS NULL
            ORDER BY d.Name;
            """);
        cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = reachableSinceUtc;
        var list = new List<RolloutCandidate>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var v = r.IsDBNull(2) ? null : r.GetString(2);
            if (v is null || r.GetInt32(3) == 1 || Kor.Operations.NetworkOps.Core.Health.AgentRules.IsOlder(v, shippedVersion))
                list.Add(new RolloutCandidate(r.GetInt32(0), r.GetString(1), v));
        }
        return list;
    }

    /// <summary>An action that starts running at once (a rollout's per-PC install): recorded for the audit, never claimed by the ActionRunner.</summary>
    public async Task<long> StartActionAsync(int deviceId, string kind, string by, string requestJson, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "INSERT NetworkOps.Actions (DeviceId, Kind, RequestedBy, Status, BeforeJson) OUTPUT inserted.ActionId VALUES (@d, @k, @by, 'Running', @req);");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 48).Value = kind;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@req", SqlDbType.NVarChar, -1).Value = requestJson;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>
    /// A PC whose agent has just come back (a laptop reconnecting, a PC switched on) is checked at once if its last
    /// health check is older than <paramref name="maxAge"/> and none is already waiting. Null when nothing was queued.
    /// </summary>
    public async Task<long?> QueueCheckIfStaleAsync(string deviceName, TimeSpan maxAge, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.JobTriggers (JobName, DeviceName, RequestedBy)
            OUTPUT inserted.TriggerId
            SELECT 'HealthSweep', d.Name, @by FROM NetworkOps.Devices d
            WHERE d.Name = @n AND d.InDirectory = 1 AND d.RetiredUtc IS NULL
              AND NOT EXISTS (SELECT 1 FROM NetworkOps.Observations o
                              WHERE o.DeviceId = d.DeviceId AND o.Probe = 'health' AND o.Status = 'Ok' AND o.CollectedUtc > DATEADD(second, -@age, SYSUTCDATETIME()))
              AND NOT EXISTS (SELECT 1 FROM NetworkOps.JobTriggers t
                              WHERE t.JobName = 'HealthSweep' AND t.DeviceName = d.Name AND t.Status IN ('Pending', 'Running'));
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = deviceName;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@age", SqlDbType.Int).Value = (int)maxAge.TotalSeconds;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }
}
