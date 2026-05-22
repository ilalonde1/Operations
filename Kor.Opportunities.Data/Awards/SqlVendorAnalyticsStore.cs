#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlVendorAnalyticsStore : IVendorAnalyticsStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlVendorAnalyticsStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<CompetitorProfile> GetCompetitorProfileAsync(string vendorName, CancellationToken ct)
        => (CompetitorProfile)await BuildProfileAsync(
            vendorName,
            "AwardedToOrganization",
            "AwardingOrganization",
            ct,
            isCompetitor: true).ConfigureAwait(false);

    public async Task<BuyerProfile> GetBuyerProfileAsync(string buyerName, CancellationToken ct)
        => (BuyerProfile)await BuildProfileAsync(
            buyerName,
            "AwardingOrganization",
            "AwardedToOrganization",
            ct,
            isCompetitor: false).ConfigureAwait(false);

    private async Task<object> BuildProfileAsync(
        string targetName,
        string filterColumn,
        string rollupColumn,
        CancellationToken ct,
        bool isCompetitor)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        decimal lifetimeValue = 0;
        var lifetimeCount = 0;
        decimal? avgValue = null;
        DateTimeOffset? firstAt = null;
        DateTimeOffset? lastAt = null;

        await using (var cmd = new SqlCommand($@"
SELECT  ISNULL(SUM(ContractValue), 0) AS TotalValue,
        COUNT(*) AS TotalCount,
        AVG(CAST(ContractValue AS decimal(18,2))) AS AvgValue,
        MIN(AwardedAtUtc) AS FirstAt,
        MAX(AwardedAtUtc) AS LastAt
FROM    opportunities.OpportunityAwards
WHERE   {filterColumn} = @name;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = targetName;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                lifetimeValue = r.GetDecimal(0);
                lifetimeCount = r.GetInt32(1);
                avgValue = r.IsDBNull(2) ? null : r.GetDecimal(2);
                firstAt = r.IsDBNull(3) ? null : r.GetDateTimeOffset(3);
                lastAt = r.IsDBNull(4) ? null : r.GetDateTimeOffset(4);
            }
        }

        var byYear = new List<YearBucket>();
        await using (var cmd = new SqlCommand($@"
SELECT  YEAR(AwardedAtUtc) AS Y, ISNULL(SUM(ContractValue),0), COUNT(*)
FROM    opportunities.OpportunityAwards
WHERE   {filterColumn} = @name AND AwardedAtUtc IS NOT NULL
GROUP   BY YEAR(AwardedAtUtc)
ORDER   BY Y DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = targetName;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                byYear.Add(new YearBucket(r.GetInt32(0), r.GetDecimal(1), r.GetInt32(2)));
        }

        var topRollup = new List<OrgRollup>();
        await using (var cmd = new SqlCommand($@"
SELECT  TOP 15 {rollupColumn} AS Name, ISNULL(SUM(ContractValue),0), COUNT(*)
FROM    opportunities.OpportunityAwards
WHERE   {filterColumn} = @name AND {rollupColumn} IS NOT NULL AND {rollupColumn} <> ''
GROUP   BY {rollupColumn}
ORDER   BY SUM(ContractValue) DESC, COUNT(*) DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = targetName;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                topRollup.Add(new OrgRollup(r.GetString(0), r.GetDecimal(1), r.GetInt32(2)));
        }

        var bySource = new List<OrgRollup>();
        await using (var cmd = new SqlCommand($@"
SELECT  s.Name, ISNULL(SUM(a.ContractValue),0), COUNT(*)
FROM    opportunities.OpportunityAwards a
JOIN    opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE   a.{filterColumn} = @name
GROUP   BY s.Name
ORDER   BY SUM(a.ContractValue) DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = targetName;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                bySource.Add(new OrgRollup(r.GetString(0), r.GetDecimal(1), r.GetInt32(2)));
        }

        var recent = new List<AwardListing>();
        await using (var cmd = new SqlCommand($@"
SELECT TOP 25
    a.Id, a.ExternalReference, s.Name AS SourceName, a.Title, a.SolicitationType,
    a.AwardingOrganization, a.AwardedToOrganization, a.ContractValue, a.ContractCurrency,
    a.AwardedAtUtc, a.IssuingLocation, a.ContractNumber, a.SourceUrl
FROM    opportunities.OpportunityAwards a
JOIN    opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE   a.{filterColumn} = @name
ORDER   BY a.AwardedAtUtc DESC, a.Id DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = targetName;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                recent.Add(new AwardListing
                {
                    Id = r.GetInt64(0),
                    ExternalReference = r.GetString(1),
                    SourceName = r.GetString(2),
                    Title = r.GetString(3),
                    SolicitationType = r.IsDBNull(4) ? null : r.GetString(4),
                    AwardingOrganization = r.GetString(5),
                    AwardedToOrganization = r.GetString(6),
                    ContractValue = r.IsDBNull(7) ? null : r.GetDecimal(7),
                    ContractCurrency = r.GetString(8),
                    AwardedAtUtc = r.IsDBNull(9) ? null : r.GetDateTimeOffset(9),
                    IssuingLocation = r.IsDBNull(10) ? null : r.GetString(10),
                    ContractNumber = r.IsDBNull(11) ? null : r.GetString(11),
                    SourceUrl = r.GetString(12),
                });
            }
        }

        if (isCompetitor)
        {
            return new CompetitorProfile
            {
                VendorName = targetName,
                LifetimeValue = lifetimeValue,
                LifetimeCount = lifetimeCount,
                AvgContractValue = avgValue,
                FirstWinAtUtc = firstAt,
                LastWinAtUtc = lastAt,
                ByYear = byYear,
                TopBuyers = topRollup,
                BySource = bySource,
                RecentWins = recent,
            };
        }

        return new BuyerProfile
        {
            BuyerName = targetName,
            LifetimeValue = lifetimeValue,
            LifetimeCount = lifetimeCount,
            AvgContractValue = avgValue,
            FirstAwardAtUtc = firstAt,
            LastAwardAtUtc = lastAt,
            ByYear = byYear,
            TopWinners = topRollup,
            BySource = bySource,
            RecentAwards = recent,
        };
    }
}
