#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Ingestion;

public sealed class SqlJobRunStore : IJobRunStore
{
    private const int CommandTimeoutSeconds = 15;

    private readonly string _connectionString;

    public SqlJobRunStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<long> StartRunAsync(string jobName, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.JobRuns (JobName)
OUTPUT inserted.Id
VALUES (@jobName);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@jobName", SqlDbType.NVarChar, 100).Value = TrimTo(jobName, 100);
        var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return id;
    }

    public async Task CompleteRunAsync(long runId, bool success, string? summary, string? error, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.JobRuns
SET EndedAtUtc = sysdatetimeoffset(),
    Success = @success,
    Summary = @summary,
    Error = @error
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@success", SqlDbType.Bit).Value = success;
        cmd.Parameters.Add("@summary", SqlDbType.NVarChar, 1000).Value = (object?)TrimTo(summary, 1000) ?? DBNull.Value;
        cmd.Parameters.Add("@error", SqlDbType.NVarChar, 2000).Value = (object?)TrimTo(error, 2000) ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobRunRow>> ListRecentAsync(int take, CancellationToken ct)
    {
        if (take <= 0)
        {
            return Array.Empty<JobRunRow>();
        }

        const string sql = @"
SELECT TOP (@take)
       Id,
       JobName,
       StartedAtUtc,
       EndedAtUtc,
       Success,
       Summary,
       Error
FROM opportunities.JobRuns
ORDER BY StartedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Min(take, 1000);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<JobRunRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobRunRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTimeOffset(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.IsDBNull(4) ? null : reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
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
