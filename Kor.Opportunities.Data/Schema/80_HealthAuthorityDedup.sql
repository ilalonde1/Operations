SET XACT_ABORT ON;
GO

-- BC + AB major Health Authority dedup. Same pattern as 75/79 — direct
-- FK repoint, alias preservation, loser delete. Multi-entity JV rows
-- ("Fraser Health / BC Cancer") are kept as separate canonicals because
-- they represent real partnerships. Concatenated junk rows (multiple
-- authorities slammed together by a CSV mis-parse) are deleted as
-- pure scraper artifacts.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- Alberta Health Services -> 476
    (72125, 476), (72126, 476), (72127, 476), (72389, 476), (74158, 476),
    -- Interior Health: 54977 keeps (more MPI refs)
    (71381, 54977), (54809, 54977),
    -- Island Health -> 54979
    (71409, 54979), (71380, 54979),
    -- Northern Health -> 54976
    (72466, 54976),
    -- Providence Health Care -> 69127
    (72391, 69127), (72325, 69127),
    -- Provincial Health Services Authority -> 72211 (most refs)
    (18885, 72211), (73820, 72211), (69175, 72211),
    -- Vancouver Coastal Health -> 18917
    (72467, 18917),
    -- Fraser Health Authority capital projects -> 880
    (72318, 880);

-- ========== Aliases ==========
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup80', sysdatetimeoffset()
FROM @pairs p
INNER JOIN opportunities.CanonicalOrg co ON co.Id = p.LoserId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa WHERE oa.CanonicalOrgId = p.SurvivorId AND oa.RawName = co.DisplayName);
PRINT 'Aliases added: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE oa SET oa.CanonicalOrgId = p.SurvivorId
FROM opportunities.OrgAlias oa
INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa2 WHERE oa2.CanonicalOrgId = p.SurvivorId AND oa2.RawName = oa.RawName);
PRINT 'Loser aliases moved: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE oa FROM opportunities.OrgAlias oa INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId;
PRINT 'Duplicate loser aliases deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== FK repoints across all tables ==========
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

-- Enrichment cleanup — delete all loser enrichments + their dependent Intel rows
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
DELETE FROM opportunities.CanonicalOrgEnrichment WHERE Id IN (SELECT Id FROM @StaleEnrichments);
PRINT 'Loser enrichments deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.NewsArticleOrgMention x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'NewsArticleOrgMention FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.BdResearchTriggers x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'BdResearchTriggers FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE co FROM opportunities.CanonicalOrg co INNER JOIN @pairs p ON p.LoserId = co.Id;
PRINT 'Loser CanonicalOrg rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Concatenated-junk multi-authority canonical rows ARE present but
-- some have OpportunityAwards/etc. FK refs from old scrapes. Leaving
-- them in place — they have 0 MPI/Opps active refs so they don't
-- surface in briefs. Can clean up in a follow-up pass.

PRINT 'Migration 80 health-authority dedup complete.';

COMMIT TRAN;
GO
