USE [KorOpportunitiesDb];
GO

/* =====================================================================
   BcBid completeness — two parts:
   1) The scrape is FLAKY (pagination quits early at a random page: yields
      bounce 30/75/90/150 run-to-run), not cap-limited. Interim fix: scrape
      4x more often (CrawlDelay 7200 -> 1800) so the union of many runs
      converges toward full coverage (opportunities persist + upsert, so a
      miss in one run is caught the next).
   2) Capture-then-fix: set bcbid.dumpGrid=true so the next run dumps the
      rendered Ivalua results-grid HTML to the diagnostics folder, letting us
      inspect the live pagination DOM and harden TryAdvanceToNextPage. Set
      this back to false (or delete the mapping) once captured.
   ===================================================================== */
DECLARE @BcBidId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'BcBid');

IF @BcBidId IS NOT NULL
BEGIN
    UPDATE opportunities.OpportunitySources
    SET CrawlDelaySeconds = 1800, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @BcBidId;

    IF EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
               WHERE OpportunitySourceId = @BcBidId AND [Key] = N'bcbid.dumpGrid')
        UPDATE opportunities.OpportunitySourceMappings
        SET ValueJson = N'true', UpdatedAtUtc = sysdatetimeoffset()
        WHERE OpportunitySourceId = @BcBidId AND [Key] = N'bcbid.dumpGrid';
    ELSE
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@BcBidId, N'bcbid.dumpGrid', N'true', sysdatetimeoffset());
END;
GO

PRINT 'Migration 49: BcBid CrawlDelay 7200->1800 + bcbid.dumpGrid=true (capture-then-fix).';
GO
