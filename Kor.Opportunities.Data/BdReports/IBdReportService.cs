#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// Read side for the BD Reports module (BD-UI-Plan-2026-06-08). The WPF
/// dashboard, the report generators and the MCP BD tools all consume these
/// same queries so dashboard, DOCX and AI answers cannot drift.
/// </summary>
public interface IBdReportService
{
    /// <summary>Verdict counts per catalog sector, for the pursuit dashboard cards.</summary>
    Task<IReadOnlyList<SectorVerdictSummary>> GetSectorSummariesAsync(CancellationToken ct);

    /// <summary>
    /// All active MPIs in the sector (including not-yet-honed rows — callers
    /// filter), hydrated with honing fields, sorted by verdict rank then
    /// estimated cost descending.
    /// </summary>
    Task<IReadOnlyList<PursuitBriefRow>> GetSectorPursuitsAsync(string sectorKey, CancellationToken ct);

    /// <summary>
    /// Top market IntelSignal rows for the sector's province footprint,
    /// filtered to BD-relevant signal types.
    /// </summary>
    Task<IReadOnlyList<SectorIntelSignalRow>> GetSectorIntelSignalsAsync(string sectorKey, CancellationToken ct);

    /// <summary>
    /// Cross-cutting call-sheet pool: every active MPI honed PURSUE or
    /// PURSUE_URGENT across ALL sectors (deduped by MPI — sector filters may
    /// overlap, this query has none). Urgent rows first, then by estimated
    /// cost descending.
    /// </summary>
    Task<IReadOnlyList<PursuitBriefRow>> GetCallSheetPoolAsync(CancellationToken ct);

    /// <summary>Cross-sector headline over DISTINCT active MPIs (exec overview).</summary>
    Task<BdExecHeadline> GetExecHeadlineAsync(CancellationToken ct);

    /// <summary>
    /// Architects ranked by linked active projects then pipeline $ (the
    /// warm-intro priority list), with org-scoped intel depth and a top-8
    /// project drill-down per architect.
    /// </summary>
    Task<IReadOnlyList<ArchitectLeverageRow>> GetArchitectLeverageAsync(int take, CancellationToken ct);

    /// <summary>
    /// Competitor-kind orgs ranked by org-scoped intel references (signals +
    /// person affiliations + actions), with exact structural-engineer project
    /// links where the org graph has them. Zero-intel competitors are omitted.
    /// </summary>
    Task<IReadOnlyList<CompetitorFootprintRow>> GetCompetitorFootprintAsync(int take, CancellationToken ct);

    /// <summary>
    /// Active honed MPIs whose honing brief mentions the given org name
    /// (substring, LIKE-escaped), cost-descending — the strategic-relationships
    /// builder's project-sample lookup. Returns "Id: Name (Province) — Cost"
    /// display lines.
    /// </summary>
    Task<IReadOnlyList<string>> GetProjectsMentioningAsync(string orgPattern, int take, CancellationToken ct);

    /// <summary>
    /// All active MPIs with a PrimeConsultantResearch enrichment, hydrated
    /// from the research JSON, cost-descending (numeric, not lexical — the
    /// audit M12 lesson).
    /// </summary>
    Task<IReadOnlyList<PrimeConsultantRow>> GetPrimeConsultantsAsync(CancellationToken ct);

    /// <summary>
    /// Every active PURSUE/PURSUE_URGENT MPI hydrated with its resolved
    /// org-graph edges (architect / structural engineer / GC CanonicalOrg
    /// links) plus org-scoped architect intel depth — the live read behind
    /// the Pursuit Dossiers report. Urgent rows first, then cost descending.
    /// </summary>
    Task<IReadOnlyList<PursuitDossierRow>> GetPursuitDossiersAsync(CancellationToken ct);

    /// <summary>
    /// Upcoming industry award programs KOR could enter for recognition,
    /// separate from contract-award/bid-result intelligence.
    /// </summary>
    Task<IReadOnlyList<AwardProgramReportRow>> GetUpcomingAwardProgramsAsync(int take, CancellationToken ct);

    /// <summary>
    /// READ-ONLY action-tracking rollup: IntelAction counts by Status ×
    /// ActionType plus the most recent open actions (on active orgs). Status
    /// writes go through IntelPersistenceService.SetActionStatusAsync in the
    /// WPF UI — AI write tools are deferred.
    /// </summary>
    Task<BdActionRollup> GetActionRollupAsync(int topOpen, CancellationToken ct);

    /// <summary>
    /// Compliance audit log (opportunities.BdReportAuditLog, migration 121):
    /// one row per generate. Format is "html" (preview) or "docx" (export).
    /// Best-effort from the UI — a logging failure must not block the report.
    /// </summary>
    Task LogReportGeneratedAsync(
        string category,
        string format,
        string generatedByUser,
        int? recordCount,
        string? notes,
        CancellationToken ct);
}
