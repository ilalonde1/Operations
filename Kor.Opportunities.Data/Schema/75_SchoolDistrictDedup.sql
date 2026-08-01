SET XACT_ABORT ON;
GO

-- Direct CanonicalOrg dedup for known-safe pairs (BC school districts +
-- Westbank). BdCanonicalDedup's fuzzy-name similarity guard rejected
-- these because the loser/survivor DisplayNames differ meaningfully
-- (e.g., "School District 39" vs "School District 39 (Vancouver)" — the
-- parenthetical suffix breaks the guard). Each pair below has been
-- manually verified against MPI ref counts + Kind + locality before
-- being added.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL, Reason NVARCHAR(200));
INSERT INTO @pairs VALUES
    -- BC school district variants (23 pairs)
    (18897, 70002, N'BC SD5 Southeast Kootenay variants'),
    (70000, 54415, N'BC SD23 Central Okanagan variant'),
    (74023, 54415, N'BC SD23 Central Okanagan variant'),
    (71410, 54542, N'BC SD33 Chilliwack variant'),
    (73876, 54542, N'BC SD33 Chilliwack variant'),
    (74048, 68848, N'BC SD34 Abbotsford variant'),
    (74049, 68849, N'BC SD35 Langley variant'),
    (71411, 53678, N'BC SD36 Surrey variant'),
    (73823, 53678, N'BC SD36 Surrey variant'),
    (74045, 68851, N'BC SD38 Richmond variant'),
    (71412, 53840, N'BC SD39 Vancouver variant'),
    (74040, 53840, N'BC SD39 Vancouver variant'),
    (74043, 53687, N'BC SD40 New Westminster variant'),
    (74042, 68853, N'BC SD41 Burnaby variant'),
    (71413, 53966, N'BC SD43 Coquitlam variant'),
    (74041, 53966, N'BC SD43 Coquitlam variant'),
    (74046, 69571, N'BC SD45 West Vancouver variant'),
    (73871, 54197, N'BC SD60 Peace River North variant'),
    (74024, 69115, N'BC SD67 Okanagan Skaha variant'),
    (74008, 69991, N'BC SD71 Comox Valley variant'),
    (70029, 53773, N'BC SD93 Conseil Scolaire Francophone variant'),
    (70954, 53773, N'BC SD93 Conseil Scolaire Francophone variant'),
    (71997, 53773, N'BC SD93 Conseil Scolaire Francophone variant'),
    -- Westbank-family (3 pairs)
    (21489, 69644, N'Westbank Corp -> Westbank Projects Corp.'),
    (73784, 40313, N'Westbank First Nation (WFN) -> Westbank First Nation'),
    (55028, 40313, N'Westbank First Nation (lease) -> Westbank First Nation');

-- ========== Step 1: preserve loser's DisplayName as alias on survivor ==========

INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup', sysdatetimeoffset()
FROM @pairs p
INNER JOIN opportunities.CanonicalOrg co ON co.Id = p.LoserId
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.OrgAlias oa
    WHERE oa.CanonicalOrgId = p.SurvivorId AND oa.RawName = co.DisplayName
);
PRINT 'Aliases added to survivors from losers: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Move loser's existing aliases over to survivor.
UPDATE oa SET oa.CanonicalOrgId = p.SurvivorId
FROM opportunities.OrgAlias oa
INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.OrgAlias oa2
    WHERE oa2.CanonicalOrgId = p.SurvivorId AND oa2.RawName = oa.RawName
);
PRINT 'Loser aliases moved to survivor: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Delete remaining loser aliases (those that would have been duplicates).
DELETE oa
FROM opportunities.OrgAlias oa
INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId;
PRINT 'Duplicate loser aliases deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== Step 2: repoint all FKs from loser to survivor ==========

-- MPI (4 role columns)
UPDATE m SET m.ProponentCanonicalOrgId = p.SurvivorId
FROM opportunities.MajorProjectsInventory m
INNER JOIN @pairs p ON p.LoserId = m.ProponentCanonicalOrgId;
PRINT 'MPI Proponent FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.ArchitectCanonicalOrgId = p.SurvivorId
FROM opportunities.MajorProjectsInventory m
INNER JOIN @pairs p ON p.LoserId = m.ArchitectCanonicalOrgId;
PRINT 'MPI Architect FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.StructuralEngineerCanonicalOrgId = p.SurvivorId
FROM opportunities.MajorProjectsInventory m
INNER JOIN @pairs p ON p.LoserId = m.StructuralEngineerCanonicalOrgId;
PRINT 'MPI StructEng FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.GeneralContractorCanonicalOrgId = p.SurvivorId
FROM opportunities.MajorProjectsInventory m
INNER JOIN @pairs p ON p.LoserId = m.GeneralContractorCanonicalOrgId;
PRINT 'MPI GC FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Opportunities (Buyer)
UPDATE o SET o.BuyerCanonicalOrgId = p.SurvivorId
FROM opportunities.Opportunities o
INNER JOIN @pairs p ON p.LoserId = o.BuyerCanonicalOrgId;
PRINT 'Opps Buyer FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- OpportunityBids, OpportunityInterestedFirms, OpportunityAwards
UPDATE x SET x.BidderCanonicalOrgId = p.SurvivorId
FROM opportunities.OpportunityBids x INNER JOIN @pairs p ON p.LoserId = x.BidderCanonicalOrgId;
PRINT 'OpportunityBids FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.ResolvedCanonicalOrgId = p.SurvivorId
FROM opportunities.OpportunityInterestedFirms x INNER JOIN @pairs p ON p.LoserId = x.ResolvedCanonicalOrgId;
PRINT 'OpportunityInterestedFirms FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.AwardingCanonicalOrgId = p.SurvivorId
FROM opportunities.OpportunityAwards x INNER JOIN @pairs p ON p.LoserId = x.AwardingCanonicalOrgId;
PRINT 'OpportunityAwards Awarding FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.AwardedToCanonicalOrgId = p.SurvivorId
FROM opportunities.OpportunityAwards x INNER JOIN @pairs p ON p.LoserId = x.AwardedToCanonicalOrgId;
PRINT 'OpportunityAwards AwardedTo FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- CrmEngagements, KorPursuits
UPDATE x SET x.BuyerCanonicalOrgId = p.SurvivorId
FROM opportunities.CrmEngagements x INNER JOIN @pairs p ON p.LoserId = x.BuyerCanonicalOrgId;
PRINT 'CrmEngagements Buyer FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.BuyerCanonicalOrgId = p.SurvivorId
FROM opportunities.KorPursuits x INNER JOIN @pairs p ON p.LoserId = x.BuyerCanonicalOrgId;
PRINT 'KorPursuits Buyer FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.LostToCanonicalOrgId = p.SurvivorId
FROM opportunities.KorPursuits x INNER JOIN @pairs p ON p.LoserId = x.LostToCanonicalOrgId;
PRINT 'KorPursuits LostTo FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- BuildingPermit (3 columns)
UPDATE x SET x.OwnerCanonicalOrgId = p.SurvivorId
FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.OwnerCanonicalOrgId;
PRINT 'BuildingPermit Owner FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.ApplicantCanonicalOrgId = p.SurvivorId
FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.ApplicantCanonicalOrgId;
PRINT 'BuildingPermit Applicant FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE x SET x.ContractorCanonicalOrgId = p.SurvivorId
FROM opportunities.BuildingPermit x INNER JOIN @pairs p ON p.LoserId = x.ContractorCanonicalOrgId;
PRINT 'BuildingPermit Contractor FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ArchitectDisplacementBriefs
UPDATE x SET x.ArchitectCanonicalOrgId = p.SurvivorId
FROM opportunities.ArchitectDisplacementBriefs x INNER JOIN @pairs p ON p.LoserId = x.ArchitectCanonicalOrgId;
PRINT 'ArchitectDisplacementBriefs FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Intel* tables (Signal, Action, Risk, Narrative, Work, PersonAffiliation)
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

-- CanonicalOrgEnrichment — when survivor has any enrichment for the same
-- provider, drop loser's enrichment (org will re-enrich on next cron).
-- Otherwise, move loser's enrichment to survivor.
DECLARE @StaleEnrichments TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @StaleEnrichments (Id)
SELECT e.Id FROM opportunities.CanonicalOrgEnrichment e
INNER JOIN @pairs p ON p.LoserId = e.CanonicalOrgId
WHERE EXISTS (
    SELECT 1 FROM opportunities.CanonicalOrgEnrichment e2
    WHERE e2.CanonicalOrgId = p.SurvivorId AND e2.ProviderName = e.ProviderName
);

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
PRINT 'Stale loser enrichments deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE e SET e.CanonicalOrgId = p.SurvivorId
FROM opportunities.CanonicalOrgEnrichment e
INNER JOIN @pairs p ON p.LoserId = e.CanonicalOrgId;
PRINT 'Loser enrichments moved to survivor: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- News + Triggers
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.NewsArticleOrgMention x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'NewsArticleOrgMention FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);
UPDATE x SET x.CanonicalOrgId = p.SurvivorId FROM opportunities.BdResearchTriggers x INNER JOIN @pairs p ON p.LoserId = x.CanonicalOrgId;
PRINT 'BdResearchTriggers FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== Step 3: delete the loser CanonicalOrgs ==========

DELETE co
FROM opportunities.CanonicalOrg co
INNER JOIN @pairs p ON p.LoserId = co.Id;
PRINT 'Loser CanonicalOrg rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 75 direct-dedup complete.';

COMMIT TRAN;
GO
