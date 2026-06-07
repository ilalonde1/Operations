SET XACT_ABORT ON;
GO

-- Provincial, Crown, Federal, and University canonical dedup. Same
-- direct-FK-repoint pattern as 75/79/80/81. Multi-entity JV rows
-- (e.g., "BC Housing / Hoy Creek Housing Co-op") stay as separate
-- canonicals — real partnerships, not variants.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- BC Housing -> 38939
    (54952, 38939), (811, 38939),
    -- BC Hydro -> 43875
    (54953, 43875),
    -- BC Min of Education -> 54954
    (69453, 54954),
    -- BC Ministry of Transportation and Infrastructure -> 53679 (1 ref wins)
    (53669, 53679), (71364, 53679),
    -- Infrastructure BC -> 54978
    (54958, 54978),
    -- UBC main -> 459
    (54371, 459), (71415, 459), (54974, 459), (72315, 459),
    -- UBC Okanagan -> 69457
    (68993, 69457), (69116, 69457), (74025, 69457),
    -- SFU -> 771
    (54973, 771), (71414, 771), (72312, 771),
    -- University of Victoria -> 68861 (more refs)
    (18916, 68861),
    -- SAIT -> 70266
    (68810, 70266), (242, 70266),
    -- Alberta Infrastructure -> 53255
    (316, 53255),
    -- Province of Alberta -> 53323
    (71133, 53323);

INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup82', sysdatetimeoffset()
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

PRINT 'Migration 82 provincial/university dedup complete.';

COMMIT TRAN;
GO
