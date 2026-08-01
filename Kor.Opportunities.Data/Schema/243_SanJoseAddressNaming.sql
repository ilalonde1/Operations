USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 243: San Jose permits were named by FOLDERNAME (raw work description,
   e.g. "TI (BEPM 100%) NEW CHILLERS..."). They carry a full gx_location address.
   (a) Backfill existing rows to "<street>, San Jose" from gx_location.
   (b) Flip config: drop projectNameColumn=FOLDERNAME, set addressColumn=gx_location
       so NEW rows name by address. (FOLDERNAME stays available as description.) */
BEGIN TRAN;
-- (a) backfill existing
UPDATE opportunities.MajorProjectsInventory
SET ProjectName = LTRIM(RTRIM(REPLACE(REPLACE(
      CASE WHEN CHARINDEX(',', JSON_VALUE(RawJson,'$.gx_location')) > 1
           THEN LEFT(JSON_VALUE(RawJson,'$.gx_location'), CHARINDEX(',', JSON_VALUE(RawJson,'$.gx_location'))-1)
           ELSE JSON_VALUE(RawJson,'$.gx_location') END,
      '   ',' '),'  ',' '))) + N', San Jose',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE SourceKey LIKE 'sanjose:%' AND RetiredAtUtc IS NULL
  AND LEN(ISNULL(JSON_VALUE(RawJson,'$.gx_location'),'')) > 0;
DECLARE @n int = @@ROWCOUNT;
-- (b) flip config for going-forward
UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(JSON_MODIFY(ConfigJson, '$.projectNameColumn', NULL), '$.addressColumn', 'gx_location'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SanJoseCkan';
PRINT 'Migration 243: San Jose - backfilled ' + CAST(@n AS varchar(10)) + ' rows to gx_location address + flipped config.';
COMMIT TRAN;
GO
