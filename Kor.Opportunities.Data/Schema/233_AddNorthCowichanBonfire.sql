USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 233: add North Cowichan (Bonfire). Of the 16 munis whose bids&tenders
  subdomains were dead (migration 232), only North Cowichan has a live feed on a
  platform we ingest: its Bonfire RSS feed is valid (verified - returns live
  opportunities). The other 15 post on BC Bid + their own portals (already covered
  via BcBid + CivicInfoBC). Courtenay's Bonfire URL returns HTML, not RSS - not
  usable, skipped.
*/
INSERT INTO opportunities.OpportunitySources
  (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
SELECT NEWID(), N'Bonfire_NorthCowichan', 3,
       N'https://northcowichan.bonfirehub.ca/opportunities/rss', 1, 7200, 30,
       sysdatetimeoffset(), sysdatetimeoffset(), 0
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'Bonfire_NorthCowichan');

PRINT 'Migration 233: added Bonfire_NorthCowichan.';
GO
