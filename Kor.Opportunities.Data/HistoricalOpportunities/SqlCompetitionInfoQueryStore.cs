#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed class SqlCompetitionInfoQueryStore : ICompetitionInfoQueryStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlCompetitionInfoQueryStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<HistoricalOpportunityListing>> ListAsync(
        CompetitionInfoFilter f,
        CancellationToken ct)
    {
        var sb = new StringBuilder(@"
SELECT TOP (@maxRows)
    h.Id, h.OpportunityKey, h.BcBidInternalId, h.Name, h.BuyerName,
    h.ProjectProvince, h.HistoricalStatus, h.RfpReleaseDate, h.SubmissionDeadlineUtc,
    h.EstimatedValue, h.Commodities, h.AmendmentCount,
    h.AwardedToOrganization, h.AwardedValue,
    ISNULL(d.DocCount, 0) AS DocCount,
    ISNULL(d.DownloadedCount, 0) AS DownloadedCount,
    h.DetailScrapedAtUtc, h.DetailUrl
FROM opportunities.HistoricalOpportunities h
LEFT JOIN (
    SELECT HistoricalOpportunityId,
           COUNT(*) AS DocCount,
           SUM(CASE WHEN LocalPath IS NOT NULL THEN 1 ELSE 0 END) AS DownloadedCount
    FROM opportunities.HistoricalOpportunityDocuments
    GROUP BY HistoricalOpportunityId
) d ON d.HistoricalOpportunityId = h.Id
WHERE 1 = 1
");

        if (!string.IsNullOrWhiteSpace(f.KeywordLike))
        {
            sb.AppendLine(
                @"  AND (h.Name LIKE @kw OR h.BuyerName LIKE @kw OR h.Commodities LIKE @kw OR h.FullDescription LIKE @kw)");
        }
        if (f.Year.HasValue)
        {
            sb.AppendLine(@"  AND YEAR(h.RfpReleaseDate) = @year");
        }
        if (!string.IsNullOrWhiteSpace(f.Province))
        {
            sb.AppendLine(@"  AND h.ProjectProvince = @prov");
        }
        if (!string.IsNullOrWhiteSpace(f.HistoricalStatus))
        {
            sb.AppendLine(@"  AND h.HistoricalStatus = @status");
        }
        if (f.MinEstimatedValue.HasValue)
        {
            sb.AppendLine(@"  AND h.EstimatedValue >= @minVal");
        }
        if (f.HasDocuments == true)
        {
            sb.AppendLine(@"  AND ISNULL(d.DownloadedCount, 0) > 0");
        }

        sb.AppendLine(@"ORDER BY h.RfpReleaseDate DESC, h.Id DESC;");

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sb.ToString(), con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@maxRows", SqlDbType.Int).Value = f.MaxRows ?? 10_000;
        if (!string.IsNullOrWhiteSpace(f.KeywordLike))
            cmd.Parameters.Add("@kw", SqlDbType.NVarChar, 200).Value = "%" + f.KeywordLike.Trim() + "%";
        if (f.Year.HasValue)
            cmd.Parameters.Add("@year", SqlDbType.Int).Value = f.Year.Value;
        if (!string.IsNullOrWhiteSpace(f.Province))
            cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = f.Province.Trim();
        if (!string.IsNullOrWhiteSpace(f.HistoricalStatus))
            cmd.Parameters.Add("@status", SqlDbType.NVarChar, 32).Value = f.HistoricalStatus.Trim();
        if (f.MinEstimatedValue.HasValue)
        {
            var p = new SqlParameter("@minVal", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = f.MinEstimatedValue.Value,
            };
            cmd.Parameters.Add(p);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<HistoricalOpportunityListing>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new HistoricalOpportunityListing
            {
                Id = reader.GetInt64(0),
                OpportunityKey = reader.GetString(1),
                BcBidInternalId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Name = reader.GetString(3),
                BuyerName = reader.GetString(4),
                ProjectProvince = reader.IsDBNull(5) ? null : reader.GetString(5),
                HistoricalStatus = reader.IsDBNull(6) ? null : reader.GetString(6),
                RfpReleaseDate = reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
                SubmissionDeadlineUtc = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                EstimatedValue = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                Commodities = reader.IsDBNull(10) ? null : reader.GetString(10),
                AmendmentCount = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                AwardedToOrganization = reader.IsDBNull(12) ? null : reader.GetString(12),
                AwardedValue = reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                DocumentCount = reader.GetInt32(14),
                DownloadedDocumentCount = reader.GetInt32(15),
                DetailScrapedAtUtc = reader.IsDBNull(16) ? null : reader.GetDateTimeOffset(16),
                DetailUrl = reader.IsDBNull(17) ? null : reader.GetString(17),
            });
        }

        return rows;
    }

    public async Task<HistoricalOpportunityDetail?> GetDetailAsync(long historicalOpportunityId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, OpportunityKey, Name, BuyerName, ProjectProvince, HistoricalStatus,
       RfpReleaseDate, SubmissionDeadlineUtc, EstimatedValue, Commodities,
       AmendmentCount, FullDescription, DetailUrl, AwardedToOrganization, AwardedValue
FROM   opportunities.HistoricalOpportunities
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = historicalOpportunityId;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;

        return new HistoricalOpportunityDetail
        {
            Id = r.GetInt64(0),
            OpportunityKey = r.GetString(1),
            Name = r.GetString(2),
            BuyerName = r.GetString(3),
            ProjectProvince = r.IsDBNull(4) ? null : r.GetString(4),
            HistoricalStatus = r.IsDBNull(5) ? null : r.GetString(5),
            RfpReleaseDate = r.IsDBNull(6) ? null : DateOnly.FromDateTime(r.GetDateTime(6)),
            SubmissionDeadlineUtc = r.IsDBNull(7) ? null : r.GetDateTimeOffset(7),
            EstimatedValue = r.IsDBNull(8) ? null : r.GetDecimal(8),
            Commodities = r.IsDBNull(9) ? null : r.GetString(9),
            AmendmentCount = r.IsDBNull(10) ? null : r.GetInt32(10),
            FullDescription = r.IsDBNull(11) ? null : r.GetString(11),
            DetailUrl = r.IsDBNull(12) ? null : r.GetString(12),
            AwardedToOrganization = r.IsDBNull(13) ? null : r.GetString(13),
            AwardedValue = r.IsDBNull(14) ? null : r.GetDecimal(14),
        };
    }

    public async Task<CompetitionInfoFacets> GetFacetsAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var years = new List<int>();
        var provinces = new List<string>();
        var statuses = new List<string>();

        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT YEAR(RfpReleaseDate) AS Y
FROM opportunities.HistoricalOpportunities
WHERE RfpReleaseDate IS NOT NULL
ORDER BY Y DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false)) years.Add(r.GetInt32(0));
        }
        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT ProjectProvince
FROM opportunities.HistoricalOpportunities
WHERE ProjectProvince IS NOT NULL
ORDER BY ProjectProvince;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false)) provinces.Add(r.GetString(0));
        }
        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT HistoricalStatus
FROM opportunities.HistoricalOpportunities
WHERE HistoricalStatus IS NOT NULL
ORDER BY HistoricalStatus;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false)) statuses.Add(r.GetString(0));
        }

        return new CompetitionInfoFacets { Years = years, Provinces = provinces, Statuses = statuses };
    }
}
