#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed record DiscoveredDocument(string FileName, string SourceUrl);

/// <summary>
/// Write access to <c>opportunities.HistoricalOpportunityDocuments</c>.
/// Used by the enrichment loop to register document links discovered on a
/// historical detail page. The download itself is Phase B3.
/// </summary>
public interface IHistoricalOpportunityDocumentStore
{
    /// <summary>
    /// Idempotent insert of multiple document rows for one opportunity.
    /// Skips rows whose (HistoricalOpportunityId, SourceUrl) already exists
    /// (unique-index on that pair). Returns the count actually inserted.
    /// </summary>
    Task<int> UpsertManyAsync(
        long historicalOpportunityId,
        IReadOnlyList<DiscoveredDocument> documents,
        CancellationToken ct);
}
