USE [KorOpportunitiesDb];
GO

/* =====================================================================
   177 — De-duplicate MajorProjectsInventory rows. Multiple research
   streams ingested the same project repeatedly (Lions Gate Hospital x8,
   Lower Mall Student Housing x3, UBC Medicine One x2 ...), inflating every
   regional funnel and THE NUMBER. Conservative two-pass soft-retire,
   keeping the best-sourced survivor:
     Pass A — identical normalized name within (Province, Region, Municipality)
     Pass B — same owner + same REAL EstimatedCostCad within (Province,
              Region, Municipality). Real costs are specific dollar figures
              so collisions between distinct projects are negligible; rows
              on the sector-median model (EstimatedCostCad NULL) are excluded
              from Pass B so median-value collisions never merge.
   "Best" survivor = has real cost, then MPI-fed (fresh), then has a stage,
   then most-recently updated, then lowest Id. Single batch (one @now).
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* ---- Pass A: exact normalized-name duplicates ---- */
;WITH norm AS (
  SELECT Id,
    LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(ProjectName), NCHAR(8211),' '), NCHAR(8212),' '), '-',' '), '.',' '), ',',' '),
      '(',' '), ')',' '), '''',' '), '/',' '), '  ',' '))) AS K
  FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL
),
rankedA AS (
  SELECT m.Id,
    ROW_NUMBER() OVER (
      PARTITION BY ISNULL(m.Province,''), ISNULL(m.RegionName,''), ISNULL(m.MunicipalityName,''), n.K
      ORDER BY CASE WHEN m.EstimatedCostCad IS NOT NULL THEN 0 ELSE 1 END,
               CASE WHEN m.SourceKey LIKE 'BC-%' THEN 0 ELSE 1 END,
               CASE WHEN NULLIF(LTRIM(RTRIM(m.ProjectStage)),'') IS NOT NULL THEN 0 ELSE 1 END,
               m.UpdatedAtUtc DESC, m.Id ASC) rn
  FROM opportunities.MajorProjectsInventory m JOIN norm n ON n.Id = m.Id
  WHERE m.RetiredAtUtc IS NULL
)
UPDATE mp SET RetiredAtUtc=@now, RetiredReason=N'Dup project row (exact name within region/muni) — mig 177', UpdatedAtUtc=@now
FROM opportunities.MajorProjectsInventory mp JOIN rankedA r ON r.Id=mp.Id WHERE r.rn>1;

/* ---- Pass B: same owner + same REAL cost (name-variant dups) ---- */
;WITH rankedB AS (
  SELECT m.Id,
    ROW_NUMBER() OVER (
      PARTITION BY ISNULL(m.Province,''), ISNULL(m.RegionName,''), ISNULL(m.MunicipalityName,''), m.ProponentCanonicalOrgId, m.EstimatedCostCad
      ORDER BY CASE WHEN m.SourceKey LIKE 'BC-%' THEN 0 ELSE 1 END,
               CASE WHEN NULLIF(LTRIM(RTRIM(m.ProjectStage)),'') IS NOT NULL THEN 0 ELSE 1 END,
               m.UpdatedAtUtc DESC, m.Id ASC) rn
  FROM opportunities.MajorProjectsInventory m
  WHERE m.RetiredAtUtc IS NULL AND m.EstimatedCostCad IS NOT NULL AND m.ProponentCanonicalOrgId IS NOT NULL
)
UPDATE mp SET RetiredAtUtc=@now, RetiredReason=N'Dup project row (owner+real cost within region/muni) — mig 177', UpdatedAtUtc=@now
FROM opportunities.MajorProjectsInventory mp JOIN rankedB r ON r.Id=mp.Id WHERE r.rn>1;
GO
