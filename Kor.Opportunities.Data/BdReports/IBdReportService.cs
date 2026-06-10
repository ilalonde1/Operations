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
    /// Cross-cutting call-sheet pool: every active MPI honed PURSUE or
    /// PURSUE_URGENT across ALL sectors (deduped by MPI — sector filters may
    /// overlap, this query has none). Urgent rows first, then by estimated
    /// cost descending.
    /// </summary>
    Task<IReadOnlyList<PursuitBriefRow>> GetCallSheetPoolAsync(CancellationToken ct);

    /// <summary>Cross-sector headline over DISTINCT active MPIs (exec overview).</summary>
    Task<BdExecHeadline> GetExecHeadlineAsync(CancellationToken ct);

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
