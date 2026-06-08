SET XACT_ABORT ON;
GO

-- US project ProjectStage backfill. 367 active US-state MPI rows
-- (CA / OR / WA) had ProjectStage = NULL because whichever ingestion
-- provider feeds them doesn't normalize the stage. Result: they were
-- invisible to every drain query (which filters on EligibleStages),
-- and to the BD Dashboard's forward pipeline.
--
-- Backfill default = 'Planned' so they enter the drain flow. Sonnet's
-- ProjectBrief research will surface the real stage in IntelProject.
-- Source-side fix (the ingestion provider) is a separate TODO.

BEGIN TRAN;

UPDATE opportunities.MajorProjectsInventory
SET ProjectStage = N'Planned'
WHERE RetiredAtUtc IS NULL
  AND Province IN (N'CA', N'OR', N'WA')
  AND ProjectStage IS NULL;
PRINT 'US projects backfilled to ProjectStage=Planned: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Post-state check
SELECT Province, COUNT(*) AS NowEligible
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province IN (N'CA', N'OR', N'WA')
  AND ProjectStage IN (N'CapitalPlan',N'Planned',N'Concept',N'Design',N'Permitting',N'Procurement',N'Approved',N'Announced')
GROUP BY Province;

PRINT 'Migration 106 complete.';

COMMIT TRAN;
GO
