#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlBdPersonResearchTriggerStore : IBdPersonResearchTriggerStore
{
    private const int CommandTimeoutSeconds = 15;

    private readonly string _connectionString;
    private readonly ILogger<SqlBdPersonResearchTriggerStore>? _logger;

    public SqlBdPersonResearchTriggerStore(
        string connectionString,
        ILogger<SqlBdPersonResearchTriggerStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<Guid> EnqueueAsync(
        long intelPersonId,
        string providerName,
        string requestedBy,
        CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;
DECLARE @triggerId uniqueidentifier;

BEGIN TRAN;

SELECT TOP (1) @triggerId = Id
FROM opportunities.BdPersonResearchTriggers WITH (UPDLOCK, HOLDLOCK)
WHERE IntelPersonId = @id
  AND ProviderName = @provider
  AND Status IN ('Pending', 'InProgress')
ORDER BY RequestedAtUtc;

IF @triggerId IS NULL
BEGIN
    SET @triggerId = NEWID();
    INSERT INTO opportunities.BdPersonResearchTriggers
        (Id, IntelPersonId, ProviderName, Status, RequestedBy)
    VALUES
        (@triggerId, @id, @provider, 'Pending', @who);
END;

COMMIT TRAN;
SELECT @triggerId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 64).Value = providerName;
        cmd.Parameters.Add("@who", SqlDbType.NVarChar, 150).Value = requestedBy;
        return (Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<BdPersonResearchTrigger?> ClaimNextPendingAsync(string claimedBy, CancellationToken ct)
    {
        const string sql = @"
UPDATE TOP (1) opportunities.BdPersonResearchTriggers
SET Status         = 'InProgress',
    ClaimedAtUtc   = sysdatetimeoffset(),
    ClaimedBy      = @who,
    ClaimToken     = NEWID(),
    ReclaimedCount = CASE WHEN Status = 'InProgress' THEN ReclaimedCount + 1 ELSE ReclaimedCount END
OUTPUT
    inserted.Id, inserted.IntelPersonId, inserted.ProviderName, inserted.Status,
    inserted.RequestedBy, inserted.RequestedAtUtc, inserted.ClaimedAtUtc,
    inserted.ClaimedBy, inserted.ClaimToken, inserted.CompletedAtUtc,
    inserted.ErrorSummary, inserted.InputTokens, inserted.OutputTokens
WHERE Id = (
    SELECT TOP (1) Id
    FROM opportunities.BdPersonResearchTriggers WITH (READPAST, UPDLOCK, ROWLOCK)
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
        if (terminalStatus is BdResearchTriggerStatus.Pending or BdResearchTriggerStatus.InProgress)
        {
            throw new ArgumentException("Terminal status must be Completed/Failed/Cancelled.", nameof(terminalStatus));
        }

        const string sql = @"
UPDATE opportunities.BdPersonResearchTriggers
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
                "CompleteAsync ignored: BD person research trigger {TriggerId} no longer owns claim {ClaimToken}.",
                triggerId,
                claimToken);
        }
    }

    public async Task<bool> HasPendingForPersonAsync(
        long intelPersonId,
        string providerName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 1
FROM opportunities.BdPersonResearchTriggers
WHERE IntelPersonId = @id
  AND ProviderName = @provider
  AND Status IN ('Pending', 'InProgress');";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 64).Value = providerName;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    private static BdPersonResearchTrigger MapReader(SqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        IntelPersonId = r.GetInt64(1),
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

    private static string StatusToString(BdResearchTriggerStatus status) => status switch
    {
        BdResearchTriggerStatus.Completed => "Completed",
        BdResearchTriggerStatus.Failed => "Failed",
        BdResearchTriggerStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown terminal status."),
    };

    private static BdResearchTriggerStatus ParseStatus(string raw) => raw switch
    {
        "Pending" => BdResearchTriggerStatus.Pending,
        "InProgress" => BdResearchTriggerStatus.InProgress,
        "Completed" => BdResearchTriggerStatus.Completed,
        "Failed" => BdResearchTriggerStatus.Failed,
        "Cancelled" => BdResearchTriggerStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown BD person research trigger status '{raw}' on disk."),
    };
}
