SET XACT_ABORT ON;
GO

-- Top architect canonical dedup. Conservative — keeps every JV/joint-
-- venture partnership row separate (real project teaming structures),
-- keeps regional operating offices distinct (Perkins+Will LA / San
-- Francisco / Seattle stay separate from the Canadian entity),
-- keeps differently-named legal entities separate even when they
-- might be related.
--
-- This pass focuses on the obvious variants: typos, all-caps,
-- parenthetical scope descriptors, regional descriptors of the same
-- legal entity, and the Hughes Condon Marler -> HCMA pre-2013 rebrand.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    -- HCMA Architecture + Design -> 8799 (5 wins, 18 MPI refs)
    (70335, 8799),    -- "HCMA (feasibility and early design)" — scope descriptor
    (72151, 8799),    -- "HCMA (renewal plan)" — scope descriptor
    (70334, 8799),    -- "HCMA Architecture & Design (feasibility/business case only)"
    (70033, 8799),    -- "HCMA Architecture + Design (feasibility)"
    (54236, 8799),    -- "Hughes Condon Marler Architects" — pre-2013 rebrand

    -- Diamond Schmitt Architects -> 54753 (5 MPI refs)
    (71464, 54753),   -- "Diamond Schmitt (Vancouver studio)"

    -- DIALOG BC -> 69865 (1 win, 2 MPI refs)
    (32468, 69865),   -- "Dialog BC" (Vendor)
    (73320, 69865),   -- "DIALOG BC ARCHITECTURE ENGINEERING" (truncated all-caps)
    (72401, 69865),   -- "DIALOG BC Architecture Engineering and Interior Design Planning Inc."
    (73771, 69865),   -- "DIALOG (Vancouver Studio)"
    (70137, 69865),   -- "DIALOG Design" (bare descriptor)
    (72443, 69865),   -- "DIALOG (Partners: Robert Swart...)" research artifact
    (72076, 69865),   -- "DIALOG (prime consultant for research centre planning)"
    (69844, 69865),   -- "Dialog (Building D confirmed)"
    (69285, 69865),   -- "DIALOG (Hotson Bakker Boniface Haden Architects)" — HBBH merged into DIALOG 2013
    (72233, 69865),   -- "DIALOG (confirmed)"

    -- DIALOG Alberta -> 71514 (3 wins)
    (70130, 71514),   -- "Dialog Alberta Architecture Engineering Interior Design Planning Inc."
    (73772, 71514),   -- "DIALOG (Calgary / Edmonton offices)"
    (72107, 71514),   -- "DIALOG (Calgary office)"

    -- IBI Group Architects (Canada) Inc -> 9217 (17 wins, Architect)
    (9229, 9217),     -- "IBI Group" (Vendor, 9 wins)
    (67244, 9217),    -- "IBI GROUP (CANADA) INC." (all-caps Vendor)
    (9215, 9217),     -- "IBI Group (Consultants)" (Vendor)
    (9216, 9217),     -- "IBI Group (IBI)" (8 wins)
    (63453, 9217),    -- "IBI Group (Kingston)" (regional)
    (9218, 9217),     -- "IBI Group Architects Engineers"
    (9223, 9217),     -- "IBI Group Professional Services" (Vendor)
    (9224, 9217),     -- "IBI Group Professional Services (Canada) Inc" (Vendor, 21 wins)
    (9225, 9217),     -- "IBI Group Professional Services (Canada) Inc. (Consultants)"
    (9230, 9217),     -- "IBI Professional Services (Canada) Inc."

    -- GBL Architects -> 54190 (11 MPI refs)
    (72270, 54190),   -- "GBL Architects Inc. (Vancouver)"

    -- Perkins + Will Architects -> 69688 (1 win, 16 MPI refs) — CANADA operations only
    (29843, 69688),   -- "Perkins + Will Architect Canada Co"
    (66168, 69688),   -- "Perkins + Will Canada Inc" (Vendor)
    (38971, 69688),   -- "Perkins&Will (Vancouver studio)"
    (74138, 69688),   -- "Perkins&Will (Busby Perkins+Will Alberta Ltd.)" — Busby P+W rebrand

    -- Michael Green Architecture -> 69760 (3 MPI refs)
    (55041, 69760),   -- "MGA (MG Architecture)"
    (73780, 69760),   -- "MGA | Michael Green Architecture"
    (72273, 69760),   -- "MGA | Michael Green Architecture (Vancouver)"
    (73770, 69760),   -- "Michael Green Architecture (MGA)"

    -- NSDA Architects -> 4 (3 MPI refs)
    (72274, 4),       -- "NSDA Architects (Vancouver / North Shore)"

    -- Public Architecture + Communication -> 68915 (2 MPI refs)
    (68878, 68915),   -- "PUBLIC Architecture" (all caps)
    (72275, 68915),   -- "Public Architecture + Communication (PAC) — Vancouver"
    (70702, 68915),   -- "Public Architecture + Communication (WMW Public Architecture)"
    (71039, 68915),   -- "Public Architecture + Communication Inc. (PUBLIC Architecture)"

    -- office of mcfarlane biggar -> 38969 (3 MPI refs)
    (33152, 38969),   -- "Office of McFarlane Biggar" (Vendor)
    (73834, 38969),   -- "OFFICE OF MCFARLANE BIGGAR ARCHITECTS & DESIGNERS INC." (all caps)

    -- BattersbyHowat Architects -> 24317
    (3066, 24317),    -- "BattersbyHowat Architects Inc. Small Scope"

    -- ZGF Architects -> 38975 (3 MPI refs)
    (20320, 38975);   -- "ZGF Cotter Architects" — older name pre-Cotter departure

-- Reclassify DIALOG ONTARIO as Architect (it's currently Vendor)
UPDATE opportunities.CanonicalOrg SET Kind = N'Architect' WHERE Id = 72809 AND Kind = N'Vendor';
PRINT 'DIALOG ONTARIO reclassified Architect: ' + CONVERT(varchar(20), @@ROWCOUNT);

INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, CreatedAtUtc)
SELECT p.SurvivorId, co.DisplayName, N'R95DirectDedup97', sysdatetimeoffset()
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
-- ArchitectDisplacementBriefs has UX_ArchitectDisplacementBriefs_Architect unique index.
-- If survivor already has a brief, drop the loser's first to avoid collision.
DELETE adb FROM opportunities.ArchitectDisplacementBriefs adb
INNER JOIN @pairs p ON p.LoserId = adb.ArchitectCanonicalOrgId
WHERE EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs adb2 WHERE adb2.ArchitectCanonicalOrgId = p.SurvivorId);
PRINT 'ArchitectDisplacementBriefs loser collisions dropped: ' + CONVERT(varchar(20), @@ROWCOUNT);

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

PRINT 'Migration 97 top-architect dedup complete.';

COMMIT TRAN;
GO
