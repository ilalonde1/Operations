#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Pulls observations from one source (HTTP CSV, RSS, JSON API, IMAP, etc.).
/// Implementations must NOT touch the database — that's the IngestionService's
/// job. Each provider just turns "go fetch from this source" into a list of
/// <see cref="OpportunityCandidate"/> ready for hashing + storage.
/// </summary>
public interface IOpportunityProvider
{
    /// <summary>Which <see cref="OpportunitySourceType"/> this provider handles.
    /// IngestionService dispatches on this value.</summary>
    OpportunitySourceType SourceType { get; }

    /// <summary>Pull all observations available from <paramref name="source"/>
    /// right now. Implementations should apply source-side filters before
    /// yielding (e.g. CanadaBuys: drop non-construction, drop non-BC/AB) so the
    /// caller doesn't waste hashes on rows we'll always discard.</summary>
    Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct);
}
