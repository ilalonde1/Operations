#nullable enable
using System;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>DTO returned by IAwardProvider.FetchAsync. Provider's responsibility
/// is mapping portal fields to this shape; the AwardIngestionService persists.</summary>
public sealed record AwardCandidate
{
    public string ExternalReference { get; init; } = "";
    public string Title { get; init; } = "";
    public string? SolicitationType { get; init; }
    public string AwardingOrganization { get; init; } = "";
    public string AwardedToOrganization { get; init; } = "";
    public decimal? ContractValue { get; init; }
    public string ContractCurrency { get; init; } = "CAD";
    public DateTimeOffset? AwardedAtUtc { get; init; }
    public string? IssuingLocation { get; init; }
    public string? SupplierAddress { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContractNumber { get; init; }
    public string SourceUrl { get; init; } = "";
    public string? RawJson { get; init; }
}
