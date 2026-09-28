-- Migration 327 (2026-09-28): merge the two records for ONE RFP — the West
-- Campus Recreation IPD structural-consultant posting in Leduc.
--
-- The same RFP is held twice because OpportunityKey is composed as
-- <8-char source prefix>-<external reference>:
--
--   APCALLBU-AB-2026-06377    Alberta Purchasing Connection
--   BIDSTEND-2026-RFP-IP-061  leduc.bidsandtenders.ca
--
-- ⚠ THIS IS NOT A BROKEN MATCHER. Ingestion dedups on a content hash and on
--   that key, and the source name is IN the key by design, so two portals
--   carrying one RFP can never collide. Cross-source matching does not exist at
--   ingest. OpportunityDuplicateScorer does exist but only guards MANUAL ENTRY
--   in the app (2026-07-07) — it is never called from IngestionService.
--
-- ⚠ AND THIS IS NOT ONE RECORD. Measured 2026-09-28: 245 titles are held under
--   more than one source prefix, across 564 rows of 8,047 active — roughly
--   7% of the pipeline, on EXACT title match alone. This migration fixes the
--   one Ian asked about; the class needs a reviewed merge pass in the shape of
--   tools/BdCanonicalDedup, not an ingest-path change made in passing.
--
-- WHICH SURVIVES. Neither row is better outright, so the merge takes the best
-- of each rather than picking a winner:
--   APC  (39332) — score 18.0, tier 2. Higher on both. Buyer recorded as
--                  "Government of Alberta (APC)", which is the PORTAL, not the
--                  buyer. No submission deadline captured at all.
--   Leduc(39322) — score 12.0, tier 1, but carries the real buyer (City of
--                  Leduc), the deadline (2026-10-15 14:00) and the tender
--                  detail URL.
-- Survivor is the APC row for its score and because it is the reference the
-- team is already circulating; the deadline, buyer and city are filled in from
-- the Leduc row, and the Leduc OBSERVATION is repointed so its URL is kept.
-- Nothing is deleted.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @keep bigint, @drop bigint, @now datetimeoffset = sysdatetimeoffset();
SELECT @keep = Id FROM opportunities.Opportunities WHERE OpportunityKey = N'APCALLBU-AB-2026-06377';
SELECT @drop = Id FROM opportunities.Opportunities WHERE OpportunityKey = N'BIDSTEND-2026-RFP-IP-061';

IF @keep IS NULL OR @drop IS NULL
    THROW 50327, 'One of the two West Campus IPD rows is missing — check the keys before running.', 1;

SELECT 'before' AS Section, o.OpportunityKey, o.BuyerName, o.SubmissionDeadlineUtc,
       o.RelevanceScore, o.RelevanceTier,
       (SELECT COUNT(*) FROM opportunities.OpportunityObservations ob WHERE ob.OpportunityId = o.Id) AS Obs
FROM opportunities.Opportunities o WHERE o.Id IN (@keep, @drop);

-- 1. Fill the survivor from the loser. COALESCE only: a value already on the
--    survivor is never overwritten, so a human edit cannot be clobbered.
UPDATE k
SET SubmissionDeadlineUtc = COALESCE(k.SubmissionDeadlineUtc, d.SubmissionDeadlineUtc),
    ProjectCity           = COALESCE(k.ProjectCity, d.ProjectCity, N'Leduc'),
    ProjectProvince       = COALESCE(k.ProjectProvince, d.ProjectProvince, N'AB'),
    EstimatedValue        = COALESCE(k.EstimatedValue, d.EstimatedValue),
    RfpReleaseDate        = COALESCE(k.RfpReleaseDate, d.RfpReleaseDate),
    -- The one deliberate OVERWRITE. "Government of Alberta (APC)" is the portal
    -- the posting was found on; the buyer is the City of Leduc, and a wrong
    -- buyer is the field somebody picks up the phone to.
    BuyerName             = N'City of Leduc',
    UpdatedAtUtc          = @now,
    UpdatedBy             = N'migration-327'
FROM opportunities.Opportunities k
CROSS JOIN opportunities.Opportunities d
WHERE k.Id = @keep AND d.Id = @drop;

-- 2. Move the loser's observations across, so the Leduc tender URL and its
--    raw payload stay reachable from the surviving record.
UPDATE opportunities.OpportunityObservations
SET OpportunityId = @keep
WHERE OpportunityId = @drop;

-- 3. Move anything else that points at the loser.
UPDATE opportunities.OpportunityDocuments SET OpportunityId = @keep WHERE OpportunityId = @drop;
UPDATE opportunities.OpportunityNotes     SET OpportunityId = @keep WHERE OpportunityId = @drop;
UPDATE opportunities.OpportunityFiles     SET OpportunityId = @keep WHERE OpportunityId = @drop;
UPDATE opportunities.OpportunityInterestedFirms SET OpportunityId = @keep WHERE OpportunityId = @drop;

-- 4. Retire the loser. Dismissed, not deleted — the app renders DismissedAtUtc
--    rows as "Removed" and keeps them out of the working view.
UPDATE opportunities.Opportunities
SET DismissedAtUtc  = @now,
    DismissedBy     = N'migration-327',
    DismissedReason = N'Same RFP as APCALLBU-AB-2026-06377 (West Campus Recreation IPD, structural consultant). Held twice because OpportunityKey carries the source prefix. Observations, deadline and buyer merged onto that record.',
    OwnerStaffId    = NULL,
    UpdatedAtUtc    = @now,
    UpdatedBy       = N'migration-327'
WHERE Id = @drop;

SELECT 'after' AS Section, o.OpportunityKey, o.BuyerName, o.SubmissionDeadlineUtc,
       o.RelevanceScore, o.RelevanceTier,
       (SELECT COUNT(*) FROM opportunities.OpportunityObservations ob WHERE ob.OpportunityId = o.Id) AS Obs,
       CASE WHEN o.DismissedAtUtc IS NULL THEN 'live' ELSE 'removed' END AS State
FROM opportunities.Opportunities o WHERE o.Id IN (@keep, @drop);

SELECT 'both source urls now reachable from the survivor' AS Section;
SELECT s.Name AS Source, ob.Url
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE ob.OpportunityId = @keep;
GO
