#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlCanonicalOrgStore : ICanonicalOrgStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlCanonicalOrgStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<long> UpsertCanonicalOrgAsync(
        string kind,
        string displayName,
        string? clendorClientId,
        string? website,
        string? notes,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.CanonicalOrg AS t
USING (SELECT @clendor AS ClendorClientId, @name AS DisplayName) AS s
   ON (@clendor IS NOT NULL AND t.ClendorClientId = @clendor)
WHEN MATCHED THEN UPDATE SET
    Kind = COALESCE(@kind, t.Kind),
    DisplayName = @name,
    Website = COALESCE(@website, t.Website),
    Notes = COALESCE(@notes, t.Notes),
    UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (Kind, DisplayName, ClendorClientId, Website, Notes)
    VALUES (@kind, @name, @clendor, @website, @notes)
OUTPUT INSERTED.Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 40).Value = kind;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = displayName;
        cmd.Parameters.Add("@clendor", SqlDbType.VarChar, 32).Value = (object?)clendorClientId ?? DBNull.Value;
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = (object?)website ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)notes ?? DBNull.Value;

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(v);
    }

    public async Task<CanonicalOrgRow?> GetCanonicalOrgAsync(long id, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, Kind, DisplayName, ClendorClientId, Website, Notes, CreatedAtUtc, UpdatedAtUtc
FROM   opportunities.CanonicalOrg
WHERE  Id = @id;";
        return await ReadSingleOrgAsync(sql, new[] { ("@id", (object)id, SqlDbType.BigInt, 0) }, ct)
            .ConfigureAwait(false);
    }

    public async Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id, Kind, DisplayName, ClendorClientId, Website, Notes, CreatedAtUtc, UpdatedAtUtc
FROM   opportunities.CanonicalOrg
WHERE  ClendorClientId = @cl;";
        return await ReadSingleOrgAsync(sql, new[] { ("@cl", (object)clendorClientId, SqlDbType.VarChar, 32) }, ct)
            .ConfigureAwait(false);
    }

    public async Task RecordBcRegistrySnapshotAsync(long canonicalOrgId, BcRegistrySnapshot s, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.CanonicalOrg
SET    BcRegistryTopicId           = @topic,
       BcRegistryLegalName         = @legal,
       BcRegistryEntityType        = @entity,
       BcRegistryStatus            = @status,
       BcRegistryIncorporationDate = @incorp,
       BcRegistryJurisdiction      = @juris,
       BcRegistryBusinessNumber    = @bn,
       BcRegistryRegisteredOffice  = @office,
       BcRegistryLastCheckedAtUtc  = sysdatetimeoffset(),
       UpdatedAtUtc                = sysdatetimeoffset()
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@topic", SqlDbType.NVarChar, 50).Value = (object?)s.TopicId ?? DBNull.Value;
        cmd.Parameters.Add("@legal", SqlDbType.NVarChar, 300).Value = (object?)s.LegalName ?? DBNull.Value;
        cmd.Parameters.Add("@entity", SqlDbType.NVarChar, 50).Value = (object?)s.EntityType ?? DBNull.Value;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 40).Value = (object?)s.Status ?? DBNull.Value;
        cmd.Parameters.Add("@incorp", SqlDbType.Date).Value = s.IncorporationDate.HasValue
            ? (object)s.IncorporationDate.Value
            : DBNull.Value;
        cmd.Parameters.Add("@juris", SqlDbType.NVarChar, 50).Value = (object?)s.Jurisdiction ?? DBNull.Value;
        cmd.Parameters.Add("@bn", SqlDbType.NVarChar, 20).Value = (object?)s.BusinessNumber ?? DBNull.Value;
        cmd.Parameters.Add("@office", SqlDbType.NVarChar, 500).Value = (object?)s.RegisteredOffice ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<(string DisplayName, string Kind)?> GetNameAndKindAsync(long canonicalOrgId, CancellationToken ct)
    {
        const string sql = "SELECT DisplayName, Kind FROM opportunities.CanonicalOrg WHERE Id = @id;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (r.GetString(0), r.GetString(1));
    }

    public async Task<long?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(normalizedName)) return null;

        const string sql = @"
SELECT TOP 1 Id FROM opportunities.CanonicalOrg
WHERE NormalizedName = @norm
ORDER BY
    CASE WHEN ClendorClientId IS NOT NULL THEN 0 ELSE 1 END,
    Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@norm", SqlDbType.NVarChar, 300).Value = normalizedName;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (v is null || v is DBNull) return null;
        return Convert.ToInt64(v);
    }

    public async Task<long> UpsertAliasAsync(
        string rawName,
        string source,
        long? canonicalOrgId,
        int confidence,
        string? classifiedBy,
        string? notes,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.OrgAlias AS t
USING (SELECT @raw AS RawName, @src AS Source) AS s
   ON t.RawName = s.RawName AND t.Source = s.Source
WHEN MATCHED THEN UPDATE SET
    CanonicalOrgId = COALESCE(@canon, t.CanonicalOrgId),
    Confidence = CASE WHEN @canon IS NOT NULL THEN @conf ELSE t.Confidence END,
    ClassifiedBy = COALESCE(@by, t.ClassifiedBy),
    ClassifiedAtUtc = CASE WHEN @canon IS NOT NULL THEN sysdatetimeoffset() ELSE t.ClassifiedAtUtc END,
    Notes = COALESCE(@notes, t.Notes)
WHEN NOT MATCHED THEN INSERT
    (RawName, Source, CanonicalOrgId, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes)
    VALUES (@raw, @src, @canon, @conf, @by,
            CASE WHEN @canon IS NOT NULL THEN sysdatetimeoffset() ELSE NULL END,
            @notes)
OUTPUT INSERTED.Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, 300).Value = rawName;
        cmd.Parameters.Add("@src", SqlDbType.NVarChar, 80).Value = source;
        cmd.Parameters.Add("@canon", SqlDbType.BigInt).Value = canonicalOrgId.HasValue
            ? (object)canonicalOrgId.Value
            : DBNull.Value;
        cmd.Parameters.Add("@conf", SqlDbType.Int).Value = confidence;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 50).Value = (object?)classifiedBy ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)notes ?? DBNull.Value;

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(v);
    }

    public async Task<OrgAliasRow?> LookupAliasAsync(string rawName, string source, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id, CanonicalOrgId, RawName, Source, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes, CreatedAtUtc
FROM   opportunities.OrgAlias
WHERE  RawName = @raw AND Source = @src;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, 300).Value = rawName;
        cmd.Parameters.Add("@src", SqlDbType.NVarChar, 80).Value = source;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return MapAlias(r);
    }

    public async Task<IReadOnlyList<OrgAliasRow>> ListUnclassifiedAsync(
        string? source,
        int batchSize,
        CancellationToken ct)
    {
        var sql = @"
SELECT TOP (@batch) Id, CanonicalOrgId, RawName, Source, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes, CreatedAtUtc
FROM   opportunities.OrgAlias
WHERE  CanonicalOrgId IS NULL "
            + (string.IsNullOrWhiteSpace(source) ? "" : " AND Source = @src ")
            + " ORDER BY CreatedAtUtc;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;
        if (!string.IsNullOrWhiteSpace(source))
            cmd.Parameters.Add("@src", SqlDbType.NVarChar, 80).Value = source;

        var list = new List<OrgAliasRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapAlias(r));
        }

        return list;
    }

    public async Task<(int Total, int Classified, int Unclassified)> GetAliasCountsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN CanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END) AS Classified,
    SUM(CASE WHEN CanonicalOrgId IS NULL THEN 1 ELSE 0 END) AS Unclassified
FROM opportunities.OrgAlias;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return (0, 0, 0);
        return (
            r.IsDBNull(0) ? 0 : r.GetInt32(0),
            r.IsDBNull(1) ? 0 : r.GetInt32(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2));
    }

    private async Task<CanonicalOrgRow?> ReadSingleOrgAsync(
        string sql,
        IReadOnlyList<(string Name, object Value, SqlDbType Type, int Size)> args,
        CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        foreach (var (name, value, type, size) in args)
        {
            if (size > 0)
            {
                cmd.Parameters.Add(name, type, size).Value = value;
            }
            else
            {
                cmd.Parameters.Add(name, type).Value = value;
            }
        }

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new CanonicalOrgRow(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetDateTimeOffset(6),
            r.GetDateTimeOffset(7));
    }

    private static OrgAliasRow MapAlias(SqlDataReader r)
        => new(
            r.GetInt64(0),
            r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
            r.GetString(2),
            r.GetString(3),
            r.GetInt32(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? (DateTimeOffset?)null : r.GetDateTimeOffset(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.GetDateTimeOffset(8));
}
