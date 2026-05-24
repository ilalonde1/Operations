#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlAwardQueryStore : IAwardQueryStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlAwardQueryStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<AwardListing>> ListAsync(AwardQueryFilter f, CancellationToken ct)
    {
        var maxRows = Math.Min(f.MaxRows ?? 10_000, 5000);
        var sb = new StringBuilder(@"
SELECT TOP (@maxRows)
    a.Id, a.ExternalReference, s.Name AS SourceName, a.Title, a.SolicitationType,
    a.AwardingOrganization, a.AwardedToOrganization, a.ContractValue, a.ContractCurrency,
    a.AwardedAtUtc, a.IssuingLocation, a.ContractNumber, a.SourceUrl,
    a.AgentVendorProfile, a.AgentContractContext, a.AgentCompetesWithKor, a.AgentEnrichedAtUtc
FROM opportunities.OpportunityAwards a
JOIN opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE 1 = 1
");

        if (!string.IsNullOrWhiteSpace(f.KeywordLike))
            sb.AppendLine(@"  AND (a.Title LIKE @kw OR a.AwardingOrganization LIKE @kw OR a.AwardedToOrganization LIKE @kw)");
        if (!string.IsNullOrWhiteSpace(f.VendorLike))
            sb.AppendLine(@"  AND a.AwardedToOrganization LIKE @vendor");
        if (f.Year.HasValue)
            sb.AppendLine(@"  AND YEAR(a.AwardedAtUtc) = @year");
        if (!string.IsNullOrWhiteSpace(f.SourceName))
            sb.AppendLine(@"  AND s.Name = @src");
        if (f.MinContractValue.HasValue)
            sb.AppendLine(@"  AND a.ContractValue >= @minVal");
        if (f.CompetesWithKorOnly == true)
            sb.AppendLine(@"  AND a.AgentCompetesWithKor = 1");

        sb.AppendLine(@"ORDER BY a.AwardedAtUtc DESC, a.Id DESC;");

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sb.ToString(), con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@maxRows", SqlDbType.Int).Value = maxRows;
        if (!string.IsNullOrWhiteSpace(f.KeywordLike))
            cmd.Parameters.Add("@kw", SqlDbType.NVarChar, 200).Value = "%" + f.KeywordLike.Trim() + "%";
        if (!string.IsNullOrWhiteSpace(f.VendorLike))
            cmd.Parameters.Add("@vendor", SqlDbType.NVarChar, 200).Value = "%" + f.VendorLike.Trim() + "%";
        if (f.Year.HasValue)
            cmd.Parameters.Add("@year", SqlDbType.Int).Value = f.Year.Value;
        if (!string.IsNullOrWhiteSpace(f.SourceName))
            cmd.Parameters.Add("@src", SqlDbType.NVarChar, 200).Value = f.SourceName.Trim();
        if (f.MinContractValue.HasValue)
        {
            var p = new SqlParameter("@minVal", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = f.MinContractValue.Value,
            };
            cmd.Parameters.Add(p);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<AwardListing>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new AwardListing
            {
                Id = reader.GetInt64(0),
                ExternalReference = reader.GetString(1),
                SourceName = reader.GetString(2),
                Title = reader.GetString(3),
                SolicitationType = reader.IsDBNull(4) ? null : reader.GetString(4),
                AwardingOrganization = reader.GetString(5),
                AwardedToOrganization = reader.GetString(6),
                ContractValue = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                ContractCurrency = reader.GetString(8),
                AwardedAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9),
                IssuingLocation = reader.IsDBNull(10) ? null : reader.GetString(10),
                ContractNumber = reader.IsDBNull(11) ? null : reader.GetString(11),
                SourceUrl = reader.GetString(12),
                AgentVendorProfile = reader.IsDBNull(13) ? null : reader.GetString(13),
                AgentContractContext = reader.IsDBNull(14) ? null : reader.GetString(14),
                AgentCompetesWithKor = reader.IsDBNull(15) ? null : reader.GetBoolean(15),
                AgentEnrichedAtUtc = reader.IsDBNull(16) ? null : reader.GetDateTimeOffset(16),
            });
        }

        return rows;
    }

    public async Task<AwardQueryFacets> GetFacetsAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var years = new List<int>();
        var sources = new List<string>();

        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT YEAR(AwardedAtUtc) AS Y
FROM opportunities.OpportunityAwards
WHERE AwardedAtUtc IS NOT NULL
ORDER BY Y DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false)) years.Add(r.GetInt32(0));
        }
        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT s.Name
FROM opportunities.OpportunityAwards a
JOIN opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
ORDER BY s.Name;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false)) sources.Add(r.GetString(0));
        }

        return new AwardQueryFacets { Years = years, SourceNames = sources };
    }
}
