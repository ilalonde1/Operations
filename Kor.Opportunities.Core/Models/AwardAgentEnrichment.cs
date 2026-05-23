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

    public string? VendorWebsite { get; init; }
    public string? VendorHqLocation { get; init; }
    public string? VendorSizeBand { get; init; }
    public int? VendorFoundedYear { get; init; }
    public IReadOnlyList<string> VendorSpecialties { get; init; } = Array.Empty<string>();
    public IReadOnlyList<VendorLeader> VendorLeadership { get; init; } = Array.Empty<VendorLeader>();
    public string? VendorOwnershipStatus { get; init; }
    public string? VendorParentCompany { get; init; }
    public IReadOnlyList<string> VendorLocations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VendorCertifications { get; init; } = Array.Empty<string>();
}

public sealed record VendorLeader(string Name, string? Title);

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
