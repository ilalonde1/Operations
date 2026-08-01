using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Alerts;

public sealed class AlertRepository
{
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<AlertRepository> _logger;

    public AlertRepository(IOptions<McpOptions> options, ILogger<AlertRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<long> InsertAsync(RichAlert alert, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO Mcp.Alert
    (RuleId, Section, Severity, Subject, Title, Body)
OUTPUT INSERTED.Id
VALUES
    (@RuleId, @Section, @Severity, @Subject, @Title, @Body);";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RuleId", alert.RuleId);
        cmd.Parameters.AddWithValue("@Section", alert.Section.ToString());
        cmd.Parameters.AddWithValue("@Severity", alert.Severity.ToString());
        cmd.Parameters.AddWithValue("@Subject", (object?)alert.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Title", alert.Title);
        cmd.Parameters.AddWithValue("@Body", alert.Body);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    public async Task<bool> HasRecentAsync(string ruleId, string? subject, int windowDays, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) 1
FROM Mcp.Alert
WHERE RuleId = @RuleId
  AND ((Subject IS NULL AND @Subject IS NULL) OR Subject = @Subject)
  AND GeneratedAt >= DATEADD(DAY, -@WindowDays, SYSUTCDATETIME());";

        await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RuleId", ruleId);
        cmd.Parameters.AddWithValue("@Subject", (object?)subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WindowDays", windowDays);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public Task<IReadOnlyList<AlertRow>> GetActiveAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, GeneratedAt, RuleId, Section, Severity, Subject, Title, Body, AcknowledgedAt, AcknowledgedBy
FROM Mcp.Alert
WHERE AcknowledgedAt IS NULL
   OR GeneratedAt >= DATEADD(DAY, -7, SYSUTCDATETIME())
ORDER BY GeneratedAt DESC;";

        return QueryAlertsAsync(sql, null, ct);
    }

    public Task<IReadOnlyList<AlertRow>> GetRecentAsync(int days, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, GeneratedAt, RuleId, Section, Severity, Subject, Title, Body, AcknowledgedAt, AcknowledgedBy
FROM Mcp.Alert
WHERE GeneratedAt >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
ORDER BY GeneratedAt DESC;";

        return QueryAlertsAsync(sql, cmd => cmd.Parameters.AddWithValue("@Days", days), ct);
    }

    public async Task AcknowledgeAsync(long id, string acknowledgedBy, CancellationToken ct)
    {
        const string sql = @"
UPDATE Mcp.Alert
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

    private async Task<IReadOnlyList<AlertRow>> QueryAlertsAsync(
        string sql,
        Action<SqlCommand>? configure,
        CancellationToken ct)
    {
        var rows = new List<AlertRow>();
        try
        {
            await using var conn = new SqlConnection(_options.Value.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            configure?.Invoke(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(ReadAlertRow(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query alert rows.");
        }

        return rows;
    }

    private static AlertRow ReadAlertRow(SqlDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            GeneratedAt: reader.GetDateTime(1),
            RuleId: reader.GetString(2),
            Section: reader.GetString(3),
            Severity: reader.GetString(4),
            Subject: reader.IsDBNull(5) ? null : reader.GetString(5),
            Title: reader.GetString(6),
            Body: reader.GetString(7),
            AcknowledgedAt: reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            AcknowledgedBy: reader.IsDBNull(9) ? null : reader.GetString(9));
}
