SET XACT_ABORT ON;
GO

-- AB project ProjectStage backfill. 399 active AB MPI rows had
-- ProjectStage = NULL, same root cause as the US bug fixed in m106:
-- ImportAlbertaMarketAsync was passing ProjectStage = "AlbertaMarketResearch",
-- which ProjectStageRouter classified as a pipeline-tag (not a lifecycle
-- stage), leaving the canonical column NULL.
--
-- Source-side fix landed in BdResearchImport (same commit). This migration
-- backfills the existing rows so they enter the BD-actionable drain flow.

BEGIN TRAN;

UPDATE opportunities.MajorProjectsInventory
SET ProjectStage = N'Planned'
WHERE RetiredAtUtc IS NULL
  AND Province = N'AB'
  AND ProjectStage IS NULL;
PRINT 'AB projects backfilled to ProjectStage=Planned: ' + CONVERT(varchar(20), @@ROWCOUNT);

SELECT Province, COUNT(*) AS NowEligible
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = N'AB'
  AND ProjectStage IN (N'CapitalPlan',N'Planned',N'Concept',N'Design',N'Permitting',N'Procurement',N'Approved',N'Announced')
GROUP BY Province;

PRINT 'Migration 107 complete.';

COMMIT TRAN;
GO
