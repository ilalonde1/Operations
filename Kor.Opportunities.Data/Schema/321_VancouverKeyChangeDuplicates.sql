-- Migration 321 (2026-09-10): remove the 20 Vancouver duplicates that the
-- key-derivation change created, and say plainly how they got there.
--
-- Migration 320 enabled the source and queued a trigger. The Worker claimed it
-- at 11:35:09, four seconds BEFORE the new Kor.Opportunities.Data.dll landed,
-- so the first run keyed 88 applications on the EngagementHQ platform id. The
-- re-run on the new build keyed the 26 that carry a DP number on the DP number
-- instead — a different key for the same application, so 20 of them were
-- inserted a second time rather than matched.
--
-- ⚠ THE CLASS, because it will happen again: CHANGING HOW AN OpportunityKey IS
--    DERIVED RE-INSERTS EVERY RECORD WHOSE KEY MOVES. The key is the identity;
--    a provider edit that touches ExternalReference is a data migration, not a
--    code change. Deploy the DLL BEFORE enabling the source, and when a key
--    derivation changes on a source that already holds rows, plan the rekey.
--
-- The survivor is the DP-keyed row in every case: it carries the file number
-- everyone actually searches by, and it is the one the applicant landed on.
-- Dismissed, not deleted — the app renders DismissedAtUtc rows as "Removed"
-- and keeps them out of the working view, which is what archive-not-delete
-- means here.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @sid uniqueidentifier;
SELECT @sid = Id FROM opportunities.OpportunitySources
WHERE Name = N'Vancouver_DevelopmentApplications';

IF @sid IS NULL
    THROW 50321, 'Vancouver_DevelopmentApplications source is missing — run 320 first.', 1;

IF OBJECT_ID('tempdb..#van') IS NOT NULL DROP TABLE #van;
SELECT o.Id, o.OpportunityKey, o.Name,
       CASE WHEN o.OpportunityKey LIKE 'VANCOUVE-[A-Z][A-Z]-20%'
            THEN 1 ELSE 0 END AS IsFileNumberKey
INTO #van
FROM opportunities.Opportunities o
WHERE o.DismissedAtUtc IS NULL
  AND EXISTS (SELECT 1 FROM opportunities.OpportunityObservations ob
              WHERE ob.OpportunityId = o.Id AND ob.OpportunitySourceId = @sid);

-- A stale twin is a platform-id-keyed row whose exact Name also exists under a
-- file-number key. Matching on Name is safe here and only here: these rows were
-- written minutes apart by the same provider from the same feed field.
IF OBJECT_ID('tempdb..#stale') IS NOT NULL DROP TABLE #stale;
SELECT v.Id, v.OpportunityKey, v.Name
INTO #stale
FROM #van v
WHERE v.IsFileNumberKey = 0
  AND EXISTS (SELECT 1 FROM #van k WHERE k.IsFileNumberKey = 1 AND k.Name = v.Name);

SELECT 'stale twins found' AS Section, COUNT(*) AS Rows FROM #stale;

UPDATE o
SET DismissedAtUtc = sysdatetimeoffset(),
    DismissedBy    = N'migration-321',
    DismissedReason = N'Duplicate of the same application under its DP file number; created when the OpportunityKey derivation changed between two ingest runs on 2026-09-10.',
    OwnerStaffId   = NULL,
    UpdatedAtUtc   = sysdatetimeoffset(),
    UpdatedBy      = N'migration-321'
FROM opportunities.Opportunities o
JOIN #stale s ON s.Id = o.Id;

SELECT 'dismissed' AS Section, COUNT(*) AS Rows
FROM opportunities.Opportunities o
JOIN #stale s ON s.Id = o.Id
WHERE o.DismissedAtUtc IS NOT NULL;

SELECT 'live Vancouver applications remaining' AS Section, COUNT(*) AS Rows
FROM opportunities.Opportunities o
WHERE o.DismissedAtUtc IS NULL
  AND EXISTS (SELECT 1 FROM opportunities.OpportunityObservations ob
              WHERE ob.OpportunityId = o.Id AND ob.OpportunitySourceId = @sid);

SELECT 'of those, an applicant is named' AS Section, COUNT(*) AS Rows
FROM opportunities.Opportunities o
WHERE o.DismissedAtUtc IS NULL
  AND o.BuyerContactName IS NOT NULL AND LEN(o.BuyerContactName) > 0
  AND EXISTS (SELECT 1 FROM opportunities.OpportunityObservations ob
              WHERE ob.OpportunityId = o.Id AND ob.OpportunitySourceId = @sid);
GO
