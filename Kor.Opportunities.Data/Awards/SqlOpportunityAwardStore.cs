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
