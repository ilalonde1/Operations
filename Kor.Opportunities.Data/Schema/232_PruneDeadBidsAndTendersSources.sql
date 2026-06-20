USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 232: prune the dead bids&tenders sources added in 231.
  Empirical validation (Playwright provider run + raw probe) showed 16 of the 18
  orgs' bids&tenders subdomains are DECOMMISSIONED - the whole site (even root)
  redirects to a bids&tenders Error page. Those orgs (CRD, New West, Delta, etc.)
  now post on BC Bid + their own portals, which we already ingest via the BcBid
  and CivicInfoBC sources - so no coverage is lost. Keep only the 2 live portals:
  Port Moody (pulled 1 insert + 6 skipped) and Vernon (live page, 0 open today).
  Deletes the validation IngestionTriggers first (FK), then the dead source rows.
*/
DECLARE @dead TABLE (Nm nvarchar(60));
INSERT INTO @dead VALUES
 (N'CRD'),(N'NewWest'),(N'Delta'),(N'CNV'),(N'WestVancouver'),(N'LangleyCity'),
 (N'WhiteRock'),(N'Chilliwack'),(N'Langford'),(N'Esquimalt'),(N'Whistler'),
 (N'SCRD'),(N'FVRD'),(N'PittMeadows'),(N'Courtenay'),(N'NorthCowichan');

DECLARE @ids TABLE (Id uniqueidentifier);
INSERT INTO @ids
SELECT s.Id FROM opportunities.OpportunitySources s
JOIN @dead d ON s.Name = N'BidsTenders_'+d.Nm OR s.Name = N'BidsTendersAwards_'+d.Nm;

DELETE FROM opportunities.IngestionTriggers WHERE OpportunitySourceId IN (SELECT Id FROM @ids);
DELETE FROM opportunities.IngestionRuns WHERE Id NOT IN (SELECT IngestionRunId FROM opportunities.IngestionTriggers WHERE IngestionRunId IS NOT NULL) AND Id IN (
  -- only orphaned validation runs for dead sources; match by ProviderName prefix to be safe
  SELECT r.Id FROM opportunities.IngestionRuns r JOIN @dead d ON r.ProviderName LIKE N'BidsTenders_'+d.Nm+N' %');
DELETE FROM opportunities.OpportunitySources WHERE Id IN (SELECT Id FROM @ids);

DECLARE @removed int = (SELECT COUNT(*) FROM @ids);
PRINT 'Migration 232: pruned dead bids&tenders sources. Rows removed: ' + CAST(@removed AS varchar(10)) + ' (kept Port Moody + Vernon).';
GO
