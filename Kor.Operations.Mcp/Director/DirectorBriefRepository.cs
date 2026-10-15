using System.Data;
using System.Text.Json;
using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Director;

public sealed class DirectorBriefRepository(IOptions<McpOptions> options)
{
    private const string Columns = "Id, GeneratedAt, WeekOf, Section, Headline, Body, Recommendation, InputTokens, OutputTokens, ToolCalls, AcknowledgedAt, AcknowledgedBy, Citations";
    private const string SectionOrder = "CASE Section WHEN 'ThisWeek' THEN 0 WHEN 'Pursuits' THEN 1 WHEN 'Accounts' THEN 2 WHEN 'Market' THEN 3 WHEN 'Asks' THEN 4 ELSE 99 END";

    public async Task<long> InsertAsync(DateTime weekOf, RichDirectorSection section, CancellationToken ct)
    {
        await using var con = new SqlConnection(options.Value.SqlConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT INTO Mcp.DirectorBrief (WeekOf, Section, Headline, Body, Recommendation, InputTokens, OutputTokens, ToolCalls, Citations)
            OUTPUT INSERTED.Id VALUES (@week, @section, @headline, @body, @recommendation, @input, @output, @calls, @citations);
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@week", SqlDbType.Date).Value = weekOf.Date;
        Text(cmd, "@section", 32, section.Section.ToString()); Text(cmd, "@headline", 512, section.Headline);
        Text(cmd, "@body", -1, section.Body); Text(cmd, "@recommendation", -1, section.Recommendation);
        Text(cmd, "@citations", -1, JsonSerializer.Serialize(section.Citations, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        cmd.Parameters.Add("@input", SqlDbType.Int).Value = section.InputTokens;
        cmd.Parameters.Add("@output", SqlDbType.Int).Value = section.OutputTokens;
        cmd.Parameters.Add("@calls", SqlDbType.Int).Value = section.ToolCalls;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public Task<IReadOnlyList<DirectorBriefRow>> GetLatestWeekAsync(CancellationToken ct) => QueryAsync("""
        WITH Ranked AS (
            SELECT *, ROW_NUMBER() OVER (PARTITION BY Section ORDER BY GeneratedAt DESC, Id DESC) AS rn
            FROM Mcp.DirectorBrief WHERE WeekOf = (SELECT MAX(WeekOf) FROM Mcp.DirectorBrief)
        )
        SELECT
        """ + " " + Columns + " FROM Ranked WHERE rn = 1 ORDER BY " + SectionOrder + ";", _ => { }, ct);

    public Task<IReadOnlyList<DirectorBriefRow>> GetHistoryAsync(int weeks, CancellationToken ct) => QueryAsync(
        "WITH Weeks AS (SELECT DISTINCT TOP (@weeks) WeekOf FROM Mcp.DirectorBrief ORDER BY WeekOf DESC) SELECT " + Columns
        + " FROM Mcp.DirectorBrief WHERE WeekOf IN (SELECT WeekOf FROM Weeks) ORDER BY WeekOf DESC, " + SectionOrder + ", Id DESC;",
        cmd => cmd.Parameters.Add("@weeks", SqlDbType.Int).Value = Math.Clamp(weeks, 1, 52), ct);

    public async Task AcknowledgeAsync(long id, string by, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > 254) throw new ArgumentException("An acknowledgement actor is required.", nameof(by));
        await using var con = new SqlConnection(options.Value.SqlConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE Mcp.DirectorBrief SET AcknowledgedAt = COALESCE(AcknowledgedAt, SYSUTCDATETIME()),
                AcknowledgedBy = COALESCE(AcknowledgedBy, @by) WHERE Id = @id;
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id; Text(cmd, "@by", 254, by);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<DirectorBriefRow>> QueryAsync(string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var con = new SqlConnection(options.Value.SqlConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<DirectorBriefRow>();
        while (await r.ReadAsync(ct)) rows.Add(new(r.GetInt64(0), r.GetDateTime(1), r.GetDateTime(2), r.GetString(3), r.GetString(4), r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
            r.IsDBNull(10) ? null : r.GetDateTime(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetString(12)));
        return rows;
    }
    private static void Text(SqlCommand cmd, string name, int size, string? text) => cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)text ?? DBNull.Value;
}
