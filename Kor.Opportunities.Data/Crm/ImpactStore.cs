#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>One recorded reading of one scoreboard metric (migration 370).</summary>
public sealed record ImpactReading(DateOnly Date, string Metric, double Value);

/// <summary>
/// The impact scoreboard's daily readings, and the BD-side counts read from the CRM and the rhythm on the day. Writes only to
/// opportunities.ImpactSnapshot; a second write on the same day replaces that day's value.
/// </summary>
public sealed class SqlImpactStore
{
    private readonly string _connectionString;

    public SqlImpactStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    /// <summary>
    /// Today's BD counts: CRM won/lost totals and fees, open pursuits, actions closed and meetings held so far. Won is stage 6
    /// with outcome 1, lost stage 7 with outcome 2, open stage 1 (the CRM's own values, 18 Oct 2026). Running totals, so a
    /// month's change is the month's activity.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, double>> ReadBdTotalsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT N'CrmWonCount', CAST(COUNT(*) AS FLOAT) FROM opportunities.CrmEngagements WHERE Stage = 6 AND WonLostOutcome = 1
UNION ALL SELECT N'CrmWonFee', CAST(COALESCE(SUM(ProposedFee), 0) AS FLOAT) FROM opportunities.CrmEngagements WHERE Stage = 6 AND WonLostOutcome = 1
UNION ALL SELECT N'CrmLostCount', CAST(COUNT(*) AS FLOAT) FROM opportunities.CrmEngagements WHERE Stage = 7 AND WonLostOutcome = 2
UNION ALL SELECT N'CrmOpenCount', CAST(COUNT(*) AS FLOAT) FROM opportunities.CrmEngagements WHERE Stage = 1
UNION ALL SELECT N'ActionsClosedTotal', CAST(COUNT(*) AS FLOAT) FROM opportunities.BdAsk WHERE Status = N'Done'
UNION ALL SELECT N'ActionsOpen', CAST(COUNT(*) AS FLOAT) FROM opportunities.BdAsk WHERE Status = N'Open'
UNION ALL SELECT N'MeetingsHeldTotal', CAST(COUNT(*) AS FLOAT) FROM opportunities.BdMeeting WHERE Status = N'Held';";
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result[r.GetString(0)] = r.GetDouble(1);
        return result;
    }

    public async Task RecordAsync(DateOnly date, IReadOnlyDictionary<string, double> metrics, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var (metric, value) in metrics)
        {
            if (string.IsNullOrWhiteSpace(metric) || metric.Length > 60 || double.IsNaN(value) || double.IsInfinity(value)) continue;
            await using var cmd = new SqlCommand(@"
MERGE opportunities.ImpactSnapshot WITH (HOLDLOCK) AS t
USING (SELECT @date AS SnapshotDate, @metric AS Metric) AS s ON t.SnapshotDate = s.SnapshotDate AND t.Metric = s.Metric
WHEN MATCHED THEN UPDATE SET Value = @value, RecordedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT (SnapshotDate, Metric, Value) VALUES (@date, @metric, @value);", con, tx) { CommandTimeout = 30 };
            cmd.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@metric", SqlDbType.NVarChar, 60).Value = metric;
            cmd.Parameters.Add("@value", SqlDbType.Float).Value = value;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImpactReading>> ListAsync(DateOnly from, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT SnapshotDate, Metric, Value FROM opportunities.ImpactSnapshot WHERE SnapshotDate >= @from ORDER BY SnapshotDate, Metric;", con)
            { CommandTimeout = 30 };
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<ImpactReading>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new ImpactReading(DateOnly.FromDateTime(r.GetDateTime(0)), r.GetString(1), r.GetDouble(2)));
        return rows;
    }
}
