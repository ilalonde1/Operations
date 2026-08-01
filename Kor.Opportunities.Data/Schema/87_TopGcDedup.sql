SET XACT_ABORT ON;
GO

-- Top GC canonical dedup — flagrant typo / parenthetical / ALL-CAPS
-- duplicates of major construction brands. Conservative: keeping
-- regional operating entities (PCL Constructors Eastern Inc vs
-- PCL Constructors Canada Inc) and JV partnerships separate. Only
-- merging variants that are clearly the same legal entity with a
-- name typo, parenthetical scope descriptor, or capitalization
-- difference.
--
-- Same direct-FK-repoint pattern as 75/79/80/81/82.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- PCL Construction (brand) -> 13243
    (13242, 13243),  -- "PCL Constrcution Management Inc." (typo)
    (72393, 13243),  -- "PCL Construction (Construction Manager)"
    (71391, 13243),  -- "PCL Constructors" (no qualifier)
    (69954, 13243),  -- "PCL Constructors (P3 consortium)"
    -- PCL Constructors Westcoast Inc. -> 20517 (BC operating entity)
    (37443, 20517),  -- "PCL Construction Westcoast INc." (typo)
    (30856, 20517),  -- "PCL Westcoast Constructors" (word-order variant)
    (72189, 20517),  -- "PCL Constructors Westcoast Inc. (contract terminated 2026)"
    (72417, 20517),  -- "PCL Constructors Westcoast Inc. (Phase 1 GC...)"
    (69854, 20517),  -- "PCL Constructors Westcoast Inc. (Phase 2 alliance partner)"

    -- EllisDon Corporation -> 22257
    (6850, 22257),   -- "ELLIS DON CONSTRUCTION SERVICES INC" (all caps, 108 wins)
    (71377, 22257),  -- "EllisDon Construction"
    (72184, 22257),  -- "EllisDon (Construction Management for Phase 3)"
    (69863, 22257),  -- "EllisDon (design-build, 480-room accommodations)"
    (6859, 22257),   -- "EllisDon Constuction Company" (typo)
    (6860, 22257),   -- "EllisDon Contruction Services Inc." (typo)
    (6861, 22257),   -- "EllisDon Industrial Services"
    (6862, 22257),   -- "EllisDon SENA Team"
    (72422, 22257),  -- "EllisDon Corporation (Design-Build contractor; Oxford Builders...)"
    (69952, 22257),  -- "EllisDon Corporation (design-build)"
    (69850, 22257),  -- "EllisDon Design Build Inc."

    -- Graham Construction -> 69232
    (70889, 69232),  -- "Graham Construction & Engineering (CM @ Risk)"
    (8349, 69232),   -- "Graham Construction & Engineering Inc"
    (52722, 69232),  -- "GRAHAM CONSTRUCTION & ENGINEERING L" (all caps, truncated)
    (8351, 69232),   -- "Graham Construction & Engineering, a JV"
    (72163, 69232),  -- "Graham Construction (Phase 1 Stage 5)"
    (69912, 69232),  -- "Graham Construction (Tandem Health Partners)"
    (49956, 69232),  -- "Graham Construction and" (truncated, 22 wins)
    (8353, 69232),   -- "Graham Construction and Engineering A JV" (17 wins)
    (8355, 69232),   -- "Graham Construction and Engineering Inc. A JV"
    (8356, 69232),   -- "Graham Construction and Engineering LP" (49 wins)
    (8357, 69232),   -- "Graham Construction and Engineering LP Succesfully" (typo+stub)
    (8359, 69232),   -- "Graham Construction Shortlisted Candidate..." (descriptive junk)

    -- Ledcor Construction -> 69671
    (73889, 69671),  -- "Ledcore (Ledcor Construction Limited)" (typo "Ledcore")
    (71868, 69671),  -- "Ledcor Construction Limited (Vancouver)"
    (10811, 69671),  -- "Ledcor Design-Build"
    (10812, 69671),  -- "Ledcor Design-Build (Alberta) Inc"
    (72198, 69671),  -- "Ledcor Construction (design-build)"

    -- Bird Construction Inc. -> 54928
    (71890, 54928),  -- "Bird Construction (Bird - Northern Alberta Buildings (NAB))"
    (71889, 54928),  -- "Bird Construction (Calgary)"
    (72424, 54928),  -- "Bird Construction (Construction Manager)"
    (71263, 54928),  -- "Bird Construction (IPD)"
    (50449, 54928),  -- "BIRD CONSTRUCTION GP LIMITED, BIRD" (all caps, 8 wins)
    (72409, 54928),  -- "Bird Construction Group (Construction Manager)"

    -- Pomerleau Inc. -> 13537
    (47309, 13537),  -- "Pomerleau Inc (Calgary)"
    (47967, 13537);  -- "Pomerleau Inc (Quebec)"

-- ========== Aliases ==========
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup87', sysdatetimeoffset()
FROM @pairs p
INNER JOIN opportunities.CanonicalOrg co ON co.Id = p.LoserId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa WHERE oa.CanonicalOrgId = p.SurvivorId AND oa.RawName = co.DisplayName);
PRINT 'Aliases added: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE oa SET oa.CanonicalOrgId = p.SurvivorId
FROM opportunities.OrgAlias oa INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias oa2 WHERE oa2.CanonicalOrgId = p.SurvivorId AND oa2.RawName = oa.RawName);
PRINT 'Loser aliases moved: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE oa FROM opportunities.OrgAlias oa INNER JOIN @pairs p ON p.LoserId = oa.CanonicalOrgId;
PRINT 'Duplicate loser aliases deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== FK repoints ==========
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

-- Enrichment cleanup
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

PRINT 'Migration 87 top-GC dedup complete.';

COMMIT TRAN;
GO
