#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

/// <summary>
/// Write access to <c>opportunities.HistoricalOpportunities</c> — the archive
/// pipeline for closed/awarded RFPs scraped from sources where
/// <see cref="OpportunitySource.IsHistorical"/> is true. Mirrors the subset of
/// <c>IOpportunityStore</c> that <c>IngestionService</c> exercises; lifecycle
/// methods (ChangeStatusAsync, ListAsync) live on the Phase B archive UI store
/// when that lands.
/// </summary>
public interface IHistoricalOpportunityStore
{
    Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct);

    Task<Opportunity> InsertAsync(
        Opportunity opportunity,
        string actorDisplay,
        string? bcBidInternalId,
        string? detailUrl,
        CancellationToken ct);

    Task<Opportunity> UpdateAsync(
        Opportunity opportunity,
        string actorDisplay,
        string? bcBidInternalId,
        string? detailUrl,
        CancellationToken ct);
}
