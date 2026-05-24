#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Ingestion;

public sealed class SqlIngestionTriggerStore : IIngestionTriggerStore
{
    private const int CommandTimeoutSeconds = 15;

    private const string AllColumns = @"
Id, OpportunitySourceId, Status, RequestedBy, RequestedAtUtc, ClaimedAtUtc,
ClaimedBy, CompletedAtUtc, IngestionRunId, ErrorSummary";

    private readonly string _connectionString;

    public SqlIngestionTriggerStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<Guid> EnqueueAsync(Guid opportunitySourceId, string requestedBy, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.IngestionTriggers
    (OpportunitySourceId, Status, RequestedBy)
OUTPUT inserted.Id
VALUES
    (@srcId, 'Pending', @who);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@srcId", SqlDbType.UniqueIdentifier).Value = opportunitySourceId;
        cmd.Parameters.Add("@who", SqlDbType.NVarChar, 150).Value = requestedBy;
        return (Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<IngestionTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct)
    {
        // Single-statement UPDATE TOP (1) ... OUTPUT — locks one Pending or
        // stale InProgress row, flips it to InProgress, and returns the
        // post-update image. Two pollers can race here without double-claiming
        // because SQL Server serializes the UPDATEs.
        const string sql = @"
UPDATE TOP (1) opportunities.IngestionTriggers
SET Status        = 'InProgress',
    ClaimedAtUtc  = sysdatetimeoffset(),
    ClaimedBy     = @who,
    ReclaimedCount = CASE WHEN Status = 'InProgress' THEN ReclaimedCount + 1 ELSE ReclaimedCount END
OUTPUT
    inserted.Id, inserted.OpportunitySourceId, inserted.Status,
    inserted.RequestedBy, inserted.RequestedAtUtc, inserted.ClaimedAtUtc,
    inserted.ClaimedBy, inserted.CompletedAtUtc, inserted.IngestionRunId,
    inserted.ErrorSummary
WHERE Id = (
    SELECT TOP (1) Id
    FROM opportunities.IngestionTriggers WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE Status = 'Pending'
       OR (Status = 'InProgress'
           AND ClaimedAtUtc < DATEADD(MINUTE, -@staleMinutes, sysutcdatetime()))
    ORDER BY CASE WHEN Status = 'Pending' THEN 0 ELSE 1 END, RequestedAtUtc
);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@who", SqlDbType.NVarChar, 200).Value = claimedBy;
        cmd.Parameters.Add("@staleMinutes", SqlDbType.Int).Value = 15;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task CompleteAsync(
        Guid triggerId,
        IngestionTriggerStatus terminalStatus,
        Guid? ingestionRunId,
        string? errorSummary,
        CancellationToken ct)
    {
        if (terminalStatus == IngestionTriggerStatus.Pending || terminalStatus == IngestionTriggerStatus.InProgress)
        {
            throw new ArgumentException("Terminal status must be Completed/Failed/Cancelled.", nameof(terminalStatus));
        }

        const string sql = @"
UPDATE opportunities.IngestionTriggers
SET Status         = @status,
    CompletedAtUtc = sysdatetimeoffset(),
    IngestionRunId = @runId,
    ErrorSummary   = @err
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = triggerId;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 32).Value = StatusToString(terminalStatus);
        cmd.Parameters.Add("@runId", SqlDbType.UniqueIdentifier).Value = (object?)ingestionRunId ?? DBNull.Value;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value = (object?)errorSummary ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IngestionTrigger>> ListRecentAsync(int max, CancellationToken ct)
    {
        if (max <= 0)
        {
            return Array.Empty<IngestionTrigger>();
        }

        var sql = $@"
SELECT TOP (@max) {AllColumns}
FROM opportunities.IngestionTriggers
ORDER BY RequestedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<IngestionTrigger>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    private static IngestionTrigger MapReader(SqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        OpportunitySourceId = r.GetGuid(1),
        Status = ParseStatus(r.GetString(2)),
        RequestedBy = r.GetString(3),
        RequestedAtUtc = r.GetDateTimeOffset(4),
        ClaimedAtUtc = r.IsDBNull(5) ? null : r.GetDateTimeOffset(5),
        ClaimedBy = r.IsDBNull(6) ? null : r.GetString(6),
        CompletedAtUtc = r.IsDBNull(7) ? null : r.GetDateTimeOffset(7),
        IngestionRunId = r.IsDBNull(8) ? null : r.GetGuid(8),
        ErrorSummary = r.IsDBNull(9) ? null : r.GetString(9),
    };

    private static string StatusToString(IngestionTriggerStatus s) => s switch
    {
        IngestionTriggerStatus.Pending => "Pending",
        IngestionTriggerStatus.InProgress => "InProgress",
        IngestionTriggerStatus.Completed => "Completed",
        IngestionTriggerStatus.Failed => "Failed",
        IngestionTriggerStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unknown status."),
    };

    private static IngestionTriggerStatus ParseStatus(string raw) => raw switch
    {
        "Pending" => IngestionTriggerStatus.Pending,
        "InProgress" => IngestionTriggerStatus.InProgress,
        "Completed" => IngestionTriggerStatus.Completed,
        "Failed" => IngestionTriggerStatus.Failed,
        "Cancelled" => IngestionTriggerStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown trigger status '{raw}' on disk."),
    };
}
