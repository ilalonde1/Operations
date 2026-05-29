#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Ingestion;

public sealed class SqlJobScheduleStore : IJobScheduleStore
{
    private const int CommandTimeoutSeconds = 15;

    private readonly string _connectionString;

    public SqlJobScheduleStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task UpsertScheduleAsync(string jobName, string? cron, bool enabled, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

BEGIN TRAN;

UPDATE opportunities.JobSchedules WITH (UPDLOCK, HOLDLOCK)
SET CronSchedule = @cron,
    Enabled = @enabled,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE JobName = @jobName;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.JobSchedules (JobName, CronSchedule, Enabled)
    VALUES (@jobName, @cron, @enabled);
END;

COMMIT TRAN;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@jobName", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        cmd.Parameters.Add("@cron", SqlDbType.NVarChar, 50).Value = (object?)TrimTo(cron, 50) ?? DBNull.Value;
        cmd.Parameters.Add("@enabled", SqlDbType.Bit).Value = enabled;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobScheduleRow>> ListWithLastRunAsync(CancellationToken ct)
    {
        const string sql = @"
WITH LatestRuns AS
(
    SELECT
        JobName,
        StartedAtUtc,
        Success,
        Summary,
        ROW_NUMBER() OVER (PARTITION BY JobName ORDER BY StartedAtUtc DESC) AS rn
    FROM opportunities.JobRuns
)
SELECT
    s.JobName,
    s.CronSchedule,
    s.Enabled,
    r.StartedAtUtc AS LastRunAtUtc,
    r.Success AS LastSuccess,
    r.Summary AS LastSummary
FROM opportunities.JobSchedules s
LEFT JOIN LatestRuns r
    ON r.JobName = s.JobName
   AND r.rn = 1
ORDER BY s.JobName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<JobScheduleRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobScheduleRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.IsDBNull(4) ? null : reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
