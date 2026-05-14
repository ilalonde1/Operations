#nullable enable
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Mcp.Smoke.Audit;

internal sealed class AuditQuery
{
    private readonly string _connectionString;

    public AuditQuery(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<AuditRow>> RowsForUserBetweenAsync(string userUpn, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        const string sql = @"
SELECT OccurredAt, ToolName, InputJson, ResultStatus, DurationMs
FROM Mcp.AuditLog
WHERE UserUpn = @UserUpn
  AND OccurredAt BETWEEN @StartUtc AND @EndUtc
ORDER BY OccurredAt ASC;";

        var rows = new List<AuditRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserUpn", userUpn);
        cmd.Parameters.AddWithValue("@StartUtc", startUtc.AddSeconds(-2));
        cmd.Parameters.AddWithValue("@EndUtc", endUtc.AddSeconds(2));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var toolName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!toolName.StartsWith("get_", StringComparison.Ordinal) && !string.Equals(toolName, "query_kor_data", StringComparison.Ordinal))
                continue;

            rows.Add(new AuditRow(
                reader.GetDateTime(0),
                toolName,
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4)));
        }
        return rows;
    }
}

internal sealed record AuditRow(
    DateTime OccurredAt,
    string ToolName,
    string InputJson,
    string ResultStatus,
    int DurationMs);
