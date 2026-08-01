#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// Persists the scoring profile to <c>opportunities.ScoringProfile</c>
/// keyed by <c>ProfileKey = 'Default'</c>.
/// </summary>
public interface IScoringProfileStore
{
    /// <summary>Returns null if no profile has been saved yet.</summary>
    Task<ScoringOptions?> LoadAsync(CancellationToken ct);

    /// <summary>Upserts the active profile (ProfileKey = "Default").</summary>
    Task SaveAsync(ScoringOptions options, CancellationToken ct);
}
