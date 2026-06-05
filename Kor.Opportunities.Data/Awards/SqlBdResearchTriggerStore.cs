#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlBdResearchTriggerStore : IBdResearchTriggerStore
{
    private const int CommandTimeoutSeconds = 15;

    private const string AllColumns = @"
Id, CanonicalOrgId, ProviderName, Status, RequestedBy, RequestedAtUtc,
ClaimedAtUtc, ClaimedBy, ClaimToken, CompletedAtUtc, ErrorSummary,
InputTokens, OutputTokens";

    private readonly string _connectionString;
    private readonly ILogger<SqlBdResearchTriggerStore>? _logger;

    public SqlBdResearchTriggerStore(string connectionString, ILogger<SqlBdResearchTriggerStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<Guid> EnqueueAsync(long canonicalOrgId, string providerName, string requestedBy, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.BdResearchTriggers
    (CanonicalOrgId, ProviderName, Status, RequestedBy)
OUTPUT inserted.Id
VALUES
    (@id, @provider, 'Pending', @who);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 64).Value = providerName;
        cmd.Parameters.Add("@who", SqlDbType.NVarChar, 150).Value = requestedBy;
        return (Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<BdResearchTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct)
    {
        // Single-statement UPDATE TOP (1) ... OUTPUT — locks one Pending or
        // stale InProgress row, flips it to InProgress, and returns the
        // post-update image. Two pollers can race here without double-claiming
        // because SQL Server serializes the UPDATEs.
        const string sql = @"
UPDATE TOP (1) opportunities.BdResearchTriggers
SET Status        = 'InProgress',
    ClaimedAtUtc  = sysdatetimeoffset(),
    ClaimedBy     = @who,
    ClaimToken    = NEWID(),
    ReclaimedCount = CASE WHEN Status = 'InProgress' THEN ReclaimedCount + 1 ELSE ReclaimedCount END
OUTPUT
    inserted.Id, inserted.CanonicalOrgId, inserted.ProviderName, inserted.Status,
    inserted.RequestedBy, inserted.RequestedAtUtc, inserted.ClaimedAtUtc,
    inserted.ClaimedBy, inserted.ClaimToken, inserted.CompletedAtUtc,
    inserted.ErrorSummary, inserted.InputTokens, inserted.OutputTokens
WHERE Id = (
    SELECT TOP (1) Id
    FROM opportunities.BdResearchTriggers WITH (READPAST, UPDLOCK, ROWLOCK)
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
        Guid claimToken,
        BdResearchTriggerStatus terminalStatus,
        long? inputTokens,
        long? outputTokens,
        string? errorSummary,
        CancellationToken ct)
    {
        if (terminalStatus == BdResearchTriggerStatus.Pending || terminalStatus == BdResearchTriggerStatus.InProgress)
        {
            throw new ArgumentException("Terminal status must be Completed/Failed/Cancelled.", nameof(terminalStatus));
        }

        const string sql = @"
UPDATE opportunities.BdResearchTriggers
SET Status         = @status,
    CompletedAtUtc = sysdatetimeoffset(),
    InputTokens    = @inputTokens,
    OutputTokens   = @outputTokens,
    ErrorSummary   = @err
WHERE Id = @id
  AND ClaimToken = @token
  AND Status = 'InProgress';";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = triggerId;
        cmd.Parameters.Add("@token", SqlDbType.UniqueIdentifier).Value = claimToken;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 32).Value = StatusToString(terminalStatus);
        cmd.Parameters.Add("@inputTokens", SqlDbType.BigInt).Value = (object?)inputTokens ?? DBNull.Value;
        cmd.Parameters.Add("@outputTokens", SqlDbType.BigInt).Value = (object?)outputTokens ?? DBNull.Value;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value = (object?)errorSummary ?? DBNull.Value;
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
        {
            _logger?.LogWarning(
                "CompleteAsync ignored: BD research trigger {TriggerId} no longer owns claim {ClaimToken}.",
                triggerId,
                claimToken);
        }
    }

    public async Task<IReadOnlyList<BdResearchTrigger>> ListRecentAsync(int max, CancellationToken ct)
    {
        if (max <= 0)
        {
            return Array.Empty<BdResearchTrigger>();
        }

        var sql = $@"
SELECT TOP (@max) {AllColumns}
FROM opportunities.BdResearchTriggers
ORDER BY RequestedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<BdResearchTrigger>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    public async Task<bool> HasPendingForOrgAsync(long canonicalOrgId, string providerName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 1
FROM opportunities.BdResearchTriggers
WHERE CanonicalOrgId = @id
  AND ProviderName = @provider
  AND Status IN ('Pending', 'InProgress');";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 64).Value = providerName;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    private static BdResearchTrigger MapReader(SqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        CanonicalOrgId = r.GetInt64(1),
        ProviderName = r.GetString(2),
        Status = ParseStatus(r.GetString(3)),
        RequestedBy = r.GetString(4),
        RequestedAtUtc = r.GetDateTimeOffset(5),
        ClaimedAtUtc = r.IsDBNull(6) ? null : r.GetDateTimeOffset(6),
        ClaimedBy = r.IsDBNull(7) ? null : r.GetString(7),
        ClaimToken = r.IsDBNull(8) ? null : r.GetGuid(8),
        CompletedAtUtc = r.IsDBNull(9) ? null : r.GetDateTimeOffset(9),
        ErrorSummary = r.IsDBNull(10) ? null : r.GetString(10),
        InputTokens = r.IsDBNull(11) ? null : r.GetInt64(11),
        OutputTokens = r.IsDBNull(12) ? null : r.GetInt64(12),
    };

    private static string StatusToString(BdResearchTriggerStatus s) => s switch
    {
        BdResearchTriggerStatus.Pending => "Pending",
        BdResearchTriggerStatus.InProgress => "InProgress",
        BdResearchTriggerStatus.Completed => "Completed",
        BdResearchTriggerStatus.Failed => "Failed",
        BdResearchTriggerStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unknown status."),
    };

    private static BdResearchTriggerStatus ParseStatus(string raw) => raw switch
    {
        "Pending" => BdResearchTriggerStatus.Pending,
        "InProgress" => BdResearchTriggerStatus.InProgress,
        "Completed" => BdResearchTriggerStatus.Completed,
        "Failed" => BdResearchTriggerStatus.Failed,
        "Cancelled" => BdResearchTriggerStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown BD research trigger status '{raw}' on disk."),
    };
}
