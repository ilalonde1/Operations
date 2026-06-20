USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 242: give SF permit rows a clean, informative name built from
   structured RawJson fields: "<address>, San Francisco - <stories>-storey
   <use> (<units> units)". A bare street address alone was uninformative.
   Rebuilds ProjectName deterministically from RawJson (idempotent). Full permit
   description remains in ScheduleNotes/ProjectDescription. */
BEGIN TRAN;
UPDATE opportunities.MajorProjectsInventory
SET ProjectName = RTRIM(
      LTRIM(RTRIM(REPLACE(REPLACE(CONCAT(
        ISNULL(JSON_VALUE(RawJson,'$.street_number'),''),' ',
        ISNULL(JSON_VALUE(RawJson,'$.street_name'),''),' ',
        ISNULL(JSON_VALUE(RawJson,'$.street_suffix'),'')),'   ',' '),'  ',' ')))
      + N', San Francisco'
      + CASE WHEN NULLIF(JSON_VALUE(RawJson,'$.proposed_use'),'') IS NOT NULL
                  OR TRY_CAST(JSON_VALUE(RawJson,'$.number_of_proposed_stories') AS int) IS NOT NULL
             THEN N' - '
                  + ISNULL(CAST(TRY_CAST(JSON_VALUE(RawJson,'$.number_of_proposed_stories') AS int) AS varchar(10)) + N'-storey ','')
                  + ISNULL(JSON_VALUE(RawJson,'$.proposed_use'),'')
                  + ISNULL(N' (' + CAST(NULLIF(TRY_CAST(JSON_VALUE(RawJson,'$.proposed_units') AS int),0) AS varchar(10)) + N' units)','')
             ELSE '' END),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE SourceKey LIKE 'sf:%' AND RetiredAtUtc IS NULL
  AND LEN(ISNULL(JSON_VALUE(RawJson,'$.street_name'),'')) > 0;
PRINT 'Migration 242: SF rich naming applied to ' + CAST(@@ROWCOUNT AS varchar(10)) + ' rows.';
COMMIT TRAN;
GO
