using Kor.Opportunities.Knowledge.Claims;

namespace Kor.Opportunities.Knowledge.Store;

public sealed record KnowledgeSource(long Id, string Kind, string Title, string? Author, short? Year,
    string Origin, string Sha256, string Licence, string? EmbeddingModel, int Passages, int Tokens,
    DateTimeOffset IngestedAtUtc, string IngestedBy, int Claims);
public sealed record KnowledgePassage(long Id, long SourceId, int Ordinal, string? Ref, string Text, int Tokens);
public sealed record KnowledgeHit(KnowledgePassage Passage, string SourceTitle, float Score);
public sealed record KnowledgeClaim(long Id, long SourceId, string SourceTitle, long PassageId, string Kind, string Statement, decimal Confidence);
public sealed record KnowledgeClaimHit(long ClaimId, long SourceId, string SourceTitle, long PassageId, string Kind, string Statement, float Score);
/// <summary>Directed relation: the From claim supports, contradicts or refines the To claim.</summary>
public sealed record KnowledgeClaimRelation(long FromClaimId, string FromStatement, string FromSourceTitle,
    long ToClaimId, string ToStatement, string ToSourceTitle, string Kind, string? Note);

public interface IKnowledgeStore
{
    Task<IReadOnlyList<(long Id, string Statement)>> ListUnembeddedClaimsAsync(int take, CancellationToken ct = default);
    Task SetClaimEmbeddingsAsync(IReadOnlyList<(long ClaimId, byte[] Embedding)> embeddings, string model, CancellationToken ct = default);
    Task<IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)>> LoadClaimVectorsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeClaim>> GetClaimsAsync(IReadOnlyList<long> ids, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeClaimRelation>> ListRelationsAsync(IReadOnlyCollection<long> claimIds, CancellationToken ct = default);
    Task<(int Written, int Rejected)> ImportRelationsAsync(RelationFile file, string by, CancellationToken ct = default);
    Task<ClaimFile> ExportAsync(long sourceId, CancellationToken ct = default);
    Task<(int PassagesCovered, int ClaimsWritten, int ClaimsRejected)> ImportClaimsAsync(
        ClaimFile file, bool force, string by, CancellationToken ct = default);
    Task<KnowledgeSource?> FindBySha256Async(string sha256, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct = default);
    Task<long> IngestAsync(KnowledgeSource source,
        IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages, bool force, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgePassage>> ListUnembeddedAsync(long? sourceId = null, CancellationToken ct = default);
    Task SetEmbeddingsAsync(IReadOnlyList<(long PassageId, byte[] Embedding)> embeddings, CancellationToken ct = default);
    Task<IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>> LoadVectorsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgePassage>> GetPassagesAsync(IReadOnlyList<long> ids, CancellationToken ct = default);
}

/// <summary>The transaction rechecks the hash, including races with another ingest process.</summary>
public sealed class AlreadyIngestedException(long sourceId)
    : InvalidOperationException($"Already ingested as source {sourceId}.")
{
    public long SourceId { get; } = sourceId;
}
