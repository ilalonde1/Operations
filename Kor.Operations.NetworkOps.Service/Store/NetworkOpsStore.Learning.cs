#nullable enable
using System.Data;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// The learning layer's persistence (db/KorNetworkOps/002): fact history, metrics, insights,
// resolutions and the Command Center's trigger queue.
internal sealed partial class NetworkOpsStore
{
    // ------------------------------------------------------------------ facts (history + change tracking)

    public async Task<Dictionary<string, string>> CurrentFactsAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT Fact, Value FROM NetworkOps.DeviceFacts WHERE DeviceId = @d AND SupersededUtc IS NULL;");
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    /// <summary>Supersedes changed/removed facts, records new values, and stamps every current fact as seen now.</summary>
    public async Task ApplyFactsAsync(int deviceId, IReadOnlyList<FactChange> changes, bool observed, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var ch in changes)
        {
            if (ch.OldValue is not null)
            {
                await using var sup = Cmd(c, "UPDATE NetworkOps.DeviceFacts SET SupersededUtc = @now WHERE DeviceId = @d AND Fact = @f AND SupersededUtc IS NULL;", tx);
                sup.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
                sup.Parameters.Add("@f", SqlDbType.VarChar, 64).Value = ch.Fact;
                sup.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                await sup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (ch.NewValue is not null)
            {
                await using var ins = Cmd(c, """
                    INSERT NetworkOps.DeviceFacts (DeviceId, Fact, Value, FirstSeenUtc, LastSeenUtc) VALUES (@d, @f, @v, @now, @now);
                    """, tx);
                ins.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
                ins.Parameters.Add("@f", SqlDbType.VarChar, 64).Value = ch.Fact;
                ins.Parameters.Add("@v", SqlDbType.NVarChar, 400).Value = Truncate(ch.NewValue, 400)!;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        if (observed)
        {
            await using var touch = Cmd(c, "UPDATE NetworkOps.DeviceFacts SET LastSeenUtc = @now WHERE DeviceId = @d AND SupersededUtc IS NULL;", tx);
            touch.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
            touch.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            await touch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>What changed in a PC's facts in (fromUtc, toUtc]: old values superseded, new values first seen.</summary>
    public async Task<IReadOnlyList<FactChange>> FactChangesBetweenAsync(int deviceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        // The PC's FIRST inventory is the baseline, not a change: facts first recorded then were not
        // "added", they were simply seen for the first time (the 09-28 proof blamed a fan fix on
        // "Revit, BIOS, CPU added" because the first inventory fell inside the window).
        await using var cmd = Cmd(c, """
            DECLARE @baseline datetime2(0) = (SELECT MIN(FirstSeenUtc) FROM NetworkOps.DeviceFacts WHERE DeviceId = @d);
            SELECT Fact, Value, CASE WHEN FirstSeenUtc > @from AND FirstSeenUtc <= @to AND FirstSeenUtc > @baseline THEN 1 ELSE 0 END AS IsNew,
                               CASE WHEN SupersededUtc > @from AND SupersededUtc <= @to THEN 1 ELSE 0 END AS IsOld
            FROM NetworkOps.DeviceFacts
            WHERE DeviceId = @d AND ((FirstSeenUtc > @from AND FirstSeenUtc <= @to AND FirstSeenUtc > @baseline) OR (SupersededUtc > @from AND SupersededUtc <= @to));
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;
        var olds = new Dictionary<string, string>(StringComparer.Ordinal);
        var news = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                // A value first seen AND superseded inside the window was transient; old wins for "was", new for "became".
                if (r.GetInt32(3) == 1) olds.TryAdd(r.GetString(0), r.GetString(1));
                if (r.GetInt32(2) == 1) news[r.GetString(0)] = r.GetString(1);
            }
        return olds.Keys.Union(news.Keys, StringComparer.Ordinal)
            .Select(f => new FactChange(f, olds.GetValueOrDefault(f), news.GetValueOrDefault(f)))
            .Where(ch => !string.Equals(ch.OldValue, ch.NewValue, StringComparison.Ordinal))
            .OrderBy(ch => ch.Fact, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ metrics (trend lines)

    public async Task InsertMetricsAsync(int deviceId, IReadOnlyList<MetricPoint> points, DateTime nowUtc, CancellationToken ct)
    {
        if (points.Count == 0) return;
        var t = new DataTable();
        t.Columns.Add("DeviceId", typeof(int));
        t.Columns.Add("Metric", typeof(string));
        t.Columns.Add("Subject", typeof(string));
        t.Columns.Add("CollectedUtc", typeof(DateTime));
        t.Columns.Add("Value", typeof(double));
        foreach (var p in points) t.Rows.Add(deviceId, p.Metric, Truncate(p.Subject, 128) ?? "", nowUtc, p.Value);

        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        using var bulk = new SqlBulkCopy(c) { DestinationTableName = "NetworkOps.Metrics", BulkCopyTimeout = 60 };
        foreach (DataColumn col in t.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(t, ct).ConfigureAwait(false);
    }

    /// <summary>What the device's LATEST read reported, every reading of it -- what its tiles are drawn from (GET
    /// /api/devices/{id}/readings). Only that read: a datastore removed on 2026-10-02 kept its tile for two days while this
    /// returned "the latest value of each reading in two days". A read stores all its readings at one time.</summary>
    public async Task<IReadOnlyList<Core.Rack.DeviceReading>> LatestReadingsAsync(int deviceId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            DECLARE @last datetime2(0) = (SELECT MAX(CollectedUtc) FROM NetworkOps.Metrics WHERE DeviceId = @d AND CollectedUtc >= DATEADD(day, -2, SYSUTCDATETIME()));
            SELECT Metric, Subject, Value, CollectedUtc FROM NetworkOps.Metrics
            WHERE DeviceId = @d AND CollectedUtc = @last ORDER BY Metric, Subject;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        var list = new List<Core.Rack.DeviceReading>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new Core.Rack.DeviceReading(r.GetString(0), r.GetString(1), r.GetDouble(2), DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
        return list;
    }

    public async Task<MetricHistory> MetricHistoryAsync(int deviceId, DateTime sinceUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT Metric, Subject, CollectedUtc, Value FROM NetworkOps.Metrics
            WHERE DeviceId = @d AND CollectedUtc >= @since ORDER BY CollectedUtc;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@since", SqlDbType.DateTime2).Value = sinceUtc;
        var h = new MetricHistory();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) h.Add(r.GetString(0), r.GetString(1), r.GetDateTime(2), r.GetDouble(3));
        return h;
    }

    public async Task<int> PurgeMetricsAsync(int keepDays, DateTime nowUtc, CancellationToken ct)
    {
        var total = 0;
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = Cmd(c, "DELETE TOP (20000) FROM NetworkOps.Metrics WHERE CollectedUtc < DATEADD(day, -@keep, @now);");
            cmd.Parameters.Add("@keep", SqlDbType.Int).Value = keepDays;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            total += n;
            if (n < 20000) return total;
        }
    }

    // ------------------------------------------------------------------ resolutions (learning what fixes things)

    public async Task<IReadOnlyList<string>> ActionsBetweenAsync(int deviceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            SELECT Kind, Detail FROM NetworkOps.Actions
            WHERE DeviceId = @d AND Status = 'Done' AND CompletedUtc > @from AND CompletedUtc <= @to ORDER BY CompletedUtc;
            """);
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = toUtc;
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(r.IsDBNull(1) ? r.GetString(0) : $"{r.GetString(0)} ({r.GetString(1)})");
        return list;
    }

    /// <summary>The ActionIdsJson column's size (nvarchar(400), migration 002).</summary>
    internal const int ActionIdsMax = 400;

    /// <summary>
    /// A JSON list that fits <paramref name="maxChars"/> WHOLE: the newest items that fit, never a cut string. It was
    /// Truncate(Serialize(list), 400), which on 2026-10-02 -- after a night of installs made the lists long -- cut through a
    /// "\u" escape and appended "…": invalid JSON in FindingResolutions, and GET /api/resolutions (which every device
    /// window loads) answered 500 until the row was read past. Serialized JSON is never truncated.
    /// </summary>
    internal static string JsonListWithin(IReadOnlyList<string> items, int maxChars)
    {
        for (var skip = 0; skip <= items.Count; skip++)
        {
            var json = JsonSerializer.Serialize(items.Skip(skip).ToList());
            if (json.Length <= maxChars) return json;
        }
        return "[]";
    }

    public async Task InsertResolutionAsync(long findingId, int deviceId, Resolution res, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            IF NOT EXISTS (SELECT 1 FROM NetworkOps.FindingResolutions WHERE FindingId = @id)
            INSERT NetworkOps.FindingResolutions (FindingId, DeviceId, RuleKey, ClearedUtc, Rebooted, ChangedFactsJson, ActionIdsJson, Summary)
            VALUES (@id, @d, @k, @at, @rb, @facts, @acts, @sum);
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = findingId;
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = deviceId;
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 160).Value = res.RuleKey;
        cmd.Parameters.Add("@at", SqlDbType.DateTime2).Value = res.ClearedUtc;
        cmd.Parameters.Add("@rb", SqlDbType.Bit).Value = res.Rebooted;
        cmd.Parameters.Add("@facts", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(res.ChangedFacts);
        cmd.Parameters.Add("@acts", SqlDbType.NVarChar, ActionIdsMax).Value = JsonListWithin(res.Actions, ActionIdsMax);
        cmd.Parameters.Add("@sum", SqlDbType.NVarChar, 1000).Value = Truncate(res.Summary, 1000)!;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ fleet insights

    public async Task<IReadOnlyList<FleetMember>> FleetMembersAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var facts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var families = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = Cmd(c, """
            SELECT d.Name, f.Fact, f.Value FROM NetworkOps.DeviceFacts f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.SupersededUtc IS NULL AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!facts.TryGetValue(r.GetString(0), out var m)) facts[r.GetString(0)] = m = new(StringComparer.Ordinal);
                m[r.GetString(1)] = r.GetString(2);
            }
        await using (var cmd = Cmd(c, """
            SELECT d.Name, f.RuleKey FROM NetworkOps.Findings f JOIN NetworkOps.Devices d ON d.DeviceId = f.DeviceId
            WHERE f.ClearedUtc IS NULL AND d.InDirectory = 1 AND d.RetiredUtc IS NULL;
            """))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!families.TryGetValue(r.GetString(0), out var s)) families[r.GetString(0)] = s = new(StringComparer.Ordinal);
                s.Add(FleetCorrelation.ProblemOf(r.GetString(1)));
            }
        // Only PCs we have facts for take part: a PC never inventoried can be neither for nor against a pattern.
        return facts.Select(kv => new FleetMember(kv.Key, kv.Value,
            families.TryGetValue(kv.Key, out var s) ? s : new HashSet<string>(StringComparer.Ordinal))).ToList();
    }

    public async Task<IReadOnlyList<string>> ActiveInsightKeysAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT RuleFamily, Fact, Value FROM NetworkOps.Insights WHERE ClearedUtc IS NULL;");
        var keys = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) keys.Add($"{r.GetString(0)}|{r.GetString(1)}|{r.GetString(2)}");
        return keys;
    }

    public async Task ApplyInsightsAsync(IReadOnlyList<(InsightChange Kind, string Key, FleetInsight? Current)> changes, DateTime nowUtc, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var (kind, key, cur) in changes)
        {
            var parts = key.Split('|', 3);
            SqlCommand cmd = kind switch
            {
                InsightChange.New => Cmd(c, """
                    INSERT NetworkOps.Insights (RuleFamily, Fact, Value, AffectedWith, TotalWith, AffectedWithout, TotalWithout, Strength, Summary, FirstSeenUtc, LastSeenUtc)
                    VALUES (@fam, @fact, @val, @aw, @tw, @ao, @to, @str, @sum, @now, @now);
                    """, tx),
                InsightChange.Unchanged => Cmd(c, """
                    UPDATE NetworkOps.Insights SET AffectedWith = @aw, TotalWith = @tw, AffectedWithout = @ao, TotalWithout = @to,
                        Strength = @str, Summary = @sum, LastSeenUtc = @now
                    WHERE RuleFamily = @fam AND Fact = @fact AND Value = @val AND ClearedUtc IS NULL;
                    """, tx),
                _ => Cmd(c, "UPDATE NetworkOps.Insights SET ClearedUtc = @now WHERE RuleFamily = @fam AND Fact = @fact AND Value = @val AND ClearedUtc IS NULL;", tx),
            };
            await using (cmd)
            {
                cmd.Parameters.Add("@fam", SqlDbType.VarChar, 64).Value = parts[0];
                cmd.Parameters.Add("@fact", SqlDbType.VarChar, 64).Value = parts[1];
                cmd.Parameters.Add("@val", SqlDbType.NVarChar, 400).Value = parts[2];
                cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
                if (cur is not null)
                {
                    cmd.Parameters.Add("@aw", SqlDbType.Int).Value = cur.AffectedWith;
                    cmd.Parameters.Add("@tw", SqlDbType.Int).Value = cur.TotalWith;
                    cmd.Parameters.Add("@ao", SqlDbType.Int).Value = cur.AffectedWithout;
                    cmd.Parameters.Add("@to", SqlDbType.Int).Value = cur.TotalWithout;
                    cmd.Parameters.Add("@str", SqlDbType.Float).Value = cur.Strength;
                    cmd.Parameters.Add("@sum", SqlDbType.NVarChar, 1000).Value = Truncate(cur.Summary, 1000)!;
                }
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Command Center triggers

    public sealed record ClaimedTrigger(long TriggerId, string JobName, string? DeviceName, string RequestedBy);

    /// <summary>Claims the oldest pending trigger for this host (UPDLOCK + READPAST: two pollers never take the same one).</summary>
    public async Task<ClaimedTrigger?> ClaimTriggerAsync(string host, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            WITH next AS (
                SELECT TOP (1) * FROM NetworkOps.JobTriggers WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = 'Pending' ORDER BY RequestedUtc)
            UPDATE next SET Status = 'Running', ClaimedUtc = SYSUTCDATETIME(), ClaimedBy = @h
            OUTPUT inserted.TriggerId, inserted.JobName, inserted.DeviceName, inserted.RequestedBy;
            """);
        cmd.Parameters.Add("@h", SqlDbType.NVarChar, 64).Value = host;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)
            ? new ClaimedTrigger(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3))
            : null;
    }

    public async Task CompleteTriggerAsync(long triggerId, bool success, string result)
    {
        await using var c = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.JobTriggers SET Status = @s, CompletedUtc = SYSUTCDATETIME(), Result = @r WHERE TriggerId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = success ? "Done" : "Failed";
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 1000).Value = Truncate(result, 1000)!;
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Triggers left 'Running' by a service that died mid-run go back to Pending on startup.</summary>
    public async Task<int> RequeueAbandonedTriggersAsync(string host, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.JobTriggers SET Status = 'Pending', ClaimedUtc = NULL, ClaimedBy = NULL WHERE Status = 'Running' AND ClaimedBy = @h;");
        cmd.Parameters.Add("@h", SqlDbType.NVarChar, 64).Value = host;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>True when migration 002 has been applied -- the learning layer cannot run without it.</summary>
    public async Task<bool> LearningSchemaPresentAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'NetworkOps.DeviceFacts', N'U') IS NOT NULL AND OBJECT_ID(N'NetworkOps.JobTriggers', N'U') IS NOT NULL THEN 1 ELSE 0 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }
}
