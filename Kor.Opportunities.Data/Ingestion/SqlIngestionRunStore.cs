#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Ingestion;

public sealed class SqlIngestionRunStore : IIngestionRunStore
{
    private const int CommandTimeoutSeconds = 15;

    private const string AllColumns = @"
Id, StartedAtUtc, EndedAtUtc, ProviderName, Success, InsertedCount, DuplicateCount,
SkippedCount, FailedCount, ErrorSummary, HostInstance, CorrelationId";

    private readonly string _connectionString;

    public SqlIngestionRunStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<Guid> StartAsync(string providerName, string? hostInstance, string? correlationId, CancellationToken ct)
    {
        // Let SQL Server allocate the Id via NEWID() default and OUTPUT it back so the
        // caller can pass it to CompleteAsync without round-tripping through C# Guid.
        const string sql = @"
INSERT INTO opportunities.IngestionRuns
    (StartedAtUtc, ProviderName, HostInstance, CorrelationId)
OUTPUT inserted.Id
VALUES
    (sysdatetimeoffset(), @provider, @host, @corr);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 200).Value = providerName;
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 200).Value = (object?)hostInstance ?? DBNull.Value;
        cmd.Parameters.Add("@corr", SqlDbType.NVarChar, 64).Value = (object?)correlationId ?? DBNull.Value;
        var id = (Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return id;
    }

    public async Task CompleteAsync(
        Guid runId,
        bool success,
        int insertedCount,
        int duplicateCount,
        int skippedCount,
        int failedCount,
        string? errorSummary,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.IngestionRuns
SET EndedAtUtc     = sysdatetimeoffset(),
    Success        = @success,
    InsertedCount  = @inserted,
    DuplicateCount = @duplicate,
    SkippedCount   = @skipped,
    FailedCount    = @failed,
    ErrorSummary   = @error
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = runId;
        cmd.Parameters.Add("@success", SqlDbType.Bit).Value = success;
        cmd.Parameters.Add("@inserted", SqlDbType.Int).Value = insertedCount;
        cmd.Parameters.Add("@duplicate", SqlDbType.Int).Value = duplicateCount;
        cmd.Parameters.Add("@skipped", SqlDbType.Int).Value = skippedCount;
        cmd.Parameters.Add("@failed", SqlDbType.Int).Value = failedCount;
        cmd.Parameters.Add("@error", SqlDbType.NVarChar, 2000).Value = (object?)errorSummary ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IngestionRun>> ListRecentAsync(int max, CancellationToken ct)
    {
        if (max <= 0)
        {
            return Array.Empty<IngestionRun>();
        }

        var sql = $@"
SELECT TOP (@max) {AllColumns}
FROM opportunities.IngestionRuns
ORDER BY StartedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<IngestionRun>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new IngestionRun
            {
                Id = reader.GetGuid(0),
                StartedAtUtc = reader.GetDateTimeOffset(1),
                EndedAtUtc = reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2),
                ProviderName = reader.GetString(3),
                Success = reader.GetBoolean(4),
                InsertedCount = reader.GetInt32(5),
                DuplicateCount = reader.GetInt32(6),
                SkippedCount = reader.GetInt32(7),
                FailedCount = reader.GetInt32(8),
                ErrorSummary = reader.IsDBNull(9) ? null : reader.GetString(9),
                HostInstance = reader.IsDBNull(10) ? null : reader.GetString(10),
                CorrelationId = reader.IsDBNull(11) ? null : reader.GetString(11),
            });
        }

        return rows;
    }
}
