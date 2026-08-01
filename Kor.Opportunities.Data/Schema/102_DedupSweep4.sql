-- *** WARNING — DO NOT COPY THIS FILE AS A TEMPLATE (BD-Audit-2026-06-09 C2/Mi4) ***
-- This applied migration contains two defect patterns, preserved here only as
-- history of what ran:
--   1. The IntelProject* DELETE block keyed on @StaleEnrichments uses
--      CanonicalOrgEnrichment ids against columns whose FK is
--      MajorProjectEnrichment(Id) — TWO SEPARATE IDENTITY SPACES. Numeric
--      collisions silently deleted unrelated project intel. The deletes were
--      also unnecessary for FK integrity. (Recovered 2026-06-09 by re-
--      extraction; tools/BdHoningIntelBackfill.)
--   2. The repoint-then-delete sequence destroys loser intel the repoint just
--      moved. Migration-path merges must REPOINT and keep (see m115-m119).
-- The ArchitectDisplacementBriefs handling also violates its unique index on
-- multi-loser clusters. Use the m115/m116/m119 templates for any new merge.
SET XACT_ABORT ON;
GO

-- Dedup sweep 4 — variant clusters surfaced by stem-2 grouping.
-- Combines retire (research artifacts) + merge (real-firm variants).

BEGIN TRAN;

-- ===========================================================
-- PART 1: Retire "Not publicly..." / "Not yet..." research artifacts
-- (these are placeholders Sonnet writes when a team member is unknown;
-- MPI FKs pointing to them should be NULL'd, then the canonical retired)
-- ===========================================================

DECLARE @ArtifactIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @ArtifactIds VALUES
    (72181), (72191), (72179), (72177), (72416), (72392),
    (72192), (72199), (72398), (72395), (72397),
    (72215), (72240), (72239), (72236), (72252),
    (72249), (72221), (72217), (72219);

-- NULL the MPI FKs first (so retiring doesn't leave dangling refs)
UPDATE m SET m.ProponentCanonicalOrgId = NULL FROM opportunities.MajorProjectsInventory m
INNER JOIN @ArtifactIds a ON a.Id = m.ProponentCanonicalOrgId;
PRINT 'MPI Proponent FKs cleared (artifact): ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.ArchitectCanonicalOrgId = NULL FROM opportunities.MajorProjectsInventory m
INNER JOIN @ArtifactIds a ON a.Id = m.ArchitectCanonicalOrgId;
PRINT 'MPI Architect FKs cleared (artifact): ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.GeneralContractorCanonicalOrgId = NULL FROM opportunities.MajorProjectsInventory m
INNER JOIN @ArtifactIds a ON a.Id = m.GeneralContractorCanonicalOrgId;
PRINT 'MPI GC FKs cleared (artifact): ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.StructuralEngineerCanonicalOrgId = NULL FROM opportunities.MajorProjectsInventory m
INNER JOIN @ArtifactIds a ON a.Id = m.StructuralEngineerCanonicalOrgId;
PRINT 'MPI StructEng FKs cleared (artifact): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m102: research artifact ("Not publicly confirmed"/"Not yet selected" placeholder)'
WHERE Id IN (SELECT Id FROM @ArtifactIds);
PRINT 'Not-publicly/Not-yet artifacts retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ===========================================================
-- PART 2: Retire concatenated-junk multi-authority rows
-- (these are pre-cleanup scraper junk where multiple authority
-- names got concatenated; mpi=0 for all)
-- ===========================================================

DECLARE @ConcatJunkIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @ConcatJunkIds VALUES
    (794), (377), (73830), (73978), (623), (55132),  -- Fraser Health concat junk
    (73825), (695);                                   -- Interior Health concat junk

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m102: scraper-concatenated multi-authority name junk (mpi=0)'
WHERE Id IN (SELECT Id FROM @ConcatJunkIds);
PRINT 'Concatenated-junk multi-authority rows retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ===========================================================
-- PART 3: Variant-cluster MERGES (direct FK-repoint pattern)
-- ===========================================================

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- ISL Engineering and Land Services Ltd. -> 59847 (217 wins)
    (9754, 59847), (9755, 59847), (9757, 59847), (9759, 59847),
    (9760, 59847), (59837, 59847), (9770, 59847), (9773, 59847),
    (9774, 59847), (9775, 59847), (9778, 59847),
    -- MPE Engineering Ltd -> 12078 (169 wins)
    (12079, 12078), (12080, 12078), (12081, 12078), (12082, 12078),
    (12083, 12078), (12084, 12078),
    -- Olson Construction -> 12799 (23 wins)
    (47050, 12799), (46679, 12799), (48147, 12799), (47258, 12799),
    (48236, 12799), (47162, 12799),
    -- Tollestrup Construction (2005) Inc -> 17082 (54 wins, keep 17084 = JV)
    (17083, 17082), (17086, 17082), (17087, 17082), (17088, 17082), (17089, 17082),
    -- Dexter Construction Company Limited -> 43653 (197 wins)
    (47333, 43653), (47326, 43653), (48357, 43653), (47278, 43653), (48359, 43653),
    -- Interior Health -> 54977 (30 mpi)
    (75027, 54977);

INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup102', sysdatetimeoffset()
FROM @pairs p INNER JOIN opportunities.CanonicalOrg co ON co.Id = p.LoserId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa WHERE oa.CanonicalOrgId = p.SurvivorId AND oa.RawName = co.DisplayName);
PRINT 'Aliases added: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE oa SET oa.CanonicalOrgId = p.SurvivorId
FROM opportunities.OrgAlias oa INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa2 WHERE oa2.CanonicalOrgId = p.SurvivorId AND oa2.RawName = oa.RawName);
PRINT 'Loser aliases moved: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE oa FROM opportunities.OrgAlias oa INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId;
PRINT 'Duplicate loser aliases deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.ProponentCanonicalOrgId = p.SurvivorId FROM opportunities.MajorProjectsInventory m INNER JOIN @pairs p ON p.LoserId = m.ProponentCanonicalOrgId;
PRINT 'MPI Proponent FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.ArchitectCanonicalOrgId = p.SurvivorId FROM opportunities.MajorProjectsInventory m INNER JOIN @pairs p ON p.LoserId = m.ArchitectCanonicalOrgId;
PRINT 'MPI Architect FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.StructuralEngineerCanonicalOrgId = p.SurvivorId FROM opportunities.MajorProjectsInventory m INNER JOIN @pairs p ON p.LoserId = m.StructuralEngineerCanonicalOrgId;
PRINT 'MPI StructEng FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE m SET m.GeneralContractorCanonicalOrgId = p.SurvivorId FROM opportunities.MajorProjectsInventory m INNER JOIN @pairs p ON p.LoserId = m.GeneralContractorCanonicalOrgId;
PRINT 'MPI GC FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE o SET o.BuyerCanonicalOrgId = p.SurvivorId FROM opportunities.Opportunities o INNER JOIN @pairs p ON p.LoserId = o.BuyerCanonicalOrgId;
PRINT 'Opps Buyer FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.BidderCanonicalOrgId = p.SurvivorId FROM opportunities.OpportunityBids x INNER JOIN @pairs p ON p.LoserId = x.BidderCanonicalOrgId;
PRINT 'OpportunityBids FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.ResolvedCanonicalOrgId = p.SurvivorId FROM opportunities.OpportunityInterestedFirms x INNER JOIN @pairs p ON p.LoserId = x.ResolvedCanonicalOrgId;
PRINT 'OppInterested FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.AwardingCanonicalOrgId = p.SurvivorId FROM opportunities.OpportunityAwards x INNER JOIN @pairs p ON p.LoserId = x.AwardingCanonicalOrgId;
PRINT 'OpportunityAwards Awarding FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.AwardedToCanonicalOrgId = p.SurvivorId FROM opportunities.OpportunityAwards x INNER JOIN @pairs p ON p.LoserId = x.AwardedToCanonicalOrgId;
PRINT 'OpportunityAwards AwardedTo FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.BuyerCanonicalOrgId = p.SurvivorId FROM opportunities.CrmEngagements x INNER JOIN @pairs p ON p.LoserId = x.BuyerCanonicalOrgId;
PRINT 'CrmEngagements FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.BuyerCanonicalOrgId = p.SurvivorId FROM opportunities.KorPursuits x INNER JOIN @pairs p ON p.LoserId = x.BuyerCanonicalOrgId;
PRINT 'KorPursuits Buyer FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.LostToCanonicalOrgId = p.SurvivorId FROM opportunities.KorPursuits x INNER JOIN @pairs p ON p.LoserId = x.LostToCanonicalOrgId;
PRINT 'KorPursuits LostTo FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.OwnerCanonicalOrgId = p.SurvivorId FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.OwnerCanonicalOrgId;
PRINT 'BuildingPermit Owner FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.ApplicantCanonicalOrgId = p.SurvivorId FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.ApplicantCanonicalOrgId;
PRINT 'BuildingPermit Applicant FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.ContractorCanonicalOrgId = p.SurvivorId FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.ContractorCanonicalOrgId;
PRINT 'BuildingPermit Contractor FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE adb FROM opportunities.ArchitectDisplacementBriefs adb
INNER JOIN @pairs p ON p.LoserId = adb.ArchitectCanonicalOrgId
WHERE EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs adb2 WHERE adb2.ArchitectCanonicalOrgId = p.SurvivorId);
UPDATE x SET x.ArchitectCanonicalOrgId = p.SurvivorId FROM opportunities.ArchitectDisplacementBriefs x INNER JOIN @pairs p ON p.LoserId = x.ArchitectCanonicalOrgId;
PRINT 'ArchitectDisplacementBriefs FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelSignal x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelAction x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelRisk x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelNarrative x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelNarrative FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelWork x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelWork FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelPersonAffiliation x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelPersonAffiliation FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.TargetCanonicalOrgId = p.SurvivorId FROM opportunities.IntelProjectAction x INNER JOIN @pairs p ON p.LoserId = x.TargetCanonicalOrgId;
PRINT 'IntelProjectAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.IntelProjectKeyPerson x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DECLARE @StaleEnrichments TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @StaleEnrichments (Id) SELECT e.Id FROM opportunities.CanonicalOrgEnrichment e INNER JOIN @pairs p ON p.LoserId = e.CanonicalOrgId;

DELETE FROM opportunities.IntelSignal             WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelAction             WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelRisk               WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelNarrative          WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelWork               WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelPersonAffiliation  WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelPerson             WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelProject            WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelProjectAction      WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelProjectRisk        WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelProjectKeyPerson   WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.IntelProjectSignal      WHERE SourceEnrichmentId IN (SELECT Id FROM @StaleEnrichments);
DELETE FROM opportunities.CanonicalOrgEnrichment  WHERE Id IN (SELECT Id FROM @StaleEnrichments);
PRINT 'Loser enrichments deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.NewsArticleOrgMention x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'NewsArticleOrgMention FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.BdResearchTriggers x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'BdResearchTriggers FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE co FROM opportunities.CanonicalOrg co INNER JOIN @pairs p ON p.LoserId = co.Id;
PRINT 'Loser CanonicalOrg rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Rename Dexter survivor to proper case (was all caps)
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Dexter Construction Company Limited'
WHERE Id = 43653 AND DisplayName COLLATE Latin1_General_CS_AS = N'DEXTER CONSTRUCTION COMPANY LIMITED' COLLATE Latin1_General_CS_AS;
PRINT 'Dexter survivor renamed to proper case: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 102 dedup sweep 4 complete.';

COMMIT TRAN;
GO
