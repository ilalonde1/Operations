#nullable enable
using System;
using System.Threading;
using Kor.Opportunities.Core.Scoring;

namespace Kor.Operations.App.Crm;

internal sealed class DeltekClientFactsAccessor : IDeltekClientFactsAccessor
{
    private readonly IDeltekClientContextService _service;

    public DeltekClientFactsAccessor(IDeltekClientContextService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public DeltekClientFactsSnapshot? GetFacts(string? deltekClientId)
    {
        if (string.IsNullOrWhiteSpace(deltekClientId)) return null;

        DeltekClientIntelligence? intelligence;
        try
        {
            // .GetAwaiter().GetResult(): same idiom ScoringOptionsAccessor
            // uses. The inner LoadAsync uses Task.Run so there's no sync-context
            // capture; cache hits return immediately via Task.FromResult.
            intelligence = _service
                .LoadAsync(deltekClientId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Best-effort - if Deltek is unreachable, scoring still works,
            // we just don't apply the Deltek bonuses this turn.
            return null;
        }

        if (intelligence is null) return null;

        return new DeltekClientFactsSnapshot(
            HasAnyHistory: intelligence.ProjectCount > 0,
            PriorWork: intelligence.Company?.PriorWork ?? false,
            Recommend: intelligence.Company?.Recommend ?? false,
            LifetimeFee: intelligence.LifetimeFee);
    }
}
