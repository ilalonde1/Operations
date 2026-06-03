#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Awards;

public interface IArchitectDisplacementBriefStore
{
    /// <summary>
    /// Returns the brief for an architect, or null if none has been generated.
    /// </summary>
    Task<ArchitectDisplacementBrief?> GetByArchitectAsync(long architectCanonicalOrgId, CancellationToken ct);

    /// <summary>
    /// Idempotent upsert keyed on ArchitectCanonicalOrgId. Used by the
    /// BdResearchImport displacement-briefs handler.
    /// </summary>
    Task UpsertAsync(
        long architectCanonicalOrgId,
        string? market,
        string? korPriority,
        decimal? confidenceScore,
        string briefJson,
        System.DateTimeOffset generatedAtUtc,
        CancellationToken ct);
}
