#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Briefs;

/// <summary>
/// Raw data needed to render a Pursuit Brief. Phase 1A of the BD brief feature
/// (opportunity / region / org). Pure data records; no rendering logic.
/// </summary>
public sealed record EventMatch(
    string Name,
    DateTime? StartDate,
    string? City,
    string? Market,
    string? SectorsThemes,
    string? Audience,
    string? TargetsPresent,
    string? RegistrationUrl);

public sealed record OpportunityBriefData(
    long Id,
    string Name,
    string BuyerName,
    long? BuyerCanonicalOrgId,
    DateTimeOffset? SubmissionDeadlineUtc,
    decimal? EstimatedValue,
    decimal PrimeConfidence,
    string? PrimeProjectSector,
    string? ProjectProvince,
    string? ProjectCity,
    int OwnerKorProjectsCount,
    int OwnerPipelineProjectCount,
    string? LikelyArchitectName,
    long? LikelyArchitectCanonicalOrgId,
    int LikelyArchitectOwnerProjectCount,
    int KorArchitectJointProjectCount,
    EventMatch? MatchedEvent);

public sealed record RegionTopOrg(
    long Id,
    string DisplayName,
    int ProjectCount,
    int KorJointCount);

public sealed record RegionLiveRfp(
    long Id,
    string Name,
    string BuyerName,
    DateTimeOffset? SubmissionDeadlineUtc,
    string? PrimeProjectSector,
    decimal PrimeConfidence);

public sealed record RegionForwardProject(
    long Id,
    string ProjectName,
    string? ProponentName,
    string? Stage,
    decimal? EstimatedCostCad);

public sealed record RegionBriefData(
    string Province,
    string? City,
    int LivePrimeRfpCount,
    int ForwardPipelineCount,
    int ActiveMpiCount,
    IReadOnlyList<RegionTopOrg> TopArchitects,
    IReadOnlyList<RegionTopOrg> TopOwners,
    IReadOnlyList<RegionTopOrg> TopCompetitors,
    IReadOnlyList<RegionLiveRfp> LiveRfps,
    IReadOnlyList<RegionForwardProject> ForwardProjects,
    IReadOnlyList<EventMatch> UpcomingEvents);

public sealed record OrgRecentProject(
    long Id,
    string ProjectName,
    int? CompletionYear,
    string? Sector,
    string? Province);

public sealed record OrgBriefData(
    long Id,
    string Kind,
    string DisplayName,
    string? Website,
    int KorProjectsCount,
    DateTimeOffset? LastKorProjectAtUtc,
    IReadOnlyList<OrgRecentProject> RecentProjects,
    int KorJointProjectCount,
    IReadOnlyList<OrgRecentProject> KorJointProjects,
    string? DataHoningEnrichmentJson);
