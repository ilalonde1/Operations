#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public static class PursuitStages
{
    public const string Considering = "Considering";
    public const string Pursuing = "Pursuing";
    public const string Submitted = "Submitted";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public const string Withdrawn = "Withdrawn";
    public const string Declined = "Declined";

    public static readonly string[] All =
    {
        Considering, Pursuing, Submitted, Won, Lost, Withdrawn, Declined
    };
}

public static class KorRoles
{
    public const string Prime = "Prime";
    public const string Sub = "Sub";
    public const string Jv = "JV";
    public const string Support = "Support";

    public static readonly string[] All = { Prime, Sub, Jv, Support };
}

public static class PursuitExternalSources
{
    public const string DeltekCustomProposal = "Deltek.CustomProposal";
    public const string DeltekPR = "Deltek.PR";
    public const string DeltekPRProposals = "Deltek.PRProposals";
}

public sealed record KorPursuitRow(
    long Id,
    string Stage,
    long? OpportunityId,
    long? HistoricalOpportunityId,
    long? OpportunityAwardId,
    string? SourceExternalRef,
    long? BuyerCanonicalOrgId,
    string BuyerName,
    string Title,
    long? LostToCanonicalOrgId,
    string? LostToName,
    decimal? BidFee,
    string BidCurrency,
    decimal? EstConstructionValue,
    string? OurRole,
    string? KorBusinessDeveloper,
    string? KorProposalManager,
    DateTime? PursuitOpenedDate,
    DateTime? DueDate,
    DateTime? SubmittedDate,
    DateTime? AwardDate,
    string? ClosedReason,
    string? Strengths,
    string? Weaknesses,
    string? Notes,
    string? CreatedByUser,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ExternalSource,
    string? ExternalSourceKey);

public sealed record KorPursuitCreate(
    string Stage,
    long? OpportunityId,
    long? HistoricalOpportunityId,
    long? OpportunityAwardId,
    string? SourceExternalRef,
    long? BuyerCanonicalOrgId,
    string BuyerName,
    string Title,
    long? LostToCanonicalOrgId,
    string? LostToName,
    decimal? BidFee,
    string BidCurrency,
    decimal? EstConstructionValue,
    string? OurRole,
    string? KorBusinessDeveloper,
    string? KorProposalManager,
    DateTime? PursuitOpenedDate,
    DateTime? DueDate,
    DateTime? SubmittedDate,
    DateTime? AwardDate,
    string? ClosedReason,
    string? Strengths,
    string? Weaknesses,
    string? Notes,
    string? CreatedByUser,
    string? ExternalSource,
    string? ExternalSourceKey);
