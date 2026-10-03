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
    /// <param name="rack">False: the PCs (the directory fleet), as /api/fleet has always served. True: the rack devices,
    /// served separately (/api/rack) so a page that predates the rack never shows a host or a UPS as a PC.</param>
    public async Task<FleetSnapshot> FleetSnapshotAsync(CancellationToken ct, bool rack = false)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var which = rack ? "d.Source = 'Rack' AND d.RetiredUtc IS NULL" : "d.InDirectory = 1 AND d.RetiredUtc IS NULL";

        var devices = new List<DeviceRow>();
        await using (var cmd = Cmd(c, $"""
            SELECT d.DeviceId, d.Name, d.LastReachableUtc,
                   (SELECT MAX(o.CollectedUtc) FROM NetworkOps.Observations o
                     WHERE o.DeviceId = d.DeviceId AND o.Probe IN ('health', 'rack') AND o.Status = 'Ok') AS LastCheckedUtc,
                   d.Kind,
                   (SELECT TOP (1) JSON_VALUE(o.PayloadJson, '$.summary') FROM NetworkOps.Observations o
                     WHERE o.DeviceId = d.DeviceId AND o.Probe = 'rack' ORDER BY o.CollectedUtc DESC) AS Summary,
                   p.Presence, p.PresenceState
            FROM NetworkOps.Devices d
            OUTER APPLY (SELECT TOP (1) JSON_VALUE(o.PayloadJson, '$[0].Session.Summary') AS Presence, JSON_VALUE(o.PayloadJson, '$[0].Session.State') AS PresenceState
                         FROM NetworkOps.Observations o WHERE o.DeviceId = d.DeviceId AND o.Probe = 'health' AND o.Status = 'Ok'
                         ORDER BY o.CollectedUtc DESC) p
            WHERE {which}
            ORDER BY d.Name;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                devices.Add(new DeviceRow(r.GetInt32(0), r.GetString(1), Utc(r, 2), Utc(r, 3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));

        var facts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = Cmd(c, $"""
            SELECT d.Name, f.Fact, f.Value
            FROM NetworkOps.DeviceFacts f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.SupersededUtc IS NULL AND {which};
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!facts.TryGetValue(r.GetString(0), out var m)) facts[r.GetString(0)] = m = new Dictionary<string, string>(StringComparer.Ordinal);
                ((Dictionary<string, string>)m)[r.GetString(1)] = r.GetString(2);
            }

        var open = new List<FleetFinding>();
        await using (var cmd = Cmd(c, $"""
            SELECT f.FindingId, d.Name, f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.LastSeenUtc,
                   f.AcknowledgedUtc, f.AcknowledgedBy, f.SnoozedUntilUtc, f.AckNote
            FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.ClearedUtc IS NULL AND {which};
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                open.Add(new FleetFinding(r.GetInt64(0), r.GetString(1), r.GetString(2), (Severity)r.GetByte(3), r.GetString(4), r.GetString(5),
                    Utc(r, 6)!.Value, Utc(r, 7)!.Value, Utc(r, 8), r.IsDBNull(9) ? null : r.GetString(9), Utc(r, 10), r.IsDBNull(11) ? null : r.GetString(11)));

        var patterns = new List<ActivePattern>();   // fleet patterns are about PCs: none for the rack
        await using (var cmd = Cmd(c, $"""
            SELECT RuleFamily, Fact, Value, AffectedWith, TotalWith, AffectedWithout, TotalWithout, Summary, FirstSeenUtc
            FROM NetworkOps.Insights WHERE ClearedUtc IS NULL AND {(rack ? "1 = 0" : "1 = 1")} ORDER BY AffectedWith DESC;
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

    /// <summary>What changed across the PCs and the rack since <paramref name="sinceUtc"/> (GET /api/changes): findings opened or
    /// cleared, fixes and runs requested, and what is open now. Retired devices are left out.</summary>
    public async Task<ChangesView> ChangesSinceAsync(DateTime sinceUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var opened = new List<ChangeFinding>();
        var cleared = new List<ChangeFinding>();
        await using (var cmd = Cmd(c, """
            SELECT TOP (1000) d.Name, f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.ClearedUtc
            FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE d.RetiredUtc IS NULL AND (f.FirstSeenUtc >= @s OR f.ClearedUtc >= @s)
            ORDER BY f.Severity DESC, d.Name, f.RuleKey;
            """))
        {
            cmd.Parameters.Add("@s", SqlDbType.DateTime2).Value = sinceUtc;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var f = new ChangeFinding(r.GetString(0), r.GetString(1), (Severity)r.GetByte(2), r.GetString(3), r.GetString(4), Utc(r, 5)!.Value, Utc(r, 6));
                (f.FirstSeenUtc >= sinceUtc ? opened : cleared).Add(f);
            }
        }

        var actions = new List<ChangeAction>();
        await using (var cmd = Cmd(c, """
            SELECT TOP (1000) d.Name, a.ActionId, a.Kind, a.RequestedBy, a.RequestedUtc, a.Status, a.Detail
            FROM NetworkOps.Actions a JOIN NetworkOps.Devices d ON d.DeviceId = a.DeviceId
            WHERE a.RequestedUtc >= @s ORDER BY a.ActionId;
            """))
        {
            cmd.Parameters.Add("@s", SqlDbType.DateTime2).Value = sinceUtc;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                actions.Add(new ChangeAction(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), Utc(r, 4)!.Value, r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        }

        int critical = 0, warning = 0, info = 0;
        await using (var cmd = Cmd(c, """
            SELECT f.Severity, COUNT(*) FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.ClearedUtc IS NULL AND d.RetiredUtc IS NULL GROUP BY f.Severity;
            """))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                switch ((Severity)r.GetByte(0))
                {
                    case Severity.Critical: critical = r.GetInt32(1); break;
                    case Severity.Warning: warning = r.GetInt32(1); break;
                    default: info += r.GetInt32(1); break;
                }
        }
        return new ChangesView(sinceUtc, DateTime.UtcNow, opened, cleared, actions, critical, warning, info);
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

        var actions = new List<ActionRow>();
        await using (var cmd = Cmd(c, """
            SELECT TOP (50) ActionId, Kind, RequestedBy, RequestedUtc, CompletedUtc, Status, Detail, AfterJson
            FROM NetworkOps.Actions WHERE DeviceId = @d ORDER BY ActionId DESC;
            """))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                actions.Add(ReadAction(r));
        }

        return new DeviceHistory(cleared, facts, notes, actions);
    }

    private static ActionRow ReadAction(SqlDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), Utc(r, 3)!.Value, Utc(r, 4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7));

    // ------------------------------------------------------------------ fixes (NetworkOps.Actions is the queue AND the audit)

    /// <param name="requestJson">What was asked: the fix, its input (for run-command, the script), the finding, the note.</param>
    public async Task<long> QueueActionAsync(int deviceId, string kind, string by, string requestJson, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.Actions (DeviceId, Kind, RequestedBy, Status, BeforeJson) OUTPUT inserted.ActionId VALUES (@d, @k, @by, 'Requested', @req);
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 48).Value = kind;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@req", SqlDbType.NVarChar, -1).Value = requestJson;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>A refused request is recorded too: the audit shows what was attempted, not just what ran.</summary>
    public async Task<long> RecordRefusedActionAsync(int deviceId, string kind, string by, string requestJson, string why, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.Actions (DeviceId, Kind, RequestedBy, Status, BeforeJson, Detail, CompletedUtc) OUTPUT inserted.ActionId
            VALUES (@d, @k, @by, 'Refused', @req, @why, SYSUTCDATETIME());
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 48).Value = kind;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@req", SqlDbType.NVarChar, -1).Value = requestJson;
        cmd.Parameters.Add("@why", SqlDbType.NVarChar, 2000).Value = Truncate(why, 2000)!;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public sealed record ClaimedAction(long ActionId, int DeviceId, string DeviceName, string Kind, string RequestJson, string RequestedBy);

    public async Task<ClaimedAction?> ClaimActionAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            WITH next AS (SELECT TOP (1) * FROM NetworkOps.Actions WITH (UPDLOCK, READPAST, ROWLOCK) WHERE Status = 'Requested' ORDER BY ActionId)
            UPDATE next SET Status = 'Running'
            OUTPUT inserted.ActionId, inserted.DeviceId, inserted.Kind, inserted.BeforeJson, inserted.RequestedBy;
            """);
        long id; int device; string kind, req, by;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            (id, device, kind, req, by) = (r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? "{}" : r.GetString(3), r.GetString(4));
        }
        await using var n = Cmd(c, "SELECT Name FROM NetworkOps.Devices WHERE DeviceId = @d;");
        n.Parameters.Add("@d", SqlDbType.Int).Value = device;
        return new ClaimedAction(id, device, (string)(await n.ExecuteScalarAsync(ct).ConfigureAwait(false))!, kind, req, by);
    }

    /// <summary>CancellationToken.None: a fix's outcome is recorded even while the service stops.</summary>
    public async Task CompleteActionAsync(long actionId, bool ok, string detail, string? outputJson)
    {
        await using var c = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.Actions SET Status = @s, Detail = @det, AfterJson = @out, CompletedUtc = SYSUTCDATETIME() WHERE ActionId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = actionId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = ok ? "Done" : "Failed";
        cmd.Parameters.Add("@det", SqlDbType.NVarChar, 2000).Value = Truncate(detail, 2000)!;
        cmd.Parameters.Add("@out", SqlDbType.NVarChar, -1).Value = (object?)outputJson ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<ActionRow?> ActionAsync(long actionId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT ActionId, Kind, RequestedBy, RequestedUtc, CompletedUtc, Status, Detail, AfterJson FROM NetworkOps.Actions WHERE ActionId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = actionId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? ReadAction(r) : null;
    }

    /// <summary>A running fix left behind by a service that stopped is marked failed at startup (never re-run: a fix is not idempotent).</summary>
    public async Task<int> AbandonRunningActionsAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.Actions SET Status = 'Failed', Detail = 'the service stopped while it ran; its outcome is unknown', CompletedUtc = SYSUTCDATETIME() WHERE Status = 'Running';");
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The device's name, kind and last known presence, for the API's checks.</summary>
    public async Task<(string Name, string Kind, string Source, string? PresenceState, string? Presence)?> DeviceForActionAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT d.Name, d.Kind, d.Source, p.S, p.P FROM NetworkOps.Devices d
            OUTER APPLY (SELECT TOP (1) JSON_VALUE(o.PayloadJson, '$[0].Session.State') AS S, JSON_VALUE(o.PayloadJson, '$[0].Session.Summary') AS P
                         FROM NetworkOps.Observations o WHERE o.DeviceId = d.DeviceId AND o.Probe = 'health' AND o.Status = 'Ok' ORDER BY o.CollectedUtc DESC) p
            WHERE d.DeviceId = @d AND d.RetiredUtc IS NULL;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)
            ? (r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4))
            : null;
    }

    /// <summary>The device's last good check as it came back (a PC's health probe first, else its newest), for a prompt to carry.</summary>
    public async Task<Core.Prompts.LastCheck?> LastCheckAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT TOP (1) o.Probe, o.CollectedUtc, o.PayloadJson FROM NetworkOps.Observations o
            WHERE o.DeviceId = @d AND o.Status = 'Ok' AND o.PayloadJson IS NOT NULL
            ORDER BY CASE WHEN o.Probe = 'health' THEN 0 ELSE 1 END, o.CollectedUtc DESC;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)
            ? new Core.Prompts.LastCheck(r.GetString(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.GetString(2))
            : null;
    }

    /// <summary>
    /// A device by name (PC or rack, not retired): its id and kind. A rack server is named with what it does --
    /// "KOR-DC01 (domain controller, DNS, DHCP)" -- so its machine name alone finds it too.
    /// </summary>
    public async Task<(int DeviceId, string Name, string Kind, string Source)?> DeviceByNameAsync(string name, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT TOP (1) DeviceId, Name, Kind, Source FROM NetworkOps.Devices
            WHERE RetiredUtc IS NULL AND (Name = @n OR (Source = 'Rack' AND Name LIKE @prefix ESCAPE '!'))
            ORDER BY CASE WHEN Name = @n THEN 0 ELSE 1 END;
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 128).Value = name;
        cmd.Parameters.Add("@prefix", SqlDbType.NVarChar, 140).Value = name.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![") + " (%";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? (r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)) : null;
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
