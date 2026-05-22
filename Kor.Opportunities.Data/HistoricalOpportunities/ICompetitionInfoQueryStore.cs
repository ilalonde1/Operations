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

public interface ICompetitionInfoQueryStore
{
    /// <summary>
    /// Returns archive rows matching the filter, ordered by RfpReleaseDate DESC
    /// then Id DESC. Joins HistoricalOpportunityDocuments for the document
    /// counts. Read-only  no concurrency token, no caching.
    /// </summary>
    Task<IReadOnlyList<HistoricalOpportunityListing>> ListAsync(
        CompetitionInfoFilter filter,
        CancellationToken ct);

    /// <summary>Distinct values for filter dropdowns. Cheap query, no filter applied.</summary>
    Task<CompetitionInfoFacets> GetFacetsAsync(CancellationToken ct);
}

public sealed record CompetitionInfoFacets
{
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Provinces { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
}
