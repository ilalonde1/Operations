#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// The sector report config rows. Filter fragments were validated against the
/// live Sector × Verdict distribution on 2026-06-09 (the Sector column is
/// free-text from many providers — e.g. "Plant and Animal Health Centre", the
/// hospitals report's biggest URGENT, carries Sector='Industrial' and is only
/// caught by name; BC Housing and Indigenous are proponent/SourceKey-driven,
/// not Sector-driven). Phase D sectors (senior living, cultural, civic) ship
/// as new rows here, not new code.
/// Geography is scoped per row since 2026-06-12: the inventory carries five
/// Province values (BC, AB, CA, WA, OR) and the sector heuristics alone
/// admitted US rows — a $1.74B Seattle hospital topped the "BC + AB"
/// hospitals report. US rows live in the us-west row instead.
/// </summary>
public static class SectorReportDefinitionCatalog
{
    public static IReadOnlyList<SectorReportDefinition> All { get; } = new[]
    {
        new SectorReportDefinition(
            "hospitals",
            "Hospitals & Healthcare",
            "KOR Structural — BC + AB Hospitals BD Report",
            "((m.Sector IN (N'Healthcare', N'Health', N'Hospital', N'Healthcare / Hospital', N'LTC', N'Long-term care')" +
            " OR m.SubSector LIKE N'%hospital%' OR m.SubSector LIKE N'%health%'" +
            " OR m.ProjectName LIKE N'%hospital%' OR m.ProjectName LIKE N'%health centre%'" +
            " OR m.SourceKey LIKE N'HCARE%')" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "schools",
            "K-12 Schools",
            "KOR Structural — BC + AB K-12 Schools BD Report",
            "((m.Sector IN (N'School', N'K-12', N'Education - K12', N'Education-K12', N'Seismic Upgrade')" +
            " OR (m.Sector = N'Education' AND (m.SubSector LIKE N'%K-12%' OR m.SubSector LIKE N'%Seismic%'))" +
            " OR m.ProjectName LIKE N'%elementary%' OR m.ProjectName LIKE N'%middle school%'" +
            " OR m.ProjectName LIKE N'%secondary school%' OR m.ProjectName LIKE N'%high school%')" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "indigenous",
            "Indigenous & First Nations",
            "KOR Structural — Indigenous Partnerships BD Report",
            "((m.SourceKey LIKE N'INDIG%' OR m.Sector = N'Indigenous'" +
            " OR m.ProponentName LIKE N'%First Nation%' OR m.ProponentName LIKE N'% Nation'" +
            " OR m.ProponentName LIKE N'% Band' OR m.ProponentName LIKE N'%Tribal%'" +
            " OR m.ProponentName LIKE N'%MST Develop%' OR m.ProponentName LIKE N'%Squamish%'" +
            " OR m.ProponentName LIKE N'%Musqueam%' OR m.ProponentName LIKE N'%Tsleil%')" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "bc-housing",
            "BC Housing & Affordable",
            "KOR Structural — BC Housing BD Report",
            "((m.ProponentName LIKE N'%BC Housing%' OR m.Sector = N'Affordable Housing'" +
            " OR m.SubSector LIKE N'%affordable%' OR m.SubSector LIKE N'%supportive%')" +
            " AND m.Province = N'BC')"),

        new SectorReportDefinition(
            "defense",
            "Defense & Military",
            "KOR Structural — Defense BD Report",
            "((m.Sector IN (N'Defense', N'Institutional / Military')" +
            " OR m.SourceKey LIKE N'DEFENSE%'" +
            " OR m.ProponentName LIKE N'%National Defence%')" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "recreational",
            "Recreation & Sport",
            "KOR Structural — BC + AB Recreational BD Report",
            "((m.Sector IN (N'Recreation', N'Civic / Recreation', N'Tourism / Recreation', N'Sports/Stadium')" +
            " OR m.SubSector LIKE N'%recreation%'" +
            " OR m.ProjectName LIKE N'%aquatic%' OR m.ProjectName LIKE N'%arena%'" +
            " OR m.ProjectName LIKE N'%recreation centre%')" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "residential",
            "Residential & Mixed-Use",
            "KOR Structural — BC + AB Residential BD Report",
            "((m.Sector LIKE N'Residential%' OR m.Sector LIKE N'Housing%'" +
            " OR m.Sector LIKE N'Mixed-Use%' OR m.Sector LIKE N'Mixed-use%'" +
            " OR m.Sector IN (N'Multifamily', N'MixedUse'))" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "commercial",
            "Commercial & Industrial",
            "KOR Structural — BC + AB Commercial BD Report",
            "((m.Sector LIKE N'Commercial%'" +
            " OR m.Sector IN (N'Industrial', N'Hospitality', N'Entertainment/Studio', N'Mixed-Use / Tech Campus'))" +
            " AND m.Province IN (N'BC', N'AB'))"),

        new SectorReportDefinition(
            "post-secondary",
            "Post-Secondary",
            "KOR Structural — BC + AB Post-Secondary BD Report",
            "((m.Sector LIKE N'Post-Secondary%' OR m.Sector LIKE N'Post-secondary%'" +
            " OR m.Sector IN (N'Academic', N'University', N'Education - Post-Secondary', N'Education-PostSec', N'StudentHousing')" +
            " OR (m.Sector = N'Education' AND (m.SubSector LIKE N'%Academic%' OR m.SubSector LIKE N'%Science%'" +
            " OR m.SubSector LIKE N'%Student Housing%' OR m.SubSector LIKE N'%Research%')))" +
            " AND m.Province IN (N'BC', N'AB'))"),

        // Region row (first non-sector cut, same config-row mechanism).
        // Validated against live MunicipalityName/RegionName on 2026-06-12:
        // only 6 of the 12 municipality values occur today (the rest are
        // future-proofing for ingest); the NULL-municipality arm recovers 12
        // genuine rows tagged with the precise Okanagan region values (UBCO,
        // Cottonwoods, Okanagan College), and the Kamloops proponent guard
        // drops SD73's New Aberdeen Secondary, which is region-mis-tagged
        // 'Okanagan'. Broader tags (Thompson-Okanagan, Okanagan/Interior)
        // stay out — they carry Kamloops, Lytton, Fernie.
        new SectorReportDefinition(
            "okanagan",
            "Okanagan Region",
            "KOR Structural — Okanagan Region BD Report",
            "((m.MunicipalityName IN (N'Kelowna', N'West Kelowna', N'Vernon', N'Penticton', N'Lake Country'," +
            " N'Summerland', N'Peachland', N'Coldstream', N'Armstrong', N'Enderby', N'Oliver', N'Osoyoos')" +
            " OR (m.MunicipalityName IS NULL" +
            " AND m.RegionName IN (N'Okanagan', N'CentralOkanagan', N'Central Okanagan', N'SouthOkanagan')" +
            " AND (m.ProponentName IS NULL OR m.ProponentName NOT LIKE N'%Kamloops%')))" +
            " AND m.Province = N'BC')"),

        // The US slice the BC + AB scoping removed from the sector rows.
        // Province is the only US geography signal today (CA 147 / OR 58 /
        // WA 17 active on 2026-06-12); add new states here when ingest grows.
        new SectorReportDefinition(
            "us-west",
            "US West Coast",
            "KOR Structural — US West Coast BD Report",
            "(m.Province IN (N'CA', N'WA', N'OR'))"),
    };
}
