USE [KorOpportunitiesDb];
GO

/* =====================================================================
   #99 — get more A&E / prime-consultant RFPs out of BC Bid.
   ---------------------------------------------------------------------
   The BcBid OPPORTUNITIES source had no playwright.maxPages mapping, so it
   used the scraper default of 10 (~150 rows) — unfiltered and capped well
   below BC Bid's open-opportunity volume, so most A&E RFPs were never
   scraped (only 3 of 279 BcBid opps flagged prime).

   Goal: get the WHOLE open board, miss nothing. The scraper auto-stops at the
   real last page (TryAdvanceToNextPage returns false), so maxPages is only a
   safety ceiling — set it high (100 = ~1,500 rows) so pagination runs to the
   end of BC Bid's open opportunities regardless of volume. The prime
   classifier filters to A&E/prime downstream, so nothing is lost.

   VERIFY after the next BcBid run: if it returns ~1,500 rows it hit the ceiling
   (there's more) — bump higher. If it returns fewer, we have the full board.
   ===================================================================== */
DECLARE @BcBidId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'BcBid');

IF @BcBidId IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
               WHERE OpportunitySourceId = @BcBidId AND [Key] = N'playwright.maxPages')
        UPDATE opportunities.OpportunitySourceMappings
        SET ValueJson = N'100', UpdatedAtUtc = sysdatetimeoffset()
        WHERE OpportunitySourceId = @BcBidId AND [Key] = N'playwright.maxPages';
    ELSE
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@BcBidId, N'playwright.maxPages', N'100', sysdatetimeoffset());
END;
GO

PRINT 'Migration 48: BcBid opportunities maxPages lifted to 100 (full pagination; was default 10).';
GO
