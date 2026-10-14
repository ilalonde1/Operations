-- 345: detach the Coquitlam tender that was merged into Maple Ridge's opportunity.
--
-- Opportunity 23710 (BCBID-233507, 'Audio Visual System Replacement and Upgrade -
-- City of Maple Ridge') holds observation 1425258 from BidsTenders_Coquitlam:
-- '26-067 - Civic Events - Electrical and Audio Visual Services' -- a DIFFERENT
-- procurement by a different municipality that shares only the tender number
-- 26-067. The 8-character OpportunityKey prefix collapsed the two tenants' keys
-- (BIDSTEND-26-067) on 2026-08-31. IngestionService.DisambiguateKey has prevented a
-- repeat since; this is the one row of damage it left behind, and the reason
-- tools/BdIntegrityCheck opportunity_merged_across_sibling_sources has read 1.
--
-- The observation is DETACHED (OpportunityId NULL, IsActive 0): not deleted, and
-- not given a hand-built parent row. IngestionService already recovers a stranded
-- observation and builds its opportunity properly if the candidate is ever seen
-- again. This tender closed 2026-09-25, so it will not be, and KOR does not pursue
-- AV-services contracts. Opportunity 23710 keeps its three Maple Ridge sightings.
--
-- Guarded on every fact above; idempotent (a detached row no longer matches).
-- Undo: opportunities.ObservationDetachLog holds the old OpportunityId and IsActive.
--
-- HOW TO RUN
--   report only:  sqlcmd ... -i 345_DetachCoquitlam26067FromMapleRidge.sql -v Apply=0
--   apply:        sqlcmd ... -i 345_DetachCoquitlam26067FromMapleRidge.sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;
DECLARE @migration nvarchar(100) = N'345_DetachCoquitlam26067FromMapleRidge';
DECLARE @obs bigint = 1425258;
DECLARE @opp bigint = 23710;

IF (SELECT is_nullable FROM sys.columns
    WHERE object_id = OBJECT_ID('opportunities.OpportunityObservations') AND name = 'OpportunityId') = 0
BEGIN
    RAISERROR(N'345: OpportunityObservations.OpportunityId is NOT NULL on this database; detaching is impossible. Nothing changed.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('opportunities.ObservationDetachLog') IS NULL
BEGIN
    CREATE TABLE opportunities.ObservationDetachLog (
        Id               bigint IDENTITY(1,1) PRIMARY KEY,
        ObservationId    bigint         NOT NULL,
        OldOpportunityId bigint         NOT NULL,
        OldIsActive      bit            NOT NULL,
        DetachedAtUtc    datetimeoffset NOT NULL DEFAULT sysdatetimeoffset(),
        MigrationName    nvarchar(100)  NOT NULL
    );

    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'UPDATE ob SET ob.OpportunityId = l.OldOpportunityId, ob.IsActive = l.OldIsActive FROM opportunities.OpportunityObservations ob JOIN opportunities.ObservationDetachLog l ON l.ObservationId = ob.Id WHERE l.MigrationName = N''345_DetachCoquitlam26067FromMapleRidge'';',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE',  @level1name = N'ObservationDetachLog';
END;

SELECT 'current' AS Report, ob.Id, ob.OpportunityId, s.Name AS Source, ob.Title, ob.Url, ob.IsActive
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE ob.OpportunityId = @opp OR ob.Id = @obs
ORDER BY ob.Id;

IF NOT EXISTS (
    SELECT 1
    FROM opportunities.OpportunityObservations ob
    JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
    WHERE ob.Id = @obs
      AND ob.OpportunityId = @opp
      AND s.Name = N'BidsTenders_Coquitlam'
      AND ob.Url LIKE N'https://coquitlam.bidsandtenders.ca/%'
      -- Title is stored without the tender number; the raw scrape text keeps it.
      AND ob.Title LIKE N'Civic Events - Electrical and Audio Visual Services%'
      AND ob.RawJson LIKE N'26-067 - Civic Events%')
BEGIN
    PRINT '';
    PRINT '345: the guarded facts do not hold (already detached, or the data moved). Nothing changed.';
    RETURN;
END;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed. Observation 1425258 would be detached from opportunity 23710.';
    RETURN;
END;

BEGIN TRANSACTION;

    INSERT INTO opportunities.ObservationDetachLog
        (ObservationId, OldOpportunityId, OldIsActive, MigrationName)
    SELECT Id, OpportunityId, IsActive, @migration
    FROM opportunities.OpportunityObservations
    WHERE Id = @obs AND OpportunityId = @opp;

    UPDATE opportunities.OpportunityObservations
       SET OpportunityId = NULL,
           IsActive      = 0
     WHERE Id = @obs AND OpportunityId = @opp;

    IF @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'345 aborted: expected exactly one row to detach. Nothing committed.', 16, 1);
        RETURN;
    END;

COMMIT TRANSACTION;

SELECT 'detached' AS Report, 1 AS Rows_;
SELECT 'after' AS Report, ob.Id, ob.OpportunityId, s.Name AS Source, ob.IsActive
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE ob.OpportunityId = @opp OR ob.Id = @obs
ORDER BY ob.Id;
