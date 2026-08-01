/*
    Adds a claim-fencing token to opportunities.IngestionTriggers so stale
    InProgress reclaim invalidates the previous worker's completion write.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.IngestionTriggers') AND name = N'ClaimToken')
BEGIN
    ALTER TABLE opportunities.IngestionTriggers
        ADD ClaimToken uniqueidentifier NULL;
END;
GO

UPDATE opportunities.IngestionTriggers
SET    ClaimToken = NEWID()
WHERE  Status = N'InProgress'
  AND  ClaimToken IS NULL;
GO

PRINT 'Migration 30 ingestion trigger claim token complete.';
GO
