USE [KorOpportunitiesDb];
GO

DECLARE @GcId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'GovCanada_ProactiveDisclosure');

IF @GcId IS NULL
BEGIN
    PRINT 'Migration 36: GovCanada_ProactiveDisclosure missing - nothing to update.';
    RETURN;
END;

/* Re-enable + restore a generous per-run cap (the SQL filter restricts to relevant rows
   server-side, so this is much safer than ramping up the unfiltered cap). */
UPDATE opportunities.OpportunitySources
SET    IsEnabled    = 1,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  Id = @GcId;

/* Add the SQL-filter mapping. The query targets construction-relevant rows by
   keyword in the description plus value floor. ORDER BY contract_date DESC so
   the newest contracts paginate first.

   Resource id MUST match the one in the BaseUrl (verified live 2026-05-24:
   fac950c0-00d5-4ec1-a4d3-9cbebf98a305). */
DECLARE @sql nvarchar(max) = N'SELECT * FROM "fac950c0-00d5-4ec1-a4d3-9cbebf98a305" '
  + N'WHERE (description_en ILIKE ''%construction%'' '
  +    N'OR description_en ILIKE ''%structural%'' '
  +    N'OR description_en ILIKE ''%engineering%'' '
  +    N'OR description_en ILIKE ''%architectural%'' '
  +    N'OR description_en ILIKE ''%building%'' '
  +    N'OR description_en ILIKE ''%renovation%'' '
  +    N'OR description_en ILIKE ''%seismic%'' '
  +    N'OR description_en ILIKE ''%retrofit%'') '
  + N'AND CAST(contract_value AS numeric) > 25000 '
  + N'ORDER BY contract_date DESC';

IF NOT EXISTS (
    SELECT 1 FROM opportunities.OpportunitySourceMappings
    WHERE OpportunitySourceId = @GcId AND [Key] = N'json.sqlQuery')
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@GcId, N'json.sqlQuery', @sql, sysdatetimeoffset());
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySourceMappings
    SET    ValueJson = @sql, UpdatedAtUtc = sysdatetimeoffset()
    WHERE  OpportunitySourceId = @GcId AND [Key] = N'json.sqlQuery';
END;

/* Bump the per-run cap back up - the SQL filter restricts the result set
   so we can safely pull more per cron tick. */
UPDATE opportunities.OpportunitySourceMappings
SET    ValueJson = N'50000', UpdatedAtUtc = sysdatetimeoffset()
WHERE  OpportunitySourceId = @GcId AND [Key] = N'json.maxRowsPerRun';

PRINT 'Migration 36: Gov Canada SQL-filter mapping installed.';
GO
