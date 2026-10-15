#nullable enable
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Knowledge;

/// <summary>Review changes attribution and retirement fields; original claims stay intact.</summary>
public sealed class SqlKnowledgeClaimStore(string connectionString) : IKnowledgeClaimStore
{
    private const string ClaimSelect = """
        SELECT TOP (@take) c.Id, c.SourceId, s.Title, c.PassageId, p.Ref, c.Kind, c.Statement,
            c.Confidence, c.ReviewedAtUtc, c.RetiredAtUtc, c.RetiredReason,
            (SELECT k.Name FROM knowledge.ClaimConcept link
             JOIN knowledge.Concept k ON k.Id = link.ConceptId
             WHERE link.ClaimId = c.Id ORDER BY k.Slug FOR JSON PATH) AS Concepts
        FROM knowledge.Claim c JOIN knowledge.Source s ON s.Id = c.SourceId
        JOIN knowledge.Passage p ON p.Id = c.PassageId
        """;

    public Task<IReadOnlyList<KnowledgeSourceSummary>> ListSourcesAsync(CancellationToken ct) => ReadAsync("""
        SELECT s.Id, s.Title, s.Author,
            (SELECT COUNT(*) FROM knowledge.Passage p WHERE p.SourceId = s.Id),
            (SELECT COUNT(*) FROM knowledge.Passage p WHERE p.SourceId = s.Id AND p.ClaimsExtractedAtUtc IS NOT NULL),
            (SELECT COUNT(*) FROM knowledge.Claim c WHERE c.SourceId = s.Id),
            (SELECT COUNT(*) FROM knowledge.Claim c WHERE c.SourceId = s.Id AND c.ReviewedAtUtc IS NOT NULL),
            (SELECT COUNT(*) FROM knowledge.Claim c WHERE c.SourceId = s.Id AND c.RetiredAtUtc IS NOT NULL)
        FROM knowledge.Source s ORDER BY s.Title, s.Id;
        """, _ => { }, r => new KnowledgeSourceSummary(r.GetInt64(0), r.GetString(1), Text(r, 2),
            r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7)), ct);

    public Task<IReadOnlyList<KnowledgeClaimRow>> ListClaimsAsync(long sourceId, string filter, int take, CancellationToken ct)
    {
        var predicate = filter switch
        {
            "Unreviewed" => "c.ReviewedAtUtc IS NULL AND c.RetiredAtUtc IS NULL",
            "Kept" => "c.ReviewedAtUtc IS NOT NULL AND c.RetiredAtUtc IS NULL",
            "Retired" => "c.RetiredAtUtc IS NOT NULL",
            "All" => "1 = 1",
            _ => throw new ArgumentException("Choose a claim filter.", nameof(filter)),
        };
        return ReadAsync(ClaimSelect + " WHERE c.SourceId = @id AND " + predicate + " ORDER BY p.Ordinal, c.Id;",
            cmd => Page(cmd, sourceId, take), ReadClaim, ct);
    }

    public Task<IReadOnlyList<KnowledgeClaimRow>> SampleUnreviewedAsync(long sourceId, int take, CancellationToken ct) =>
        ReadAsync(ClaimSelect + " WHERE c.SourceId = @id AND c.ReviewedAtUtc IS NULL AND c.RetiredAtUtc IS NULL ORDER BY NEWID();",
            cmd => Page(cmd, sourceId, take), ReadClaim, ct);

    public Task<IReadOnlyList<KnowledgeConceptRow>> ListConceptsAsync(CancellationToken ct) => ReadAsync("""
        SELECT k.Id, k.Slug, k.Name, COUNT(c.Id), COUNT(DISTINCT c.SourceId)
        FROM knowledge.Concept k LEFT JOIN knowledge.ClaimConcept link ON link.ConceptId = k.Id
        LEFT JOIN knowledge.Claim c ON c.Id = link.ClaimId
        GROUP BY k.Id, k.Slug, k.Name ORDER BY k.Name, k.Slug;
        """, _ => { }, r => new KnowledgeConceptRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)), ct);

    public Task<IReadOnlyList<KnowledgeClaimRow>> ListClaimsByConceptAsync(long conceptId, int take, CancellationToken ct) =>
        ReadAsync(ClaimSelect + " WHERE EXISTS (SELECT 1 FROM knowledge.ClaimConcept link WHERE link.ClaimId = c.Id AND link.ConceptId = @id) ORDER BY s.Title, p.Ordinal, c.Id;",
            cmd => Page(cmd, conceptId, take), ReadClaim, ct);

    public async Task<KnowledgePassageText?> GetPassageAsync(long passageId, CancellationToken ct)
    {
        var rows = await ReadAsync("""
            SELECT p.Id, s.Title, p.Ref, p.[Text] FROM knowledge.Passage p
            JOIN knowledge.Source s ON s.Id = p.SourceId WHERE p.Id = @id;
            """, cmd => cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = passageId,
            r => new KnowledgePassageText(r.GetInt64(0), r.GetString(1), Text(r, 2), r.GetString(3)), ct).ConfigureAwait(false);
        return rows.SingleOrDefault();
    }

    public Task KeepAsync(long claimId, string by, CancellationToken ct) => ExecuteAsync("""
        UPDATE knowledge.Claim SET ReviewedAtUtc = sysdatetimeoffset(), ReviewedBy = @by WHERE Id = @id;
        """, cmd => Identity(cmd, claimId, by), ct);

    public Task RetireAsync(long claimId, string by, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200)
            throw new ArgumentException("Give a reason of 1–200 characters.", nameof(reason));
        return ExecuteAsync("""
            UPDATE knowledge.Claim SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason,
                ReviewedAtUtc = sysdatetimeoffset(), ReviewedBy = @by
            WHERE Id = @id AND RetiredAtUtc IS NULL;
            """, cmd => { Identity(cmd, claimId, by); cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 200).Value = reason.Trim(); }, ct);
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Action<SqlCommand> parameters, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add(map(reader));
        return rows;
    }

    private async Task ExecuteAsync(string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        parameters(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Page(SqlCommand cmd, long id, int take)
    {
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
    }
    private static void Identity(SqlCommand cmd, long id, string by)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > 150) throw new ArgumentException("A reviewer is required.", nameof(by));
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = by;
    }
    private static KnowledgeClaimRow ReadClaim(SqlDataReader r)
    {
        using var concepts = JsonDocument.Parse(r.GetString(11));
        return new KnowledgeClaimRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), Text(r, 4),
            r.GetString(5), r.GetString(6), r.GetDecimal(7),
            concepts.RootElement.EnumerateArray().Select(c => c.GetProperty("Name").GetString()!).ToArray(),
            Date(r, 8), Date(r, 9), Text(r, 10));
    }
    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTimeOffset? Date(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
}
