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
SET XACT_ABORT ON;

DECLARE @existingId bigint;
DECLARE @normalizedName nvarchar(300) = CAST(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    @name,
    ' ',''), '.',''), ',',''), '''',''), '-',''), '&',''), '/',''), '(',''), ')',''), '+',''))
    AS nvarchar(300));

BEGIN TRAN;

SELECT TOP (1) @existingId = Id
FROM opportunities.CanonicalOrg WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
WHERE (@clendor IS NOT NULL AND ClendorClientId = @clendor)
   OR (@clendor IS NULL AND ClendorClientId IS NULL AND NormalizedName = @normalizedName)
ORDER BY CASE WHEN ClendorClientId IS NOT NULL THEN 0 ELSE 1 END, Id;

IF @existingId IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrg
        (Kind, DisplayName, ClendorClientId, Website, Notes)
    VALUES
        (@kind, @name, @clendor, @website, @notes);

    SET @existingId = CONVERT(bigint, SCOPE_IDENTITY());
END
ELSE
BEGIN
    UPDATE opportunities.CanonicalOrg
    SET Kind = CASE
            WHEN @kind IS NULL THEN Kind
            WHEN CASE @kind
                    WHEN 'KorClient' THEN 0
                    WHEN 'KorStructural' THEN 0
                    WHEN 'Competitor' THEN 1
                    WHEN 'Developer' THEN 2
                    WHEN 'Architect' THEN 3
                    WHEN 'GC' THEN 4
                    WHEN 'Subcontractor' THEN 5
                    WHEN 'Buyer' THEN 6
                    WHEN 'Vendor' THEN 7
                    ELSE 8
                 END
                 < CASE Kind
                    WHEN 'KorClient' THEN 0
                    WHEN 'KorStructural' THEN 0
                    WHEN 'Competitor' THEN 1
                    WHEN 'Developer' THEN 2
                    WHEN 'Architect' THEN 3
                    WHEN 'GC' THEN 4
                    WHEN 'Subcontractor' THEN 5
                    WHEN 'Buyer' THEN 6
                    WHEN 'Vendor' THEN 7
                    ELSE 8
                   END
            THEN @kind
            ELSE Kind
        END,
        DisplayName = @name,
        Website = COALESCE(@website, Website),
        Notes = CASE
            WHEN RetiredAtUtc IS NOT NULL THEN
                COALESCE(COALESCE(@notes, Notes) + NCHAR(13) + NCHAR(10), N'')
                     + N'[Auto-resurrected by GetOrCreate match on ' + CONVERT(nvarchar(33), sysdatetimeoffset(), 127)
                     + N'; was retired: ' + COALESCE(RetiredReason, N'(no reason)') + N']'
            ELSE COALESCE(@notes, Notes)
        END,
        -- BD-Audit-2026-06-09 C4/M8: a matched-but-retired org must resurrect,
        -- not absorb fresh data invisibly. Direct GetOrCreate callers (Deltek
        -- pursuit sync, --ingest-canonical, custom-proposal import) bypass
        -- CanonicalOrgResolver's UnretireAsync, so the resurrect lives here too.
        RetiredAtUtc = NULL,
        RetiredReason = NULL,
        UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @existingId;
END;

COMMIT TRAN;

SELECT @existingId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 40).Value = (object?)kind ?? DBNull.Value;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = displayName;
        cmd.Parameters.Add("@clendor", SqlDbType.VarChar, 32).Value = (object?)clendorClientId ?? DBNull.Value;
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = (object?)website ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)notes ?? DBNull.Value;

        try
        {
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(v);
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            if (clendorClientId is not null)
            {
                var existing = await GetCanonicalOrgByClendorIdAsync(clendorClientId, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    return existing.Id;
                }
            }
            else
            {
                var id = await FindByNormalizedNameAsync(NormalizeName(displayName), ct).ConfigureAwait(false);
                if (id.HasValue)
                {
                    return id.Value;
                }
            }

            throw;
        }
    }

    public async Task<CanonicalOrgRow?> GetCanonicalOrgAsync(long id, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, Kind, DisplayName, ClendorClientId, Website, Notes, CreatedAtUtc, UpdatedAtUtc,
       ISNULL(KorProjectsCount, 0) AS KorProjectsCount, LastKorProjectAtUtc, RetiredAtUtc
FROM   opportunities.CanonicalOrg
WHERE  Id = @id;";
        return await ReadSingleOrgAsync(sql, new[] { ("@id", (object)id, SqlDbType.BigInt, 0) }, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsAsync(
        string? query,
        string? kind,
        int take,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take) Id, Kind, DisplayName, ClendorClientId, Website, Notes, CreatedAtUtc, UpdatedAtUtc,
       ISNULL(KorProjectsCount, 0) AS KorProjectsCount, LastKorProjectAtUtc, RetiredAtUtc
FROM   opportunities.CanonicalOrg
WHERE  RetiredAtUtc IS NULL
   AND (@q IS NULL OR DisplayName LIKE '%' + @q + '%' ESCAPE '\')
   AND (@kind IS NULL OR Kind = @kind)
   AND (@kind IS NOT NULL OR Kind NOT IN ('Vendor','Unknown'))
ORDER BY DisplayName;";

        return await RunSearchAsync(sql, query, kind, take, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsWithRelationshipsAsync(
        string? query,
        string? kind,
        int take,
        CancellationToken ct)
    {
        // Filter to orgs the firm actually has a relationship with. Each EXISTS
        // hits an existing filtered/covering index on the FK column (see
        // schema 22_OpportunityCanonicalLinks.sql, 38_MajorProjectsInventory.sql,
        // 20_KorPursuits.sql) so the OR doesn't fan out into a table scan.
        const string sql = @"
SELECT TOP (@take) co.Id, co.Kind, co.DisplayName, co.ClendorClientId, co.Website, co.Notes, co.CreatedAtUtc, co.UpdatedAtUtc,
       ISNULL(co.KorProjectsCount, 0) AS KorProjectsCount, co.LastKorProjectAtUtc, co.RetiredAtUtc
FROM   opportunities.CanonicalOrg co
WHERE  co.RetiredAtUtc IS NULL
   AND (@q IS NULL OR co.DisplayName LIKE '%' + @q + '%' ESCAPE '\')
   AND (@kind IS NULL OR co.Kind = @kind)
   AND (@kind IS NOT NULL OR co.Kind NOT IN ('Vendor','Unknown'))
   AND (co.ClendorClientId IS NOT NULL
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId = co.Id)
     OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory mp WHERE mp.ProponentCanonicalOrgId = co.Id AND mp.RetiredAtUtc IS NULL)
     OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory mp WHERE mp.ArchitectCanonicalOrgId = co.Id AND mp.RetiredAtUtc IS NULL)
     OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory mp WHERE mp.StructuralEngineerCanonicalOrgId = co.Id AND mp.RetiredAtUtc IS NULL)
     OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory mp WHERE mp.GeneralContractorCanonicalOrgId = co.Id AND mp.RetiredAtUtc IS NULL)
     OR EXISTS (SELECT 1 FROM opportunities.Opportunities o WHERE o.BuyerCanonicalOrgId = co.Id)
     OR EXISTS (SELECT 1 FROM opportunities.OpportunityAwards a WHERE a.AwardingCanonicalOrgId = co.Id OR a.AwardedToCanonicalOrgId = co.Id)
     OR EXISTS (SELECT 1 FROM opportunities.KorPursuits p WHERE p.BuyerCanonicalOrgId = co.Id OR p.LostToCanonicalOrgId = co.Id))
ORDER BY co.DisplayName;";

        return await RunSearchAsync(sql, query, kind, take, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CanonicalOrgRow>> RunSearchAsync(
        string sql,
        string? query,
        string? kind,
        int take,
        CancellationToken ct)
    {
        var safeTake = Math.Clamp(take, 1, 2000);
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(query)
            ? (object)DBNull.Value
            : EscapeLikeQuery(query.Trim());
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(kind)
            ? (object)DBNull.Value
            : kind.Trim();
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = safeTake;

        var list = new List<CanonicalOrgRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapOrg(r));
        }

        return list;
    }

    public async Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id, Kind, DisplayName, ClendorClientId, Website, Notes, CreatedAtUtc, UpdatedAtUtc,
       ISNULL(KorProjectsCount, 0) AS KorProjectsCount, LastKorProjectAtUtc, RetiredAtUtc
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

    public async Task<bool> UnretireAsync(long canonicalOrgId, string reason, CancellationToken ct)
    {
        // BD-Audit-2026-06-09 M8: the resurrect reason used to be discarded
        // (`_ = reason;`) — the only audit trail was a log line. Persist it
        // into Notes alongside the original RetiredReason so the data itself
        // records why a retired org came back.
        const string sql = @"
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = NULL,
    RetiredReason = NULL,
    Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'')
            + N'[Unretired ' + CONVERT(nvarchar(33), sysdatetimeoffset(), 127) + N': ' + COALESCE(@reason, N'(unspecified)')
            + N'; was retired: ' + COALESCE(RetiredReason, N'(no reason)') + N']'
OUTPUT INSERTED.Id, DELETED.RetiredAtUtc
WHERE Id = @id AND RetiredAtUtc IS NOT NULL;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = (object?)reason ?? DBNull.Value;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false);
    }

    private static bool IsDuplicateKey(SqlException ex)
        => ex.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627);

    private static string NormalizeName(string name)
        => name.Trim().ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("&", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal)
            .Replace("(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal);

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
        return MapOrg(r);
    }

    private static CanonicalOrgRow MapOrg(SqlDataReader r)
        => new(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetDateTimeOffset(6),
            r.GetDateTimeOffset(7),
            r.GetInt32(8),
            r.IsDBNull(9) ? null : new DateTimeOffset(r.GetDateTime(9), TimeSpan.Zero),
            r.IsDBNull(10) ? null : r.GetDateTimeOffset(10));

    private static string EscapeLikeQuery(string query)
        => query
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal);

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
