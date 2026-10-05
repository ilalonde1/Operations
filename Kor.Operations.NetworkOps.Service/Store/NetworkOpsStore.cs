#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Store;

// Everything NetworkOps persists, in KorNetworkOps (db/KorNetworkOps/001_CreateDatabaseAndSchema.sql).
// Raw ADO.NET, typed parameters, a fresh connection per call -- the FileSync control-plane pattern.
// All times are UTC.
internal sealed partial class NetworkOpsStore
{
    private readonly string _cs;

    public NetworkOpsStore(IOptions<NetworkOpsOptions> options) => _cs = options.Value.Db;

    /// <summary>The host wires this to its log. A SqlException 208 ("invalid object name") is treated as graceful
    /// pre-migration degradation, but it is reported ONCE per table so that a table dropped or renamed AFTER its migration
    /// ran is distinguishable from "migration not run yet" (otherwise both read identically, forever).</summary>
    public static Action<string>? SchemaGap;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ReportedGaps = new(StringComparer.Ordinal);

    /// <summary>The host wires this to its log. Called when an Active Directory name already belongs to a non-AD (Rack/Manual)
    /// device, so that AD machine is not represented -- reported once per name rather than left silent.</summary>
    public static Action<string>? DirectoryConflict;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ReportedConflicts = new(StringComparer.OrdinalIgnoreCase);
    private static bool MissingObject(SqlException ex, string what)
    {
        if (ex.Number != 208) return false;
        if (ReportedGaps.TryAdd(what, 0))
            SchemaGap?.Invoke($"{what}: {ex.Message.Trim()} -- treated as 'migration not run'; if that migration HAS run, the table was dropped or renamed.");
        return true;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction? tx = null)
        => new(sql, c, tx) { CommandTimeout = 30 };

    // ------------------------------------------------------------------ devices

    /// <summary>Directory machines become devices; machines that left the directory are marked, not deleted.</summary>
    public async Task SyncDirectoryAsync(IReadOnlyCollection<string> names, CancellationToken ct)
    {
        // An empty answer from the directory is a failed query, not a firm with no computers:
        // never let it mark the whole fleet as gone.
        if (names.Count == 0) throw new InvalidOperationException("Active Directory returned no workstations; refusing to mark the fleet as gone.");
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);

        // A non-empty but incomplete answer (a transient DC error, LDAP paging truncation) is the same hazard as an empty
        // one: it would retire every live PC it happens to omit, blacking out monitoring silently until the next full sync.
        // Treat a drop to under half the currently-managed count as a failed query and refuse the whole sync (rolls back,
        // leaving InDirectory as it was). The upserts only ever set InDirectory=1, so losing them to the rollback is safe.
        await using (var count = Cmd(c, "SELECT COUNT(*) FROM NetworkOps.Devices WHERE Source = 'AD' AND InDirectory = 1;", tx))
        {
            var current = (int)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            if (current >= 4 && names.Count * 2 < current)
                throw new InvalidOperationException($"Active Directory returned {names.Count} workstations but {current} are managed; refusing a sync that looks like a partial query.");
        }

        foreach (var n in names)
        {
            // Only ever touch AD rows: a Rack/Manual device that happens to share a name must never be flipped to look like
            // a domain PC (Devices.Name is UNIQUE, so insert only when the name is free -- a collision is left untouched,
            // not mislabeled, and not thrown).
            await using var up = Cmd(c, """
                IF EXISTS (SELECT 1 FROM NetworkOps.Devices WHERE Name = @n AND Source = 'AD')
                    UPDATE NetworkOps.Devices SET InDirectory = 1 WHERE Name = @n AND Source = 'AD' AND InDirectory = 0;
                ELSE IF NOT EXISTS (SELECT 1 FROM NetworkOps.Devices WHERE Name = @n)
                    INSERT NetworkOps.Devices (Name, Kind, Source) VALUES (@n, 'Workstation', 'AD');
                """, tx);
            up.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = n;
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var inList = string.Join(",", names.Select((_, i) => "@p" + i));
        // A name AD wants but that already belongs to a non-AD (Rack/Manual) row was left untouched above -- so the AD
        // machine of that name is never represented. That must not be silent: surface each conflict once (2026-10-03 re-audit).
        if (DirectoryConflict is { } report)
        {
            var conflicts = new List<string>();
            await using (var clash = Cmd(c, $"SELECT Name, Source FROM NetworkOps.Devices WHERE Source <> 'AD' AND RetiredUtc IS NULL AND Name IN ({inList});", tx))
            {
                var j = 0;
                foreach (var n in names) clash.Parameters.Add("@p" + j++, SqlDbType.NVarChar, 64).Value = n;
                await using var cr = await clash.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await cr.ReadAsync(ct).ConfigureAwait(false))
                    if (ReportedConflicts.TryAdd(cr.GetString(0), 0)) conflicts.Add($"{cr.GetString(0)} (held by Source '{cr.GetString(1)}')");
            }
            if (conflicts.Count > 0) report($"Active Directory names already held by a non-AD device, so those AD machines are not tracked: {string.Join(", ", conflicts)}");
        }
        await using (var gone = Cmd(c, $"UPDATE NetworkOps.Devices SET InDirectory = 0 WHERE Source = 'AD' AND InDirectory = 1 AND Name NOT IN ({inList});", tx))
        {
            var i = 0;
            foreach (var n in names) gone.Parameters.Add("@p" + i++, SqlDbType.NVarChar, 64).Value = n;
            await gone.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> DirectoryDevicesAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT Name, DeviceId FROM NetworkOps.Devices WHERE InDirectory = 1 AND RetiredUtc IS NULL;");
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) map[r.GetString(0)] = r.GetInt32(1);
        return map;
    }

    public async Task RecordCensusAsync(CensusRow row, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            UPDATE NetworkOps.Devices SET
                LastCensusUtc = @now,
                LastReachableUtc = CASE WHEN @reach = 1 THEN @now ELSE LastReachableUtc END,
                AnsweredOn = @ans, Resolved = @res, AdminWrite = @aw, ServiceControl = @sc,
                WinRm = @winrm, Rdp = @rdp, RemoteRegistry = @rr
            WHERE Name = @n;
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = row.Host;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        cmd.Parameters.Add("@reach", SqlDbType.Bit).Value = row.Reachable;
        cmd.Parameters.Add("@ans", SqlDbType.VarChar, 45).Value = (object?)row.AnsweredOn ?? DBNull.Value;
        cmd.Parameters.Add("@res", SqlDbType.VarChar, 200).Value = string.Join(",", row.Resolved);
        cmd.Parameters.Add("@aw", SqlDbType.Bit).Value = row.AdminWrite;
        cmd.Parameters.Add("@sc", SqlDbType.Bit).Value = row.ServiceControl;
        cmd.Parameters.Add("@winrm", SqlDbType.Bit).Value = row.WinRm;
        cmd.Parameters.Add("@rdp", SqlDbType.Bit).Value = row.Rdp;
        cmd.Parameters.Add("@rr", SqlDbType.VarChar, 20).Value = (object?)row.RemoteRegistry ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Directory machines not reachable for <paramref name="days"/> days (or never, once they have existed that long).</summary>
    public async Task<IReadOnlyList<(int DeviceId, string Name, DateTime? LastReachableUtc)>> SilentDevicesAsync(int days, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT DeviceId, Name, LastReachableUtc FROM NetworkOps.Devices
            WHERE InDirectory = 1 AND RetiredUtc IS NULL
              AND COALESCE(LastReachableUtc, FirstSeenUtc) < DATEADD(day, -@days, @now);
            """);
        cmd.Parameters.Add("@days", SqlDbType.Int).Value = days;
        cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
        var list = new List<(int, string, DateTime?)>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDateTime(2)));
        return list;
    }

    // ------------------------------------------------------------------ observations

    public async Task RecordObservationAsync(int deviceId, string probe, int? probeVersion, DateTime collectedUtc,
        string status, string? payloadJson, string? error, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.Observations (DeviceId, Probe, ProbeVersion, CollectedUtc, Status, PayloadJson, Error)
            VALUES (@d, @p, @v, @at, @s, @json, @err);
            -- A probe that got past reachability is proof the PC answered, just as the census is: without
            -- this, "check this PC now" succeeds while the page still says it was last seen an hour ago.
            IF @s <> 'Offline'
                UPDATE NetworkOps.Devices SET LastReachableUtc = @at
                WHERE DeviceId = @d AND (LastReachableUtc IS NULL OR LastReachableUtc < @at);
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@p", SqlDbType.VarChar, 32).Value = probe;
        cmd.Parameters.Add("@v", SqlDbType.Int).Value = (object?)probeVersion ?? DBNull.Value;
        cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = collectedUtc;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = status;
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = (object?)payloadJson ?? DBNull.Value;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 1000).Value = (object?)Truncate(error, 1000) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> PurgeObservationsAsync(int keepDays, DateTime nowUtc, CancellationToken ct)
    {
        var total = 0;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        while (true)   // small batches: SQL Express, and the table is read while this runs
        {
            await using var cmd = Cmd(c, "DELETE TOP (5000) FROM NetworkOps.Observations WHERE CollectedUtc < DATEADD(day, -@keep, @now);");
            cmd.Parameters.Add("@keep", SqlDbType.Int).Value = keepDays;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            total += n;
            if (n < 5000) return total;
        }
    }

    // ------------------------------------------------------------------ findings

    /// <summary>Every live device with this rule open. A job that owns a rule clears from THIS, not from what it remembers
    /// raising: memory is empty after a restart, and a finding raised by an older version of the rule is never revisited
    /// (23 link-fault findings from 0.32's dropped-packet rule stayed open for days after 0.33.1 retired it, 2026-10-05).</summary>
    public async Task<IReadOnlyList<int>> DevicesWithOpenFindingAsync(string ruleKey, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT DISTINCT f.DeviceId FROM NetworkOps.Findings f
            JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.RuleKey = @k AND f.ClearedUtc IS NULL AND d.RetiredUtc IS NULL;
            """);
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 160).Value = ruleKey;
        var list = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(r.GetInt32(0));
        return list;
    }

    /// <param name="excludeRulePrefix">Rules owned by another job (the census owns device-silent) are left alone.</param>
    public async Task<IReadOnlyList<OpenFinding>> OpenFindingsAsync(int deviceId, string? excludeRulePrefix, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT FindingId, RuleKey, Severity, FirstSeenUtc, NotifiedSeverity, LastSeenUtc
            FROM NetworkOps.Findings
            WHERE DeviceId = @d AND ClearedUtc IS NULL AND (@ex IS NULL OR RuleKey NOT LIKE @ex + '%');
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@ex", SqlDbType.NVarChar, 160).Value = (object?)excludeRulePrefix ?? DBNull.Value;
        var list = new List<OpenFinding>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new OpenFinding(r.GetInt64(0), r.GetString(1), (Severity)r.GetByte(2), r.GetDateTime(3),
                r.IsDBNull(4) ? null : (Severity)r.GetByte(4)) { LastSeenUtc = r.GetDateTime(5) });
        return list;
    }

    /// <summary>Applies one device's diff atomically: insert new, refresh still-open, clear what stopped firing.</summary>
    public async Task ApplyChangesAsync(int deviceId, IReadOnlyList<FindingChange> changes, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var ch in changes)
        {
            SqlCommand cmd;
            switch (ch.Kind)
            {
                case ChangeKind.New:
                    cmd = Cmd(c, """
                        INSERT NetworkOps.Findings (DeviceId, RuleKey, Severity, Title, Evidence, FirstSeenUtc, LastSeenUtc)
                        VALUES (@d, @k, @sev, @t, @e, @now, @now);
                        """, tx);
                    break;
                case ChangeKind.Escalated:
                case ChangeKind.Unchanged:
                    cmd = Cmd(c, """
                        UPDATE NetworkOps.Findings SET Severity = @sev, Title = @t, Evidence = @e, LastSeenUtc = @now
                        WHERE DeviceId = @d AND RuleKey = @k AND ClearedUtc IS NULL;
                        """, tx);
                    break;
                default:
                    cmd = Cmd(c, "UPDATE NetworkOps.Findings SET ClearedUtc = @now WHERE DeviceId = @d AND RuleKey = @k AND ClearedUtc IS NULL;", tx);
                    break;
            }
            await using (cmd)
            {
                cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 160).Value = ch.RuleKey;
                cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                if (ch.Current is { } f)
                {
                    cmd.Parameters.Add("@sev", SqlDbType.TinyInt).Value = (byte)f.Severity;
                    cmd.Parameters.Add("@t", SqlDbType.NVarChar, 200).Value = Truncate(f.Title, 200)!;
                    cmd.Parameters.Add("@e", SqlDbType.NVarChar, 2000).Value = Truncate(f.Evidence, 2000)!;
                }
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records that the open findings for these keys were mailed at their current severity.</summary>
    public async Task MarkNotifiedAsync(int deviceId, IEnumerable<string> ruleKeys, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        foreach (var k in ruleKeys)
        {
            await using var cmd = Cmd(c, """
                UPDATE NetworkOps.Findings SET NotifiedUtc = @now, NotifiedSeverity = Severity
                WHERE DeviceId = @d AND RuleKey = @k AND ClearedUtc IS NULL;
                """);
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            cmd.Parameters.Add("@k", SqlDbType.NVarChar, 160).Value = k;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ runs + heartbeat

    public async Task<long> StartRunAsync(string job, string host, string version, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.JobRuns (JobName, Host, ServiceVersion) OUTPUT INSERTED.RunId VALUES (@j, @h, @v);
            """);
        cmd.Parameters.Add("@j", SqlDbType.VarChar, 64).Value = job;
        cmd.Parameters.Add("@h", SqlDbType.NVarChar, 64).Value = host;
        cmd.Parameters.Add("@v", SqlDbType.VarChar, 32).Value = version;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task FinishRunAsync(long runId, bool success, string? summary, string? error)
    {
        // CancellationToken.None: a run's terminal state is written even while the host shuts down.
        await using var c = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            UPDATE NetworkOps.JobRuns SET FinishedUtc = SYSUTCDATETIME(), Status = @s, Summary = @sum, Error = @err WHERE RunId = @id;
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = success ? "Success" : "Failed";
        cmd.Parameters.Add("@sum", SqlDbType.NVarChar, 2000).Value = (object?)Truncate(summary, 2000) ?? DBNull.Value;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, -1).Value = (object?)error ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task BeatAsync(string host, DateTime startedUtc, string version, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            MERGE NetworkOps.ServiceHeartbeat AS t
            USING (SELECT @h AS Host) AS s ON t.Host = s.Host
            WHEN MATCHED THEN UPDATE SET LastBeatUtc = SYSUTCDATETIME(), StartedUtc = @st, ServiceVersion = @v
            WHEN NOT MATCHED THEN INSERT (Host, StartedUtc, LastBeatUtc, ServiceVersion) VALUES (@h, @st, SYSUTCDATETIME(), @v);
            """);
        cmd.Parameters.Add("@h", SqlDbType.NVarChar, 64).Value = host;
        cmd.Parameters.Add("@st", SqlDbType.DateTime2).Value = startedUtc;
        cmd.Parameters.Add("@v", SqlDbType.VarChar, 32).Value = version;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..(max - 1)] + "…";
}
