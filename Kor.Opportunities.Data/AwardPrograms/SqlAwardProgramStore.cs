#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.AwardPrograms;

public sealed class SqlAwardProgramStore : IAwardProgramStore
{
    private const int CommandTimeoutSeconds = 120;
    private readonly string _connectionString;

    public SqlAwardProgramStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<DateTimeOffset?> GetLastCatalogRefreshUtcAsync(CancellationToken ct)
    {
        const string sql = "SELECT MAX(LastSeenAtUtc) FROM opportunities.AwardProgram WHERE SourceProvider = N'AwardProgramFinder';";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : (DateTimeOffset)value;
    }

    public async Task<int> UpsertAsync(IReadOnlyList<AwardProgramUpsert> programs, CancellationToken ct)
    {
        if (programs.Count == 0)
        {
            return 0;
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            var rows = 0;
            foreach (var program in programs)
            {
                rows += await UpsertOneAsync(con, tx, program, ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return rows;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<AwardProgramRow>> ListUpcomingAsync(int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
    Id, NaturalKey, AwardingBody, ProgramName, CycleYear, Category, Discipline, Region,
    EligibilitySummary, SubmissionDeadline, EntryFee, Url, FirstSeenAtUtc, LastSeenAtUtc
FROM opportunities.AwardProgram
WHERE RetiredAtUtc IS NULL
  AND (SubmissionDeadline IS NULL OR SubmissionDeadline >= CONVERT(date, sysdatetimeoffset()))
ORDER BY CASE WHEN SubmissionDeadline IS NULL THEN 1 ELSE 0 END,
         SubmissionDeadline,
         AwardingBody,
         ProgramName;";

        var rows = new List<AwardProgramRow>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Max(1, take);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new AwardProgramRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetDateTimeOffset(12),
                reader.GetDateTimeOffset(13)));
        }

        return rows;
    }

    private static async Task<int> UpsertOneAsync(SqlConnection con, SqlTransaction tx, AwardProgramUpsert program, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.AwardProgram WITH (HOLDLOCK) AS target
USING (SELECT @naturalKey AS NaturalKey) AS source
ON target.NaturalKey = source.NaturalKey
WHEN MATCHED THEN UPDATE SET
    AwardingBody = @awardingBody,
    ProgramName = @programName,
    CycleYear = @cycleYear,
    Category = @category,
    Discipline = @discipline,
    Region = @region,
    EligibilitySummary = @eligibilitySummary,
    SubmissionDeadline = @submissionDeadline,
    EntryFee = @entryFee,
    Url = @url,
    SourceProvider = @sourceProvider,
    LastSeenAtUtc = sysdatetimeoffset(),
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (NaturalKey, AwardingBody, ProgramName, CycleYear, Category, Discipline, Region,
     EligibilitySummary, SubmissionDeadline, EntryFee, Url, SourceProvider)
VALUES
    (@naturalKey, @awardingBody, @programName, @cycleYear, @category, @discipline, @region,
     @eligibilitySummary, @submissionDeadline, @entryFee, @url, @sourceProvider);";

        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@naturalKey", SqlDbType.NVarChar, 300).Value = program.NaturalKey;
        cmd.Parameters.Add("@awardingBody", SqlDbType.NVarChar, 300).Value = program.AwardingBody;
        cmd.Parameters.Add("@programName", SqlDbType.NVarChar, 300).Value = program.ProgramName;
        cmd.Parameters.Add("@cycleYear", SqlDbType.Int).Value = (object?)program.CycleYear ?? DBNull.Value;
        cmd.Parameters.Add("@category", SqlDbType.NVarChar, 200).Value = Db(program.Category);
        cmd.Parameters.Add("@discipline", SqlDbType.NVarChar, 100).Value = Db(program.Discipline);
        cmd.Parameters.Add("@region", SqlDbType.NVarChar, 50).Value = Db(program.Region);
        cmd.Parameters.Add("@eligibilitySummary", SqlDbType.NVarChar, -1).Value = Db(program.EligibilitySummary);
        cmd.Parameters.Add("@submissionDeadline", SqlDbType.Date).Value =
            program.SubmissionDeadline is { } d ? (object)d.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@entryFee", SqlDbType.NVarChar, 100).Value = Db(program.EntryFee);
        cmd.Parameters.Add("@url", SqlDbType.NVarChar, 1000).Value = Db(program.Url);
        cmd.Parameters.Add("@sourceProvider", SqlDbType.NVarChar, 100).Value = program.SourceProvider;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
