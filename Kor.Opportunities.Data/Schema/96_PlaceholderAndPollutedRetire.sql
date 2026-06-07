SET XACT_ABORT ON;
GO

-- Soft-retire placeholders + bid-status-polluted canonical rows.
-- Audit found 173 canonicals with bid-status-descriptor pollution
-- (DisplayName contains "Bids are under review", "Proposal Accepted",
-- "Submissions are under review", etc.) — these are import artifacts
-- where a bid-status column got concatenated into the org name during
-- legacy ingestion. They cannot be researched as entities.
--
-- Also retiring two big placeholders:
--   * [VENDOR-UNNAMED] (1,854 wins) — pure placeholder
--   * Pending - Bid Opened (298 wins) — bid-status placeholder

BEGIN TRAN;

DECLARE @retiredCount int;

-- ========== Pure placeholder canonicals ==========
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra audit: pure placeholder canonical (not a real entity)'
WHERE RetiredAtUtc IS NULL
  AND Id IN (17623, 13303);  -- [VENDOR-UNNAMED], Pending - Bid Opened
SET @retiredCount = @@ROWCOUNT;
PRINT 'Pure placeholders retired: ' + CONVERT(varchar(20), @retiredCount);

-- ========== Bid-status-descriptor pollution ==========
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra audit: bid-status pollution (DisplayName has concatenated bid status, not entity name)'
WHERE RetiredAtUtc IS NULL
  AND (
       DisplayName LIKE N'% Bids are under review%'
    OR DisplayName LIKE N'% Submissions are under review%'
    OR DisplayName LIKE N'% Proposal Accepted%'
    OR DisplayName LIKE N'% Proposal is under evaluation%'
    OR DisplayName LIKE N'% Proposals under review%'
    OR DisplayName LIKE N'% Awarded the opportunity%'
    OR DisplayName LIKE N'% Awarded contract %'
    OR DisplayName LIKE N'% Shortlisted%'
    OR DisplayName LIKE N'% Successfull%'
    OR DisplayName LIKE N'% Two-envelope%'
    OR DisplayName LIKE N'% Bids ranged from%'
    OR DisplayName LIKE N'% Bids have been reviewed%'
    OR DisplayName LIKE N'%highest evaluated%'
    OR DisplayName LIKE N'% Received proposal%'
    OR DisplayName LIKE N'% Total potential spend%'
    OR DisplayName LIKE N'%Envelope Evaluation%'
    OR DisplayName LIKE N'% Price N/A%'
    OR DisplayName LIKE N'% Successful Bidder%'
    OR DisplayName LIKE N'% Bid Amount %'
    OR DisplayName LIKE N'% Tender evaluated%'
    OR DisplayName LIKE N'% Evaluated Tender%'
    OR DisplayName LIKE N'% Sucessful Bidder%'  -- typo variant
    OR DisplayName LIKE N'% Successfully Shortlisted%'
    OR DisplayName LIKE N'% Successful - Shortlisted%'
    OR DisplayName LIKE N'% under review%'
    OR DisplayName LIKE N'% Awarded on %');
SET @retiredCount = @@ROWCOUNT;
PRINT 'Bid-status-polluted canonicals retired: ' + CONVERT(varchar(20), @retiredCount);

-- Summary
SELECT 'Total retired CanonicalOrg rows after this pass' AS Stat, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NOT NULL;

PRINT 'Migration 96 placeholder + pollution retire complete.';

COMMIT TRAN;
GO
