/*
    Kill-list review query (CRM plan 2.6, 2026-07-07).

    Run before shipping ANY new BD/CRM surface — the standing rule: no new
    surface while an equivalent surface shows zero organic opens. Standing
    dockets: the remaining nav cuts past 11 (D8), report-catalog
    consolidation, duplicate intel doors, and any surface here at ~0.

    Usage: SSMS against KorOpportunitiesDb, or /ask "run the kill-list review".
*/

-- Surface opens, last 30 / 90 days (opportunities.BdUiOpens, plan 2.2c).
SELECT Surface,
       SUM(CASE WHEN OpenedAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()) THEN 1 ELSE 0 END) AS Opens30d,
       SUM(CASE WHEN OpenedAtUtc >= DATEADD(DAY, -90, sysdatetimeoffset()) THEN 1 ELSE 0 END) AS Opens90d,
       COUNT(DISTINCT CASE WHEN OpenedAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()) THEN ByStaffId END) AS DistinctUsers30d,
       MAX(OpenedAtUtc) AS LastOpen
FROM opportunities.BdUiOpens
GROUP BY Surface
ORDER BY Opens30d DESC;

-- Report generation, last 30 / 90 days (BdReportAuditLog, migration 121).
SELECT ReportKey,
       SUM(CASE WHEN GeneratedAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()) THEN 1 ELSE 0 END) AS Gens30d,
       SUM(CASE WHEN GeneratedAtUtc >= DATEADD(DAY, -90, sysdatetimeoffset()) THEN 1 ELSE 0 END) AS Gens90d,
       MAX(GeneratedAtUtc) AS LastGenerated
FROM opportunities.BdReportAuditLog
GROUP BY ReportKey
ORDER BY Gens30d DESC;

-- Adoption milestones (plan Phase 1 exit criteria): first organic grab,
-- first user-typed activity.
SELECT
    (SELECT COUNT(*) FROM opportunities.OpportunityAssignmentLog WHERE Action = N'Grab')  AS GrabsEver,
    (SELECT COUNT(*) FROM opportunities.OpportunityAssignmentLog WHERE Action = N'Claim') AS ClaimsEver,
    (SELECT COUNT(*) FROM opportunities.CrmActivities
     WHERE CreatedBy NOT LIKE N'%Import%' AND CreatedBy NOT LIKE N'%claude%' AND CreatedBy NOT LIKE N'%(Bd%') AS NonImporterActivities,
    (SELECT COUNT(*) FROM opportunities.CrmEngagements WHERE ExternalSource LIKE N'Grab.%') AS LivePursuitsFromGrabs,
    (SELECT COUNT(*) FROM opportunities.CrmEngagements WHERE WonProjectWbs1 IS NOT NULL)   AS WinsLinkedToDeltek;
