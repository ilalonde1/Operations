#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

/// <summary>
/// Read/write access to <c>opportunities.HistoricalOpportunityObservations</c>.
/// Mirror of <c>IOpportunityObservationStore</c> for the archive pipeline.
/// Dedup hash works the same way (unique index on HashSha256).
/// </summary>
public interface IHistoricalOpportunityObservationStore
{
    Task<OpportunityObservation?> TryInsertAsync(OpportunityObservation observation, CancellationToken ct);

    Task LinkAsync(long observationId, long historicalOpportunityId, CancellationToken ct);

    /// <summary>Fetches the observation carrying the given content hash, or null.
    /// Used by ingestion's repair path (see <c>IOpportunityObservationStore</c>).</summary>
    Task<OpportunityObservation?> TryGetByHashAsync(byte[] hashSha256, CancellationToken ct);
}
