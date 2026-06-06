#nullable enable
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Projects;

public interface IMajorProjectEnrichmentTrackingStore
{
    Task RecordAttemptAsync(
        long majorProjectsInventoryId,
        string providerName,
        EnrichmentResult result,
        DateTimeOffset? nextRefreshAtUtc,
        CancellationToken ct);
}
