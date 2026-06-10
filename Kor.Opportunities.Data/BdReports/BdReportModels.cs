#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// Honing verdict vocabulary per tools/BdQueueDrainPrompts/HONING-OUTPUT-CONTRACT.md.
/// Verdicts are read from MajorProjectEnrichment.ResultJson via
/// COALESCE(JSON_VALUE($.honingPass.verdict), JSON_VALUE($.verdict)) — the
/// repo-wide recipe (see tools/BdVerdictBackfill).
/// </summary>
public static class BdVerdicts
{
    public const string PursueUrgent = "PURSUE_URGENT";
    public const string Pursue = "PURSUE";
    public const string Monitor = "MONITOR";
    public const string Discover = "DISCOVER";
    public const string Dead = "DEAD";
    public const string Duplicate = "DUPLICATE";

    /// <summary>Sort rank for report/dashboard ordering. Unknown or missing verdicts sort last.</summary>
    public static int Rank(string? verdict) => verdict switch
    {
        PursueUrgent => 0,
        Pursue => 1,
        Monitor => 2,
        Discover => 3,
        Dead => 4,
        Duplicate => 5,
        _ => 6,
    };

    /// <summary>
    /// Urgency rule, parity with the shipped PowerShell report builders
    /// (tools/BdReportBuilders): PURSUE_URGENT, or PURSUE whose korAngle
    /// mentions URGENT (case-insensitive, matching PowerShell -match).
    /// </summary>
    public static bool IsUrgent(string? verdict, string? korAngle)
    {
        if (string.Equals(verdict, PursueUrgent, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(verdict, Pursue, StringComparison.Ordinal)
            && korAngle is not null
            && korAngle.Contains("URGENT", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// One active MajorProjectsInventory row joined to its (at most one)
/// ProjectBriefHoning enrichment row, hydrated for report generation.
/// </summary>
public sealed record PursuitBriefRow(
    long MpiId,
    string ProjectName,
    string Province,
    string? Sector,
    string? SubSector,
    string? Stage,
    string? ProponentName,
    decimal? EstimatedCostCad,
    string? EstimatedCostText,
    string? MunicipalityName,
    string? RegionName,
    string? Verdict,
    string? KorAngle,
    string? HoningStatus,
    string? Description,
    double? OverallConfidence,
    DateTimeOffset? LastRefreshAtUtc,
    bool IsUrgent);

/// <summary>
/// Per-sector verdict counts for the pursuit dashboard cards, plus honing
/// freshness buckets over the honed rows (BD-UI-Plan decision 6: green
/// &lt;30 days, yellow 30-90, red 90+ on LastRefreshAtUtc).
/// </summary>
public sealed record SectorVerdictSummary(
    string SectorKey,
    string SectorTitle,
    int PursueUrgent,
    int Pursue,
    int Monitor,
    int Discover,
    int Dead,
    int Duplicate,
    int NoVerdict,
    int Total,
    int FreshCount,
    int AgingCount,
    int StaleCount,
    decimal HonedCostCad);

/// <summary>
/// Cross-sector headline for the executive overview. Computed over DISTINCT
/// active MPIs — summing per-sector totals would double-count because sector
/// filters deliberately overlap.
/// </summary>
public sealed record BdExecHeadline(
    int TotalActiveMpis,
    int HonedCount,
    decimal HonedCostCad);

/// <summary>
/// One architect ranked by pipeline leverage. Built from the STRUCTURED org
/// graph (MajorProjectsInventory.ArchitectCanonicalOrgId + org-scoped Intel
/// rows) — the original .architect-freq.json text-mining pull was never saved
/// and its counts are not reconstructible from any table, so this is the
/// auditable replacement (same intent: one architect relationship = N
/// projects).
/// </summary>
/// <summary>
/// One structural-engineering competitor's footprint across KOR's pipeline:
/// org-scoped Intel counts (the verified recipe behind the shipped
/// competitor report) plus exact StructuralEngineerCanonicalOrgId links.
/// </summary>
public sealed record CompetitorFootprintRow(
    long CanonicalOrgId,
    string DisplayName,
    int Signals,
    int People,
    int Actions,
    int LinkedProjects,
    IReadOnlyList<string> TopProjects)
{
    public int Total => Signals + People + Actions;
}

/// <summary>
/// One project's prime-consultant research result (ProviderName =
/// 'PrimeConsultantResearch' enrichment on an active MPI).
/// </summary>
public sealed record PrimeConsultantRow(
    long MpiId,
    string ProjectName,
    string Province,
    string? ProponentName,
    string CostDisplay,
    decimal CostNumeric,
    string? PrimeFirm,
    string? PrincipalInCharge,
    string? ContactEmail,
    bool KnownToKor,
    string? KorRelationshipNotes,
    string? KorAngle,
    double? Confidence)
{
    public bool HasPrime => !string.IsNullOrWhiteSpace(PrimeFirm);
}

public sealed record ArchitectLeverageRow(
    long CanonicalOrgId,
    string DisplayName,
    int ActiveProjects,
    decimal PipelineCostCad,
    int Signals,
    int People,
    int OpenActions,
    IReadOnlyList<string> TopProjects)
{
    public int TotalRefs => Signals + People + OpenActions;
}
