-- Migration 328 (2026-09-28): ACEC-BC's event calendar goes live on the new
-- iCal parser, and the four associations that cannot be read say why.
--
-- Seven of eight event sources sat on ParserKey 'unmapped' with IsActive = 0,
-- because each looked like its own scraping job and only 'icba-cards' was ever
-- written. Probed 2026-09-28: most run The Events Calendar on WordPress, which
-- serves RFC 5545 iCal at /events/?ical=1. One documented format replaces one
-- reader per association — the same argument as the ArcGIS and EngagementHQ
-- adapters.
--
--   ACEC-BC   https://www.acec-bc.ca/events/?ical=1   ✅ 3 VEVENTs, real feed
--   VICA      answers ?ical=1 with its HTML page       ✗ no machine feed
--   VRCA      answers ?ical=1 with its HTML page       ✗ its RSS is 699 bytes,
--                                                        zero items
--   UDI BC    answers ?ical=1 with its HTML page       ✗ no machine feed
--   SEABC     HTTP 403 to a browser user-agent         ✗ bot protection
--
-- ⚠ The four that cannot be read are LEFT DISABLED and their reason is written
--   to LastErrorMessage, so nobody probes them a third time. They need per-site
--   scraping, which the early-signal design says to do last and for good reason.
--
-- The ACEC-BC feed is worth having on its own. Its three live events include a
-- client-engagement evening with the Cities of Burnaby, Coquitlam and New
-- Westminster, moderated by WSP's director of business development — municipal
-- decision-makers in a room, which is exactly the rung this programme exists to
-- reach.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @now datetimeoffset = sysdatetimeoffset();

UPDATE opportunities.IndustryEventSource
SET CalendarUrl      = N'https://www.acec-bc.ca/events/?ical=1',
    SiteUrl          = N'https://www.acec-bc.ca/events/',
    ParserKey        = N'ical',
    IsActive         = 1,
    LastErrorMessage = NULL,
    UpdatedAtUtc     = @now
WHERE Name = N'ACEC-BC';

-- Record WHY each of the others is off, against the row itself.
UPDATE opportunities.IndustryEventSource
SET LastErrorMessage = N'No machine-readable feed: /events/?ical=1 returns the HTML page (checked 2026-09-28). Needs a per-site reader.',
    UpdatedAtUtc = @now
WHERE Name IN (N'VICA', N'UDI BC');

UPDATE opportunities.IndustryEventSource
SET LastErrorMessage = N'No machine-readable feed: ?ical=1 returns HTML and /events/feed/ is 699 bytes with zero items (checked 2026-09-28). Needs a per-site reader.',
    UpdatedAtUtc = @now
WHERE Name = N'VRCA';

UPDATE opportunities.IndustryEventSource
SET LastErrorMessage = N'HTTP 403 to a browser user-agent with full Accept headers (checked 2026-09-23 and 2026-09-28) — bot protection, not a dead link.',
    UpdatedAtUtc = @now
WHERE Name = N'SEABC';

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), s.Id, N'Pending', @now, N'migration-328'
FROM opportunities.OpportunitySources s
WHERE 1 = 0;  -- event sources are polled by their own job, not the ingest trigger queue

SELECT 'event sources after this migration' AS Section;
SELECT Name, ParserKey, IsActive, CalendarUrl, LEFT(ISNULL(LastErrorMessage,''), 58) AS Note
FROM opportunities.IndustryEventSource ORDER BY IsActive DESC, Name;
GO
