SET XACT_ABORT ON;
GO

-- Hard-delete the 14 orphan placeholder CanonicalOrg rows that
-- migration 73 left dangling. They have zero MPI/Opps/Bid refs (the
-- FKs were nulled in migration 73), but the CanonicalOrg rows
-- themselves still exist with junk DisplayNames ("Unknown", "TBD (..)",
-- etc.) and a single placeholder CanonicalOrgEnrichment each.
--
-- Excluded from delete: 16535 "TBD Architecture & Urban Planning" and
-- 62949 "TBD Media Group Limited" — both real firms whose name happens
-- to start with TBD.

BEGIN TRAN;

DECLARE @junkIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @junkIds VALUES
    (63552), -- "SPARKES KENNETH Commissionaire NDHQ ..." (person mistakenly created as Buyer org)
    (72088), -- "Developer TBD (City incentive program)"
    (72136), -- "Unknown" (Competitor)
    (72143), -- "Unknown (design-build RFP issued 2026)"
    (72147), -- "Unknown (design contract awarded; $13M architect contract)"
    (72154), -- "Unknown (proponent selected 2024)"
    (72159), -- "Unknown — Architectural Services RFP closed April 9, 2026"
    (72195), -- "TBD" (GC)
    (72208), -- "TBD (Colliers Project Leaders as owner's rep)"
    (72411), -- "TBD (design-build architect not yet publicly identified)"
    (72412), -- "TBD (design-build contractor not yet publicly named)"
    (72426), -- "TBD (not publicly identified)"
    (72427), -- "TBD (not publicly named)"
    (72430); -- "TBD (Kaigo as DBOM operator...)"

-- ========== Delete dependent Intel rows referencing the enrichments ==========

DECLARE @enrichIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @enrichIds (Id)
SELECT Id FROM opportunities.CanonicalOrgEnrichment
WHERE CanonicalOrgId IN (SELECT Id FROM @junkIds);

DELETE FROM opportunities.IntelSignal             WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelAction             WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelRisk               WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelNarrative          WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelWork               WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelPersonAffiliation  WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelPerson             WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelProject            WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelProjectAction      WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelProjectRisk        WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelProjectKeyPerson   WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);
DELETE FROM opportunities.IntelProjectSignal      WHERE SourceEnrichmentId IN (SELECT Id FROM @enrichIds);

-- ========== Delete the enrichments + aliases + canonical orgs ==========

DELETE FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId IN (SELECT Id FROM @junkIds);
PRINT 'Placeholder enrichments deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId IN (SELECT Id FROM @junkIds);
PRINT 'Placeholder aliases deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE FROM opportunities.CanonicalOrg WHERE Id IN (SELECT Id FROM @junkIds);
PRINT 'Placeholder CanonicalOrgs deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 76 orphan placeholder purge complete.';

COMMIT TRAN;
GO
