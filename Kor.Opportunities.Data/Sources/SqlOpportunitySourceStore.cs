#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Sources;

public sealed class SqlOpportunitySourceStore : IOpportunitySourceStore
{
    private const int CommandTimeoutSeconds = 15;

    private const string AllSourceColumns = @"
Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
CreatedAtUtc, UpdatedAtUtc, IsHistorical";

    private readonly string _connectionString;

    public SqlOpportunitySourceStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<OpportunitySource>> ListEnabledAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT {AllSourceColumns}
FROM opportunities.OpportunitySources
WHERE IsEnabled = 1
ORDER BY Name;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<OpportunitySource>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapSource(reader));
        }

        return rows;
    }

    public async Task<OpportunitySource?> GetByNameAsync(string name, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllSourceColumns}
FROM opportunities.OpportunitySources
WHERE Name = @name;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapSource(reader) : null;
    }

    public async Task<OpportunitySource> EnsureAsync(OpportunitySource desired, CancellationToken ct)
    {
        // Two-step: try a guarded insert (no clobber), then re-read by name.
        // We don't use MERGE here because the row may already exist with hand-tweaked
        // BaseUrl / CrawlDelaySeconds values that we don't want to overwrite.
        var insertSql = $@"
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = @name)
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds)
    VALUES
        (@name, @type, @url, @enabled, @crawl, @timeout);
END;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(insertSql, con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = desired.Name;
            cmd.Parameters.Add("@type", SqlDbType.Int).Value = (int)desired.SourceType;
            cmd.Parameters.Add("@url", SqlDbType.NVarChar, 2000).Value = desired.BaseUrl;
            cmd.Parameters.Add("@enabled", SqlDbType.Bit).Value = desired.IsEnabled;
            cmd.Parameters.Add("@crawl", SqlDbType.Int).Value = desired.CrawlDelaySeconds;
            cmd.Parameters.Add("@timeout", SqlDbType.Int).Value = desired.RequestTimeoutSeconds;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Re-read so the caller sees the persisted Id + audit columns.
        var sql = $@"
SELECT {AllSourceColumns}
FROM opportunities.OpportunitySources
WHERE Name = @name;";
        await using var readCmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        readCmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = desired.Name;
        await using var reader = await readCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Source '{desired.Name}' not found after EnsureAsync. Schema migration may have failed.");
        }

        return MapSource(reader);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetMappingsAsync(Guid opportunitySourceId, CancellationToken ct)
    {
        const string sql = @"
SELECT [Key], ValueJson
FROM opportunities.OpportunitySourceMappings
WHERE OpportunitySourceId = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = opportunitySourceId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            dict[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return dict;
    }

    public async Task SetMappingAsync(Guid opportunitySourceId, string key, string valueJson, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.OpportunitySourceMappings AS t
USING (SELECT @id AS OpportunitySourceId, @key AS [Key]) AS s
    ON t.OpportunitySourceId = s.OpportunitySourceId AND t.[Key] = s.[Key]
WHEN MATCHED THEN
    UPDATE SET ValueJson = @json, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson) VALUES (@id, @key, @json);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = opportunitySourceId;
        cmd.Parameters.Add("@key", SqlDbType.VarChar, 64).Value = key;
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = valueJson;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task EnsureMappingsAsync(
        Guid opportunitySourceId,
        IReadOnlyDictionary<string, string> mappings,
        CancellationToken ct)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
               WHERE OpportunitySourceId = @id AND [Key] = @key)
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson)
    VALUES (@id, @key, @value);
END;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        foreach (var mapping in mappings)
        {
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = opportunitySourceId;
            cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = mapping.Key;
            cmd.Parameters.Add("@value", SqlDbType.NVarChar, -1).Value = mapping.Value;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static OpportunitySource MapSource(SqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Name = r.GetString(1),
        SourceType = (OpportunitySourceType)r.GetInt32(2),
        BaseUrl = r.GetString(3),
        IsEnabled = r.GetBoolean(4),
        CrawlDelaySeconds = r.GetInt32(5),
        RequestTimeoutSeconds = r.GetInt32(6),
        CreatedAtUtc = r.GetDateTimeOffset(7),
        UpdatedAtUtc = r.GetDateTimeOffset(8),
        IsHistorical = r.GetBoolean(9),
    };
}
