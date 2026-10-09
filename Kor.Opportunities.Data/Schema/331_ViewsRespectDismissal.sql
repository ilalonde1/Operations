-- Migration 331 (2026-10-08): the two views that read Opportunities now respect
-- DismissedAtUtc.
--
-- THE DEFECT. `opportunities.PrimePipeline` and `opportunities.vIslandApplications`
-- are the only two views that read `opportunities.Opportunities`, and neither filtered
-- dismissed rows. So the two ways a row is meant to leave the pipeline both failed to
-- reach them:
--
--   * "Not for us" (migration 284) — an audited dismissal with a reason. The row leaves
--     the Bazaar, the weekly sheet and the digests, and keeps appearing here.
--   * A dedup merge. `tools/BdOpportunityDedup` sets DismissedAtUtc on the loser with
--     "Same RFP as <key>; ... Observations and child rows merged onto that record." On
--     2026-10-08 that ran twice, merging 121 rows into 105 survivors — and changed
--     nothing on these two surfaces, which carried on counting both copies.
--
-- The dedup tool's own header already said so: "PrimePipeline and vIslandApplications
-- do not filter DismissedAtUtc, so merging changes nothing for those two views. They
-- were already counting both copies and will carry on doing so until they are fixed
-- separately." This is that fix.
--
-- ⚠ WHAT I GOT WRONG FIRST. I reported this as "15 of 17 views ignore DismissedAtUtc",
--   from a string match on every view definition. Fourteen of those never read the
--   Opportunities table at all — they are org, project and Deltek views where the column
--   does not exist and the filter would be meaningless. The real number is TWO, and
--   `vw_ActionableOpportunities` already had it right. Count what the thing does, not
--   what a LIKE matches.
--
-- NOT TOUCHED: the MajorProjectsInventory arm of PrimePipeline. That table has its own
-- RetiredAtUtc, already filtered, and no DismissedAtUtc column.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

SELECT 'before' AS Section,
       (SELECT COUNT(*) FROM opportunities.PrimePipeline)                                AS PrimePipeline,
       (SELECT COUNT(*) FROM opportunities.PrimePipeline WHERE PipelineType = 'Open RFP') AS PrimePipelineOpenRfp,
       (SELECT COUNT(*) FROM opportunities.vIslandApplications)                           AS IslandApplications,
       (SELECT COUNT(*) FROM opportunities.Opportunities WHERE DismissedAtUtc IS NOT NULL) AS DismissedRows;
GO

ALTER VIEW opportunities.PrimePipeline AS
SELECT
    CAST('Open RFP' AS nvarchar(20))            AS PipelineType,
    CAST(o.Id AS nvarchar(40))                   AS SourceRef,
    CAST(o.Name AS nvarchar(500))                AS ProjectName,
    CAST(o.BuyerName AS nvarchar(500))           AS BuyerOrOwner,
    CAST(o.PrimeProjectSector AS nvarchar(120))  AS Sector,
    CAST(o.Status AS nvarchar(120))              AS Stage,
    CAST(o.EstimatedValue AS decimal(18,2))      AS EstimatedValueCad,
    CAST(o.ProjectProvince AS nvarchar(40))      AS Province,
    CAST(o.ProjectCity AS nvarchar(200))         AS City,
    CAST(NULL AS nvarchar(500))                  AS ArchitectName,
    CAST(NULL AS nvarchar(1000))                 AS SourceUrl
FROM opportunities.Opportunities o
WHERE o.IsPrimeConsultantRfp = 1
  AND o.Status NOT IN (6, 7)
  AND o.DismissedAtUtc IS NULL   -- 331: a dismissed or merged-away row is not pipeline
  AND (o.SubmissionDeadlineUtc IS NULL OR o.SubmissionDeadlineUtc >= SYSDATETIMEOFFSET())
UNION ALL
SELECT
    CAST('Pipeline Project' AS nvarchar(20)),
    CAST(m.Id AS nvarchar(40)),
    CAST(m.ProjectName AS nvarchar(500)),
    CAST(m.ProponentName AS nvarchar(500)),
    CAST(m.Sector AS nvarchar(120)),
    CAST(m.Stage AS nvarchar(120)),
    CAST(m.EstimatedCostCad AS decimal(18,2)),
    CAST(m.Province AS nvarchar(40)),
    CAST(m.MunicipalityName AS nvarchar(200)),
    CAST(m.ArchitectName AS nvarchar(500)),
    CAST(m.SourceUrl AS nvarchar(1000))
FROM opportunities.MajorProjectsInventory m
WHERE m.Province IN ('BC','AB','CA','WA','OR')
  AND m.RetiredAtUtc IS NULL
  AND (
        m.Sector LIKE '%school%' OR m.Sector LIKE '%hospital%' OR m.Sector LIKE '%health%'
     OR m.Sector LIKE '%recreation%' OR m.Sector LIKE '%civic%' OR m.Sector LIKE '%cultural%'
     OR m.Sector LIKE '%universit%' OR m.Sector LIKE '%college%' OR m.Sector LIKE '%library%'
     OR m.Sector LIKE '%communit%' OR m.Sector LIKE '%education%' OR m.Sector LIKE '%housing%'
     OR m.Sector LIKE '%institution%' OR m.Sector LIKE '%care%'
     OR m.Sector IN ('Civic','Tourism / Recreation','Government','Mixed-use')
      )
  AND (m.Stage IS NULL OR NOT (
        m.Stage LIKE '%complet%' OR m.Stage LIKE 'construction%' OR m.Stage LIKE '%under construction%'
     OR m.Stage LIKE '%construction started%' OR m.Stage LIKE '%in construction%'
     OR m.Stage LIKE '%construction phase%' OR m.Stage LIKE '%in-service%' OR m.Stage LIKE '%in service%'
     OR m.Stage LIKE '%operating%' OR m.Stage LIKE '%occupancy%' OR m.Stage LIKE '%built%'
     OR m.Stage LIKE '%in progress%' OR m.Stage LIKE '%underway%' OR m.Stage LIKE '%demolition%'
      ));
GO

ALTER VIEW opportunities.vIslandApplications
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
WHERE o.DismissedAtUtc IS NULL   -- 331: a dismissed or merged-away row is not an application
  AND s.Name IN (
    N'Victoria_DevelopmentApplications',      N'Saanich_DevelopmentApplications',
    N'Langford_DevelopmentApplications',      N'Colwood_DevelopmentApplications',
    N'Nanaimo_WhatsBuilding',                 N'Courtenay_DevelopmentApplications',
    N'CampbellRiver_DevelopmentApplications', N'Comox_PlanningPermits',
    N'Comox_BuildingPermits',                 N'QualicumBeach_DevelopmentApplications',
    N'RDN_DevelopmentApplications',           N'Parksville_DevelopmentApplications');
GO

SELECT 'after' AS Section,
       (SELECT COUNT(*) FROM opportunities.PrimePipeline)                                AS PrimePipeline,
       (SELECT COUNT(*) FROM opportunities.PrimePipeline WHERE PipelineType = 'Open RFP') AS PrimePipelineOpenRfp,
       (SELECT COUNT(*) FROM opportunities.vIslandApplications)                           AS IslandApplications;

SELECT 'every view reading Opportunities now filters dismissal' AS Check_,
       COUNT(*) AS StillUnfiltered
FROM sys.views v
JOIN sys.sql_modules m ON m.object_id = v.object_id
WHERE m.definition LIKE '%opportunities.Opportunities%'
  AND m.definition NOT LIKE '%DismissedAtUtc%';
GO
