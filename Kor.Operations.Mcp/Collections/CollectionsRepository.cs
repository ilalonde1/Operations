using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Collections;

public sealed class CollectionsRepository
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<CollectionsRepository> _logger;

    public CollectionsRepository(IOptions<McpOptions> options, ILogger<CollectionsRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<CollectionsCaseRow>> GetAllAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ClientID, Status, OpenedAt, OpenedBy, LastUpdatedAt, LastUpdatedBy, ResolvedAt, LegalAmount, Notes
FROM Mcp.CollectionsCase
ORDER BY OpenedAt DESC;";

        return QueryCasesAsync(sql, null, ct);
    }

    public Task<IReadOnlyList<CollectionsCaseRow>> GetActiveAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ClientID, Status, OpenedAt, OpenedBy, LastUpdatedAt, LastUpdatedBy, ResolvedAt, LegalAmount, Notes
FROM Mcp.CollectionsCase
WHERE Status <> N'Resolved'
ORDER BY OpenedAt DESC;";

        return QueryCasesAsync(sql, null, ct);
    }

    public async Task<CollectionsCaseRow?> GetActiveByClientAsync(string clientId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ClientID, Status, OpenedAt, OpenedBy, LastUpdatedAt, LastUpdatedBy, ResolvedAt, LegalAmount, Notes
FROM Mcp.CollectionsCase
WHERE ClientID = @ClientID
  AND Status <> N'Resolved';";

        var rows = await QueryCasesAsync(
            sql,
            cmd => cmd.Parameters.AddWithValue("@ClientID", clientId),
            ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<long> InsertAsync(
        string clientId,
        CollectionsCaseStatus status,
        decimal? legalAmount,
        string? notes,
        string openedBy,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO Mcp.CollectionsCase
    (ClientID, Status, OpenedBy, LastUpdatedBy, LegalAmount, Notes)
OUTPUT INSERTED.Id
VALUES
    (@ClientID, @Status, @OpenedBy, @OpenedBy, @LegalAmount, @Notes);";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ClientID", clientId);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@OpenedBy", openedBy);
        cmd.Parameters.AddWithValue("@LegalAmount", (object?)legalAmount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    public async Task UpdateAsync(
        long id,
        CollectionsCaseStatus status,
        decimal? legalAmount,
        string? notes,
        string updatedBy,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE Mcp.CollectionsCase
SET Status = @Status,
    LegalAmount = @LegalAmount,
    Notes = @Notes,
    LastUpdatedAt = SYSUTCDATETIME(),
    LastUpdatedBy = @UpdatedBy,
    ResolvedAt = CASE
        WHEN @Status = N'Resolved' THEN COALESCE(ResolvedAt, SYSUTCDATETIME())
        ELSE NULL
    END
WHERE Id = @Id;";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@LegalAmount", (object?)legalAmount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CollectionsCaseRow>> QueryCasesAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken ct)
    {
        var rows = new List<CollectionsCaseRow>();
        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            configure?.Invoke(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadRow(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query collections cases.");
        }

        return rows;
    }

    private static CollectionsCaseRow ReadRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            ClientID: reader.GetString(1),
            Status: reader.GetString(2),
            OpenedAt: reader.GetDateTime(3),
            OpenedBy: reader.GetString(4),
            LastUpdatedAt: reader.GetDateTime(5),
            LastUpdatedBy: reader.GetString(6),
            ResolvedAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            LegalAmount: reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            Notes: reader.IsDBNull(9) ? null : reader.GetString(9));
}
