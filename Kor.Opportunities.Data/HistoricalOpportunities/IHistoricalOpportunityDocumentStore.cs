#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed record DiscoveredDocument(string FileName, string SourceUrl);

public sealed record PendingDocumentRow(
    long Id,
    long HistoricalOpportunityId,
    string FileName,
    string SourceUrl,
    int DownloadAttemptCount);

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

    Task<IReadOnlyList<PendingDocumentRow>> ListPendingAsync(
        int batchSize,
        int maxAttempts,
        CancellationToken ct);

    Task RecordSuccessAsync(
        long id,
        string localPath,
        byte[] sha256,
        long sizeBytes,
        string? contentType,
        CancellationToken ct);

    Task RecordFailureAsync(long id, string error, CancellationToken ct);

    Task<IReadOnlyList<HistoricalOpportunityDocumentListing>> ListByOpportunityAsync(
        long historicalOpportunityId,
        CancellationToken ct);
}
