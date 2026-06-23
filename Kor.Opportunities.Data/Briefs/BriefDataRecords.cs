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

public sealed record ProjectMentionInWorkHistory(
    string OrgDisplayName,
    long CanonicalOrgId,
    string Kind,
    string? Role,
    string? YearApprox,
    string? Notes);

public sealed record ProjectRecentMentionSignal(
    string OrgDisplayName,
    long CanonicalOrgId,
    string SignalType,
    string Subject,
    string? Detail,
    string? OccurredAtApprox,
    DateTimeOffset LastSeenAtUtc);

public sealed record ProjectOpenActionMention(
    string OrgDisplayName,
    long CanonicalOrgId,
    string ActionType,
    string Recommendation,
    string? TimingNotes,
    DateTimeOffset LastSeenAtUtc);

public sealed record ProjectIntelSignalRow(
    string SignalType,
    string Subject,
    string? Detail,
    string? OccurredAtApprox,
    DateTimeOffset LastSeenAtUtc);

public sealed record ProjectIntelActionRow(
    string ActionType,
    string Recommendation,
    string? TargetPersonName,
    string? TargetOrgName,
    string? TimingNotes,
    DateTimeOffset LastSeenAtUtc);

public sealed record ProjectIntelRiskRow(
    string RiskType,
    string Description,
    string? MitigationNotes);

public sealed record ProjectIntelKeyPersonRow(
    string DisplayName,
    string? Title,
    string Side,
    string? OrgName);

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
    LinkedOrgSummary? GeneralContractorSummary,
    IReadOnlyList<ProjectMentionInWorkHistory> MentionsInWorkHistory,
    IReadOnlyList<ProjectRecentMentionSignal> RecentMentionSignals,
    IReadOnlyList<ProjectOpenActionMention> OpenActionsMentioning,
    string? IntelDescription,
    string? IntelSchedule,
    string? IntelStatus,
    string? IntelKorAngle,
    IReadOnlyList<ProjectIntelKeyPersonRow> IntelKeyPeople,
    IReadOnlyList<ProjectIntelSignalRow> IntelSignals,
    IReadOnlyList<ProjectIntelActionRow> IntelActions,
    IReadOnlyList<ProjectIntelRiskRow> IntelRisks);

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
    public string? ResolvedFromNote { get; init; }

    /// <summary>
    /// User-friendly note explaining why the Deltek engagement-history section
    /// is absent (no link / no matching Deltek client / lookup error). Rendered
    /// inline by the PDF + DOCX brief generators when <see cref="Deltek"/> is
    /// null. Null = silently skip (no note shown).
    /// </summary>
    public string? DeltekNote { get; init; }

    public Kor.Opportunities.Data.Intel.OrgIntelBundle? Intel { get; init; }

    /// <summary>
    /// MAX(UpdatedAtUtc) across this org's CanonicalOrgEnrichment rows — when the
    /// dossier intelligence was last refreshed. Null = never enriched.
    /// </summary>
    public DateTimeOffset? LastEnrichedAtUtc { get; init; }

    /// <summary>
    /// One-line trust/provenance summary for the brief header: how fresh the
    /// intelligence is (last refresh + days ago) and the evidence base behind it
    /// (signal / people / portfolio counts). Lets the reader judge currency at a
    /// glance instead of trusting an undated brief.
    /// </summary>
    public string IntelProvenanceLine
    {
        get
        {
            var parts = new List<string>();
            if (LastEnrichedAtUtc is { } ts)
            {
                var days = (int)Math.Max(0, (DateTimeOffset.UtcNow - ts).TotalDays);
                parts.Add($"refreshed {ts:yyyy-MM-dd} ({days}d ago)");
            }
            else
            {
                parts.Add("not yet researched");
            }

            if (Intel is { } b)
            {
                var ev = new List<string>();
                if (b.Signals.Count > 0) ev.Add($"{b.Signals.Count} signals");
                if (b.People.Count > 0) ev.Add($"{b.People.Count} people");
                if (b.Works.Count > 0) ev.Add($"{b.Works.Count} portfolio");
                if (ev.Count > 0) parts.Add(string.Join(", ", ev));
            }

            return string.Join("  ·  ", parts);
        }
    }
}

// === Sector Brief ===

public sealed record SectorBriefRequest(
    string Province,
    string? City,
    IReadOnlyList<string> SectorBuckets,
    IReadOnlyList<string> StructuralTypes,
    bool IndigenousOnly,
    string? IndigenousNationFilter,
    string? ExtraKeyword)
{
    // Canonical sector bucket names  magic strings that
    // SqlBriefDataStore.GetSectorBriefAsync maps to MPI Sector/
    // SubSector LIKE clauses. Adding a new bucket requires extending
    // BOTH this list AND the SQL bucket mapper.
    public static IReadOnlyList<string> AllSectorBuckets { get; } = new[]
    {
        "Healthcare",
        "K-12",
        "Post-Secondary",
        "Civic",
        "Recreation",
        "Residential-LowRise",
        "Residential-HighRise",
        "Industrial-Other",
    };

    // Canonical structural-type names. Same magic-string contract.
    public static IReadOnlyList<string> AllStructuralTypes { get; } = new[]
    {
        "Wood-Frame",
        "Concrete-Tower",
        "Mass-Timber",
        "Steel",
        "Hybrid",
    };
}

public sealed record SectorBriefCounts(
    int LiveRfpCount,
    int ForwardPipelineCount,
    int RecentAwardCount,
    decimal? TotalForwardPipelineCostCad);

public sealed record SectorTopOrg(
    long CanonicalOrgId,
    string DisplayName,
    string Kind,
    int ProjectCount,
    int KorJointCount);

public sealed record SectorRecentAward(
    long OpportunityId,
    string ProjectName,
    string BuyerName,
    string? AwardedToName,
    decimal? AwardedValueCad,
    DateTimeOffset? AwardedAtUtc);

public sealed record SectorKorPortfolioRow(
    long MpiId,
    string ProjectName,
    string? Stage,
    decimal? EstimatedCostCad,
    string? City);

public sealed record SectorIntelSignalRow(
    string OrgDisplayName,
    string SignalType,
    string Subject,
    string? Detail,
    string? OccurredAtApprox,
    DateTimeOffset LastSeenAtUtc);

public sealed record SectorBriefData(
    SectorBriefRequest Request,
    SectorBriefCounts Counts,
    IReadOnlyList<RegionLiveRfp> LiveRfps,
    IReadOnlyList<RegionForwardProject> ForwardProjects,
    IReadOnlyList<SectorRecentAward> RecentAwards,
    IReadOnlyList<SectorTopOrg> TopArchitects,
    IReadOnlyList<SectorTopOrg> TopOwners,
    IReadOnlyList<SectorTopOrg> TopGcs,
    IReadOnlyList<SectorTopOrg> TopStructuralCompetitors,
    IReadOnlyList<SectorKorPortfolioRow> KorPortfolio,
    IReadOnlyList<SectorIntelSignalRow> RelevantSignals);
