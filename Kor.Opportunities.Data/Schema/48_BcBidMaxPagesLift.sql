USE [KorOpportunitiesDb];
GO

/* =====================================================================
   #99 — get more A&E / prime-consultant RFPs out of BC Bid.
   ---------------------------------------------------------------------
   The BcBid OPPORTUNITIES source had no playwright.maxPages mapping, so it
   used the scraper default of 10 (~150 rows) — unfiltered and capped well
   below BC Bid's open-opportunity volume, so most A&E RFPs were never
   scraped (only 3 of 279 BcBid opps flagged prime).

   Lift the cap to 50 (~750 rows, matching BcBidAwards/Historical/Unverified)
   so the scrape captures essentially the whole open board; the prime
   classifier then surfaces the A&E/prime ones downstream. No keyword filter
   needed — and no fragile live-form automation.

   If a future run returns ~750 rows (hit the cap), bump higher.
   ===================================================================== */
DECLARE @BcBidId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'BcBid');

IF @BcBidId IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
               WHERE OpportunitySourceId = @BcBidId AND [Key] = N'playwright.maxPages')
        UPDATE opportunities.OpportunitySourceMappings
        SET ValueJson = N'50', UpdatedAtUtc = sysdatetimeoffset()
        WHERE OpportunitySourceId = @BcBidId AND [Key] = N'playwright.maxPages';
    ELSE
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@BcBidId, N'playwright.maxPages', N'50', sysdatetimeoffset());
END;
GO

PRINT 'Migration 48: BcBid opportunities maxPages lifted to 50 (was default 10).';
GO
