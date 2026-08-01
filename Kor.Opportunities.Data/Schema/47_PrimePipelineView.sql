USE [KorOpportunitiesDb];
GO

/* =====================================================================
   opportunities.PrimePipeline — the unified prime-consultant job pool.
   ---------------------------------------------------------------------
   Combines, in one list:
     1) Open prime-consultant RFPs (Opportunities.IsPrimeConsultantRfp=1)
        — the "See" stage, team-forming NOW.
     2) Upcoming BUILDING projects from MajorProjectsInventory in KOR
        markets that are NOT yet built/under-construction — prime
        opportunities earlier than the RFP (get on the team first).

   Building sectors only (KOR structural wheelhouse: health/education/
   civic/recreation/cultural/housing/institutional/community) — excludes
   infrastructure/oil&gas/power/pipelines/industrial. Stage filter is a
   negative "not built/under construction" (the Stage column is free-text
   across sources, so we exclude the done states rather than enumerate
   the upcoming ones).

   This is the substrate the AI "Crucible" will score; the WPF Prime
   Pipeline view reads it directly.
   ===================================================================== */
CREATE OR ALTER VIEW opportunities.PrimePipeline AS
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
  AND (
        m.Sector LIKE '%school%' OR m.Sector LIKE '%hospital%' OR m.Sector LIKE '%health%'
     OR m.Sector LIKE '%recreation%' OR m.Sector LIKE '%civic%' OR m.Sector LIKE '%cultural%'
     OR m.Sector LIKE '%universit%' OR m.Sector LIKE '%college%' OR m.Sector LIKE '%library%'
     OR m.Sector LIKE '%communit%' OR m.Sector LIKE '%education%' OR m.Sector LIKE '%housing%'
     OR m.Sector LIKE '%institution%' OR m.Sector LIKE '%care%'
     OR m.Sector IN ('Civic','Tourism / Recreation','Government','Mixed-use')
      )
  -- Keep unknown (NULL) Stage as a prospect; only exclude the clearly built /
  -- under-construction states. (NOT(NULL) is NULL, which WHERE drops — so the
  -- NULL case must be allowed explicitly.)
  AND (m.Stage IS NULL OR NOT (
        m.Stage LIKE '%complet%' OR m.Stage LIKE 'construction%' OR m.Stage LIKE '%under construction%'
     OR m.Stage LIKE '%construction started%' OR m.Stage LIKE '%in construction%'
     OR m.Stage LIKE '%construction phase%' OR m.Stage LIKE '%in-service%' OR m.Stage LIKE '%in service%'
     OR m.Stage LIKE '%operating%' OR m.Stage LIKE '%occupancy%' OR m.Stage LIKE '%built%'
     OR m.Stage LIKE '%in progress%' OR m.Stage LIKE '%underway%' OR m.Stage LIKE '%demolition%'
      ));
GO

PRINT 'Migration 47: opportunities.PrimePipeline view created (open prime RFPs + upcoming building projects).';
GO
