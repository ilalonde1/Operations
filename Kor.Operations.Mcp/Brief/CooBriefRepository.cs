using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Brief;

public sealed class CooBriefRepository
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<CooBriefRepository> _logger;

    public CooBriefRepository(IOptions<McpOptions> options, ILogger<CooBriefRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<long> InsertAsync(DateTime weekOf, RichBriefSection section, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO Mcp.CooBrief
    (WeekOf, Section, Headline, Body, Recommendation, InputTokens, OutputTokens, ToolCalls)
OUTPUT INSERTED.Id
VALUES
    (@WeekOf, @Section, @Headline, @Body, @Recommendation, @InputTokens, @OutputTokens, @ToolCalls);";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@WeekOf", weekOf.Date);
        cmd.Parameters.AddWithValue("@Section", section.Section.ToString());
        cmd.Parameters.AddWithValue("@Headline", section.Headline);
        cmd.Parameters.AddWithValue("@Body", section.Body);
        cmd.Parameters.AddWithValue("@Recommendation", (object?)section.Recommendation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InputTokens", section.InputTokens);
        cmd.Parameters.AddWithValue("@OutputTokens", section.OutputTokens);
        cmd.Parameters.AddWithValue("@ToolCalls", section.ToolCalls);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    public Task<IReadOnlyList<BriefRow>> GetLatestWeekAsync(CancellationToken ct)
    {
        const string sql = @"
DECLARE @LatestWeek DATE = (SELECT MAX(WeekOf) FROM Mcp.CooBrief);

SELECT Id, GeneratedAt, WeekOf, Section, Headline, Body, Recommendation, InputTokens, OutputTokens, ToolCalls, AcknowledgedAt, AcknowledgedBy
FROM Mcp.CooBrief
WHERE WeekOf = @LatestWeek
ORDER BY
    CASE Section
        WHEN 'FinancialHealth' THEN 0
        WHEN 'PortfolioHealth' THEN 1
        WHEN 'ClientStrategy' THEN 2
        WHEN 'BdMarket' THEN 3
        WHEN 'OperationsTalent' THEN 4
        WHEN 'WatchItems' THEN 5
        ELSE 99
    END;";

        return QueryBriefsAsync(sql, null, ct);
    }

    public Task<IReadOnlyList<BriefRow>> GetHistoryAsync(int weeks, CancellationToken ct)
    {
        const string sql = @"
WITH Weeks AS
(
    SELECT DISTINCT TOP (@Weeks) WeekOf
    FROM Mcp.CooBrief
    ORDER BY WeekOf DESC
)
SELECT b.Id, b.GeneratedAt, b.WeekOf, b.Section, b.Headline, b.Body, b.Recommendation,
       b.InputTokens, b.OutputTokens, b.ToolCalls, b.AcknowledgedAt, b.AcknowledgedBy
FROM Mcp.CooBrief b
INNER JOIN Weeks w ON b.WeekOf = w.WeekOf
ORDER BY b.WeekOf DESC,
    CASE b.Section
        WHEN 'FinancialHealth' THEN 0
        WHEN 'PortfolioHealth' THEN 1
        WHEN 'ClientStrategy' THEN 2
        WHEN 'BdMarket' THEN 3
        WHEN 'OperationsTalent' THEN 4
        WHEN 'WatchItems' THEN 5
        ELSE 99
    END;";

        return QueryBriefsAsync(sql, cmd => cmd.Parameters.AddWithValue("@Weeks", Math.Max(1, weeks)), ct);
    }

    public async Task AcknowledgeAsync(long id, string acknowledgedBy, CancellationToken ct)
    {
        const string sql = @"
UPDATE Mcp.CooBrief
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

    private async Task<IReadOnlyList<BriefRow>> QueryBriefsAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken ct)
    {
        var rows = new List<BriefRow>();
        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            configure?.Invoke(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadBriefRow(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query COO brief rows.");
        }

        return rows;
    }

    private static BriefRow ReadBriefRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            GeneratedAt: reader.GetDateTime(1),
            WeekOf: reader.GetDateTime(2),
            Section: reader.GetString(3),
            Headline: reader.GetString(4),
            Body: reader.GetString(5),
            Recommendation: reader.IsDBNull(6) ? null : reader.GetString(6),
            InputTokens: reader.GetInt32(7),
            OutputTokens: reader.GetInt32(8),
            ToolCalls: reader.GetInt32(9),
            AcknowledgedAt: reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            AcknowledgedBy: reader.IsDBNull(11) ? null : reader.GetString(11));
}
