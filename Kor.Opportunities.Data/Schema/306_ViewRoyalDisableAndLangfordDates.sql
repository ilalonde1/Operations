-- Migration 306 (2026-09-04): disable the View Royal source, and re-run Langford
-- now that string dates parse.
--
-- VIEW ROYAL WAS SEEDED ON A BAD VERIFICATION — mine. I probed
-- viewroyal.ca/webapps/ourcity/prospero/search.aspx, got HTTP 200, and concluded
-- the town ran Tempest. It does not: that url is a SOFT 404 — the page body says
-- "View Royal | Page Not Found. We recently updated our website and the page you
-- are looking for has probably moved." I checked the status code instead of the
-- artifact, which is the exact failure the repo rules warn about.
--
-- The scraper behaved correctly on it: 1 run, Success = 1, zero rows, and a
-- logged warning rather than an invented result. But an enabled source that can
-- never deliver is noise, and would eventually trip
-- source_never_delivered_anything, so it is disabled rather than left running.
--
-- View Royal's REAL tracker is a hand-maintained HTML table:
--   https://www.viewroyal.ca/EN/main/business/Land_Development/active-development-tracker.html
--   Two tables, seven data rows, columns District | Location | Description, e.g.
--   "7 Erskine Lane (Overlook) | 79 Condominiums" and
--   "181 Island Hwy (Grand & Fir) | 82 Condominium".
-- It is genuinely useful content but carries NO application number and NO date,
-- so keys would have to be text hashes that churn whenever the table is edited.
-- Left unwired deliberately, and recorded here so the next person does not
-- re-discover it: it is a CivicInfoHtml-shaped job, not a Prospero one.
--
-- LANGFORD dates: its tracker types Entered and Issued as esriFieldTypeString
-- holding "2024-03-07", so the epoch-milliseconds guard refused them — correctly,
-- since a wrong-but-plausible date is worse than none — and all 349 applications
-- landed with no date. The provider now attempts a text date parse alongside, so
-- a re-run fills them.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

UPDATE opportunities.OpportunitySources
SET IsEnabled = 0, UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'ViewRoyal_DevelopmentApplications';

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), s.Id, 'Pending', SYSDATETIMEOFFSET(), 'langford-dates'
FROM opportunities.OpportunitySources s
WHERE s.Name = N'Langford_DevelopmentApplications'
  AND NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                  WHERE t.OpportunitySourceId = s.Id AND t.Status IN ('Pending','InProgress'));
GO

PRINT 'Migration 306: View Royal disabled (soft 404); Langford re-queued for date backfill.';
GO
