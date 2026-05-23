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
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<ClientBdIntelligence> LoadByClendorIdAsync(string clendorClientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clendorClientId))
            return Empty(null, null);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        long? canonId = null;
        await using (var cmd = new SqlCommand(
            "SELECT TOP 1 Id FROM opportunities.CanonicalOrg WHERE ClendorClientId = @cl", con)
        { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@cl", SqlDbType.VarChar, 32).Value = clendorClientId;
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (v is not null && v is not DBNull) canonId = Convert.ToInt64(v);
        }

        if (!canonId.HasValue) return Empty(null, clendorClientId);

        var pursuits = await LoadPursuitSummaryAsync(con, canonId.Value, ct).ConfigureAwait(false);
        var recentPursuits = await LoadRecentPursuitsAsync(con, canonId.Value, ct).ConfigureAwait(false);
        var external = await LoadExternalActivityAsync(con, canonId.Value, ct).ConfigureAwait(false);
        var recentAwards = await LoadRecentExternalAwardsAsync(con, canonId.Value, ct).ConfigureAwait(false);
        var competitor = await LoadCompetitorActivityAsync(con, canonId.Value, ct).ConfigureAwait(false);

        return new ClientBdIntelligence(
            canonId,
            clendorClientId,
            pursuits,
            recentPursuits,
            external,
            recentAwards,
            competitor);
    }

    private static async Task<KorPursuitSummary> LoadPursuitSummaryAsync(SqlConnection con, long canonId, CancellationToken ct)
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
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return new KorPursuitSummary(0, 0, 0, 0, 0, null, null);

        return new KorPursuitSummary(
            r.IsDBNull(0) ? 0 : r.GetInt32(0),
            r.IsDBNull(1) ? 0 : r.GetInt32(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2),
            r.IsDBNull(3) ? 0 : r.GetInt32(3),
            r.IsDBNull(4) ? 0 : r.GetInt32(4),
            r.IsDBNull(5) ? null : r.GetDecimal(5),
            r.IsDBNull(6) ? null : new DateTimeOffset(r.GetDateTime(6), TimeSpan.Zero));
    }

    private static async Task<IReadOnlyList<KorPursuitRecent>> LoadRecentPursuitsAsync(SqlConnection con, long canonId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
SELECT TOP 8 Id, Title, Stage, BidFee, SubmittedDate, AwardDate
FROM opportunities.KorPursuits
WHERE BuyerCanonicalOrgId = @id
ORDER BY COALESCE(SubmittedDate, PursuitOpenedDate, CreatedAtUtc) DESC;", con)
        { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<KorPursuitRecent>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new KorPursuitRecent(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDecimal(3),
                r.IsDBNull(4) ? null : r.GetDateTime(4),
                r.IsDBNull(5) ? null : r.GetDateTime(5)));
        }

        return rows;
    }

    private static async Task<ExternalAwardActivity> LoadExternalActivityAsync(SqlConnection con, long canonId, CancellationToken ct)
    {
        int awardCount;
        decimal? totalVal;
        int distinct;

        await using (var cmd = new SqlCommand(@"
SELECT
    COUNT(*),
    SUM(ContractValue),
    COUNT(DISTINCT AwardedToOrganization)
FROM opportunities.OpportunityAwards
WHERE AwardingCanonicalOrgId = @id;", con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                awardCount = 0;
                totalVal = null;
                distinct = 0;
            }
            else
            {
                awardCount = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                totalVal = r.IsDBNull(1) ? null : r.GetDecimal(1);
                distinct = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            }
        }

        await using var cmd2 = new SqlCommand(
            "SELECT COUNT(*) FROM opportunities.Opportunities WHERE BuyerCanonicalOrgId = @id;", con)
        { CommandTimeout = CommandTimeoutSeconds };
        cmd2.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
        var v = await cmd2.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var activeOppCount = v is null || v is DBNull ? 0 : Convert.ToInt32(v);

        return new ExternalAwardActivity(awardCount, activeOppCount, totalVal, distinct);
    }

    private static async Task<IReadOnlyList<ExternalAwardRecent>> LoadRecentExternalAwardsAsync(SqlConnection con, long canonId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
SELECT TOP 8 a.Title, a.AwardedToOrganization, a.ContractValue, a.AwardedAtUtc, s.Name
FROM opportunities.OpportunityAwards a
LEFT JOIN opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE a.AwardingCanonicalOrgId = @id
ORDER BY a.AwardedAtUtc DESC;", con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<ExternalAwardRecent>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ExternalAwardRecent(
                r.IsDBNull(0) ? "" : r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetDecimal(2),
                r.IsDBNull(3) ? null : r.GetDateTimeOffset(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    private static async Task<CompetitorActivity> LoadCompetitorActivityAsync(SqlConnection con, long canonId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
SELECT
    COUNT(*),
    SUM(ContractValue),
    COUNT(DISTINCT AwardingOrganization),
    MAX(AwardedAtUtc)
FROM opportunities.OpportunityAwards
WHERE AwardedToCanonicalOrgId = @id;", con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return new CompetitorActivity(0, null, 0, null);

        return new CompetitorActivity(
            r.IsDBNull(0) ? 0 : r.GetInt32(0),
            r.IsDBNull(1) ? null : r.GetDecimal(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetDateTimeOffset(3));
    }

    private static ClientBdIntelligence Empty(long? canonId, string? clendorId)
        => new(
            canonId,
            clendorId,
            new KorPursuitSummary(0, 0, 0, 0, 0, null, null),
            Array.Empty<KorPursuitRecent>(),
            new ExternalAwardActivity(0, 0, null, 0),
            Array.Empty<ExternalAwardRecent>(),
            new CompetitorActivity(0, null, 0, null));
}
