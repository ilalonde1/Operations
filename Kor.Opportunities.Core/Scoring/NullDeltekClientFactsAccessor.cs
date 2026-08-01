#nullable enable

namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// No-op implementation. Registered in hosts that have no Deltek ODBC
/// access (Kor.Opportunities.Worker). Always returns null so the scorer
/// applies only the rules-based path.
/// </summary>
public sealed class NullDeltekClientFactsAccessor : IDeltekClientFactsAccessor
{
    public DeltekClientFactsSnapshot? GetFacts(string? deltekClientId) => null;
}
