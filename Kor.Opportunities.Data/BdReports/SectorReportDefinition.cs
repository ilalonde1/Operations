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
public sealed record SectorReportDefinition(
    string Key,
    string Title,
    string ReportTitle,
    string MpiWhere);
