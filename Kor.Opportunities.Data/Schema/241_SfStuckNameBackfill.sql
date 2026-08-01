USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 241: backfill ProjectName for SF permit rows ingested BEFORE the
   composite-address provider fix (migration 219). The re-ingest MERGE updates
   LastSeen but not ProjectName, so these old rows stayed stuck on the raw permit
   description even though their RawJson has street_number/street_name/suffix.
   Rename to the composite street address (description is preserved in
   ScheduleNotes). Going forward, new SF rows are already address-named. */
BEGIN TRAN;
UPDATE opportunities.MajorProjectsInventory
SET ProjectName = LTRIM(RTRIM(REPLACE(REPLACE(
      CONCAT(ISNULL(JSON_VALUE(RawJson,'$.street_number'),''),' ',
             ISNULL(JSON_VALUE(RawJson,'$.street_name'),''),' ',
             ISNULL(JSON_VALUE(RawJson,'$.street_suffix'),'')),
    '   ',' '),'  ',' '))),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE SourceKey LIKE 'sf:%' AND RetiredAtUtc IS NULL
  AND (ProjectName LIKE '%erect%' OR ProjectName LIKE '%story%' OR ProjectName LIKE 'to %' OR ProjectName LIKE '%dwelling%' OR ProjectName LIKE '%bldg%' OR ProjectName LIKE '%alter%')
  AND LEN(ISNULL(JSON_VALUE(RawJson,'$.street_name'),'')) > 0;
PRINT 'Migration 241: renamed ' + CAST(@@ROWCOUNT AS varchar(10)) + ' stuck SF rows to composite address.';
COMMIT TRAN;
GO
