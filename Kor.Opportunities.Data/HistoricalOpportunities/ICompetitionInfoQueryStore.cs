#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed record CompetitionInfoFilter
{
    public string? KeywordLike { get; init; }
    public int? Year { get; init; }
    public string? Province { get; init; }
    public string? HistoricalStatus { get; init; }
    public decimal? MinEstimatedValue { get; init; }
    public bool? HasDocuments { get; init; }
    public int? MaxRows { get; init; }
}

public sealed record HistoricalOpportunityDetail
{
    public long Id { get; init; }
    public string OpportunityKey { get; init; } = "";
    public string Name { get; init; } = "";
    public string BuyerName { get; init; } = "";
    public string? ProjectProvince { get; init; }
    public string? HistoricalStatus { get; init; }
    public DateOnly? RfpReleaseDate { get; init; }
    public DateTimeOffset? SubmissionDeadlineUtc { get; init; }
    public decimal? EstimatedValue { get; init; }
    public string? Commodities { get; init; }
    public int? AmendmentCount { get; init; }
    public string? FullDescription { get; init; }
    public string? DetailUrl { get; init; }
    public string? AwardedToOrganization { get; init; }
    public decimal? AwardedValue { get; init; }
}

public interface ICompetitionInfoQueryStore
{
    /// <summary>
    /// Returns archive rows matching the filter, ordered by RfpReleaseDate DESC
    /// then Id DESC. Joins HistoricalOpportunityDocuments for the document
    /// counts. Read-only, no concurrency token, no caching.
    /// </summary>
    Task<IReadOnlyList<HistoricalOpportunityListing>> ListAsync(CompetitionInfoFilter filter, CancellationToken ct);

    Task<HistoricalOpportunityDetail?> GetDetailAsync(long historicalOpportunityId, CancellationToken ct);

    /// <summary>Distinct values for filter dropdowns. Cheap query, no filter applied.</summary>
    Task<CompetitionInfoFacets> GetFacetsAsync(CancellationToken ct);
}

public sealed record CompetitionInfoFacets
{
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Provinces { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
}
