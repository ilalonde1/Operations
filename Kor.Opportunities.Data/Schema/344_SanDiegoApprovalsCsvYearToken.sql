-- 344: CA_SocrataSanDiego follows the calendar instead of carrying 2026 as a literal.
--
-- The City of San Diego publishes one approvals CSV per year
-- (approvals_created_<year>_datasd.csv; the all-years file is 700 MB, over the
-- provider's cap). Migration 197 wired the 2026 file by name, and nothing rewrote
-- the year, so on 1 January 2027 the source would have kept fetching a frozen 2026
-- file and reporting success -- the class the Worker's CLAUDE.md is about. Measured
-- 2026-10-13: 2025 and 2026 answer 200; 2027 answers 403 (not published yet).
--
-- CaSocrataMajorProjectsInventoryProvider.ResolveBaseUrl now expands the literal
-- token {year} to the current UTC year, and the CSV fetch falls back to the previous
-- year's file (with a warning) when the new year's file is not up yet in January.
--
-- ⛔ ORDER: deploy the Worker that carries ResolveBaseUrl BEFORE applying this. The
-- old binary would request a URL containing the literal text "{year}" and fail.
--
-- Idempotent: rewrites only the literal-2026 form. Undo: set BaseUrl back to the
-- value the 'current' report prints.
--
-- HOW TO RUN
--   report only:  sqlcmd ... -i 344_SanDiegoApprovalsCsvYearToken.sql -v Apply=0
--   apply:        sqlcmd ... -i 344_SanDiegoApprovalsCsvYearToken.sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;
DECLARE @old nvarchar(400) = N'https://seshat.datasd.org/development_permits/approvals_created_2026_datasd.csv';
DECLARE @new nvarchar(400) = N'https://seshat.datasd.org/development_permits/approvals_created_{year}_datasd.csv';

SELECT 'current' AS Report, Name, BaseUrl, IsEnabled
FROM opportunities.OpportunitySources
WHERE Name = N'CA_SocrataSanDiego';

SELECT 'to update' AS Report, COUNT(*) AS Rows_
FROM opportunities.OpportunitySources
WHERE Name = N'CA_SocrataSanDiego' AND BaseUrl = @old;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed.';
    RETURN;
END;

UPDATE opportunities.OpportunitySources
   SET BaseUrl      = @new,
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE Name = N'CA_SocrataSanDiego'
   AND BaseUrl = @old;

SELECT 'updated' AS Report, @@ROWCOUNT AS Rows_;

SELECT 'after' AS Report, Name, BaseUrl
FROM opportunities.OpportunitySources
WHERE Name = N'CA_SocrataSanDiego';
