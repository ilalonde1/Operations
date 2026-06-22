#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Intel;

public enum IntelFreshness { Fresh, Aged, Stale }

public sealed record IntelPersonRow(
    long Id, string DisplayName, string? Email, string? Phone, string? LinkedinUrl,
    string? Title, bool IsCurrent, string? AffiliationNotes,
    int Corroborations, string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness,
    string? RefreshStatus = null)
{
    public long IntelPersonId => Id;
}

public sealed record IntelSignalRow(
    long Id, string SignalType, string Subject, string? Detail, string? OccurredAtApprox, string? SourceUrl,
    int Corroborations, string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness);

public sealed record IntelActionRow(
    long Id, string ActionType, string Recommendation, string? TargetPersonName, string? TimingNotes, string Status,
    string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness);

public sealed record IntelWorkRow(
    long Id, string ProjectName, string? Role, string? YearApprox,
    decimal? EstimatedValueCad, string? EstimatedValueText, string? Notes,
    long? MajorProjectsInventoryId,
    string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness);

public sealed record IntelRiskRow(
    long Id, string RiskType, string Description, string? MitigationNotes,
    string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness);

public sealed record RegionCapacityRiskRow(
    string OrgDisplayName,
    string Description,
    string? MitigationNotes,
    IntelConfidence Confidence,
    IntelFreshness Freshness,
    DateTimeOffset RefreshedAtUtc);

public sealed record IntelNarrativeRow(
    long Id, string NarrativeType, string ParagraphText,
    string SourceProviderName, IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc, IntelFreshness Freshness);

public sealed record OrgIntelBundle(
    string? SynopsisParagraph1,
    string? SynopsisParagraph2,
    IReadOnlyList<IntelPersonRow> People,
    IReadOnlyList<IntelSignalRow> Signals,
    IReadOnlyList<IntelActionRow> Actions,
    IReadOnlyList<IntelWorkRow> Works,
    IReadOnlyList<IntelRiskRow> Risks,
    IReadOnlyList<IntelNarrativeRow> Narratives)
{
    public static OrgIntelBundle Empty { get; } = new(
        null, null,
        Array.Empty<IntelPersonRow>(), Array.Empty<IntelSignalRow>(),
        Array.Empty<IntelActionRow>(), Array.Empty<IntelWorkRow>(),
        Array.Empty<IntelRiskRow>(), Array.Empty<IntelNarrativeRow>());
}

public sealed record RegionIntelRollup(
    IReadOnlyList<IntelActionRow> TopActions,
    IReadOnlyList<IntelSignalRow> RecentLeadershipChanges,
    IReadOnlyList<RegionCapacityRiskRow> TopCapacityRisks);

public sealed record OpportunityIntelBundle(
    OrgIntelBundle? BuyerIntel,
    OrgIntelBundle? ArchitectIntel);

/// <summary>
/// One row in the BD-Dashboard Priority Actions surface. Joins
/// IntelAction (Status='Open') to its CanonicalOrg so the queue can
/// show the org name + provide drill-down to the Dossier without an
/// extra round-trip.
/// </summary>
public sealed record PriorityActionRow(
    long ActionId,
    long CanonicalOrgId,
    string OrgDisplayName,
    string OrgKind,
    string? OrgClendorClientId,
    string ActionType,
    string Recommendation,
    string? TargetPersonName,
    string? TimingNotes,
    string SourceProviderName,
    IntelConfidence Confidence,
    DateTimeOffset RefreshedAtUtc,
    IntelFreshness Freshness);

/// <summary>
/// Filter parameters for the Priority Actions queue. All optional —
/// any null/empty value means "no filter on this dimension." Province
/// scopes via MPI canonical-org membership (matches the Region Brief
/// rollup's region-membership logic).
/// </summary>
public sealed record PriorityActionFilter(
    string? Province = null,
    string? ActionType = null,
    IntelConfidence? MinConfidence = null);
