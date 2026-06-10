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
}
