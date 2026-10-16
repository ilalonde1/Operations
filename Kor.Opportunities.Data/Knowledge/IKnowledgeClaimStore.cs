#nullable enable
namespace Kor.Opportunities.Data.Knowledge;

public sealed record KnowledgeSourceSummary(long Id, string Title, string? Author, int Passages,
    int PassagesExtracted, int Claims, int Reviewed, int Retired);
public sealed record KnowledgeClaimRow(long Id, long SourceId, string SourceTitle, long PassageId,
    string? Ref, string Kind, string Statement, decimal Confidence, IReadOnlyList<string> Concepts,
    DateTimeOffset? ReviewedAtUtc, DateTimeOffset? RetiredAtUtc, string? RetiredReason);
public sealed record KnowledgeConceptRow(long Id, string Slug, string Name, int Claims, int Sources);
public sealed record KnowledgePassageText(long Id, string SourceTitle, string? Ref, string Text);
public sealed record ClaimVerdict(long ClaimId, int Right, int Wrong, int Unclear, int Pending,
    IReadOnlyList<long> WrongDecisionIds);

public interface IKnowledgeClaimStore
{
    Task<IReadOnlyList<ClaimVerdict>> ListClaimVerdictsAsync(CancellationToken ct = default);
    Task<KnowledgeClaimRow?> GetClaimAsync(long claimId, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeSourceSummary>> ListSourcesAsync(CancellationToken ct);
    Task<IReadOnlyList<KnowledgeClaimRow>> ListClaimsAsync(long sourceId, string filter, int take, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeClaimRow>> SampleUnreviewedAsync(long sourceId, int take, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeConceptRow>> ListConceptsAsync(CancellationToken ct);
    Task<IReadOnlyList<KnowledgeClaimRow>> ListClaimsByConceptAsync(long conceptId, int take, CancellationToken ct);
    Task<KnowledgePassageText?> GetPassageAsync(long passageId, CancellationToken ct);
    Task KeepAsync(long claimId, string by, CancellationToken ct);
    Task RetireAsync(long claimId, string by, string reason, CancellationToken ct);
}
