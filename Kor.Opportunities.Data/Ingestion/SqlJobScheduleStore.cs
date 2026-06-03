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

    public async Task UpsertScheduleAsync(string jobName, string? cron, bool enabled, string? category, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

BEGIN TRAN;

UPDATE opportunities.JobSchedules WITH (UPDLOCK, HOLDLOCK)
SET CronSchedule = @cron,
    Enabled = @enabled,
    Category = @category,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE JobName = @jobName;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.JobSchedules (JobName, CronSchedule, Enabled, Category)
    VALUES (@jobName, @cron, @enabled, @category);
END;

COMMIT TRAN;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@jobName", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        cmd.Parameters.Add("@cron", SqlDbType.NVarChar, 50).Value = (object?)TrimTo(cron, 50) ?? DBNull.Value;
        cmd.Parameters.Add("@enabled", SqlDbType.Bit).Value = enabled;
        cmd.Parameters.Add("@category", SqlDbType.NVarChar, 40).Value = (object?)TrimTo(category, 40) ?? DBNull.Value;
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
    s.Category,
    s.LastReportPath,
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
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.Default, ct).ConfigureAwait(false);

        var rows = new List<JobScheduleRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobScheduleRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    public async Task UpdateEnabledAsync(string jobName, bool enabled, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.JobSchedules WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET Enabled = @e,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE JobName = @j;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        cmd.Parameters.Add("@e", SqlDbType.Bit).Value = enabled;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateLastReportPathAsync(string jobName, string? reportPath, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.JobSchedules WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET LastReportPath = @p,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE JobName = @j;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 500).Value = (object?)TrimTo(reportPath, 500) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> EnqueueTriggerAsync(string jobName, string? requestedBy, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.JobTriggerRequests (JobName, RequestedBy)
OUTPUT INSERTED.Id
VALUES (@j, @r);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        cmd.Parameters.Add("@r", SqlDbType.NVarChar, 100).Value = (object?)TrimTo(requestedBy, 100) ?? DBNull.Value;
        var id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(id);
    }

    public async Task<IReadOnlyList<JobTriggerRequestRow>> ListPendingTriggersAsync(int top, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@top)
    Id,
    JobName,
    RequestedAtUtc,
    RequestedBy
FROM opportunities.JobTriggerRequests
WHERE Status = 'Pending'
ORDER BY RequestedAtUtc;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.Default, ct).ConfigureAwait(false);

        var rows = new List<JobTriggerRequestRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobTriggerRequestRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTimeOffset(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    public async Task MarkTriggerFiredAsync(long requestId, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.JobTriggerRequests
SET Status = 'Fired',
    ProcessedAtUtc = sysdatetimeoffset(),
    ErrorMessage = NULL
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = requestId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkTriggerFailedAsync(long requestId, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.JobTriggerRequests
SET Status = 'Failed',
    ProcessedAtUtc = sysdatetimeoffset(),
    ErrorMessage = @err
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = requestId;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, -1).Value = (object?)errorMessage ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobRunRow>> GetRecentRunsAsync(string jobName, int top, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@top)
    Id,
    StartedAtUtc,
    EndedAtUtc,
    Success,
    Summary,
    Error
FROM opportunities.JobRuns
WHERE JobName = @j
ORDER BY StartedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;
        cmd.Parameters.Add("@j", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.Default, ct).ConfigureAwait(false);

        var rows = new List<JobRunRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobRunRow(
                reader.GetInt64(0),
                jobName,
                reader.GetDateTimeOffset(1),
                reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2),
                reader.IsDBNull(3) ? null : reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
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
