using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

public sealed class SqlDirectorBriefStore(string connectionString) : IDirectorBriefStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Columns = "Id, WeekOf, Section, Headline, Body, Recommendation, Citations, WrittenBy, ImportedAtUtc, AcknowledgedAtUtc, AcknowledgedBy";
    private const string SectionOrder = "CASE Section WHEN N'ThisWeek' THEN 0 WHEN N'Pursuits' THEN 1 WHEN N'Accounts' THEN 2 WHEN N'Market' THEN 3 WHEN N'Asks' THEN 4 ELSE 99 END";

    public Task<IReadOnlyList<DirectorBriefRow>> GetLatestAsync(CancellationToken ct = default) => QueryAsync("""
        WITH Ranked AS (
            SELECT *, ROW_NUMBER() OVER (PARTITION BY Section ORDER BY ImportedAtUtc DESC, Id DESC) AS rn
            FROM opportunities.DirectorBrief WHERE WeekOf = (SELECT MAX(WeekOf) FROM opportunities.DirectorBrief)
        )
        SELECT
        """ + " " + Columns + " FROM Ranked WHERE rn = 1 ORDER BY " + SectionOrder + ";", _ => { }, ct);

    public Task<IReadOnlyList<DirectorBriefRow>> GetHistoryAsync(int weeks, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeks);
        return QueryAsync("WITH Weeks AS (SELECT DISTINCT TOP (@weeks) WeekOf FROM opportunities.DirectorBrief ORDER BY WeekOf DESC) SELECT "
            + Columns + " FROM opportunities.DirectorBrief WHERE WeekOf IN (SELECT WeekOf FROM Weeks) ORDER BY WeekOf DESC, ImportedAtUtc DESC, Id DESC;",
            cmd => cmd.Parameters.Add("@weeks", SqlDbType.Int).Value = weeks, ct);
    }

    public async Task AcknowledgeAsync(long id, string by, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > 100) throw new ArgumentException("An acknowledgement actor of 1–100 characters is required.", nameof(by));
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE opportunities.DirectorBrief SET AcknowledgedAtUtc = sysdatetimeoffset(), AcknowledgedBy = @by
            WHERE Id = @id AND AcknowledgedAtUtc IS NULL;
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        Text(cmd, "@by", 100, by);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(int SectionsWritten, IReadOnlyList<string> Rejected)> ImportAsync(DirectorBriefFile file,
        string exportSha256, string fileName, string fileSha256, string by, bool force, CancellationToken ct = default)
    {
        var errors = file.ValidateShape().ToList();
        if (string.IsNullOrWhiteSpace(by) || by.Length > 100) errors.Add("Imported by must contain 1–100 characters.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260) errors.Add("File name must contain 1–260 characters.");
        if (!IsHash(exportSha256) || !IsHash(fileSha256)) errors.Add("Export and file SHA-256 hashes must contain 64 hexadecimal characters.");
        if (errors.Count != 0) return (0, errors);

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var check = new SqlCommand("SELECT TOP (1) Id FROM opportunities.DirectorBriefImport WITH (UPDLOCK, HOLDLOCK) WHERE FileSha256 = @hash;", con, tx) { CommandTimeout = 30 })
        {
            check.Parameters.Add("@hash", SqlDbType.Char, 64).Value = fileSha256.ToUpperInvariant();
            var existing = await check.ExecuteScalarAsync(ct);
            if (!force && existing is not null)
            {
                await tx.CommitAsync(ct);
                return (0, Array.Empty<string>());
            }
        }

        // Hold claims and source titles until commit: a retirement between CLI validation and
        // this read rejects the entire file; a later retirement waits until the snapshots commit.
        var citations = new Dictionary<long, DirectorCitation>();
        var retiredAt = new Dictionary<long, DateTimeOffset?>();
        foreach (var id in file.Sections.SelectMany(s => s.Citations).Distinct().Order())
        {
            await using var read = new SqlCommand("""
                SELECT c.PassageId, c.Statement, s.Title, c.RetiredAtUtc
                FROM knowledge.Claim c WITH (UPDLOCK, HOLDLOCK)
                JOIN knowledge.Source s WITH (HOLDLOCK) ON s.Id = c.SourceId
                WHERE c.Id = @id;
                """, con, tx) { CommandTimeout = 30 };
            read.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            await using var r = await read.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                retiredAt.Add(id, r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3));
                if (r.IsDBNull(3)) citations.Add(id, new(id, r.GetInt64(0), r.GetString(1), r.GetString(2)));
            }
        }
        errors.AddRange(file.ValidateCurrentCitations(retiredAt));
        if (errors.Count != 0) { await tx.RollbackAsync(ct); return (0, errors); }

        var importedAt = DateTimeOffset.UtcNow;
        foreach (var section in file.Sections)
        {
            await using var insert = new SqlCommand("""
                INSERT INTO opportunities.DirectorBrief
                    (WeekOf, Section, Headline, Body, Recommendation, Citations, WrittenBy, ImportedAtUtc, ImportedBy)
                VALUES (@week, @section, @headline, @body, @recommendation, @citations, @writtenBy, @at, @by);
                """, con, tx) { CommandTimeout = 30 };
            insert.Parameters.Add("@week", SqlDbType.Date).Value = file.WeekOf.ToDateTime(TimeOnly.MinValue);
            Text(insert, "@section", 32, section.Section); Text(insert, "@headline", 512, section.Headline);
            Text(insert, "@body", -1, section.Body); Text(insert, "@recommendation", -1, section.Recommendation);
            Text(insert, "@citations", -1, JsonSerializer.Serialize(section.Citations.Distinct().Select(id => citations[id]), JsonOptions));
            Text(insert, "@writtenBy", 100, file.WrittenBy); Text(insert, "@by", 100, by);
            insert.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = importedAt;
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var ledger = new SqlCommand("""
            INSERT INTO opportunities.DirectorBriefImport
                (WeekOf, FileName, FileSha256, ExportSha256, ImportedAtUtc, ImportedBy, SectionsWritten, Forced)
            VALUES (@week, @file, @hash, @export, @at, @by, @count, @force);
            """, con, tx) { CommandTimeout = 30 })
        {
            ledger.Parameters.Add("@week", SqlDbType.Date).Value = file.WeekOf.ToDateTime(TimeOnly.MinValue);
            Text(ledger, "@file", 260, fileName); Text(ledger, "@by", 100, by);
            ledger.Parameters.Add("@hash", SqlDbType.Char, 64).Value = fileSha256.ToUpperInvariant();
            ledger.Parameters.Add("@export", SqlDbType.Char, 64).Value = exportSha256.ToUpperInvariant();
            ledger.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = importedAt;
            ledger.Parameters.Add("@count", SqlDbType.Int).Value = file.Sections.Count;
            ledger.Parameters.Add("@force", SqlDbType.Bit).Value = force;
            await ledger.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (file.Sections.Count, Array.Empty<string>());
    }

    private async Task<IReadOnlyList<DirectorBriefRow>> QueryAsync(string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<DirectorBriefRow>();
        while (await r.ReadAsync(ct)) rows.Add(new(r.GetInt64(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetString(2), r.GetString(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), JsonSerializer.Deserialize<DirectorCitation[]>(r.GetString(6), JsonOptions) ?? Array.Empty<DirectorCitation>(),
            r.GetString(7), r.GetFieldValue<DateTimeOffset>(8), r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetString(10)));
        return rows;
    }
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void Text(SqlCommand cmd, string name, int size, string? value) => cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;
}
