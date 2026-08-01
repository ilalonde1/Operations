#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

/// <summary>
/// Persists <see cref="Opportunity"/> records into the archive table
/// <c>opportunities.HistoricalOpportunities</c>. Pursuit-lifecycle fields
/// (Status, IdentifiedAt/PursuingSince/..., Owner, WonLost, etc.) are dropped
/// at the SQL boundary  the archive table has no columns for them.
/// Archive-only fields (<c>BcBidInternalId</c>, <c>DetailUrl</c>) come in as
/// explicit ingestion-side parameters since they don't belong on the
/// canonical Opportunity record.
/// </summary>
public sealed class SqlHistoricalOpportunityStore : IHistoricalOpportunityStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns = @"
Id, OpportunityKey, Name,
BuyerName, BuyerType,
ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
Discipline, ConstructionType, ProjectCategory,
EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
HistoricalStatus, IngestedAtUtc,
RelevanceScore, RelevanceTier,
BcBidInternalId, DetailUrl,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy, RowVersion";

    private readonly string _connectionString;

    public SqlHistoricalOpportunityStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.HistoricalOpportunities
WHERE OpportunityKey = @key;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = opportunityKey;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<Opportunity> InsertAsync(
        Opportunity o,
        string actorDisplay,
        string? bcBidInternalId,
        string? detailUrl,
        CancellationToken ct)
    {
        var sql = $@"
INSERT INTO opportunities.HistoricalOpportunities
    (OpportunityKey, Name,
     BuyerName, BuyerType,
     ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
     Discipline, ConstructionType, ProjectCategory,
     EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
     IngestedAtUtc,
     RelevanceScore, RelevanceTier,
     BcBidInternalId, DetailUrl,
     CreatedBy, UpdatedBy)
OUTPUT {OutputInsertedColumns()}
VALUES
    (@key, @name,
     @buyer, @buyerType,
     @addr, @city, @prov, @postal, @lat, @lng,
     @disc, @ctype, @pcat,
     @value, @ccy, @rfpDate, @deadline,
     @ingestedAt,
     @score, @tier,
     @internalId, @detailUrl,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, o);
        cmd.Parameters.Add("@internalId", SqlDbType.NVarChar, 50).Value = (object?)bcBidInternalId ?? DBNull.Value;
        cmd.Parameters.Add("@detailUrl", SqlDbType.NVarChar, 2000).Value = (object?)detailUrl ?? DBNull.Value;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row.");
        }

        return MapReader(reader);
    }

    public async Task<Opportunity> UpdateAsync(
        Opportunity o,
        string actorDisplay,
        string? bcBidInternalId,
        string? detailUrl,
        CancellationToken ct)
    {
        // COALESCE on the archive-only fields so a missing candidate value doesn't
        // wipe a previously-populated row (matches the SubmissionDeadline / RfpReleaseDate
        // null-coalesce pattern in IngestionService.ProcessHistoricalCandidateAsync).
        var sql = $@"
UPDATE opportunities.HistoricalOpportunities
SET Name = @name,
    BuyerName = @buyer, BuyerType = @buyerType,
    ProjectAddress = @addr, ProjectCity = @city, ProjectProvince = @prov, ProjectPostalCode = @postal,
    ProjectLatitude = @lat, ProjectLongitude = @lng,
    Discipline = @disc, ConstructionType = @ctype, ProjectCategory = @pcat,
    EstimatedValue = @value, EstimatedValueCurrency = @ccy,
    RfpReleaseDate = @rfpDate, SubmissionDeadlineUtc = @deadline,
    RelevanceScore = @score, RelevanceTier = @tier,
    BcBidInternalId = COALESCE(@internalId, BcBidInternalId),
    DetailUrl       = COALESCE(@detailUrl,  DetailUrl),
    UpdatedAtUtc = sysdatetimeoffset(), UpdatedBy = @actor
OUTPUT {OutputInsertedColumns()}
WHERE OpportunityKey = @key;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, o);
        cmd.Parameters.Add("@internalId", SqlDbType.NVarChar, 50).Value = (object?)bcBidInternalId ?? DBNull.Value;
        cmd.Parameters.Add("@detailUrl", SqlDbType.NVarChar, 2000).Value = (object?)detailUrl ?? DBNull.Value;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"HistoricalOpportunity with key '{o.OpportunityKey}' not found for UPDATE.");
        }

        return MapReader(reader);
    }

    public async Task<IReadOnlyList<PendingEnrichmentRow>> ListPendingEnrichmentAsync(
        int batchSize,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@n) Id, OpportunityKey, DetailUrl
FROM   opportunities.HistoricalOpportunities
WHERE  DetailUrl IS NOT NULL AND DetailScrapedAtUtc IS NULL
ORDER  BY IngestedAtUtc DESC, Id DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<PendingEnrichmentRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PendingEnrichmentRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    public async Task UpdateEnrichmentAsync(
        long historicalOpportunityId,
        HistoricalOpportunityEnrichmentPayload p,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.HistoricalOpportunities
SET    Commodities            = COALESCE(@commodities, Commodities),
       AmendmentCount         = COALESCE(@amendmentCount, AmendmentCount),
       FullDescription        = COALESCE(@fullDescription, FullDescription),
       EstimatedValue         = COALESCE(@estValue, EstimatedValue),
       EstimatedValueCurrency = COALESCE(@estCcy, EstimatedValueCurrency),
       AwardedToOrganization  = COALESCE(@awardedOrg, AwardedToOrganization),
       AwardedValue           = COALESCE(@awardedVal, AwardedValue),
       AwardedCurrency        = COALESCE(@awardedCcy, AwardedCurrency),
       AwardedAtUtc           = COALESCE(@awardedAt, AwardedAtUtc),
       DetailScrapedAtUtc     = sysdatetimeoffset(),
       UpdatedAtUtc           = sysdatetimeoffset(),
       UpdatedBy              = 'enrichment'
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = historicalOpportunityId;
        cmd.Parameters.Add("@commodities", SqlDbType.NVarChar, 1000).Value = (object?)p.Commodities ?? DBNull.Value;
        cmd.Parameters.Add("@amendmentCount", SqlDbType.Int).Value = p.AmendmentCount.HasValue
            ? (object)p.AmendmentCount.Value
            : DBNull.Value;
        cmd.Parameters.Add("@fullDescription", SqlDbType.NVarChar, -1).Value =
            (object?)p.FullDescription ?? DBNull.Value;
        AddDecimal(cmd, "@estValue", precision: 18, scale: 2, value: p.EstimatedValue);
        cmd.Parameters.Add("@estCcy", SqlDbType.NVarChar, 3).Value =
            (object?)p.EstimatedValueCurrency ?? DBNull.Value;
        cmd.Parameters.Add("@awardedOrg", SqlDbType.NVarChar, 300).Value =
            (object?)p.AwardedToOrganization ?? DBNull.Value;
        AddDecimal(cmd, "@awardedVal", precision: 18, scale: 2, value: p.AwardedValue);
        cmd.Parameters.Add("@awardedCcy", SqlDbType.NVarChar, 3).Value =
            (object?)p.AwardedCurrency ?? DBNull.Value;
        cmd.Parameters.Add("@awardedAt", SqlDbType.DateTimeOffset).Value =
            (object?)p.AwardedAtUtc ?? DBNull.Value;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void BindOpportunityParams(SqlCommand cmd, Opportunity o)
    {
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = o.OpportunityKey;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = o.Name;
        cmd.Parameters.Add("@buyer", SqlDbType.NVarChar, 300).Value = o.BuyerName;
        cmd.Parameters.Add("@buyerType", SqlDbType.Int).Value = (int)o.BuyerType;

        cmd.Parameters.Add("@addr", SqlDbType.NVarChar, 500).Value = (object?)o.ProjectAddress ?? DBNull.Value;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)o.ProjectCity ?? DBNull.Value;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectProvince ?? DBNull.Value;
        cmd.Parameters.Add("@postal", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectPostalCode ?? DBNull.Value;
        AddDecimal(
            cmd,
            "@lat",
            precision: 9,
            scale: 6,
            value: o.ProjectLatitude.HasValue ? (decimal?)o.ProjectLatitude.Value : null);
        AddDecimal(
            cmd,
            "@lng",
            precision: 9,
            scale: 6,
            value: o.ProjectLongitude.HasValue ? (decimal?)o.ProjectLongitude.Value : null);

        cmd.Parameters.Add("@disc", SqlDbType.Int).Value = (int)o.Discipline;
        cmd.Parameters.Add("@ctype", SqlDbType.NVarChar, 100).Value = (object?)o.ConstructionType ?? DBNull.Value;
        cmd.Parameters.Add("@pcat", SqlDbType.NVarChar, 100).Value = (object?)o.ProjectCategory ?? DBNull.Value;

        AddDecimal(cmd, "@value", precision: 18, scale: 2, value: o.EstimatedValue);
        cmd.Parameters.Add("@ccy", SqlDbType.NVarChar, 3).Value = string.IsNullOrEmpty(o.EstimatedValueCurrency)
            ? "CAD"
            : o.EstimatedValueCurrency;
        cmd.Parameters.Add("@rfpDate", SqlDbType.Date).Value = o.RfpReleaseDate.HasValue
            ? (object)o.RfpReleaseDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;
        cmd.Parameters.Add("@deadline", SqlDbType.DateTimeOffset).Value =
            (object?)o.SubmissionDeadlineUtc ?? DBNull.Value;

        cmd.Parameters.Add("@ingestedAt", SqlDbType.DateTimeOffset).Value = o.IdentifiedAtUtc;

        AddDecimal(cmd, "@score", precision: 10, scale: 4, value: o.RelevanceScore);
        cmd.Parameters.Add("@tier", SqlDbType.Int).Value = o.RelevanceTier.HasValue
            ? (object)(int)o.RelevanceTier.Value
            : DBNull.Value;
    }

    private static void AddDecimal(SqlCommand cmd, string name, byte precision, byte scale, decimal? value)
    {
        var p = new SqlParameter(name, SqlDbType.Decimal) { Precision = precision, Scale = scale };
        p.Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static string OutputInsertedColumns()
    {
        var columnNames = AllColumns
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ", Array.ConvertAll(columnNames, c => $"INSERTED.{c}"));
    }

    // Pursuit columns aren't stored in the archive table  those fields stay at
    // their record defaults. Archive-only columns BcBidInternalId (22) and
    // DetailUrl (23) aren't mapped onto the Opportunity record; they're persisted
    // and read at the SQL projection level only, and surfaced through a richer
    // reader in the Phase B archive UI.
    private static Opportunity MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        OpportunityKey = r.GetString(1),
        Name = r.GetString(2),

        BuyerName = r.GetString(3),
        BuyerType = (BuyerType)r.GetInt32(4),

        ProjectAddress = r.IsDBNull(5) ? null : r.GetString(5),
        ProjectCity = r.IsDBNull(6) ? null : r.GetString(6),
        ProjectProvince = r.IsDBNull(7) ? null : r.GetString(7),
        ProjectPostalCode = r.IsDBNull(8) ? null : r.GetString(8),
        ProjectLatitude = r.IsDBNull(9) ? null : r.GetDecimal(9),
        ProjectLongitude = r.IsDBNull(10) ? null : r.GetDecimal(10),

        Discipline = (OpportunityDiscipline)r.GetInt32(11),
        ConstructionType = r.IsDBNull(12) ? null : r.GetString(12),
        ProjectCategory = r.IsDBNull(13) ? null : r.GetString(13),

        EstimatedValue = r.IsDBNull(14) ? null : r.GetDecimal(14),
        EstimatedValueCurrency = r.GetString(15),
        RfpReleaseDate = r.IsDBNull(16) ? null : DateOnly.FromDateTime(r.GetDateTime(16)),
        SubmissionDeadlineUtc = r.IsDBNull(17) ? null : r.GetDateTimeOffset(17),

        // HistoricalStatus (18) is archive-only.
        IdentifiedAtUtc = r.GetDateTimeOffset(19),

        RelevanceScore = r.IsDBNull(20) ? null : r.GetDecimal(20),
        RelevanceTier = r.IsDBNull(21) ? null : (RelevanceTier)r.GetInt32(21),

        // BcBidInternalId (22), DetailUrl (23) are archive-only.
        CreatedAtUtc = r.GetDateTimeOffset(24),
        CreatedBy = r.GetString(25),
        UpdatedAtUtc = r.GetDateTimeOffset(26),
        UpdatedBy = r.GetString(27),
        RowVersion = (byte[])r.GetValue(28),
    };
}
