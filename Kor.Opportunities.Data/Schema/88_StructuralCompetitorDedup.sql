SET XACT_ABORT ON;
GO

-- KOR's direct structural-engineering competitor dedup. Same direct
-- FK-repoint pattern as 75/79/80/81/82/87. Conservative — keeping JV
-- partnership rows separate (Fast + Epp / WSP Canada, RJC / MKA,
-- Glotman Simpson confirmed-on-X-towers, etc.) because those represent
-- real project-specific partnerships. Also keeping McElhanney's
-- land-surveying division separate from McElhanney structural.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- RJC Engineers -> 69014 (25 mpi, 51 wins)
    (71534, 69014),  -- "Read Jones Christoffersen Ltd." (11 wins)
    (72116, 69014),  -- "Read Jones Christoffersen — NOTE: Same legal entity as RJC Engineers"
    (71386, 69014),  -- "RJC Engineers (Read Jones Christoffersen)"
    (72289, 69014),  -- "RJC Engineers (Vancouver)"

    -- Glotman Simpson Consulting Engineers -> 38926
    (71519, 38926),  -- "Glotman Simpson" (bare)
    (73321, 38926),  -- "GLOTMAN, SIMPSON CONSULTING" (Vendor, all-caps)

    -- Fast + Epp -> 7345
    (72234, 7345),   -- "Fast + Epp (confirmed)"
    (72197, 7345),   -- "Fast + Epp Structural Engineers"

    -- Entuitive Corporation -> 7017
    (72290, 7017),   -- "Entuitive (Vancouver Office)"

    -- Krahn Engineering Ltd. -> 38928
    (72297, 38928),  -- "Krahn Engineering (Vancouver)"
    (71429, 38928),  -- "Krahn Group of Companies (incl. Krahn Engineering Ltd.)"
    (74134, 38928),  -- "Krahn Group of Companies (Krahn Engineering Ltd.)"

    -- Morrison Hershfield -> 69543 (151 wins)
    (72300, 69543),  -- "Morrison Hershfield (now Stantec) – Vancouver Structural"
    (12019, 69543),  -- "Morrison Hershfield Limited Shortlisted PB4..." (Vendor)
    (12021, 69543),  -- "Morrison Hershfield Limited/Morrison Hershfield Limitee" (Vendor)
    (12022, 69543),  -- "Morrison Hershfield Limted" (typo Vendor)
    (12024, 69543),  -- "Morrison Hershfield Ltd. Bid have not been reviewed..." (Vendor)
    (12025, 69543),  -- "Morrison Hershfield Ltd. Submissions are under review" (Vendor)

    -- StructureCraft -> 69326
    (73787, 69326),  -- "StructureCraft Builders"

    -- McElhanney (structural) -> 69529 (2 mpi, 214 wins)
    (72788, 69529),  -- "MCELHANNEY CONSULTING SERVICES LTD." (Vendor, 155 wins)
    (73442, 69529),  -- "McElhanney Ltl" (typo)
    (71810, 69529),  -- "McElhanney Ltd. - Edmonton"

    -- Hatch Ltd. -> 8742 (28 wins)
    (62089, 8742),   -- "HATCH ASSOCIATES LTD."
    (8743, 8742),    -- "Hatch Ltd (EPCM)"
    (66438, 8742),   -- "HATCH LTD./HATCH LTÉE" (bilingual variant)
    (64503, 8742);   -- "HATCH LTD/HATCH LTEE" (bilingual variant)

-- ========== Aliases ==========
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup88', sysdatetimeoffset()
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

PRINT 'Migration 88 structural-competitor dedup complete.';

COMMIT TRAN;
GO
