-- Migration 316 (2026-09-08): ONE definition of an Island big-ticket application.
--
-- ⛔ WHY THIS IS A VIEW AND NOT A QUERY. The filter Rory asked for ("weed out
--    single family homes, TI stuff") was written twice in one session, as a CASE
--    with buckets and as a NOT(...) predicate, and the two disagreed:
--    1,130 vs 1,156 rows. One of them shipped in a dossier. The predicate lives
--    here now so a count in the app, a count in a dossier and a count in an
--    ad-hoc query cannot drift apart again.
--
-- WHAT IT COVERS: the twelve Vancouver Island municipal application feeds.
--    It removes single-lot residential permits, single-family dwellings, tenant
--    improvements, alterations, secondary suites and accessory dwellings, signs,
--    accessory structures (decks, fences, pools, sheds, garages, carports,
--    retaining walls, driveways, boat sheds), demolitions, and servicing or
--    engineering permits.
--
-- WHAT IT DOES NOT COVER, stated so nobody reads the name and stops looking:
--    * It cannot rank by value. Only Langford publishes a construction value, so
--      "big ticket" here means "not obviously small", not "over $X".
--    * It reads Title and the newest observation Description. An application
--      whose scope text is empty or uninformative passes the filter by default.
--    * A multi-unit building described only as "RESIDENTIAL BUILDING PERMIT" is
--      removed with the single lots, because the source gives nothing to tell
--      them apart. Saanich is where that bites hardest.
--    * ⚠ It says nothing about DATES. Four sources publish none at all --
--      Nanaimo, Campbell River and both Comox layers -- so any "last N months"
--      figure taken from this view silently excludes them. Use IsDated.
--
-- A same-class fault it would NOT catch: a tenant improvement described only as
-- "interior renovation" carries none of the words above and stays in.
USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER VIEW opportunities.vIslandApplications
AS
SELECT
    o.Id                              AS OpportunityId,
    s.Name                            AS SourceName,
    REPLACE(REPLACE(REPLACE(s.Name, '_DevelopmentApplications', ''),
            '_WhatsBuilding', ''), '_', ' ')  AS Municipality,
    o.Name                            AS Title,
    o.BuyerContactName                AS Applicant,
    o.ProjectAddress                  AS Address,
    o.EstimatedValue                  AS EstimatedValue,
    o.RfpReleaseDate                  AS FiledDate,
    CAST(CASE WHEN o.RfpReleaseDate IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsDated,
    d.Descr                           AS Scope,
    CAST(CASE WHEN
         o.Name LIKE '%RESIDENTIAL BUILDING PERMIT%' OR o.Name LIKE '%RESIDENTIAL PERMIT%'
      OR o.Name LIKE '%SFD%'            OR d.Descr LIKE '%SFD%'
      OR d.Descr LIKE '%single family%' OR d.Descr LIKE '%single-family%'
      OR d.Descr LIKE '%single dwelling%'
      OR d.Descr LIKE '%tenant improvement%' OR o.Name LIKE '%TENANT IMPROVEMENT%'
      OR d.Descr LIKE '%First TI%'
      OR o.Name LIKE '%ALTERATION%'     OR d.Descr LIKE '%alteration%'
      OR d.Descr LIKE '%secondary suite%' OR o.Name LIKE '%SECONDARY SUITE%'
      OR d.Descr LIKE '%carriage house%'  OR d.Descr LIKE '%coach house%'
      OR d.Descr LIKE '%garden suite%'    OR d.Descr LIKE '%accessory dwelling%'
      OR o.Name LIKE '%SIGN%'           OR d.Descr LIKE '%ign Permit%'
      OR d.Descr LIKE '%deck%'   OR d.Descr LIKE '%fence%'  OR d.Descr LIKE '%pool%'
      OR d.Descr LIKE '%shed%'   OR d.Descr LIKE '%garage%' OR d.Descr LIKE '%carport%'
      OR d.Descr LIKE '%retaining wall%' OR d.Descr LIKE '%driveway%'
      OR d.Descr LIKE '%demolition%'
      OR d.Descr LIKE '%service connection%' OR o.Name LIKE '%SERVICE CONNECTION%'
      OR d.Descr LIKE '%ENGINEERING PERMIT%'
    THEN 0 ELSE 1 END AS bit)         AS IsBigTicket
FROM opportunities.Opportunities o
JOIN (
    SELECT DISTINCT ob.OpportunityId, ob.OpportunitySourceId
    FROM opportunities.OpportunityObservations ob
) x ON x.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = x.OpportunitySourceId
OUTER APPLY (
    SELECT TOP 1 ob2.Description AS Descr
    FROM opportunities.OpportunityObservations ob2
    WHERE ob2.OpportunityId = o.Id
    ORDER BY ob2.Id DESC
) d
WHERE s.Name IN (
    N'Victoria_DevelopmentApplications',      N'Saanich_DevelopmentApplications',
    N'Langford_DevelopmentApplications',      N'Colwood_DevelopmentApplications',
    N'Nanaimo_WhatsBuilding',                 N'Courtenay_DevelopmentApplications',
    N'CampbellRiver_DevelopmentApplications', N'Comox_PlanningPermits',
    N'Comox_BuildingPermits',                 N'QualicumBeach_DevelopmentApplications',
    N'RDN_DevelopmentApplications',           N'Parksville_DevelopmentApplications');
GO

SELECT 'the one definition, as of now' AS Section;
SELECT COUNT(*) AS AllApplications,
       SUM(CASE WHEN IsBigTicket = 1 THEN 1 ELSE 0 END) AS BigTicket,
       SUM(CASE WHEN IsBigTicket = 1 AND IsDated = 0 THEN 1 ELSE 0 END) AS BigTicketUndated,
       SUM(CASE WHEN IsBigTicket = 1 AND IsDated = 1 THEN 1 ELSE 0 END) AS BigTicketDated,
       SUM(CASE WHEN IsBigTicket = 1 AND FiledDate >= DATEADD(day,-365,SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS BigTicketLast12mo
FROM opportunities.vIslandApplications;

SELECT 'by municipality' AS Section;
SELECT Municipality, COUNT(*) AS Total,
       SUM(CASE WHEN IsBigTicket = 1 THEN 1 ELSE 0 END) AS BigTicket,
       SUM(CASE WHEN IsBigTicket = 1 AND IsDated = 1 THEN 1 ELSE 0 END) AS BigTicketDated
FROM opportunities.vIslandApplications
GROUP BY Municipality ORDER BY SUM(CASE WHEN IsBigTicket = 1 THEN 1 ELSE 0 END) DESC;
GO
