#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.IndustryEvents;

public sealed class SqlIndustryEventStore : IIndustryEventStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns = @"
Id, SourceKey, Name, Organizer, EventType, StartDate, EndDate, Recurrence, City,
Market, Format, SectorsThemes, Audience, TargetsPresent, RegistrationUrl,
CostNote, KorRelevance, SourceNote, RetiredAtUtc, RetiredReason, CreatedAtUtc,
UpdatedAtUtc, IndustryEventSourceId, LastSeenAtUtc";

    private readonly string _connectionString;

    public SqlIndustryEventStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task UpsertAsync(IndustryEventRecord record, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IndustryEvents WITH (HOLDLOCK) AS target
USING (SELECT @sourceKey AS SourceKey) AS source
    ON target.SourceKey = source.SourceKey
WHEN MATCHED THEN
    UPDATE SET
        Name = @name,
        Organizer = @organizer,
        EventType = @eventType,
        StartDate = @startDate,
        EndDate = @endDate,
        Recurrence = @recurrence,
        City = @city,
        Market = @market,
        Format = @format,
        SectorsThemes = @sectorsThemes,
        Audience = @audience,
        TargetsPresent = @targetsPresent,
        RegistrationUrl = @registrationUrl,
        CostNote = @costNote,
        KorRelevance = @korRelevance,
        SourceNote = @sourceNote,
        -- An ingested row claims provenance; a hand-curated upsert leaves it alone.
        IndustryEventSourceId = COALESCE(@industryEventSourceId, target.IndustryEventSourceId),
        LastSeenAtUtc = CASE
            WHEN @industryEventSourceId IS NULL THEN target.LastSeenAtUtc
            ELSE sysdatetimeoffset()
        END,
        UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT
        (SourceKey, Name, Organizer, EventType, StartDate, EndDate, Recurrence,
         City, Market, Format, SectorsThemes, Audience, TargetsPresent,
         RegistrationUrl, CostNote, KorRelevance, SourceNote,
         IndustryEventSourceId, LastSeenAtUtc)
    VALUES
        (@sourceKey, @name, @organizer, @eventType, @startDate, @endDate, @recurrence,
         @city, @market, @format, @sectorsThemes, @audience, @targetsPresent,
         @registrationUrl, @costNote, @korRelevance, @sourceNote,
         @industryEventSourceId,
         CASE WHEN @industryEventSourceId IS NULL THEN NULL ELSE sysdatetimeoffset() END);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddParams(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndustryEventRow>> ListUpcomingAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.IndustryEvents
WHERE RetiredAtUtc IS NULL
  AND (EndDate IS NULL OR EndDate >= CAST(SYSDATETIMEOFFSET() AS date))
ORDER BY
    CASE WHEN StartDate IS NULL THEN 1 ELSE 0 END,
    StartDate,
    Name;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<IndustryEventRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapRow(reader));
        }

        return rows;
    }

    private static IndustryEventRow MapRow(SqlDataReader r)
    {
        return new IndustryEventRow(
            r.GetInt64(0),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : DateOnly.FromDateTime(r.GetDateTime(5)),
            r.IsDBNull(6) ? null : DateOnly.FromDateTime(r.GetDateTime(6)),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetString(12),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.IsDBNull(14) ? null : r.GetString(14),
            r.IsDBNull(15) ? null : r.GetString(15),
            r.IsDBNull(16) ? null : r.GetString(16),
            r.IsDBNull(17) ? null : r.GetString(17),
            r.IsDBNull(18) ? null : r.GetDateTimeOffset(18),
            r.IsDBNull(19) ? null : r.GetString(19),
            r.GetDateTimeOffset(20),
            r.GetDateTimeOffset(21),
            r.IsDBNull(22) ? null : r.GetInt64(22),
            r.IsDBNull(23) ? null : r.GetDateTimeOffset(23));
    }

    private static void AddParams(SqlCommand cmd, IndustryEventRecord record)
    {
        AddString(cmd, "@sourceKey", record.SourceKey, 64);
        AddString(cmd, "@name", record.Name, 300);
        AddString(cmd, "@organizer", record.Organizer, 300);
        AddString(cmd, "@eventType", record.EventType, 40);
        AddDate(cmd, "@startDate", record.StartDate);
        AddDate(cmd, "@endDate", record.EndDate);
        AddString(cmd, "@recurrence", record.Recurrence, 200);
        AddString(cmd, "@city", record.City, 200);
        AddString(cmd, "@market", record.Market, 100);
        AddString(cmd, "@format", record.Format, 300);
        AddString(cmd, "@sectorsThemes", record.SectorsThemes, -1);
        AddString(cmd, "@audience", record.Audience, 500);
        AddString(cmd, "@targetsPresent", record.TargetsPresent, -1);
        AddString(cmd, "@registrationUrl", record.RegistrationUrl, 1000);
        AddString(cmd, "@costNote", record.CostNote, 300);
        AddString(cmd, "@korRelevance", record.KorRelevance, 1000);
        AddString(cmd, "@sourceNote", record.SourceNote, 500);
        cmd.Parameters.Add("@industryEventSourceId", SqlDbType.BigInt).Value =
            record.IndustryEventSourceId.HasValue
                ? record.IndustryEventSourceId.Value
                : DBNull.Value;
    }

    private static void AddString(SqlCommand cmd, string name, string? value, int size)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        if (string.IsNullOrWhiteSpace(value))
        {
            parameter.Value = DBNull.Value;
            return;
        }

        var trimmed = value.Trim();
        parameter.Value = size > 0 && trimmed.Length > size
            ? trimmed[..size]
            : trimmed;
    }

    private static void AddDate(SqlCommand cmd, string name, DateOnly? value)
    {
        cmd.Parameters.Add(name, SqlDbType.Date).Value = value.HasValue
            ? (object)value.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;
    }
}
