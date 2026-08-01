#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IEnrichmentTrackingStore
{
    /// <summary>
    /// Canonical-org IDs that are due for refresh by the given provider.
    /// Returns ids where either (a) no tracking row exists yet, OR
    /// (b) NextRefreshAtUtc is in the past and Status is not 'blocked'.
    /// </summary>
    Task<IReadOnlyList<long>> ListDueAsync(string providerName, int batchSize, CancellationToken ct);

    Task<EnrichmentTrackingRow?> GetAsync(long canonicalOrgId, string providerName, CancellationToken ct);

    Task<IReadOnlyList<EnrichmentTrackingRow>> ListByOrgAsync(long canonicalOrgId, CancellationToken ct);

    /// <summary>
    /// Record the outcome of a refresh attempt. nextRefreshUtc is the scheduler hint
    /// for when to try again (e.g. now + TTL on success, now + backoff on failure).
    /// </summary>
    Task RecordAttemptAsync(
        long canonicalOrgId,
        string providerName,
        EnrichmentResult result,
        System.DateTimeOffset nextRefreshAtUtc,
        CancellationToken ct);

    Task<int> CountByStatusAsync(string providerName, string status, CancellationToken ct);
}
