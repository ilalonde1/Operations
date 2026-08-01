#nullable enable

namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// Lean per-row snapshot consumed by <see cref="IOpportunityScoringService"/>.
/// Wraps the rich DeltekClientIntelligence to keep the Core package free
/// of Windows-only ODBC types.
/// </summary>
public sealed record DeltekClientFactsSnapshot(
    bool HasAnyHistory,
    bool PriorWork,
    bool Recommend,
    decimal LifetimeFee);

/// <summary>
/// Synchronous accessor to per-client Deltek facts used during scoring.
/// Implementations decide whether to hit ODBC (App side) or no-op (Worker).
/// Returns null when no client id is given OR the lookup fails OR no
/// Clendor row exists. Callers must treat null as "no Deltek bonuses."
/// </summary>
public interface IDeltekClientFactsAccessor
{
    DeltekClientFactsSnapshot? GetFacts(string? deltekClientId);
}
