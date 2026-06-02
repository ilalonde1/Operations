#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public static class OrgKinds
{
    public const string KorClient = "KorClient";
    public const string Vendor = "Vendor";
    public const string Buyer = "Buyer";
    public const string Architect = "Architect";
    public const string GeneralContractor = "GC";
    public const string Subcontractor = "Subcontractor";
    public const string Developer = "Developer";
    public const string Competitor = "Competitor";
    public const string KorStructural = "KorStructural"; // KOR's own canonical row (self), used for KOR/BMZ award attribution
    public const string Unknown = "Unknown";
}

public static class OrgAliasSources
{
    public const string OpportunityAwardsAwardedTo = "OpportunityAwards.AwardedTo";
    public const string OpportunityAwardsAwarding = "OpportunityAwards.Awarding";
    public const string OpportunitiesBuyer = "Opportunities.Buyer";
    public const string VendorSiteCrawl = "VendorSiteCrawl";
    public const string ClendorName = "Clendor.Name";
    public const string Manual = "Manual";
}

public sealed record CanonicalOrgRow(
    long Id,
    string Kind,
    string DisplayName,
    string? ClendorClientId,
    string? Website,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int KorProjectsCount,
    DateTimeOffset? LastKorProjectAtUtc);

public sealed record OrgAliasRow(
    long Id,
    long? CanonicalOrgId,
    string RawName,
    string Source,
    int Confidence,
    string? ClassifiedBy,
    DateTimeOffset? ClassifiedAtUtc,
    string? Notes,
    DateTimeOffset CreatedAtUtc);
