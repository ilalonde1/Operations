SET XACT_ABORT ON;
GO

-- MPI duplicate-project dedup. 49 clusters of (ProjectName, Province) had
-- COUNT > 1, but a real audit showed three sub-patterns:
--   (a) safe auto-merge clusters — same project surfaced by 2-4 scrapers
--       with name-variant proponents. Sample: "Burtch Road Middle School"
--       with proponents "School District 23 (Central Okanagan)" and
--       "School District 23".
--   (b) generic-name false clusters — multiple DIFFERENT projects sharing
--       a generic placeholder name (Condominium Development, Residential
--       Condominium, Highrise Condominiums) with DIFFERENT proponents.
--       Do NOT merge.
--   (c) cross-province bugs — same project scraped under both BC and AB
--       (e.g., Cardston Health Centre is in AB but two rows have
--       Province='BC'; Foothills Multisport Fieldhouse is in Calgary AB
--       but one row has Province='BC'). Fix Province FIRST, then dedup
--       picks up the now-merged cluster.

BEGIN TRAN;

-- ========== Step 1: fix cross-province bugs ==========

DECLARE @provFixed int;
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB'
WHERE Id IN (
    6474, 6510,       -- Cardston Health Centre Replacement misfiled as BC
    6478              -- Foothills Multisport Fieldhouse misfiled as BC
);
SET @provFixed = @@ROWCOUNT;
PRINT 'Cross-province bugs fixed: ' + CONVERT(varchar(20), @provFixed) + ' rows';

-- ========== Step 2: identify safe-merge clusters ==========

-- Exclude generic placeholder names where multiple rows are actually
-- different projects.
DECLARE @GenericNames TABLE (Name NVARCHAR(200) PRIMARY KEY);
INSERT INTO @GenericNames VALUES
    (N'Condominium Development'),
    (N'Residential Condominium'),
    (N'Highrise Condominiums'),
    (N'Lowrise Condominium'),
    (N'Rental Towers'),
    (N'Residential Tower'),
    (N'Mixed-Use Development'),
    (N'Office Building'),
    (N'Office Tower');

-- Cluster = (ProjectName, Province) with >1 active rows, not a generic
-- name, and at most 1 distinct ProponentCanonicalOrgId (NULLs ok).
WITH SafeClusters AS (
    SELECT ProjectName, Province
    FROM opportunities.MajorProjectsInventory
    WHERE RetiredAtUtc IS NULL AND ProjectName IS NOT NULL
      AND ProjectName NOT IN (SELECT Name FROM @GenericNames)
    GROUP BY ProjectName, Province
    HAVING COUNT(*) > 1
       AND COUNT(DISTINCT ProponentCanonicalOrgId) <= 1
),
-- Survivor per cluster = lowest Id. Siblings = the rest.
RankedRows AS (
    SELECT m.Id, m.ProjectName, m.Province,
           ROW_NUMBER() OVER (PARTITION BY m.ProjectName, m.Province ORDER BY m.Id) AS rn
    FROM opportunities.MajorProjectsInventory m
    INNER JOIN SafeClusters sc ON sc.ProjectName = m.ProjectName AND sc.Province = m.Province
    WHERE m.RetiredAtUtc IS NULL
),
Survivors AS (SELECT Id FROM RankedRows WHERE rn = 1),
Siblings  AS (SELECT Id FROM RankedRows WHERE rn > 1)
SELECT Id, rn INTO #ClusterRows FROM RankedRows;

DECLARE @clusterRowCount int = (SELECT COUNT(*) FROM #ClusterRows);
DECLARE @survivorCount   int = (SELECT COUNT(*) FROM #ClusterRows WHERE rn = 1);
DECLARE @siblingCount    int = (SELECT COUNT(*) FROM #ClusterRows WHERE rn > 1);
PRINT 'Safe clusters identified: ' + CONVERT(varchar(20), @survivorCount)
    + ' (survivor rows), ' + CONVERT(varchar(20), @siblingCount) + ' siblings to retire';

-- ========== Step 3: backfill survivor's NULL fields from siblings ==========

-- For ProponentName: if survivor's is NULL, take the first non-NULL from a sibling.
UPDATE s
SET s.ProponentName = best.ProponentName,
    s.ProponentCanonicalOrgId = best.ProponentCanonicalOrgId
FROM opportunities.MajorProjectsInventory s
INNER JOIN #ClusterRows cr ON cr.Id = s.Id AND cr.rn = 1
CROSS APPLY (
    SELECT TOP 1 sib.ProponentName, sib.ProponentCanonicalOrgId
    FROM opportunities.MajorProjectsInventory sib
    INNER JOIN #ClusterRows crSib ON crSib.Id = sib.Id AND crSib.rn > 1
    WHERE sib.ProjectName = s.ProjectName AND sib.Province = s.Province
      AND sib.ProponentName IS NOT NULL
    ORDER BY CASE WHEN sib.ProponentCanonicalOrgId IS NOT NULL THEN 0 ELSE 1 END, sib.Id
) best
WHERE s.ProponentName IS NULL;
PRINT 'Survivor ProponentName backfilled from sibling: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- EstimatedCostCad: if survivor's is NULL, take MAX from siblings (largest reasonable value).
UPDATE s
SET s.EstimatedCostCad = sib.MaxCost,
    s.EstimatedCostText = COALESCE(s.EstimatedCostText, sib.AnyText)
FROM opportunities.MajorProjectsInventory s
INNER JOIN #ClusterRows cr ON cr.Id = s.Id AND cr.rn = 1
CROSS APPLY (
    SELECT MAX(sib.EstimatedCostCad) AS MaxCost, MAX(sib.EstimatedCostText) AS AnyText
    FROM opportunities.MajorProjectsInventory sib
    INNER JOIN #ClusterRows crSib ON crSib.Id = sib.Id AND crSib.rn > 1
    WHERE sib.ProjectName = s.ProjectName AND sib.Province = s.Province
) sib
WHERE s.EstimatedCostCad IS NULL AND sib.MaxCost IS NOT NULL;
PRINT 'Survivor EstimatedCostCad backfilled from sibling: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Architect / StructEng / GC FK+Name: prefer survivor's value; if NULL, take from sibling.
UPDATE s
SET s.ArchitectName = sib.ArchitectName,
    s.ArchitectCanonicalOrgId = sib.ArchitectCanonicalOrgId
FROM opportunities.MajorProjectsInventory s
INNER JOIN #ClusterRows cr ON cr.Id = s.Id AND cr.rn = 1
CROSS APPLY (
    SELECT TOP 1 sib.ArchitectName, sib.ArchitectCanonicalOrgId
    FROM opportunities.MajorProjectsInventory sib
    INNER JOIN #ClusterRows crSib ON crSib.Id = sib.Id AND crSib.rn > 1
    WHERE sib.ProjectName = s.ProjectName AND sib.Province = s.Province
      AND sib.ArchitectName IS NOT NULL
    ORDER BY CASE WHEN sib.ArchitectCanonicalOrgId IS NOT NULL THEN 0 ELSE 1 END, sib.Id
) sib
WHERE s.ArchitectName IS NULL;
PRINT 'Survivor ArchitectName backfilled: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE s
SET s.StructuralEngineerName = sib.StructuralEngineerName,
    s.StructuralEngineerCanonicalOrgId = sib.StructuralEngineerCanonicalOrgId
FROM opportunities.MajorProjectsInventory s
INNER JOIN #ClusterRows cr ON cr.Id = s.Id AND cr.rn = 1
CROSS APPLY (
    SELECT TOP 1 sib.StructuralEngineerName, sib.StructuralEngineerCanonicalOrgId
    FROM opportunities.MajorProjectsInventory sib
    INNER JOIN #ClusterRows crSib ON crSib.Id = sib.Id AND crSib.rn > 1
    WHERE sib.ProjectName = s.ProjectName AND sib.Province = s.Province
      AND sib.StructuralEngineerName IS NOT NULL
    ORDER BY CASE WHEN sib.StructuralEngineerCanonicalOrgId IS NOT NULL THEN 0 ELSE 1 END, sib.Id
) sib
WHERE s.StructuralEngineerName IS NULL;
PRINT 'Survivor StructEngName backfilled: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE s
SET s.GeneralContractorName = sib.GeneralContractorName,
    s.GeneralContractorCanonicalOrgId = sib.GeneralContractorCanonicalOrgId
FROM opportunities.MajorProjectsInventory s
INNER JOIN #ClusterRows cr ON cr.Id = s.Id AND cr.rn = 1
CROSS APPLY (
    SELECT TOP 1 sib.GeneralContractorName, sib.GeneralContractorCanonicalOrgId
    FROM opportunities.MajorProjectsInventory sib
    INNER JOIN #ClusterRows crSib ON crSib.Id = sib.Id AND crSib.rn > 1
    WHERE sib.ProjectName = s.ProjectName AND sib.Province = s.Province
      AND sib.GeneralContractorName IS NOT NULL
    ORDER BY CASE WHEN sib.GeneralContractorCanonicalOrgId IS NOT NULL THEN 0 ELSE 1 END, sib.Id
) sib
WHERE s.GeneralContractorName IS NULL;
PRINT 'Survivor GCName backfilled: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== Step 4: retire siblings ==========

DECLARE @retired int;
UPDATE m
SET m.RetiredAtUtc = sysdatetimeoffset(),
    m.RetiredReason = N'R95-extra: merged into survivor MPI row (dup ProjectName+Province cluster) 2026-06-07'
FROM opportunities.MajorProjectsInventory m
INNER JOIN #ClusterRows cr ON cr.Id = m.Id AND cr.rn > 1;
SET @retired = @@ROWCOUNT;
PRINT 'Sibling rows retired: ' + CONVERT(varchar(20), @retired);

PRINT 'Migration 74 MPI duplicate-project merge complete.';

COMMIT TRAN;
GO
