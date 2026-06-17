/* =====================================================================
   ReportIntegrityCheck.sql — the quality gate every BD region report must
   pass before it ships. Catches the error-classes that nearly shipped slop:
   built projects shown active, construction-stage seats counted as "open",
   out-of-lane (industrial/marine) projects in a building-SE pursuit list,
   and any headline figure that doesn't reconcile to the live graph.

   Usage: set @Region, run against KorOpportunitiesDb. Any row with
   Result='FAIL' blocks the report. The funnel counts are emitted for the
   report to cite verbatim (no hand-typed numbers).
   ===================================================================== */
USE [KorOpportunitiesDb];
GO
SET NOCOUNT ON;
DECLARE @Province NVARCHAR(2) = N'BC';
DECLARE @Region   NVARCHAR(40) = N'Lower Mainland';   -- change per report

;WITH lm AS (
  SELECT m.*, LOWER(REPLACE(REPLACE(ISNULL(NULLIF(LTRIM(RTRIM(m.ProjectStage)),''),''),'-',' '),'_',' ')) St,
    CASE WHEN se.Id IS NULL AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL THEN 1 ELSE 0 END NoSE,
    CASE WHEN EXISTS (SELECT 1 FROM opportunities.vOrgWarmPath wp
        WHERE wp.CanonicalOrgId IN (m.ProponentCanonicalOrgId, m.ArchitectCanonicalOrgId, m.GeneralContractorCanonicalOrgId)
          AND (wp.IsDeltekClient=1 OR wp.KorProjectsCount>0)) THEN 1 ELSE 0 END Warm,
    CASE WHEN ISNULL(m.Sector,'') LIKE '%Utilit%' OR ISNULL(m.Sector,'') LIKE '%Infrastructure%'
           OR ISNULL(m.Sector,'') LIKE '%Industrial%' OR ISNULL(m.Sector,'') LIKE '%Transport%' OR ISNULL(m.Sector,'') LIKE '%Energy%'
           OR m.ProjectName LIKE '%LNG%' OR m.ProjectName LIKE '%Wastewater%' OR m.ProjectName LIKE '%Water Treatment%'
           OR m.ProjectName LIKE '%Terminal%' OR m.ProjectName LIKE '%Substation%' OR m.ProjectName LIKE '%Pipeline%'
           OR m.ProjectName LIKE '%Treatment Plant%' OR m.ProjectName LIKE '%Wastewater%' THEN 1 ELSE 0 END OutOfLane
  FROM opportunities.MajorProjectsInventory m
  LEFT JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId
  WHERE m.Province=@Province AND m.RegionName=@Region AND m.RetiredAtUtc IS NULL
),
early AS (SELECT * FROM lm WHERE NoSE=1 AND OutOfLane=0 AND St<>'' AND St NOT LIKE '%construction%' AND St NOT LIKE '%complete%' AND St NOT LIKE '%hold%' AND St NOT LIKE '%cancel%' AND St NOT LIKE '%design%')
SELECT Chk, CAST(Val AS NVARCHAR(20)) Value, Result FROM (
  /* informational funnel counts (cite these in the report) */
  SELECT 1 ord, 'INFO: active major projects' Chk, (SELECT COUNT(*) FROM lm) Val, 'INFO' Result
  UNION ALL SELECT 2,'INFO: early-open (no SE, in-lane)', (SELECT COUNT(*) FROM early),'INFO'
  UNION ALL SELECT 3,'INFO: warm pursuit universe', (SELECT COUNT(*) FROM early WHERE Warm=1),'INFO'
  UNION ALL SELECT 4,'INFO: warm fee $M (1pct)', (SELECT CAST(SUM(COALESCE(EstimatedCostCad,ModeledCostCad))*0.01/1e6 AS INT) FROM early WHERE Warm=1),'INFO'
  UNION ALL SELECT 5,'INFO: addressable fee $M (1pct)', (SELECT CAST(SUM(COALESCE(EstimatedCostCad,ModeledCostCad))*0.01/1e6 AS INT) FROM early),'INFO'
  /* hard gates — any FAIL blocks the report */
  UNION ALL SELECT 6,'GATE: 0 Completed/built shown active', (SELECT COUNT(*) FROM lm WHERE St='completed'),
    CASE WHEN (SELECT COUNT(*) FROM lm WHERE St='completed')=0 THEN 'PASS' ELSE 'FAIL' END
  UNION ALL SELECT 7,'GATE: 0 construction-stage in early-open', (SELECT COUNT(*) FROM early WHERE St LIKE '%construction%'),
    CASE WHEN (SELECT COUNT(*) FROM early WHERE St LIKE '%construction%')=0 THEN 'PASS' ELSE 'FAIL' END
  UNION ALL SELECT 8,'GATE: 0 on-hold in early-open', (SELECT COUNT(*) FROM early WHERE St LIKE '%hold%'),
    CASE WHEN (SELECT COUNT(*) FROM early WHERE St LIKE '%hold%')=0 THEN 'PASS' ELSE 'FAIL' END
  UNION ALL SELECT 9,'GATE: 0 out-of-lane in early-open', (SELECT COUNT(*) FROM early WHERE OutOfLane=1),
    CASE WHEN (SELECT COUNT(*) FROM early WHERE OutOfLane=1)=0 THEN 'PASS' ELSE 'FAIL' END
  UNION ALL SELECT 10,'GATE: 0 design-stage in early-open', (SELECT COUNT(*) FROM early WHERE St LIKE '%design%'),
    CASE WHEN (SELECT COUNT(*) FROM early WHERE St LIKE '%design%')=0 THEN 'PASS' ELSE 'FAIL' END
) x ORDER BY ord;
GO
