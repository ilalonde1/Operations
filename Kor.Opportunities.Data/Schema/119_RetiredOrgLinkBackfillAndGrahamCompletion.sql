-- 119_RetiredOrgLinkBackfillAndGrahamCompletion.sql
-- BD-Audit-2026-06-09 M5 + C6 completion.
--
-- Part A (C6): finish m108's never-executed Steps 1-4 — consolidate Graham
-- dupes 72237 / 72457 into survivor 8361 with the FULL FK template
-- (m108's committed repoint list covered ~12 of 26 tables; this uses the
-- complete set, collision-ranked where unique keys demand it).
--
-- Part B (C6): dedupe Alex Trifunov — IntelPerson 7844 (manual mint) and
-- 7845 (FirmNarrative extractor) are the same person with duplicate
-- affiliations to org 8361. Keep 7845 (conventional SHA1 NaturalKey, so
-- future extracts update instead of re-minting); move 7844's research
-- Notes across; retire 7844 + its affiliation as dups.
--
-- Part C (M5): active rows still linking to retired orgs (2,656 awards —
-- 1,854 of them to "pure placeholder" canonicals — plus interested-firm,
-- bid, and MPI architect/SE/proponent links). Every m116-class duplicate
-- was already repointed to its survivor; what remains points at true
-- artifacts/placeholders, so the links are NULLed. Raw name strings are
-- retained on every row for future re-resolution by the resolver.
--
-- Part D (M5): org-side intel rows still ACTIVE on retired orgs retire
-- with their parent (children retire on lifecycle retire — m115 rule).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- ---------------------------------------------------------------------------
-- Part A0: m98-review research resolutions (web-verified 2026-06-09; see
-- tools/BdCanonicalDedup/output/m98-review-remaining.csv for evidence URLs).
--   * 24547 HNPA Architecture + Planning — real AIBC firm (HPNA is a typo):
--     un-retire; 18983 maps to it.
--   * 70763 MGA | Architecture + Design — real Kelowna firm (Mark Aquilon,
--     AIBC), distinct from Michael Green Architecture (69760): un-retire,
--     rename with the geographic disambiguator.
--   * 38972 PUBLIC: — current legal name is "Public Architecture + Design
--     Inc." (Vancouver licence 25-137148); rename; 21715 (WMW predecessor
--     name) and 69298 (current-legal-name row) map to it.
--   * TKA+D = renamed Taylor Kurtz — a FIVE-way active cluster: survivor
--     68897 (richest enrichment + live MPI link), renamed to the legal
--     form; 23375 / 70024 / 29301 / 68898 / 71122 map to it.
--   * 10867 Lemay + Toker concat artifact -> Toker + Associates (17075).
--   * Left retired per research: 70148 (Bora/Petretti team artifact),
--     71482 (Ryder+Hotson — two firms, never merged; Hotson -> SvN 2024).
-- ---------------------------------------------------------------------------
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = NULL, RetiredReason = NULL,
    Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'') + N'[m119: un-retired — web-verified real firm (m98-review research 2026-06-09)]',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id IN (24547, 70763) AND RetiredAtUtc IS NOT NULL;
PRINT 'Research un-retires: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'MGA Architecture (Kelowna)',
    Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'') + N'[m119: principal Mark Aquilon, AIBC; DISTINCT from Michael Green Architecture (69760)]',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 70763;

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Public Architecture + Design Inc.',
    Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'') + N'[m119: current legal name per Vancouver licence 25-137148; formerly WMW Public / PUBLIC: Architecture + Communication Inc.]',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 38972;

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'TKA+D Architecture + Design Inc.',
    Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'') + N'[m119: legal name per tkad.ca; formerly Taylor Kurtz Architecture + Design]',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 68897;
PRINT 'Research renames applied.';

-- ---------------------------------------------------------------------------
-- Part A: variant consolidation — Graham (m108 completion) + research-mapped
-- variants. The full FK template below repoints every referencing table.
-- ---------------------------------------------------------------------------
CREATE TABLE #VarMap (VariantId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO #VarMap (VariantId, SurvivorId) VALUES
    (72237, 8361), (72457, 8361),    -- Graham (m108 Steps 1-4 completion)
    (21715, 38972), (69298, 38972),  -- WMW / Public Architecture + Design
    (18983, 24547),                  -- HPNA typo -> HNPA
    (23375, 68897), (70024, 68897),  -- Taylor Kurtz / TKA+D and Design Inc.
    (29301, 68897), (68898, 68897),  -- TKA+D vendor row / TKA+D + RDHA
    (71122, 68897),                  -- TKA+D (with RDHA)
    (10867, 17075);                  -- Lemay + Toker concat -> Toker + Associates

IF EXISTS (SELECT 1 FROM #VarMap v JOIN opportunities.CanonicalOrg s ON s.Id = v.SurvivorId
           WHERE s.RetiredAtUtc IS NOT NULL)
    THROW 50121, 'm119: a mapped survivor is retired — abort.', 1;
IF EXISTS (SELECT 1 FROM #VarMap a JOIN #VarMap b ON a.VariantId = b.SurvivorId)
    THROW 50122, 'm119: an id appears as both variant and survivor — abort.', 1;

WITH EnrichCandidates AS (
    SELECT e.Id, v.SurvivorId, v.VariantId,
           ROW_NUMBER() OVER (PARTITION BY v.SurvivorId, e.ProviderName
                              ORDER BY e.LastRefreshAtUtc DESC, e.Id DESC) AS rn
    FROM opportunities.CanonicalOrgEnrichment e
    JOIN #VarMap v ON v.VariantId = e.CanonicalOrgId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment s
                      WHERE s.CanonicalOrgId = v.SurvivorId AND s.ProviderName = e.ProviderName)
)
UPDATE e SET CanonicalOrgId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset(),
             Notes = COALESCE(e.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m119: repointed from Graham variant ' + CAST(c.VariantId AS nvarchar(12)) + N']'
FROM opportunities.CanonicalOrgEnrichment e JOIN EnrichCandidates c ON c.Id = e.Id AND c.rn = 1;
PRINT 'Variant enrichment repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE a SET CanonicalOrgId = v.SurvivorId
FROM opportunities.OrgAlias a JOIN #VarMap v ON v.VariantId = a.CanonicalOrgId;
PRINT 'Variant aliases repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelAction x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelAction repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelSignal x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelSignal repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelRisk x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelRisk repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelWork x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelWork repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelNarrative x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelNarrative repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant affiliations repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET ProponentCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.ProponentCanonicalOrgId;
PRINT 'Variant MPI.Proponent repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ArchitectCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.ArchitectCanonicalOrgId;
PRINT 'Variant MPI.Architect repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET StructuralEngineerCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.StructuralEngineerCanonicalOrgId;
PRINT 'Variant MPI.StructEng repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET GeneralContractorCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.GeneralContractorCanonicalOrgId;
PRINT 'Variant MPI.GC repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET TargetCanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN #VarMap v ON v.VariantId = x.TargetCanonicalOrgId;
PRINT 'Variant IntelProjectAction.Target repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant IntelProjectKeyPerson repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.Opportunities x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'Variant Opportunities.Buyer repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET AwardedToCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityAwards x JOIN #VarMap v ON v.VariantId = x.AwardedToCanonicalOrgId;
PRINT 'Variant Awards.AwardedTo repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET AwardingCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityAwards x JOIN #VarMap v ON v.VariantId = x.AwardingCanonicalOrgId;
PRINT 'Variant Awards.Awarding repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BidderCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityBids x JOIN #VarMap v ON v.VariantId = x.BidderCanonicalOrgId;
PRINT 'Variant Bids.Bidder repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ResolvedCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityInterestedFirms x JOIN #VarMap v ON v.VariantId = x.ResolvedCanonicalOrgId;
PRINT 'Variant InterestedFirms repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.CrmEngagements x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'Variant CrmEngagements repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.KorPursuits x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'Variant KorPursuits.Buyer repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET LostToCanonicalOrgId = v.SurvivorId
FROM opportunities.KorPursuits x JOIN #VarMap v ON v.VariantId = x.LostToCanonicalOrgId;
PRINT 'Variant KorPursuits.LostTo repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ApplicantCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.ApplicantCanonicalOrgId;
PRINT 'Variant Permit.Applicant repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ContractorCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.ContractorCanonicalOrgId;
PRINT 'Variant Permit.Contractor repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET OwnerCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.OwnerCanonicalOrgId;
PRINT 'Variant Permit.Owner repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId
FROM opportunities.BdResearchTriggers x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'Variant BdResearchTriggers repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET CanonicalOrgId = v.SurvivorId
FROM opportunities.NewsArticleOrgMention x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention s
                  WHERE s.NewsArticleId = x.NewsArticleId AND s.CanonicalOrgId = v.SurvivorId);
DELETE x FROM opportunities.NewsArticleOrgMention x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;

WITH AdbCandidates AS (
    SELECT b.Id, v.SurvivorId,
           ROW_NUMBER() OVER (PARTITION BY v.SurvivorId ORDER BY b.Id DESC) AS rn
    FROM opportunities.ArchitectDisplacementBriefs b
    JOIN #VarMap v ON v.VariantId = b.ArchitectCanonicalOrgId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs s
                      WHERE s.ArchitectCanonicalOrgId = v.SurvivorId)
)
UPDATE b SET ArchitectCanonicalOrgId = c.SurvivorId
FROM opportunities.ArchitectDisplacementBriefs b JOIN AdbCandidates c ON c.Id = b.Id AND c.rn = 1;

UPDATE o SET RetiredAtUtc = COALESCE(o.RetiredAtUtc, sysdatetimeoffset()),
             RetiredReason = N'm119: duplicate of survivor ' + CAST(v.SurvivorId AS nvarchar(12)) + N' (m108 completion / m98-review research)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg o JOIN #VarMap v ON v.VariantId = o.Id;
PRINT 'Variants retired/relabeled: ' + CAST(@@ROWCOUNT AS varchar(10));
DROP TABLE #VarMap;

-- ---------------------------------------------------------------------------
-- Part B: Trifunov dedup (keep 7845, retire 7844 + affiliation 11272).
-- ---------------------------------------------------------------------------
UPDATE keep SET
    Notes = CASE WHEN keep.Notes IS NULL OR keep.Notes = N'' THEN dup.Notes
                 WHEN dup.Notes IS NULL OR dup.Notes = N'' THEN keep.Notes
                 ELSE keep.Notes + NCHAR(13) + NCHAR(10) + N'[m119, from manual mint 7844] ' + dup.Notes END,
    Corroborations = keep.Corroborations + 1,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson keep
JOIN opportunities.IntelPerson dup ON dup.Id = 7844
WHERE keep.Id = 7845;
PRINT 'Trifunov 7845 enriched from 7844: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'm119: duplicate affiliation — person merged into IntelPerson 7845',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 11272 AND RetiredAtUtc IS NULL;
PRINT 'Affiliation 11272 retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE opportunities.IntelPerson
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'm119: duplicate of IntelPerson 7845 (Alex Trifunov, FirmNarrative-keyed)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 7844 AND RetiredAtUtc IS NULL;
PRINT 'IntelPerson 7844 retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part C: NULL stale links from active rows to retired orgs (names retained).
-- ---------------------------------------------------------------------------
UPDATE a SET AwardedToCanonicalOrgId = NULL
FROM opportunities.OpportunityAwards a
JOIN opportunities.CanonicalOrg o ON o.Id = a.AwardedToCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL;
PRINT 'Awards.AwardedTo links to retired orgs NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE a SET AwardingCanonicalOrgId = NULL
FROM opportunities.OpportunityAwards a
JOIN opportunities.CanonicalOrg o ON o.Id = a.AwardingCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL;
PRINT 'Awards.Awarding links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE b SET BidderCanonicalOrgId = NULL
FROM opportunities.OpportunityBids b
JOIN opportunities.CanonicalOrg o ON o.Id = b.BidderCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL;
PRINT 'Bids.Bidder links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE f SET ResolvedCanonicalOrgId = NULL
FROM opportunities.OpportunityInterestedFirms f
JOIN opportunities.CanonicalOrg o ON o.Id = f.ResolvedCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL;
PRINT 'InterestedFirms links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE m SET ProponentCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg o ON o.Id = m.ProponentCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND m.RetiredAtUtc IS NULL;
PRINT 'Active MPI.Proponent links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE m SET ArchitectCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg o ON o.Id = m.ArchitectCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND m.RetiredAtUtc IS NULL;
PRINT 'Active MPI.Architect links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE m SET StructuralEngineerCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg o ON o.Id = m.StructuralEngineerCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND m.RetiredAtUtc IS NULL;
PRINT 'Active MPI.StructEng links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE m SET GeneralContractorCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg o ON o.Id = m.GeneralContractorCanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND m.RetiredAtUtc IS NULL;
PRINT 'Active MPI.GC links NULLed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part D: retire org-side intel still ACTIVE on retired orgs.
-- ---------------------------------------------------------------------------
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelAction x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelAction retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelSignal x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelSignal retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelRisk x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelRisk retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelWork x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelWork retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelNarrative x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelNarrative retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm119: parent org retired',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId
WHERE o.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelPersonAffiliation retired with parent org: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm119 committed.';
GO

-- Verify: no active row may link to a retired org; Graham + Trifunov state.
SELECT 'Awards->retired' AS Chk, COUNT(*) AS N FROM opportunities.OpportunityAwards a JOIN opportunities.CanonicalOrg o ON o.Id = a.AwardedToCanonicalOrgId WHERE o.RetiredAtUtc IS NOT NULL
UNION ALL SELECT 'InterestedFirms->retired', COUNT(*) FROM opportunities.OpportunityInterestedFirms f JOIN opportunities.CanonicalOrg o ON o.Id = f.ResolvedCanonicalOrgId WHERE o.RetiredAtUtc IS NOT NULL
UNION ALL SELECT 'ActiveMPI->retiredOrg', COUNT(*) FROM opportunities.MajorProjectsInventory m JOIN opportunities.CanonicalOrg o ON o.Id IN (m.ProponentCanonicalOrgId, m.ArchitectCanonicalOrgId, m.StructuralEngineerCanonicalOrgId, m.GeneralContractorCanonicalOrgId) WHERE o.RetiredAtUtc IS NOT NULL AND m.RetiredAtUtc IS NULL
UNION ALL SELECT 'ActiveOrgIntel->retiredOrg', COUNT(*) FROM (
    SELECT CanonicalOrgId FROM opportunities.IntelAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT CanonicalOrgId FROM opportunities.IntelSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT CanonicalOrgId FROM opportunities.IntelWork WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT CanonicalOrgId FROM opportunities.IntelPersonAffiliation WHERE RetiredAtUtc IS NULL
) x JOIN opportunities.CanonicalOrg o ON o.Id = x.CanonicalOrgId WHERE o.RetiredAtUtc IS NOT NULL;
SELECT Id, LEFT(DisplayName,50) AS Name, CASE WHEN RetiredAtUtc IS NULL THEN 'ACTIVE' ELSE 'RETIRED' END St FROM opportunities.CanonicalOrg WHERE Id IN (8361, 72237, 72457);
SELECT Id, CASE WHEN RetiredAtUtc IS NULL THEN 'ACTIVE' ELSE 'RETIRED' END St FROM opportunities.IntelPerson WHERE Id IN (7844, 7845);
GO
