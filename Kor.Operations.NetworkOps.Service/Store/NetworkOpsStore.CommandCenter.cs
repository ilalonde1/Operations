#nullable enable
using System.Data;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// What the Command Center API serves. These queries used to run in the page itself, over a SQL login
// on Ian's PC; they run here now, behind Entra + MFA, and the PC holds no database credential.
// "by" on every write is the SIGNED-IN user from the Entra token, never something the caller claims.
internal sealed partial class NetworkOpsStore
{
    public async Task<FleetSnapshot> FleetSnapshotAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);

        var devices = new List<DeviceRow>();
        await using (var cmd = Cmd(c, """
            SELECT d.DeviceId, d.Name, d.LastReachableUtc,
                   (SELECT MAX(o.CollectedUtc) FROM NetworkOps.Observations o
                     WHERE o.DeviceId = d.DeviceId AND o.Probe = 'health' AND o.Status = 'Ok') AS LastCheckedUtc
            FROM NetworkOps.Devices d
            WHERE d.InDirectory = 1 AND d.RetiredUtc IS NULL
            ORDER BY d.Name;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                devices.Add(new DeviceRow(r.GetInt32(0), r.GetString(1), Utc(r, 2), Utc(r, 3)));

        var facts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = Cmd(c, """
            SELECT d.Name, f.Fact, f.Value
            FROM NetworkOps.DeviceFacts f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.SupersededUtc IS NULL AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!facts.TryGetValue(r.GetString(0), out var m)) facts[r.GetString(0)] = m = new Dictionary<string, string>(StringComparer.Ordinal);
                ((Dictionary<string, string>)m)[r.GetString(1)] = r.GetString(2);
            }

        var open = new List<FleetFinding>();
        await using (var cmd = Cmd(c, """
            SELECT f.FindingId, d.Name, f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.LastSeenUtc,
                   f.AcknowledgedUtc, f.AcknowledgedBy, f.SnoozedUntilUtc, f.AckNote
            FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.ClearedUtc IS NULL AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                open.Add(new FleetFinding(r.GetInt64(0), r.GetString(1), r.GetString(2), (Severity)r.GetByte(3), r.GetString(4), r.GetString(5),
                    Utc(r, 6)!.Value, Utc(r, 7)!.Value, Utc(r, 8), r.IsDBNull(9) ? null : r.GetString(9), Utc(r, 10), r.IsDBNull(11) ? null : r.GetString(11)));

        var patterns = new List<ActivePattern>();
        await using (var cmd = Cmd(c, """
            SELECT RuleFamily, Fact, Value, AffectedWith, TotalWith, AffectedWithout, TotalWithout, Summary, FirstSeenUtc
            FROM NetworkOps.Insights WHERE ClearedUtc IS NULL ORDER BY AffectedWith DESC;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                patterns.Add(new ActivePattern(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
                    r.GetString(7), Utc(r, 8)!.Value));

        ServiceBeat? beat = null;
        await using (var cmd = Cmd(c, "SELECT TOP (1) Host, StartedUtc, LastBeatUtc, ServiceVersion FROM NetworkOps.ServiceHeartbeat ORDER BY LastBeatUtc DESC;"))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            if (await r.ReadAsync(ct).ConfigureAwait(false))
                beat = new ServiceBeat(r.GetString(0), Utc(r, 1)!.Value, Utc(r, 2)!.Value, r.IsDBNull(3) ? null : r.GetString(3));

        return new FleetSnapshot(devices, facts, open, patterns, beat);
    }

    public async Task<DeviceHistory> DeviceHistoryAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);

        var cleared = new List<ClearedFinding>();
        await using (var cmd = Cmd(c, """
            SELECT TOP (200) f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.ClearedUtc, fr.Summary
            FROM NetworkOps.Findings f LEFT JOIN NetworkOps.FindingResolutions fr ON fr.FindingId = f.FindingId
            WHERE f.DeviceId = @d AND f.ClearedUtc IS NOT NULL
            ORDER BY f.ClearedUtc DESC;
            """))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                cleared.Add(new ClearedFinding(r.GetString(0), (Severity)r.GetByte(1), r.GetString(2), r.GetString(3), Utc(r, 4)!.Value, Utc(r, 5)!.Value,
                    r.IsDBNull(6) ? null : r.GetString(6)));
        }

        var facts = new List<FactHistoryRow>();
        await using (var cmd = Cmd(c, "SELECT TOP (300) Fact, Value, FirstSeenUtc, SupersededUtc FROM NetworkOps.DeviceFacts WHERE DeviceId = @d ORDER BY FirstSeenUtc DESC, Fact;"))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                facts.Add(new FactHistoryRow(r.GetString(0), r.GetString(1), Utc(r, 2)!.Value, Utc(r, 3)));
        }

        var notes = new List<NoteRow>();
        await using (var cmd = Cmd(c, "SELECT TOP (100) Author, CreatedUtc, Body FROM NetworkOps.DeviceNotes WHERE DeviceId = @d ORDER BY CreatedUtc DESC;"))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                notes.Add(new NoteRow(r.GetString(0), Utc(r, 1)!.Value, r.GetString(2)));
        }

        return new DeviceHistory(cleared, facts, notes);
    }

    public async Task<IReadOnlyList<ResolutionRow>> ResolutionRowsAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT RuleKey, ClearedUtc, Rebooted, ChangedFactsJson, ActionIdsJson FROM NetworkOps.FindingResolutions;");
        var list = new List<ResolutionRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ResolutionRow(r.GetString(0), Utc(r, 1)!.Value, r.GetBoolean(2),
                (r.IsDBNull(3) ? null : JsonSerializer.Deserialize<List<FactChange>>(r.GetString(3))) ?? [],
                (r.IsDBNull(4) ? null : JsonSerializer.Deserialize<List<string>>(r.GetString(4))) ?? []));
        return list;
    }

    /// <summary>Queues "check this PC now"; the TriggerPoller claims it within ~5 s. Null when the PC is not in the fleet.</summary>
    public async Task<long?> QueueCheckAsync(string deviceName, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.JobTriggers (JobName, DeviceName, RequestedBy)
            OUTPUT inserted.TriggerId
            SELECT 'HealthSweep', d.Name, @by FROM NetworkOps.Devices d
            WHERE d.Name = @n AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = deviceName;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long id ? id : null;
    }

    public async Task<TriggerState?> TriggerStateAsync(long triggerId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT Status, Result, RequestedUtc, ClaimedUtc, CompletedUtc FROM NetworkOps.JobTriggers WHERE TriggerId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new TriggerState(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), Utc(r, 2)!.Value, Utc(r, 3), Utc(r, 4));
    }

    /// <summary>Cancels a check nobody has claimed yet. False when the service already took it.</summary>
    public async Task<bool> CancelCheckAsync(long triggerId, string by, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            UPDATE NetworkOps.JobTriggers SET Status = 'Cancelled', CompletedUtc = SYSUTCDATETIME(), Result = N'cancelled by ' + @by
            WHERE TriggerId = @id AND Status = 'Pending';
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public enum Annotation { Acknowledge, Snooze, Reopen }

    /// <summary>Acknowledge / snooze / reopen an OPEN finding. False when it has cleared (or never existed).</summary>
    public async Task<bool> AnnotateAsync(long findingId, Annotation kind, string by, string? note, DateTime? untilUtc, CancellationToken ct)
    {
        var set = kind switch
        {
            Annotation.Acknowledge => "AcknowledgedUtc = SYSUTCDATETIME(), AcknowledgedBy = @by, SnoozedUntilUtc = NULL, AckNote = @note",
            Annotation.Snooze => "SnoozedUntilUtc = @until, AcknowledgedUtc = NULL, AcknowledgedBy = @by, AckNote = @note",
            _ => "AcknowledgedUtc = NULL, SnoozedUntilUtc = NULL, AcknowledgedBy = @by, AckNote = NULL",
        };
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, $"UPDATE NetworkOps.Findings SET {set} WHERE FindingId = @id AND ClearedUtc IS NULL;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = findingId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@note", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(note) ? DBNull.Value : Truncate(note.Trim(), 1000)!;
        cmd.Parameters.Add("@until", SqlDbType.DateTime2).Value = (object?)untilUtc ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> AddNoteAsync(int deviceId, string author, string body, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.DeviceNotes (DeviceId, Author, Body)
            SELECT DeviceId, @a, @b FROM NetworkOps.Devices WHERE DeviceId = @d;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@a", SqlDbType.NVarChar, 128).Value = author;
        cmd.Parameters.Add("@b", SqlDbType.NVarChar, 4000).Value = Truncate(body.Trim(), 4000)!;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    // datetime2 is stored in UTC; SqlClient hands it back as Unspecified.
    private static DateTime? Utc(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
}
