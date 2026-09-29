#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.App.NetworkOps;

// Everything the Command Center reads from, and the little it writes to, the KorNetworkOps
// database. It connects as networkops_ui, which may read everything but write only: a check-now
// request, a cancel of one, a note, and acknowledge/snooze on a finding
// (db/KorNetworkOps/002_LearningLayerAndCommandCenter.sql). It cannot change what a finding says,
// clear one, or touch the service's history -- the service owns the truth, the page annotates it.
//
// The connection string comes from the KOR_NETWORKOPS_UIDB environment variable on the one PC that
// runs the page, NOT App.config: App.config ships to every PC in the firm.
public sealed class NetworkOpsReader
{
    public const string ConnectionVariable = "KOR_NETWORKOPS_UIDB";

    // 30 s matches Kor.Operations.Data.SqlTimeouts.UiFacing for parity with the rest of the App.
    private const int UiTimeoutSeconds = 30;

    private readonly string? _cs;

    public NetworkOpsReader(string? connectionString)
    {
        _cs = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }

    public static NetworkOpsReader FromEnvironment() => new(Environment.GetEnvironmentVariable(ConnectionVariable));

    public bool IsConfigured => _cs is not null;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        if (_cs is null)
            throw new InvalidOperationException($"{ConnectionVariable} is not set on this PC, so the page has no database to read. It holds the networkops_ui connection string.");
        var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        return con;
    }

    private static SqlCommand Cmd(SqlConnection con, string sql) => new(sql, con) { CommandTimeout = UiTimeoutSeconds };

    // ------------------------------------------------------------------ the fleet

    public async Task<FleetSnapshot> GetFleetAsync(CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);

        var devices = new List<DeviceRow>();
        await using (var cmd = Cmd(con, """
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
        await using (var cmd = Cmd(con, """
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
        await using (var cmd = Cmd(con, """
            SELECT f.FindingId, d.Name, f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.LastSeenUtc,
                   f.AcknowledgedUtc, f.AcknowledgedBy, f.SnoozedUntilUtc, f.AckNote
            FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.ClearedUtc IS NULL AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                open.Add(ReadFinding(r));

        var patterns = new List<ActivePattern>();
        await using (var cmd = Cmd(con, """
            SELECT RuleFamily, Fact, Value, AffectedWith, TotalWith, AffectedWithout, TotalWithout, Summary, FirstSeenUtc
            FROM NetworkOps.Insights WHERE ClearedUtc IS NULL
            ORDER BY AffectedWith DESC;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                patterns.Add(new ActivePattern(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
                    r.GetString(7), Utc(r, 8)!.Value));

        ServiceBeat? beat = null;
        await using (var cmd = Cmd(con, "SELECT TOP (1) Host, StartedUtc, LastBeatUtc, ServiceVersion FROM NetworkOps.ServiceHeartbeat ORDER BY LastBeatUtc DESC;"))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            if (await r.ReadAsync(ct).ConfigureAwait(false))
                beat = new ServiceBeat(r.GetString(0), Utc(r, 1)!.Value, Utc(r, 2)!.Value, r.IsDBNull(3) ? null : r.GetString(3));

        return new FleetSnapshot(devices, facts, open, patterns, beat);
    }

    // ------------------------------------------------------------------ one PC

    public async Task<DeviceHistory> GetDeviceHistoryAsync(int deviceId, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);

        var cleared = new List<ClearedFinding>();
        await using (var cmd = Cmd(con, """
            SELECT TOP (200) f.RuleKey, f.Severity, f.Title, f.Evidence, f.FirstSeenUtc, f.ClearedUtc, fr.Summary
            FROM NetworkOps.Findings f
            LEFT JOIN NetworkOps.FindingResolutions fr ON fr.FindingId = f.FindingId
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

        // Fact history: every value a fact has had, newest first. The first inventory is the baseline,
        // so a fact's first row is "first seen", not a change.
        var factHistory = new List<FactHistoryRow>();
        await using (var cmd = Cmd(con, """
            SELECT TOP (300) Fact, Value, FirstSeenUtc, SupersededUtc
            FROM NetworkOps.DeviceFacts WHERE DeviceId = @d
            ORDER BY FirstSeenUtc DESC, Fact;
            """))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                factHistory.Add(new FactHistoryRow(r.GetString(0), r.GetString(1), Utc(r, 2)!.Value, Utc(r, 3)));
        }

        var notes = new List<NoteRow>();
        await using (var cmd = Cmd(con, "SELECT TOP (100) Author, CreatedUtc, Body FROM NetworkOps.DeviceNotes WHERE DeviceId = @d ORDER BY CreatedUtc DESC;"))
        {
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                notes.Add(new NoteRow(r.GetString(0), Utc(r, 1)!.Value, r.GetString(2)));
        }

        return new DeviceHistory(cleared, factHistory, notes);
    }

    /// <summary>Every recorded resolution in the fleet, rebuilt for FixLearning -- the evidence behind "what fixed it before".</summary>
    public async Task<IReadOnlyList<Resolution>> GetResolutionsAsync(CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(con, "SELECT RuleKey, ClearedUtc, Rebooted, ChangedFactsJson, ActionIdsJson FROM NetworkOps.FindingResolutions;");
        var list = new List<Resolution>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var facts = r.IsDBNull(3) ? null : JsonSerializer.Deserialize<List<FactChange>>(r.GetString(3));
            var actions = r.IsDBNull(4) ? null : JsonSerializer.Deserialize<List<string>>(r.GetString(4));
            list.Add(new Resolution(r.GetString(0), Utc(r, 1)!.Value, r.GetBoolean(2), facts ?? [], actions ?? []));
        }
        return list;
    }

    // ------------------------------------------------------------------ check now

    /// <summary>Queues "check this PC now"; the service claims it within ~5 s and writes the result back to the row.</summary>
    public async Task<long> QueueCheckAsync(string deviceName, string requestedBy, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(con, """
            INSERT NetworkOps.JobTriggers (JobName, DeviceName, RequestedBy)
            OUTPUT inserted.TriggerId
            VALUES ('HealthSweep', @n, @by);
            """);
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 64).Value = deviceName;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = requestedBy;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<TriggerState?> GetTriggerAsync(long triggerId, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(con, "SELECT Status, Result, RequestedUtc, ClaimedUtc, CompletedUtc FROM NetworkOps.JobTriggers WHERE TriggerId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new TriggerState(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), Utc(r, 2)!.Value, Utc(r, 3), Utc(r, 4));
    }

    /// <summary>Cancels a check that has not been claimed yet. False when the service already took it.</summary>
    public async Task<bool> CancelCheckAsync(long triggerId, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(con, "UPDATE NetworkOps.JobTriggers SET Status = 'Cancelled' WHERE TriggerId = @id AND Status = 'Pending';");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    // ------------------------------------------------------------------ annotations

    /// <summary>Acknowledge: "known, leave it" -- the PC stops showing red for it until it clears and comes back.</summary>
    public Task AcknowledgeAsync(long findingId, string by, string? note, CancellationToken ct)
        => AnnotateAsync(findingId, "AcknowledgedUtc = SYSUTCDATETIME(), AcknowledgedBy = @by, SnoozedUntilUtc = NULL, AckNote = @note", by, note, null, ct);

    /// <summary>Snooze: quiet until a time, then it counts again if it is still there.</summary>
    public Task SnoozeAsync(long findingId, DateTime untilUtc, string by, string? note, CancellationToken ct)
        => AnnotateAsync(findingId, "SnoozedUntilUtc = @until, AcknowledgedUtc = NULL, AcknowledgedBy = @by, AckNote = @note", by, note, untilUtc, ct);

    /// <summary>Undo an acknowledge or snooze: the finding counts again at once.</summary>
    public Task ReopenAsync(long findingId, string by, CancellationToken ct)
        => AnnotateAsync(findingId, "AcknowledgedUtc = NULL, SnoozedUntilUtc = NULL, AcknowledgedBy = @by, AckNote = NULL", by, null, null, ct);

    private async Task AnnotateAsync(long findingId, string set, string by, string? note, DateTime? untilUtc, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        // Only an OPEN finding: one the service cleared a moment ago is history and stays as it was.
        await using var cmd = Cmd(con, $"UPDATE NetworkOps.Findings SET {set} WHERE FindingId = @id AND ClearedUtc IS NULL;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = findingId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@note", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim();
        cmd.Parameters.Add("@until", SqlDbType.DateTime2).Value = (object?)untilUtc ?? DBNull.Value;
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("That finding has cleared since the page loaded; refresh to see it in the history.");
    }

    public async Task AddNoteAsync(int deviceId, string author, string body, CancellationToken ct)
    {
        await using var con = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(con, "INSERT NetworkOps.DeviceNotes (DeviceId, Author, Body) VALUES (@d, @a, @b);");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@a", SqlDbType.NVarChar, 128).Value = author;
        cmd.Parameters.Add("@b", SqlDbType.NVarChar, 4000).Value = body.Trim();
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ helpers

    private static FleetFinding ReadFinding(SqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), (Severity)r.GetByte(3), r.GetString(4), r.GetString(5),
        Utc(r, 6)!.Value, Utc(r, 7)!.Value, Utc(r, 8), r.IsDBNull(9) ? null : r.GetString(9), Utc(r, 10), r.IsDBNull(11) ? null : r.GetString(11));

    // The store keeps datetime2 in UTC; SqlClient hands it back as Unspecified.
    private static DateTime? Utc(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
}

public sealed record DeviceRow(int DeviceId, string Name, DateTime? LastReachableUtc, DateTime? LastCheckedUtc);

public sealed record ServiceBeat(string Host, DateTime StartedUtc, DateTime LastBeatUtc, string? Version);

public sealed record FleetSnapshot(
    IReadOnlyList<DeviceRow> Devices,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FactsByDevice,
    IReadOnlyList<FleetFinding> OpenFindings,
    IReadOnlyList<ActivePattern> Patterns,
    ServiceBeat? Service)
{
    public IReadOnlyDictionary<string, string> FactsOf(string device)
        => FactsByDevice.TryGetValue(device, out var f) ? f : new Dictionary<string, string>();

    public IEnumerable<FleetFinding> OpenOn(string device)
        => OpenFindings.Where(f => f.Device.Equals(device, StringComparison.OrdinalIgnoreCase));
}

public sealed record ClearedFinding(string RuleKey, Severity Severity, string Title, string Evidence, DateTime FirstSeenUtc, DateTime ClearedUtc, string? Resolution);

public sealed record FactHistoryRow(string Fact, string Value, DateTime FirstSeenUtc, DateTime? SupersededUtc);

public sealed record NoteRow(string Author, DateTime CreatedUtc, string Body);

public sealed record DeviceHistory(IReadOnlyList<ClearedFinding> Cleared, IReadOnlyList<FactHistoryRow> Facts, IReadOnlyList<NoteRow> Notes);

public sealed record TriggerState(string Status, string? Result, DateTime RequestedUtc, DateTime? ClaimedUtc, DateTime? CompletedUtc);
