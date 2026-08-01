using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.CooCard;

public sealed class CooCardRepository
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<CooCardRepository> _logger;

    public CooCardRepository(IOptions<McpOptions> options, ILogger<CooCardRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Replaces any existing rows for the week with the freshly-generated 5
    /// items. Atomic via a single transaction so a partial regenerate can't
    /// leave the table in a state where the WPF tile shows mixed-week data.
    /// </summary>
    public async Task ReplaceWeekAsync(DateTime weekOf, IReadOnlyList<RichCooCardItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return;
        }

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using (var del = new SqlCommand("DELETE FROM Mcp.CooCardItem WHERE WeekOf = @WeekOf;", conn, tx))
            {
                del.Parameters.AddWithValue("@WeekOf", weekOf.Date);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            const string insertSql = @"
INSERT INTO Mcp.CooCardItem
    (WeekOf, Rank, Severity, Headline, Body, Recommendation, SourceTags)
VALUES
    (@WeekOf, @Rank, @Severity, @Headline, @Body, @Recommendation, @SourceTags);";

            foreach (var item in items)
            {
                await using var ins = new SqlCommand(insertSql, conn, tx);
                ins.Parameters.AddWithValue("@WeekOf", weekOf.Date);
                ins.Parameters.AddWithValue("@Rank", item.Rank);
                ins.Parameters.AddWithValue("@Severity", item.Severity);
                ins.Parameters.AddWithValue("@Headline", item.Headline);
                ins.Parameters.AddWithValue("@Body", item.Body);
                ins.Parameters.AddWithValue("@Recommendation", item.Recommendation ?? string.Empty);
                ins.Parameters.AddWithValue("@SourceTags", (object?)item.SourceTags ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try { await tx.RollbackAsync(ct).ConfigureAwait(false); } catch { /* best effort */ }
            throw;
        }
    }

    public Task<IReadOnlyList<CooCardItemRow>> GetLatestWeekAsync(CancellationToken ct)
    {
        const string sql = @"
DECLARE @LatestWeek DATE = (SELECT MAX(WeekOf) FROM Mcp.CooCardItem);

SELECT Id, GeneratedAt, WeekOf, Rank, Severity, Headline, Body, Recommendation, SourceTags, AcknowledgedAt, AcknowledgedBy
FROM Mcp.CooCardItem
WHERE WeekOf = @LatestWeek
ORDER BY Rank ASC;";

        return QueryItemsAsync(sql, null, ct);
    }

    public async Task AcknowledgeAsync(long id, string acknowledgedBy, CancellationToken ct)
    {
        const string sql = @"
UPDATE Mcp.CooCardItem
SET AcknowledgedAt = COALESCE(AcknowledgedAt, SYSUTCDATETIME()),
    AcknowledgedBy = COALESCE(AcknowledgedBy, @AcknowledgedBy)
WHERE Id = @Id;";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@AcknowledgedBy", acknowledgedBy);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CooCardItemRow>> QueryItemsAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken ct)
    {
        var rows = new List<CooCardItemRow>();
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
            _logger.LogWarning(ex, "Failed to query COO Card rows.");
        }

        return rows;
    }

    private static CooCardItemRow ReadRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            GeneratedAt: reader.GetDateTime(1),
            WeekOf: reader.GetDateTime(2),
            Rank: reader.GetInt32(3),
            Severity: reader.GetString(4),
            Headline: reader.GetString(5),
            Body: reader.GetString(6),
            Recommendation: reader.GetString(7),
            SourceTags: reader.IsDBNull(8) ? null : reader.GetString(8),
            AcknowledgedAt: reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            AcknowledgedBy: reader.IsDBNull(10) ? null : reader.GetString(10));
}
