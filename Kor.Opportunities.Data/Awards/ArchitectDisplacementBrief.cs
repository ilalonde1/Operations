#nullable enable
using System;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// One-pager BD playbook per architecture firm. Synthesized by the
/// KOR-Structural-Partner-Map Sonnet session from competitor-research,
/// architect-pipelines, prime-decisionmakers, capability-corpus, and
/// the structural-partner-map itself.
///
/// BriefJson is the full structured payload (incumbents, pipeline,
/// displacement angle, decision-makers, first move, verification gaps,
/// confidence score, _meta). The WPF dossier renders it; we keep the
/// queryable scalars (Market / KorPriority / ConfidenceScore) as
/// dedicated columns for ranking + filtering.
/// </summary>
public sealed record ArchitectDisplacementBrief(
    long Id,
    long ArchitectCanonicalOrgId,
    string? Market,
    string? KorPriority,
    decimal? ConfidenceScore,
    string BriefJson,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset UpdatedAtUtc);
