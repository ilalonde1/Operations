#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record AwardAgentEnrichmentPayload
{
    public string? VendorProfile { get; init; }
    public string? ContractContext { get; init; }
    public bool? CompetesWithKor { get; init; }
    public string? CompetitionNotes { get; init; }
    public IReadOnlyList<string> SourceUrls { get; init; } = Array.Empty<string>();
}

public sealed record PendingAgentEnrichmentRow(
    long Id,
    string ExternalReference,
    string Title,
    string AwardingOrganization,
    string AwardedToOrganization,
    decimal? ContractValue,
    string ContractCurrency,
    DateTimeOffset? AwardedAtUtc,
    string? IssuingLocation,
    string SourceName,
    int Attempts);
