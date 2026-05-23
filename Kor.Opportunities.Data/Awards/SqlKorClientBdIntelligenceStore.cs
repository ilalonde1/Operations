#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlKorClientBdIntelligenceStore : IKorClientBdIntelligenceStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlKorClientBdIntelligenceStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<ClientBdIntelligence> LoadByClendorIdAsync(string clendorClientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clendorClientId))
        {
            return Empty(null, null);
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        long? canonId = null;
        {
            await using var cmd = new SqlCommand(
                "SELECT TOP 1 Id FROM opportunities.CanonicalOrg WHERE ClendorClientId = @cl",
                con)
            { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@cl", SqlDbType.VarChar, 32).Value = clendorClientId;
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (v is not null && v is not DBNull)
            {
                canonId = Convert.ToInt64(v);
            }
        }

        if (!canonId.HasValue)
        {
            return Empty(null, clendorClientId);
        }

        KorPursuitSummary pursuits = new(0, 0, 0, 0, 0, null, null);
        {
            await using var cmd = new SqlCommand(@"
SELECT
    COUNT(*),
    SUM(CASE WHEN Stage='Won' THEN 1 ELSE 0 END),
    SUM(CASE WHEN Stage='Submitted' THEN 1 ELSE 0 END),
    SUM(CASE WHEN Stage='Pursuing' THEN 1 ELSE 0 END),
    SUM(CASE WHEN Stage='Lost' THEN 1 ELSE 0 END),
    SUM(BidFee),
    MAX(SubmittedDate)
FROM opportunities.KorPursuits
WHERE BuyerCanonicalOrgId = @id;", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                pursuits = new KorPursuitSummary(
                    r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                    r.IsDBNull(6) ? (DateTimeOffset?)null : new DateTimeOffset(r.GetDateTime(6), TimeSpan.Zero));
            }
        }

        var recentPursuits = new List<KorPursuitRecent>();
        {
            await using var cmd = new SqlCommand(@"
SELECT TOP 8 Id, Title, Stage, BidFee, SubmittedDate, AwardDate
FROM opportunities.KorPursuits
WHERE BuyerCanonicalOrgId = @id
ORDER BY COALESCE(SubmittedDate, PursuitOpenedDate, CreatedAtUtc) DESC;", con)
            { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                recentPursuits.Add(new KorPursuitRecent(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? (decimal?)null : r.GetDecimal(3),
                    r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                    r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5)));
            }
        }

        ExternalAwardActivity extActivity = new(0, 0, null, 0);
        {
            await using var cmd = new SqlCommand(@"
SELECT
    COUNT(*),
    SUM(ContractValue),
    COUNT(DISTINCT AwardedToOrganization)
FROM opportunities.OpportunityAwards
WHERE AwardingCanonicalOrgId = @id;", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                var awardCount = 0;
                decimal? totalValue = null;
                var distinctVendors = 0;
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    awardCount = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    totalValue = r.IsDBNull(1) ? (decimal?)null : r.GetDecimal(1);
                    distinctVendors = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                }

                extActivity = new ExternalAwardActivity(awardCount, 0, totalValue, distinctVendors);
            }

            await using var cmd2 = new SqlCommand(
                "SELECT COUNT(*) FROM opportunities.Opportunities WHERE BuyerCanonicalOrgId = @id;",
                con)
            { CommandTimeout = CommandTimeoutSeconds };
            cmd2.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            var v = await cmd2.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var activeOppCount = v is null || v is DBNull ? 0 : Convert.ToInt32(v);
            extActivity = extActivity with { ActiveOpportunityCount = activeOppCount };
        }

        var recentAwards = new List<ExternalAwardRecent>();
        {
            await using var cmd = new SqlCommand(@"
SELECT TOP 8 a.Title, a.AwardedToOrganization, a.ContractValue, a.AwardedAtUtc, s.Name
FROM opportunities.OpportunityAwards a
LEFT JOIN opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE a.AwardingCanonicalOrgId = @id
ORDER BY a.AwardedAtUtc DESC;", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                recentAwards.Add(new ExternalAwardRecent(
                    r.IsDBNull(0) ? "" : r.GetString(0),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? (decimal?)null : r.GetDecimal(2),
                    r.IsDBNull(3) ? (DateTimeOffset?)null : r.GetDateTimeOffset(3),
                    r.IsDBNull(4) ? null : r.GetString(4)));
            }
        }

        CompetitorActivity compActivity = new(0, null, 0, null);
        {
            await using var cmd = new SqlCommand(@"
SELECT
    COUNT(*),
    SUM(ContractValue),
    COUNT(DISTINCT AwardingOrganization),
    MAX(AwardedAtUtc)
FROM opportunities.OpportunityAwards
WHERE AwardedToCanonicalOrgId = @id;", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                compActivity = new CompetitorActivity(
                    r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    r.IsDBNull(1) ? (decimal?)null : r.GetDecimal(1),
                    r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    r.IsDBNull(3) ? (DateTimeOffset?)null : r.GetDateTimeOffset(3));
            }
        }

        var recentNewsMentions = new List<NewsMentionRecent>();
        {
            await using var cmd = new SqlCommand(@"
SELECT TOP 10
    m.NewsArticleId, a.Title, a.Url, a.PublishedAtUtc,
    f.Name AS FeedName, m.MentionType, m.Confidence, m.Excerpt
FROM   opportunities.NewsArticleOrgMention m
JOIN   opportunities.NewsArticle a ON a.Id = m.NewsArticleId
LEFT   JOIN opportunities.NewsFeed f ON f.Id = a.FeedId
WHERE  m.CanonicalOrgId = @id
ORDER  BY a.PublishedAtUtc DESC, m.CreatedAtUtc DESC;", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                recentNewsMentions.Add(new NewsMentionRecent(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? (DateTimeOffset?)null : r.GetDateTimeOffset(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt32(6),
                    r.IsDBNull(7) ? null : r.GetString(7)));
            }
        }

        return new ClientBdIntelligence(
            canonId,
            clendorClientId,
            pursuits,
            recentPursuits,
            extActivity,
            recentAwards,
            compActivity,
            recentNewsMentions);
    }

    private static ClientBdIntelligence Empty(long? canonId, string? clendorId) =>
        new(
            canonId,
            clendorId,
            new KorPursuitSummary(0, 0, 0, 0, 0, null, null),
            Array.Empty<KorPursuitRecent>(),
            new ExternalAwardActivity(0, 0, null, 0),
            Array.Empty<ExternalAwardRecent>(),
            new CompetitorActivity(0, null, 0, null),
            Array.Empty<NewsMentionRecent>());
}
