USE [KorOpportunitiesDb];
GO

/* =====================================================================
   173 — Backfill ProjectStage from the BC MPI feed's PROJECT_STATUS, then
   retire the built ones. The BC provider mapped Stage from PROJECT_STAGE
   (mostly blank) and the PROJECT_STATUS fallback didn't land, so 658 LM
   "open seats" had blank stage — including 399 "Construction started"
   (SE locked) and 22 "Completed" (built). Without the real stage the
   open-seat count is dishonest (the JCC-class error at scale).

   PROJECT_STATUS values in the feed: Construction started / Proposed /
   On hold / Completed. Backfill where Stage is blank; then retire Completed.
   (Provider fix to map PROJECT_STATUS going forward is queued separately so
   the weekly re-ingest doesn't re-blank this.)
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET ProjectStage = LEFT(LTRIM(RTRIM(JSON_VALUE(RawJson, '$.PROJECT_STATUS'))), 50), UpdatedAtUtc = @now
WHERE Province = 'BC' AND RetiredAtUtc IS NULL
  AND (ProjectStage IS NULL OR LTRIM(RTRIM(ProjectStage)) = '')
  AND RawJson IS NOT NULL AND ISJSON(RawJson) = 1
  AND NULLIF(LTRIM(RTRIM(JSON_VALUE(RawJson, '$.PROJECT_STATUS'))), '') IS NOT NULL;

/* retire the built ones (Completed = not pursuable, parity with Gibson/FPGH) */
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = @now, RetiredReason = N'Built/complete per BC MPI PROJECT_STATUS — migration 173', UpdatedAtUtc = @now
WHERE Province = 'BC' AND RetiredAtUtc IS NULL AND LTRIM(RTRIM(ProjectStage)) = 'Completed';
GO
