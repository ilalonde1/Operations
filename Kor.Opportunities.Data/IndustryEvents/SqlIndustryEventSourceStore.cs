#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.IndustryEvents;

public sealed class SqlIndustryEventSourceStore : IIndustryEventSourceStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns = @"
Id, Name, Organizer, CalendarUrl, SiteUrl, ParserKey, Region, DefaultMarket,
DefaultEventType, KorRelevance, IsActive, CrawlDelaySeconds, LastPolledAtUtc,
LastErrorMessage, LastEventCount";

    private readonly string _connectionString;

    public SqlIndustryEventSourceStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IndustryEventSourceRow> EnsureAsync(IndustryEventSourceSeed seed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM opportunities.IndustryEventSource WHERE CalendarUrl = @calendarUrl)
BEGIN
    INSERT INTO opportunities.IndustryEventSource
        (Name, Organizer, CalendarUrl, SiteUrl, ParserKey, Region, DefaultMarket,
         DefaultEventType, KorRelevance, IsActive, CrawlDelaySeconds)
    VALUES
        (@name, @organizer, @calendarUrl, @siteUrl, @parserKey, @region, @defaultMarket,
         @defaultEventType, @korRelevance, @isActive, @crawlDelaySeconds);
END;

SELECT {AllColumns}
FROM opportunities.IndustryEventSource
WHERE CalendarUrl = @calendarUrl;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddString(cmd, "@name", seed.Name, 200);
        AddString(cmd, "@organizer", seed.Organizer, 300);
        AddString(cmd, "@calendarUrl", seed.CalendarUrl, 800);
        AddString(cmd, "@siteUrl", seed.SiteUrl, 800);
        AddString(cmd, "@parserKey", seed.ParserKey, 60);
        AddString(cmd, "@region", seed.Region, 40);
        AddString(cmd, "@defaultMarket", seed.DefaultMarket, 100);
        AddString(cmd, "@defaultEventType", seed.DefaultEventType, 40);
        AddString(cmd, "@korRelevance", seed.KorRelevance, 1000);
        cmd.Parameters.Add("@isActive", SqlDbType.Bit).Value = seed.IsActive;
        cmd.Parameters.Add("@crawlDelaySeconds", SqlDbType.Int).Value = seed.CrawlDelaySeconds;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"IndustryEventSource row for '{seed.CalendarUrl}' was neither found nor inserted.");
        }

        return MapRow(reader);
    }

    public Task<IReadOnlyList<IndustryEventSourceRow>> ListActiveAsync(CancellationToken ct)
        => ListAsync("WHERE IsActive = 1", ct);

    public Task<IReadOnlyList<IndustryEventSourceRow>> ListAllAsync(CancellationToken ct)
        => ListAsync(string.Empty, ct);

    public async Task UpdateHeartbeatAsync(
        long sourceId,
        int? eventCount,
        string? errorMessage,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.IndustryEventSource
SET LastPolledAtUtc = sysdatetimeoffset(),
    LastEventCount = @eventCount,
    LastErrorMessage = @errorMessage,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = sourceId;
        cmd.Parameters.Add("@eventCount", SqlDbType.Int).Value =
            eventCount.HasValue ? eventCount.Value : DBNull.Value;
        AddString(cmd, "@errorMessage", errorMessage, 1000);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IndustryEventSourceRow>> ListAsync(string whereClause, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.IndustryEventSource
{whereClause}
ORDER BY Name;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<IndustryEventSourceRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapRow(reader));
        }

        return rows;
    }

    private static IndustryEventSourceRow MapRow(SqlDataReader r)
    {
        return new IndustryEventSourceRow(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            r.GetBoolean(10),
            r.GetInt32(11),
            r.IsDBNull(12) ? null : r.GetDateTimeOffset(12),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.IsDBNull(14) ? null : r.GetInt32(14));
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
        parameter.Value = size > 0 && trimmed.Length > size ? trimmed[..size] : trimmed;
    }
}
