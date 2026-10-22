-- 381: two Events rows were research notes, not events (2026-10-22): "Housing Central Conference 2026 - already
-- included above (deduplicated)" showed on the Events screen as a second Housing Central, and "2026 UBCM Convention
-- (duplicate entry - merged above)" is the same kind. The real Housing Central and UBCM rows stay. Retired, never
-- deleted; the events import's MERGE does not touch RetiredAtUtc, so a re-import does not bring them back.
-- Only rows not already retired, and only names that say they are duplicates, so re-running changes nothing.
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

UPDATE opportunities.IndustryEvents
   SET RetiredAtUtc  = SYSDATETIMEOFFSET(),
       RetiredReason = N'A research note naming a duplicate, not an event (migration 381).',
       UpdatedAtUtc  = SYSDATETIMEOFFSET()
 WHERE RetiredAtUtc IS NULL
   AND (Name LIKE N'%already included above%' OR Name LIKE N'%(duplicate entry%');

COMMIT;

SELECT (SELECT COUNT(*) FROM opportunities.IndustryEvents
         WHERE Name LIKE N'%already included above%' OR Name LIKE N'%(duplicate entry%') AS NoteRows,
       (SELECT COUNT(*) FROM opportunities.IndustryEvents
         WHERE RetiredAtUtc IS NULL AND (Name LIKE N'%already included above%' OR Name LIKE N'%(duplicate entry%')) AS StillShown,
       (SELECT COUNT(*) FROM opportunities.IndustryEvents
         WHERE RetiredAtUtc IS NULL AND (Name LIKE N'Housing Central Conference 2026%' OR Name LIKE N'%UBCM Convention%')) AS RealRowsKept;
