#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlOpportunityAwardStore : IOpportunityAwardStore
{
    private readonly CanonicalOrgResolver? _canonicalResolver;
    private readonly ILogger<SqlOpportunityAwardStore> _logger;

    public async Task RecordSiteExtractionAsync(
        string vendorWebsite,
        Kor.Opportunities.Core.Models.VendorSiteExtractionPayload p,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.OpportunityAwards
SET    AgentVendorPortfolio        = @portfolio,
       AgentVendorSpecificServices = @services,
       AgentVendorSectorFocus      = @sectors,
       AgentVendorOpenPositions    = @positions,
       AgentVendorLeadershipDetail = @leadership,
       AgentVendorBondingCapacity  = @bonding,
       AgentVendorTagline          = @tagline,
       AgentSiteCrawledAtUtc       = sysdatetimeoffset()
WHERE  AgentVendorWebsite = @website;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = vendorWebsite;

        static string? JsonOrNull<T>(IReadOnlyList<T> list)
            => list.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(list);

        cmd.Parameters.Add("@portfolio", SqlDbType.NVarChar, -1).Value = (object?)JsonOrNull(p.Portfolio) ?? DBNull.Value;
        cmd.Parameters.Add("@services", SqlDbType.NVarChar, -1).Value = (object?)JsonOrNull(p.SpecificServices) ?? DBNull.Value;
        cmd.Parameters.Add("@sectors", SqlDbType.NVarChar, -1).Value = (object?)JsonOrNull(p.SectorFocus) ?? DBNull.Value;
        cmd.Parameters.Add("@positions", SqlDbType.NVarChar, -1).Value = (object?)JsonOrNull(p.OpenPositions) ?? DBNull.Value;
        cmd.Parameters.Add("@leadership", SqlDbType.NVarChar, -1).Value = (object?)JsonOrNull(p.LeadershipDetail) ?? DBNull.Value;
        cmd.Parameters.Add("@bonding", SqlDbType.NVarChar, 300).Value = (object?)p.BondingCapacity ?? DBNull.Value;
        cmd.Parameters.Add("@tagline", SqlDbType.NVarChar, 300).Value = (object?)p.Tagline ?? DBNull.Value;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordAgentVendorDetailsAsync(
        long id,
        Kor.Opportunities.Core.Models.AwardAgentEnrichmentPayload p,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.OpportunityAwards
SET    AgentVendorWebsite     = @website,
       AgentVendorHqLocation  = @hq,
       AgentVendorSizeBand    = @size,
       AgentVendorFoundedYear = @founded,
       AgentVendorSpecialties = @specialties,
       AgentVendorLeadership  = @leadership,
       AgentVendorOwnershipStatus = @ownership,
       AgentVendorParentCompany   = @parent,
       AgentVendorLocations      = @locations,
       AgentVendorCertifications = @certs,
       AgentVendorRecentNews     = @news,
       AgentVendorLinkedInUrl    = @linkedin,
       AgentKorOverlapScore      = @overlap,
       AgentContractProjectType  = @ptype
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = (object?)p.VendorWebsite ?? DBNull.Value;
        cmd.Parameters.Add("@hq", SqlDbType.NVarChar, 200).Value = (object?)p.VendorHqLocation ?? DBNull.Value;
        cmd.Parameters.Add("@size", SqlDbType.NVarChar, 20).Value = (object?)p.VendorSizeBand ?? DBNull.Value;
        cmd.Parameters.Add("@founded", SqlDbType.Int).Value = p.VendorFoundedYear.HasValue
            ? (object)p.VendorFoundedYear.Value
            : DBNull.Value;

        var specJson = p.VendorSpecialties.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(p.VendorSpecialties);
        cmd.Parameters.Add("@specialties", SqlDbType.NVarChar, -1).Value = (object?)specJson ?? DBNull.Value;

        var leadJson = p.VendorLeadership.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(p.VendorLeadership);
        cmd.Parameters.Add("@leadership", SqlDbType.NVarChar, -1).Value = (object?)leadJson ?? DBNull.Value;
        var locJson = p.VendorLocations.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(p.VendorLocations);
        cmd.Parameters.Add("@locations", SqlDbType.NVarChar, -1).Value = (object?)locJson ?? DBNull.Value;
        var certJson = p.VendorCertifications.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(p.VendorCertifications);
        cmd.Parameters.Add("@certs", SqlDbType.NVarChar, -1).Value = (object?)certJson ?? DBNull.Value;
        var newsJson = p.VendorRecentNews.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(p.VendorRecentNews);
        cmd.Parameters.Add("@news", SqlDbType.NVarChar, -1).Value = (object?)newsJson ?? DBNull.Value;
        cmd.Parameters.Add("@linkedin", SqlDbType.NVarChar, 500).Value = (object?)p.VendorLinkedInUrl ?? DBNull.Value;
        cmd.Parameters.Add("@overlap", SqlDbType.TinyInt).Value = p.VendorKorOverlapScore.HasValue
            ? (object)(byte)Math.Clamp(p.VendorKorOverlapScore.Value, 0, 10)
            : DBNull.Value;
        cmd.Parameters.Add("@ptype", SqlDbType.NVarChar, 80).Value = (object?)p.ContractProjectType ?? DBNull.Value;
        cmd.Parameters.Add("@ownership", SqlDbType.NVarChar, 50).Value = (object?)p.VendorOwnershipStatus ?? DBNull.Value;
        cmd.Parameters.Add("@parent", SqlDbType.NVarChar, 300).Value = (object?)p.VendorParentCompany ?? DBNull.Value;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    public SqlOpportunityAwardStore(
        string connectionString,
        CanonicalOrgResolver? canonicalResolver = null,
        ILogger<SqlOpportunityAwardStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _canonicalResolver = canonicalResolver;
        _logger = logger ?? NullLogger<SqlOpportunityAwardStore>.Instance;
    }

    /// <summary>Convenience overload that always resolves canonical orgs.
    /// Used by historical bulk-import tools that pre-filter their input.</summary>
    public Task<long> UpsertAsync(OpportunityAward a, CancellationToken ct)
        => UpsertAsync(a, resolveCanonicalOrgs: true, ct);

    public async Task<long> UpsertAsync(OpportunityAward a, bool resolveCanonicalOrgs, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted table (Id bigint NOT NULL);

BEGIN TRAN;

UPDATE opportunities.OpportunityAwards WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    Title = @title,
    SolicitationType = @stype,
    AwardingOrganization = @awardingOrg,
    AwardedToOrganization = @awardedTo,
    ContractValue = @value,
    ContractCurrency = @currency,
    AwardedAtUtc = @awardedAt,
    IssuingLocation = @issLoc,
    SupplierAddress = @supAddr,
    ContactEmail = @contactEmail,
    ContractNumber = @contractNumber,
    SourceUrl = @url,
    RawJson = @raw,
    UpdatedAtUtc = sysdatetimeoffset(),
    IngestionRunId = COALESCE(@runId, IngestionRunId)
WHERE OpportunitySourceId = @sourceId
  AND ExternalReference = @extRef;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.OpportunityAwards
    (ExternalReference, OpportunitySourceId, Title, SolicitationType,
     AwardingOrganization, AwardedToOrganization, ContractValue, ContractCurrency,
     AwardedAtUtc, IssuingLocation, SupplierAddress, ContactEmail, ContractNumber,
     SourceUrl, RawJson, IngestionRunId)
    OUTPUT inserted.Id INTO @inserted
    VALUES
    (@extRef, @sourceId, @title, @stype,
     @awardingOrg, @awardedTo, @value, @currency,
     @awardedAt, @issLoc, @supAddr, @contactEmail, @contractNumber,
     @url, @raw, @runId);
END;

COMMIT TRAN;

SELECT COALESCE((SELECT TOP (1) Id FROM @inserted), CONVERT(bigint, 0));";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@sourceId", SqlDbType.UniqueIdentifier).Value = a.OpportunitySourceId;
        cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200).Value = a.ExternalReference;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 400).Value = a.Title;
        cmd.Parameters.Add("@stype", SqlDbType.NVarChar, 150).Value = (object?)a.SolicitationType ?? DBNull.Value;
        cmd.Parameters.Add("@awardingOrg", SqlDbType.NVarChar, 300).Value = a.AwardingOrganization;
        cmd.Parameters.Add("@awardedTo", SqlDbType.NVarChar, 300).Value = a.AwardedToOrganization;
        cmd.Parameters.Add("@value", SqlDbType.Decimal).Value = (object?)a.ContractValue ?? DBNull.Value;
        ((SqlParameter)cmd.Parameters["@value"]).Precision = 18;
        ((SqlParameter)cmd.Parameters["@value"]).Scale = 2;
        cmd.Parameters.Add("@currency", SqlDbType.Char, 3).Value = a.ContractCurrency;
        cmd.Parameters.Add("@awardedAt", SqlDbType.DateTimeOffset).Value = (object?)a.AwardedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@issLoc", SqlDbType.NVarChar, 300).Value = (object?)a.IssuingLocation ?? DBNull.Value;
        cmd.Parameters.Add("@supAddr", SqlDbType.NVarChar, 500).Value = (object?)a.SupplierAddress ?? DBNull.Value;
        cmd.Parameters.Add("@contactEmail", SqlDbType.NVarChar, 200).Value = (object?)a.ContactEmail ?? DBNull.Value;
        cmd.Parameters.Add("@contractNumber", SqlDbType.NVarChar, 150).Value = (object?)a.ContractNumber ?? DBNull.Value;
        cmd.Parameters.Add("@url", SqlDbType.NVarChar, 800).Value = a.SourceUrl;
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, -1).Value = (object?)a.RawJson ?? DBNull.Value;
        cmd.Parameters.Add("@runId", SqlDbType.UniqueIdentifier).Value = (object?)a.IngestionRunId ?? DBNull.Value;

        var idResult = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var id = idResult is null ? 0L : (long)idResult;

        // Resolve canonical orgs at insert/update time (best-effort).
        // Round 9c — keeps AwardingCanonicalOrgId / AwardedToCanonicalOrgId fresh
        // so we don't need backfill jobs going forward.
        // BD-Audit-2026-06-09 M7: skipped entirely when the caller's relevance
        // gate rejected the award — the row keeps raw org-name strings only.
        if (resolveCanonicalOrgs && _canonicalResolver is not null)
        {
            try
            {
                var awardingId = await _canonicalResolver.ResolveBuyerAsync(a.AwardingOrganization, ct).ConfigureAwait(false);
                var awardedToId = await _canonicalResolver.ResolveAsync(
                    a.AwardedToOrganization,
                    OrgKinds.Vendor,
                    OrgAliasSources.OpportunityAwardsAwardedTo,
                    ct,
                    createArchived: true).ConfigureAwait(false);
                if (awardingId.HasValue || awardedToId.HasValue)
                {
                    await using var con2 = new SqlConnection(_connectionString);
                    await con2.OpenAsync(ct).ConfigureAwait(false);
                    await using var upd = new SqlCommand(@"
UPDATE opportunities.OpportunityAwards
SET    AwardingCanonicalOrgId  = COALESCE(@aw, AwardingCanonicalOrgId),
       AwardedToCanonicalOrgId = COALESCE(@at, AwardedToCanonicalOrgId)
WHERE  OpportunitySourceId = @src AND ExternalReference = @ref;", con2)
                    { CommandTimeout = 30 };
                    upd.Parameters.Add("@src", SqlDbType.UniqueIdentifier).Value = a.OpportunitySourceId;
                    upd.Parameters.Add("@ref", SqlDbType.NVarChar, 200).Value = a.ExternalReference;
                    upd.Parameters.Add("@aw", SqlDbType.BigInt).Value = (object?)awardingId ?? DBNull.Value;
                    upd.Parameters.Add("@at", SqlDbType.BigInt).Value = (object?)awardedToId ?? DBNull.Value;
                    await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Canonical resolution failed for award (source={SourceId}, ref='{Ref}')",
                    a.OpportunitySourceId,
                    a.ExternalReference);
            }
        }

        return id;
    }

      public async Task<IReadOnlyList<PendingAgentEnrichmentRow>> ListPendingAgentEnrichmentAsync(
          int batchSize,
          int maxAttempts,
          CancellationToken ct)
          => await ListPendingAgentEnrichmentAsync(
              batchSize,
              maxAttempts,
              excludeSourceIds: null,
              minContractValue: null,
              ct).ConfigureAwait(false);

      public async Task<IReadOnlyList<PendingAgentEnrichmentRow>> ListPendingAgentEnrichmentAsync(
          int batchSize,
          int maxAttempts,
          IReadOnlyCollection<Guid>? excludeSourceIds,
          decimal? minContractValue,
          CancellationToken ct)
      {
          const string sql = @"
SELECT TOP (@n)
    a.Id, a.ExternalReference, a.Title, a.AwardingOrganization, a.AwardedToOrganization,
    a.ContractValue, a.ContractCurrency, a.AwardedAtUtc, a.IssuingLocation,
    s.Name AS SourceName, a.AgentEnrichmentAttempts
FROM   opportunities.OpportunityAwards a
JOIN   opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE  a.AgentEnrichedAtUtc IS NULL
  AND  ISNULL(a.AgentEnrichmentAttempts, 0) < @max
  AND  (@hasExcludeIds = 0 OR a.OpportunitySourceId NOT IN (
        SELECT TRY_CONVERT(uniqueidentifier, value)
        FROM STRING_SPLIT(@excludeIds, ',')
        WHERE TRY_CONVERT(uniqueidentifier, value) IS NOT NULL
  ))
  AND  (@minValue IS NULL OR ISNULL(a.ContractValue, 0) >= @minValue)
ORDER  BY ISNULL(a.ContractValue, 0) DESC, a.Id ASC;";

          await using var con = new SqlConnection(_connectionString);
          await con.OpenAsync(ct).ConfigureAwait(false);
          await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
          cmd.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;
          cmd.Parameters.Add("@max", SqlDbType.Int).Value = maxAttempts;
          var excludeCsv = excludeSourceIds is { Count: > 0 }
              ? string.Join(",", excludeSourceIds)
              : string.Empty;
          cmd.Parameters.Add("@hasExcludeIds", SqlDbType.Bit).Value = excludeSourceIds is { Count: > 0 };
          cmd.Parameters.Add("@excludeIds", SqlDbType.NVarChar, -1).Value = excludeCsv;
          cmd.Parameters.Add("@minValue", SqlDbType.Decimal).Value = minContractValue.HasValue
              ? (object)minContractValue.Value
              : DBNull.Value;
          ((SqlParameter)cmd.Parameters["@minValue"]).Precision = 18;
          ((SqlParameter)cmd.Parameters["@minValue"]).Scale = 2;

          await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
          var rows = new List<PendingAgentEnrichmentRow>();
          while (await r.ReadAsync(ct).ConfigureAwait(false))
          {
              rows.Add(new PendingAgentEnrichmentRow(
                  r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                  r.IsDBNull(5) ? null : r.GetDecimal(5),
                  r.GetString(6),
                  r.IsDBNull(7) ? null : r.GetDateTimeOffset(7),
                  r.IsDBNull(8) ? null : r.GetString(8),
                  r.GetString(9),
                  r.GetInt32(10)));
          }

          return rows;
      }

      public async Task RecordAgentEnrichmentAsync(
          long id,
          AwardAgentEnrichmentPayload p,
          CancellationToken ct)
      {
          const string sql = @"
UPDATE opportunities.OpportunityAwards
SET    AgentVendorProfile      = @vp,
       AgentContractContext    = @cc,
       AgentCompetesWithKor    = @cw,
       AgentCompetitionNotes   = @cn,
       AgentSourceUrls         = @urls,
       AgentEnrichedAtUtc      = sysdatetimeoffset(),
       AgentLastAttemptAtUtc   = sysdatetimeoffset(),
       AgentLastError          = NULL,
       AgentEnrichmentAttempts = AgentEnrichmentAttempts + 1
WHERE Id = @id;";

          await using var con = new SqlConnection(_connectionString);
          await con.OpenAsync(ct).ConfigureAwait(false);
          await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
          cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
          cmd.Parameters.Add("@vp", SqlDbType.NVarChar, -1).Value = (object?)p.VendorProfile ?? DBNull.Value;
          cmd.Parameters.Add("@cc", SqlDbType.NVarChar, -1).Value = (object?)p.ContractContext ?? DBNull.Value;
          cmd.Parameters.Add("@cw", SqlDbType.Bit).Value = p.CompetesWithKor.HasValue
              ? (object)p.CompetesWithKor.Value
              : DBNull.Value;
          cmd.Parameters.Add("@cn", SqlDbType.NVarChar, -1).Value = (object?)p.CompetitionNotes ?? DBNull.Value;
          var urlsJson = p.SourceUrls.Count == 0
              ? null
              : System.Text.Json.JsonSerializer.Serialize(p.SourceUrls);
          cmd.Parameters.Add("@urls", SqlDbType.NVarChar, -1).Value = (object?)urlsJson ?? DBNull.Value;

          await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
      }

      public async Task RecordAgentFailureAsync(long id, string error, CancellationToken ct)
      {
          const string sql = @"
UPDATE opportunities.OpportunityAwards
SET    AgentEnrichmentAttempts = AgentEnrichmentAttempts + 1,
       AgentLastAttemptAtUtc   = sysdatetimeoffset(),
       AgentLastError          = @err
WHERE Id = @id;";

          await using var con = new SqlConnection(_connectionString);
          await con.OpenAsync(ct).ConfigureAwait(false);
          await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
          cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
          cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value =
              error.Length > 2000 ? error.Substring(0, 2000) : error;
          await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
      }

      public async Task<int> CountAgentEnrichedAsync(CancellationToken ct)
      {
          const string sql = "SELECT COUNT(*) FROM opportunities.OpportunityAwards WHERE AgentEnrichedAtUtc IS NOT NULL;";
          await using var con = new SqlConnection(_connectionString);
          await con.OpenAsync(ct).ConfigureAwait(false);
          await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
          var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
          return result is null or DBNull ? 0 : System.Convert.ToInt32(result);
      }

      public async Task<IReadOnlyList<OpportunityAward>> ListRecentAsync(int max, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@max) Id, ExternalReference, OpportunitySourceId, Title, SolicitationType,
       AwardingOrganization, AwardedToOrganization, ContractValue, ContractCurrency,
       AwardedAtUtc, IssuingLocation, SupplierAddress, ContactEmail, ContractNumber,
       SourceUrl, RawJson, CreatedAtUtc, UpdatedAtUtc, IngestionRunId, RowVersion
FROM opportunities.OpportunityAwards
ORDER BY AwardedAtUtc DESC, CreatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;

        var list = new List<OpportunityAward>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new OpportunityAward
            {
                Id = r.GetInt64(0),
                ExternalReference = r.GetString(1),
                OpportunitySourceId = r.GetGuid(2),
                Title = r.GetString(3),
                SolicitationType = r.IsDBNull(4) ? null : r.GetString(4),
                AwardingOrganization = r.GetString(5),
                AwardedToOrganization = r.GetString(6),
                ContractValue = r.IsDBNull(7) ? null : r.GetDecimal(7),
                ContractCurrency = r.GetString(8),
                AwardedAtUtc = r.IsDBNull(9) ? null : r.GetDateTimeOffset(9),
                IssuingLocation = r.IsDBNull(10) ? null : r.GetString(10),
                SupplierAddress = r.IsDBNull(11) ? null : r.GetString(11),
                ContactEmail = r.IsDBNull(12) ? null : r.GetString(12),
                ContractNumber = r.IsDBNull(13) ? null : r.GetString(13),
                SourceUrl = r.GetString(14),
                RawJson = r.IsDBNull(15) ? null : r.GetString(15),
                CreatedAtUtc = r.GetDateTimeOffset(16),
                UpdatedAtUtc = r.GetDateTimeOffset(17),
                IngestionRunId = r.IsDBNull(18) ? null : r.GetGuid(18),
                RowVersion = (byte[])r["RowVersion"],
            });
        }

        return list;
    }
}
