#nullable enable
using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Returns rows that have a DetailUrl set but no DetailScrapedAtUtc, ordered
    /// by IngestedAtUtc DESC so the analyst sees the newest archive entries
    /// enrich first. Used by the enrichment loop.
    /// </summary>
    Task<IReadOnlyList<PendingEnrichmentRow>> ListPendingEnrichmentAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Writes enrichment columns onto a single HistoricalOpportunities row by Id
    /// and stamps DetailScrapedAtUtc to sysdatetimeoffset(). Uses COALESCE on each
    /// payload field so partial extractions don't blank previously-populated
    /// values. Idempotent: re-running with the same payload is a no-op semantically.
    /// </summary>
    Task UpdateEnrichmentAsync(
        long historicalOpportunityId,
        HistoricalOpportunityEnrichmentPayload payload,
        CancellationToken ct);
}

public sealed record PendingEnrichmentRow(long Id, string OpportunityKey, string DetailUrl);

public sealed record HistoricalOpportunityEnrichmentPayload
{
    public string? Commodities { get; init; }
    public int? AmendmentCount { get; init; }
    public string? FullDescription { get; init; }
    public decimal? EstimatedValue { get; init; }
    public string? EstimatedValueCurrency { get; init; }
    public string? AwardedToOrganization { get; init; }
    public decimal? AwardedValue { get; init; }
    public string? AwardedCurrency { get; init; }
    public DateTimeOffset? AwardedAtUtc { get; init; }
}
