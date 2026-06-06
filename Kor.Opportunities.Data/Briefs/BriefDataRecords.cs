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
    EventMatch? MatchedEvent)
{
    public Kor.Opportunities.Data.Intel.OpportunityIntelBundle? Intel { get; init; }
}

public sealed record ProjectSearchRow(
    long Id,
    string ProjectName,
    string? ProponentName,
    string? Stage,
    string? City,
    string Province)
{
    public string Subtitle
    {
        get
        {
            var location = string.IsNullOrWhiteSpace(City) ? Province : $"{City}, {Province}";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Stage)) parts.Add(Stage);
            if (!string.IsNullOrWhiteSpace(ProponentName)) parts.Add(ProponentName);
            if (!string.IsNullOrWhiteSpace(location)) parts.Add(location);
            return string.Join("  ", parts);
        }
    }
}

public sealed record LinkedOrgSummary(
    long CanonicalOrgId,
    string DisplayName,
    string Kind,
    int IntelPersonCount,
    int OpenActionCount,
    int RecentSignalCount,
    DateTimeOffset? LastRefreshAtUtc);

public sealed record ProjectBriefData(
    long Id,
    string ProjectName,
    string Province,
    string? City,
    string? Region,
    string? Sector,
    string? SubSector,
    string? Stage,
    decimal? EstimatedCostCad,
    string? EstimatedCostText,
    short? StartYear,
    short? CompletionYear,
    string? ScheduleNotes,
    string? ProponentName,
    string? ArchitectName,
    string? StructuralEngineerName,
    string? GeneralContractorName,
    string? SourceUrl,
    string? ProjectDescription,
    LinkedOrgSummary? ProponentSummary,
    LinkedOrgSummary? ArchitectSummary,
    LinkedOrgSummary? StructuralSummary,
    LinkedOrgSummary? GeneralContractorSummary);

public sealed record PersonSearchRow(
    long IntelPersonId,
    string DisplayName,
    string? CurrentTitle,
    string? CurrentEmployerName)
{
    public string Subtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CurrentTitle) && !string.IsNullOrWhiteSpace(CurrentEmployerName))
            {
                return $"{CurrentTitle} @ {CurrentEmployerName}";
            }

            if (!string.IsNullOrWhiteSpace(CurrentTitle))
            {
                return CurrentTitle!;
            }

            return string.IsNullOrWhiteSpace(CurrentEmployerName) ? "Unaffiliated" : "@ " + CurrentEmployerName;
        }
    }
}

public sealed record PersonAffiliationRow(
    string OrgName,
    long CanonicalOrgId,
    string? Title,
    string? Department,
    bool IsCurrent,
    string? StartDateApprox,
    string? EndDateApprox);

public sealed record PersonSignalRow(
    string? OccurredAtApprox,
    string SignalType,
    string Subject,
    string? Detail,
    string OrgName);

public sealed record PersonActionRow(
    string ActionType,
    string Recommendation,
    string? TimingNotes,
    string OrgName);

public sealed record PersonBriefData(
    long IntelPersonId,
    string DisplayName,
    string? CurrentTitle,
    string? CurrentEmployerName,
    long? CurrentEmployerCanonicalOrgId,
    string? Email,
    string? Phone,
    string? LinkedinUrl,
    string? Notes,
    DateTimeOffset? LastSeenAtUtc,
    IReadOnlyList<PersonAffiliationRow> CurrentAffiliations,
    IReadOnlyList<PersonAffiliationRow> FormerAffiliations,
    IReadOnlyList<PersonSignalRow> RecentSignals,
    IReadOnlyList<PersonActionRow> OpenActions);

public sealed record RegionTopOrg(
    long Id,
    string DisplayName,
    int ProjectCount,
    int KorJointCount,
    string? ClendorClientId);

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
    IReadOnlyList<EventMatch> UpcomingEvents)
{
    public Kor.Opportunities.Data.Intel.RegionIntelRollup? Intel { get; init; }
}

public sealed record OrgRecentProject(
    long Id,
    string ProjectName,
    int? CompletionYear,
    string? Sector,
    string? Province);

public sealed record OrgBriefDeltekProject(
    string Wbs1,
    string Name,
    DateTime? OpenDate,
    string? Status,
    decimal Fee,
    decimal FeeBilled);

public sealed record OrgBriefDeltekSection(
    string DeltekClientId,
    string ClientName,
    int ProjectCount,
    decimal LifetimeFee,
    DateTime? LatestProjectStart,
    string? LatestProjectName,
    IReadOnlyList<OrgBriefDeltekProject> RecentProjects,
    int ContactCount,
    decimal ArOutstanding,
    decimal Ar90Plus,
    bool DegradedSections);

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
    string? DataHoningEnrichmentJson,
    string? DeltekClientId,
    OrgBriefDeltekSection? Deltek)
{
    /// <summary>
    /// User-friendly note explaining why the Deltek engagement-history section
    /// is absent (no link / no matching Deltek client / lookup error). Rendered
    /// inline by the PDF + DOCX brief generators when <see cref="Deltek"/> is
    /// null. Null = silently skip (no note shown).
    /// </summary>
    public string? DeltekNote { get; init; }

    public Kor.Opportunities.Data.Intel.OrgIntelBundle? Intel { get; init; }
}
