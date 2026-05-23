#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlOpportunityAwardStore : IOpportunityAwardStore
{
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
       AgentVendorLeadership  = @leadership
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

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    public SqlOpportunityAwardStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<long> UpsertAsync(OpportunityAward a, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.OpportunityAwards AS target
USING (SELECT @sourceId AS OpportunitySourceId, @extRef AS ExternalReference) AS src
   ON target.OpportunitySourceId = src.OpportunitySourceId
  AND target.ExternalReference = src.ExternalReference
WHEN MATCHED THEN UPDATE SET
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
    IngestionRunId = COALESCE(@runId, target.IngestionRunId)
WHEN NOT MATCHED THEN INSERT
    (ExternalReference, OpportunitySourceId, Title, SolicitationType,
     AwardingOrganization, AwardedToOrganization, ContractValue, ContractCurrency,
     AwardedAtUtc, IssuingLocation, SupplierAddress, ContactEmail, ContractNumber,
     SourceUrl, RawJson, IngestionRunId)
VALUES
    (@extRef, @sourceId, @title, @stype,
     @awardingOrg, @awardedTo, @value, @currency,
     @awardedAt, @issLoc, @supAddr, @contactEmail, @contractNumber,
     @url, @raw, @runId)
OUTPUT CASE WHEN $action = 'INSERT' THEN INSERTED.Id ELSE CONVERT(BIGINT, 0) END;";

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

        var id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return id is null ? 0 : (long)id;
    }

      public async Task<IReadOnlyList<PendingAgentEnrichmentRow>> ListPendingAgentEnrichmentAsync(
          int batchSize,
          int maxAttempts,
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
  AND  a.AgentEnrichmentAttempts < @max
ORDER  BY ISNULL(a.ContractValue, 0) DESC, a.Id ASC;";

          await using var con = new SqlConnection(_connectionString);
          await con.OpenAsync(ct).ConfigureAwait(false);
          await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
          cmd.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;
          cmd.Parameters.Add("@max", SqlDbType.Int).Value = maxAttempts;

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
