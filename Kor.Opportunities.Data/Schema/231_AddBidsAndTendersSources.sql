USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 231: add CRD + net-new BC municipalities/regional-districts that run
  on the bids&tenders platform (provider already exists - Playwright-based,
  ConfigJson=NULL, BaseUrl is all it needs). Adds both live Tenders (type 10) and
  Awards (type 12) rows, matching the existing pattern. Idempotent (NOT EXISTS).
  Excludes orgs already covered via Bonfire (Victoria, Saanich, Kelowna, Kamloops,
  Penticton) to avoid double-ingestion. Validate post-insert; disable any dead.
*/
DECLARE @orgs TABLE (Nm nvarchar(60), Slug nvarchar(60));
INSERT INTO @orgs VALUES
 (N'CRD',           N'crd'),            -- Capital Regional District (Victoria)
 (N'NewWest',       N'newwest'),        -- City of New Westminster
 (N'Delta',         N'delta'),          -- City of Delta
 (N'CNV',           N'cnv'),            -- City of North Vancouver
 (N'WestVancouver', N'westvancouver'),  -- District of West Vancouver
 (N'LangleyCity',   N'langleycity'),    -- City of Langley
 (N'PortMoody',     N'portmoody'),      -- City of Port Moody
 (N'WhiteRock',     N'whiterock'),      -- City of White Rock
 (N'Chilliwack',    N'chilliwack'),     -- City of Chilliwack
 (N'Langford',      N'langford'),       -- City of Langford
 (N'Esquimalt',     N'esquimalt'),      -- Township of Esquimalt
 (N'Whistler',      N'whistler'),       -- Resort Municipality of Whistler
 (N'SCRD',          N'scrd'),           -- Sunshine Coast Regional District
 (N'FVRD',          N'fvrd'),           -- Fraser Valley Regional District
 (N'PittMeadows',   N'pittmeadows'),    -- City of Pitt Meadows
 (N'Courtenay',     N'courtenay'),      -- City of Courtenay
 (N'NorthCowichan', N'northcowichan'),  -- District of North Cowichan
 (N'Vernon',        N'vernon');         -- City of Vernon

-- Live Tenders (SourceType 10)
INSERT INTO opportunities.OpportunitySources
  (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
SELECT NEWID(), N'BidsTenders_'+o.Nm, 10,
       N'https://'+o.Slug+N'.bidsandtenders.ca/Module/Tenders/en', 1, 21600, 60,
       sysdatetimeoffset(), sysdatetimeoffset(), 0
FROM @orgs o
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources s WHERE s.Name = N'BidsTenders_'+o.Nm);

-- Awards (SourceType 12)
INSERT INTO opportunities.OpportunitySources
  (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
SELECT NEWID(), N'BidsTendersAwards_'+o.Nm, 12,
       N'https://'+o.Slug+N'.bidsandtenders.ca/Module/Tenders/en', 1, 21600, 60,
       sysdatetimeoffset(), sysdatetimeoffset(), 0
FROM @orgs o
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources s WHERE s.Name = N'BidsTendersAwards_'+o.Nm);

DECLARE @added int = (SELECT COUNT(*) FROM opportunities.OpportunitySources WHERE SourceType IN (10,12) AND Name LIKE 'BidsTenders%' AND CONVERT(date,CreatedAtUtc)=CONVERT(date,sysdatetimeoffset()));
PRINT 'Migration 231: added bids&tenders sources (Tenders + Awards) for CRD + 17 BC orgs. New rows today: ' + CAST(@added AS varchar(10));
GO
