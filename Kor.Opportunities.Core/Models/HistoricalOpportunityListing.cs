#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Grid-shaped projection of <c>opportunities.HistoricalOpportunities</c>
/// for the Competition Info window. Only the columns the grid actually
/// renders  no FullDescription, no audit, no RowVersion.
/// </summary>
public sealed record HistoricalOpportunityListing
{
    public long Id { get; init; }
    public string OpportunityKey { get; init; } = "";
    public string? BcBidInternalId { get; init; }
    public string Name { get; init; } = "";
    public string BuyerName { get; init; } = "";
    public string? ProjectProvince { get; init; }
    public string? HistoricalStatus { get; init; }
    public DateOnly? RfpReleaseDate { get; init; }
    public DateTimeOffset? SubmissionDeadlineUtc { get; init; }
    public decimal? EstimatedValue { get; init; }
    public string? Commodities { get; init; }
    public int? AmendmentCount { get; init; }
    public string? AwardedToOrganization { get; init; }
    public decimal? AwardedValue { get; init; }
    public int DocumentCount { get; init; }
    public int DownloadedDocumentCount { get; init; }
    public DateTimeOffset? DetailScrapedAtUtc { get; init; }
    public string? DetailUrl { get; init; }
}
