-- 129_ProponentUnresolvable.sql
-- The proponents drain re-batched the same 5 dead-end MPIs every run:
-- their proponent is genuinely un-researchable (sessions skipped them in
-- batches 004, 005 and 006), but nothing RECORDED that conclusion, so the
-- empty-ProponentName selector re-picked them forever. Record the
-- conclusion as a ProponentResearch enrichment row with
-- Status='Unresolvable'; the selector (Generate-Batch 'proponents' kind)
-- now skips any MPI with a ProponentResearch attempt in the last 90 days,
-- so these auto-retry quarterly.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

INSERT INTO opportunities.MajorProjectEnrichment
    (MajorProjectsInventoryId, ProviderName, Status, LastRefreshAtUtc, NextRefreshAtUtc, Attempts, Notes)
SELECT m.Id, N'ProponentResearch', N'Unresolvable', sysdatetimeoffset(), DATEADD(DAY, 90, sysdatetimeoffset()), 3,
       N'm129: proponent unresolvable after 3 drain sessions (batches 004-006, 2026-06-10/11); auto-retries after 90 days'
FROM opportunities.MajorProjectsInventory m
WHERE m.Id IN (890, 891, 3296, 3377, 3566)
  AND m.RetiredAtUtc IS NULL
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment e
                  WHERE e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProponentResearch');
PRINT 'Unresolvable proponent rows recorded: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
GO
