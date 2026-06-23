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
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Subject LIKE N'%hospital%' OR s.Detail LIKE N'%hospital%' OR s.Detail LIKE N'%health centre%'" +
            " OR s.Detail LIKE N'%health authority%' OR s.Detail LIKE N'%acute care%' OR s.Detail LIKE N'%patient tower%'" +
            " OR s.Detail LIKE N'%cancer centre%' OR s.Detail LIKE N'%long-term care%' OR s.Detail LIKE N'%ambulatory%')"),

        new SectorReportDefinition(
            "schools",
            "K-12 Schools",
            "KOR Structural — BC + AB K-12 Schools BD Report",
            "((m.Sector IN (N'School', N'K-12', N'Education - K12', N'Education-K12', N'Seismic Upgrade')" +
            " OR (m.Sector = N'Education' AND (m.SubSector LIKE N'%K-12%' OR m.SubSector LIKE N'%Seismic%'))" +
            " OR m.ProjectName LIKE N'%elementary%' OR m.ProjectName LIKE N'%middle school%'" +
            " OR m.ProjectName LIKE N'%secondary school%' OR m.ProjectName LIKE N'%high school%')" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Subject LIKE N'%school district%' OR s.Detail LIKE N'%school district%' OR s.Detail LIKE N'%K-12%'" +
            " OR s.Detail LIKE N'%elementary school%' OR s.Detail LIKE N'%secondary school%'" +
            " OR s.Detail LIKE N'%seismic upgrade%' OR s.Detail LIKE N'%seismic mitigation%')"),

        new SectorReportDefinition(
            "indigenous",
            "Indigenous & First Nations",
            "KOR Structural — Indigenous Partnerships BD Report",
            "((m.SourceKey LIKE N'INDIG%' OR m.Sector = N'Indigenous'" +
            " OR m.ProponentName LIKE N'%First Nation%' OR m.ProponentName LIKE N'% Nation'" +
            " OR m.ProponentName LIKE N'% Band' OR m.ProponentName LIKE N'%Tribal%'" +
            " OR m.ProponentName LIKE N'%MST Develop%' OR m.ProponentName LIKE N'%Squamish%'" +
            " OR m.ProponentName LIKE N'%Musqueam%' OR m.ProponentName LIKE N'%Tsleil%')" +
            " AND m.Province IN (N'BC', N'AB'))",
            // Topic bridge: Indigenous-relevant signals surface even when attached to
            // an org not FK-linked to an Indigenous MPI (e.g. an Indigenous civil partner).
            "(s.Subject LIKE N'%Indigenous%' OR s.Subject LIKE N'%First Nation%'" +
            " OR s.Detail LIKE N'%Indigenous%' OR s.Detail LIKE N'%First Nation%'" +
            " OR s.Detail LIKE N'%CCAB%' OR s.Detail LIKE N'%Haida%' OR s.Detail LIKE N'%Métis%')"),

        // BC+AB, not BC-only: Calgary Housing Company's portfolio (incl. the
        // sector's only URGENT, Bridgeland Place 6649) rides the Affordable
        // Housing sector tag and matches no other report — a BC-only guard
        // orphans it (caught 2026-06-12 against the Phase E draft). The
        // "BC Housing" title undersells the AB half; rename is proposed in
        // the Phase E review, not decided here.
        new SectorReportDefinition(
            "bc-housing",
            "BC Housing & Affordable",
            "KOR Structural — BC Housing BD Report",
            "((m.ProponentName LIKE N'%BC Housing%' OR m.Sector = N'Affordable Housing'" +
            " OR m.SubSector LIKE N'%affordable%' OR m.SubSector LIKE N'%supportive%')" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Subject LIKE N'%BC Housing%' OR s.Detail LIKE N'%BC Housing%' OR s.Detail LIKE N'%BC Builds%'" +
            " OR s.Detail LIKE N'%affordable housing%' OR s.Detail LIKE N'%supportive housing%'" +
            " OR s.Detail LIKE N'%complex care%' OR s.Detail LIKE N'%transitional housing%')"),

        new SectorReportDefinition(
            "defense",
            "Defense & Military",
            "KOR Structural — Defense BD Report",
            "((m.Sector IN (N'Defense', N'Institutional / Military')" +
            " OR m.SourceKey LIKE N'DEFENSE%'" +
            " OR m.ProponentName LIKE N'%National Defence%')" +
            " AND m.Province IN (N'BC', N'AB'))",
            // Topic bridge: defense-relevant signals surface even when attached to
            // an org not FK-linked to a defense MPI (e.g. a civil teaming partner).
            "(s.Subject LIKE N'%defence%' OR s.Subject LIKE N'%defense%' OR s.Subject LIKE N'%military%'" +
            " OR s.Detail LIKE N'%Defence Canada%' OR s.Detail LIKE N'%Defence Construction Canada%'" +
            " OR s.Detail LIKE N'% DND%' OR s.Detail LIKE N'%CFB %' OR s.Detail LIKE N'%CFHA%'" +
            " OR s.Detail LIKE N'% DCC %' OR s.Detail LIKE N'%CFB Esquimalt%' OR s.Detail LIKE N'%A/B Jetty%'" +
            " OR s.Detail LIKE N'%19 Wing%' OR s.Detail LIKE N'%Canadian Forces Base%'" +
            " OR s.Detail LIKE N'%Defence Construction%')"),

        new SectorReportDefinition(
            "recreational",
            "Recreation & Sport",
            "KOR Structural — BC + AB Recreational BD Report",
            "((m.Sector IN (N'Recreation', N'Civic / Recreation', N'Tourism / Recreation', N'Sports/Stadium')" +
            " OR m.SubSector LIKE N'%recreation%'" +
            " OR m.ProjectName LIKE N'%aquatic%' OR m.ProjectName LIKE N'%arena%'" +
            " OR m.ProjectName LIKE N'%recreation centre%')" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Subject LIKE N'%recreation%' OR s.Detail LIKE N'%recreation centre%' OR s.Detail LIKE N'%aquatic centre%'" +
            " OR s.Detail LIKE N'%ice arena%' OR s.Detail LIKE N'%field house%' OR s.Detail LIKE N'%community centre%'" +
            " OR s.Detail LIKE N'%curling%' OR s.Detail LIKE N'%aquatic%')"),

        new SectorReportDefinition(
            "residential",
            "Residential & Mixed-Use",
            "KOR Structural — BC + AB Residential BD Report",
            "((m.Sector LIKE N'Residential%' OR m.Sector LIKE N'Housing%'" +
            " OR m.Sector LIKE N'Mixed-Use%' OR m.Sector LIKE N'Mixed-use%'" +
            " OR m.Sector IN (N'Multifamily', N'MixedUse'))" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Detail LIKE N'%condominium%' OR s.Detail LIKE N'%condo tower%' OR s.Detail LIKE N'%multi-family%'" +
            " OR s.Detail LIKE N'%multifamily%' OR s.Detail LIKE N'%purpose-built rental%' OR s.Detail LIKE N'%missing middle%'" +
            " OR s.Detail LIKE N'%mixed-use%' OR s.Detail LIKE N'%mid-rise%')"),

        new SectorReportDefinition(
            "commercial",
            "Commercial & Industrial",
            "KOR Structural — BC + AB Commercial BD Report",
            "((m.Sector LIKE N'Commercial%'" +
            " OR m.Sector IN (N'Industrial', N'Hospitality', N'Entertainment/Studio', N'Mixed-Use / Tech Campus'))" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Detail LIKE N'%office tower%' OR s.Detail LIKE N'%commercial office%' OR s.Detail LIKE N'%mass timber office%'" +
            " OR s.Detail LIKE N'%anchor tenant%' OR s.Detail LIKE N'%industrial park%' OR s.Detail LIKE N'%data centre%')"),

        new SectorReportDefinition(
            "post-secondary",
            "Post-Secondary",
            "KOR Structural — BC + AB Post-Secondary BD Report",
            "((m.Sector LIKE N'Post-Secondary%' OR m.Sector LIKE N'Post-secondary%'" +
            " OR m.Sector IN (N'Academic', N'University', N'Education - Post-Secondary', N'Education-PostSec', N'StudentHousing')" +
            " OR (m.Sector = N'Education' AND (m.SubSector LIKE N'%Academic%' OR m.SubSector LIKE N'%Science%'" +
            " OR m.SubSector LIKE N'%Student Housing%' OR m.SubSector LIKE N'%Research%')))" +
            " AND m.Province IN (N'BC', N'AB'))",
            "(s.Subject LIKE N'%university%' OR s.Detail LIKE N'%university%' OR s.Detail LIKE N'%college campus%'" +
            " OR s.Detail LIKE N'%student housing%' OR s.Detail LIKE N'%polytechnic%' OR s.Detail LIKE N'%post-secondary%'" +
            " OR s.Detail LIKE N'%UBC %' OR s.Detail LIKE N'%SFU %' OR s.Detail LIKE N'%UVic%')"),

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
            " AND m.Province = N'BC')",
            "(s.Subject LIKE N'%Okanagan%' OR s.Detail LIKE N'%Okanagan%' OR s.Detail LIKE N'%Kelowna%'" +
            " OR s.Detail LIKE N'%Penticton%' OR s.Detail LIKE N'%West Kelowna%' OR s.Detail LIKE N'%Lake Country%')"),

        // The US slice the BC + AB scoping removed from the sector rows.
        // Province is the only US geography signal today (CA 147 / OR 58 /
        // WA 17 active on 2026-06-12); add new states here when ingest grows.
        new SectorReportDefinition(
            "us-west",
            "US West Coast",
            "KOR Structural — US West Coast BD Report",
            "(m.Province IN (N'CA', N'WA', N'OR'))",
            "(s.Detail LIKE N'%California%' OR s.Detail LIKE N'%Seattle%' OR s.Detail LIKE N'%Portland%'" +
            " OR s.Detail LIKE N'%Los Angeles%' OR s.Detail LIKE N'%San Diego%' OR s.Detail LIKE N'%Sacramento%'" +
            " OR s.Detail LIKE N'%Oregon%' OR s.Detail LIKE N'%Washington State%')"),
    };
}
