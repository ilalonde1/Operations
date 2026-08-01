#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Thin UI-facing seam over <see cref="IOpportunityStore.FindPossibleDuplicatesAsync"/>
/// for the manual-entry duplicate guard (2026-07-07). Keeps the entry dialog
/// free of store/"take" details and DI-resolvable independently.
/// </summary>
public interface IOpportunityDuplicateFinder
{
    Task<IReadOnlyList<OpportunityDuplicateCandidate>> FindAsync(
        string buyerName, string name, string? city, string? excludeKey, CancellationToken ct);
}

public sealed class OpportunityDuplicateFinder : IOpportunityDuplicateFinder
{
    private const int MaxCandidates = 6;

    private readonly IOpportunityStore _store;

    public OpportunityDuplicateFinder(IOpportunityStore store) => _store = store;

    public Task<IReadOnlyList<OpportunityDuplicateCandidate>> FindAsync(
        string buyerName, string name, string? city, string? excludeKey, CancellationToken ct)
        => _store.FindPossibleDuplicatesAsync(buyerName, name, city, excludeKey, MaxCandidates, ct);
}
