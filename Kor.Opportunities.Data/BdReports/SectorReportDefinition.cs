#nullable enable
namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// One sector report config row (BD-UI-Plan-2026-06-08: sectors ship as
/// catalog rows, not per-sector generator classes).
/// </summary>
/// <param name="Key">Stable key used by the UI, MCP tools and audit log (e.g. "hospitals").</param>
/// <param name="Title">Short display title for dashboard cards.</param>
/// <param name="ReportTitle">H1 title of the generated report document.</param>
/// <param name="MpiWhere">
/// SQL filter fragment over alias <c>m</c> (opportunities.MajorProjectsInventory).
/// Catalog constants only — never user input — and always ANDed with
/// <c>m.RetiredAtUtc IS NULL</c> by the service. Same mechanism as
/// SqlBriefDataStore.BuildSectorBucketWhere. Sectors may overlap by design
/// (e.g. a BC Housing project is also Residential).
/// </param>
/// <param name="SignalTopicWhere">
/// Optional SQL filter fragment over the IntelSignal alias <c>s</c> (Subject /
/// Detail). Catalog constants only — never user input. When set, a signal also
/// surfaces in this sector if its TEXT is about the sector — not only when its
/// org sits on one of the sector's MPIs. This is the topic bridge: a teaming /
/// opportunity signal attached to a firm that isn't FK-linked to the sector's
/// projects (e.g. a civil teaming partner on a defense pursuit) still surfaces.
/// Null = org-membership scoping only.
/// </param>
public sealed record SectorReportDefinition(
    string Key,
    string Title,
    string ReportTitle,
    string MpiWhere,
    string? SignalTopicWhere = null);
