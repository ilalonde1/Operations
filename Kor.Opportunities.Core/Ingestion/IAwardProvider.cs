#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>Provider that scrapes/fetches awarded contracts (competitive
/// intelligence). Parallel to IOpportunityProvider; different domain.</summary>
public interface IAwardProvider
{
    OpportunitySourceType SourceType { get; }

    Task<IReadOnlyList<AwardCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct);
}
