#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Grid-shaped projection of opportunities.OpportunityAwards for the
/// Competition Info Awards tab. Shows who won what, for how much, and where.
/// </summary>
public sealed record AwardListing
{
    public long Id { get; init; }
    public string ExternalReference { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string Title { get; init; } = "";
    public string? SolicitationType { get; init; }
    public string AwardingOrganization { get; init; } = "";
    public string AwardedToOrganization { get; init; } = "";
    public decimal? ContractValue { get; init; }
    public string ContractCurrency { get; init; } = "CAD";
    public DateTimeOffset? AwardedAtUtc { get; init; }
    public string? IssuingLocation { get; init; }
    public string? ContractNumber { get; init; }
    public string SourceUrl { get; init; } = "";
}
