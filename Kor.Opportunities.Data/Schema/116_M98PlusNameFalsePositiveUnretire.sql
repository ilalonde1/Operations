-- 116_M98PlusNameFalsePositiveUnretire.sql
-- BD-Audit-2026-06-09 C3: m98's "multi-entity research artifact" heuristic
-- retired ~98 orgs on the '+' character. A large fraction are REAL single
-- firms whose brand contains '+' ("X Architecture + Design", "Y + Partners",
-- "Z + Associates") — architects, KOR's prime-consultant target population,
-- carrying FirmNarrative/DecisionMakers/pipeline enrichment.
--
-- Classification (each row reviewed by name against the live list):
--   * UN-RETIRE (50): single-brand '+' names with no second-firm tell.
--   * VARIANT (12): same real firm, second row (office/contact/typo
--     variants) — FKs + enrichment repoint to the un-retired survivor,
--     row stays retired with a corrected RetiredReason. Includes ACTIVE
--     near-dup 70022 "DA Architects" which folds into 85.
--   * TRUE ARTIFACT (~27): names joining two distinct firms ("A + B
--     Architects", "/", ";", "and", JV) — untouched.
--   * AMBIGUOUS (9): left retired; exported to review CSV for Ian
--     (see tools/BdCanonicalDedup/output/m98-review-remaining.csv):
--     69298, 21715, 24547, 18983, 70148, 70763, 71122, 71482, 10867.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- ---------------------------------------------------------------------------
-- 1. Un-retire the 50 verified real single firms.
-- ---------------------------------------------------------------------------
DECLARE @UnRetire TABLE (Id bigint PRIMARY KEY);
INSERT INTO @UnRetire (Id) VALUES
    (31),    -- WNDR Architecture + Design Inc.
    (85),    -- DA Architects + Planners (survivor for 72171, 70022)
    (940),   -- 1080 Architecture, Planning + Interiors
    (5464),  -- Coupland Kraemer Architecture + Interior Design Inc.
    (7828),  -- FUSE Architecture + Design
    (10822), -- LEES + Associates Landscape Architects
    (11948), -- Modern Office Design + Architecture (MODA; survivor for 72102)
    (14910), -- SAHURI + Partners Architecture Inc. (survivor for 71950, 73934)
    (17075), -- Toker + Associates Architecture
    (19223), -- Khora Architecture + Interiors
    (19357), -- Anyone Architecture + Design
    (19531), -- Tony Osborn Architecture + Design Inc.
    (20024), -- Koka Architecture + Design Inc.
    (20151), -- J & R Katz Design + Architecture Inc.
    (20300), -- Studio Senbel Architecture + Design Inc.
    (21012), -- Sean Best Architecture + Design Inc.
    (21595), -- Bau Studio + Architecture Inc.
    (21856), -- Noble Architecture + Interiors
    (22529), -- Johnston Davidson Architecture + Planning Inc.
    (23375), -- Taylor Kurtz Architecture + Design Inc. (survivor for 37676 typo)
    (23687), -- One Seed Architecture + Interiors Inc.
    (23970), -- collabor8 Architecture + Design Inc.
    (24306), -- Frits de Vries Architects + Associates Ltd.
    (25733), -- Kasian Architecture + Interiors
    (26654), -- Simplex + G Architecture Inc.
    (27469), -- Harmonic Architecture + Design
    (28098), -- Mara + Natha architecture
    (28938), -- Dandyk + Wollin Architects Inc.
    (33512), -- Nada Awadi Architecture + Design
    (33938), -- Hessey Consulting + Architecture Inc.
    (34059), -- ZAS Architects + Interiors
    (35713), -- euoi studio architecture + design
    (38972), -- PUBLIC: Architecture + Communication Inc. (survivor for 68915)
    (40036), -- Stewart + Tsai Architects
    (54375), -- D'Ambrosio Architecture + Urbanism (survivor for 70766, 70769, 69882)
    (56314), -- PROVENCHER ROY + ASSOCIES ARCHITECTES INC.
    (63332), -- Norr Limited Architects + Engineers
    (68640), -- House + House Architects
    (68917), -- Local Practice Architecture + Design
    (68976), -- Berry Architecture + Associates
    (69095), -- Formline Architecture + Urbanism
    (69122), -- Proscenium Architecture + Interiors
    (69249), -- Busby + Associates Architects (historic; became Perkins&Will)
    (69348), -- Studio 9 Architecture + Planning
    (69758), -- Leckie Studio Architecture + Design
    (69775), -- Human Studio Architecture + Urban Design
    (70623), -- Sebastien Garon Architecture + Design Inc.
    (70824), -- Lake Monster Studio Architecture + Design
    (72276), -- Omicron (Vancouver) — descriptor parenthetical, single firm
    (74000), -- THUJA Architecture + Design
    (38969); -- office of mcfarlane biggar architects + designers (OMB)
             -- (survivor for 27564, 20561)

UPDATE o SET RetiredAtUtc = NULL,
             RetiredReason = NULL,
             Notes = COALESCE(o.Notes + NCHAR(13) + NCHAR(10), N'')
                     + N'[m116: un-retired — m98 multi-entity heuristic false positive; ''+'' is part of the firm''s brand name]',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg o
JOIN @UnRetire u ON u.Id = o.Id
WHERE o.RetiredAtUtc IS NOT NULL
  AND o.RetiredReason LIKE N'%multi-entity%';
PRINT 'Un-retired m98 false positives: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- 2. Variant rows: repoint FKs + enrichment to the un-retired survivor,
--    keep (or make) the variant retired with a corrected reason.
-- ---------------------------------------------------------------------------
CREATE TABLE #VarMap (VariantId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO #VarMap (VariantId, SurvivorId) VALUES
    (72171, 85),    -- DA Architects + Planners (Mark Ehman)
    (70022, 85),    -- DA Architects (ACTIVE near-dup, 3 enr) -> 85 (10 enr)
    (71950, 14910), -- SAHURI + Partners (Alberta)
    (73934, 14910), -- Sahuri + Partners (Calgary office)
    (70766, 54375), -- D'Ambrosio (DAU Studio)
    (70769, 54375), -- DAU Studio (D'Ambrosio ...)
    (69882, 54375), -- D'Ambrosio (master plan / high-... descriptor)
    (27564, 38969), -- McFarlane Biggar Architects + Designers Inc.
    (20561, 38969), -- McFarlane Biggar Architects + Design
    (37676, 23375), -- Taylor Krutz (typo of Taylor Kurtz)
    (72102, 11948), -- MODA (Modern Office of Design + Architecture)
    (68915, 38972); -- Public Architecture + Communication (short form)

-- Survivors must be active after step 1.
IF EXISTS (SELECT 1 FROM #VarMap v JOIN opportunities.CanonicalOrg s ON s.Id = v.SurvivorId
           WHERE s.RetiredAtUtc IS NOT NULL)
    THROW 50117, 'm116: a variant survivor is still retired — step 1 did not cover it, abort.', 1;

-- 2a. Enrichment: UX_CanonicalOrgEnrichment_OrgProvider is unique on
--     (CanonicalOrgId, ProviderName) and several variants share a survivor —
--     rank candidates and move only the freshest per (survivor, provider).
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
             Notes = COALESCE(e.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m116: repointed from variant org ' + CAST(c.VariantId AS nvarchar(12)) + N']'
FROM opportunities.CanonicalOrgEnrichment e
JOIN EnrichCandidates c ON c.Id = e.Id AND c.rn = 1;
PRINT 'Enrichment repointed to survivors: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2b. Aliases follow the survivor (unique key RawName+Source unaffected).
UPDATE a SET CanonicalOrgId = v.SurvivorId
FROM opportunities.OrgAlias a JOIN #VarMap v ON v.VariantId = a.CanonicalOrgId;
PRINT 'OrgAlias repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2c. Org-side intel: NaturalKey is globally unique per table, repoint clean
--     (migration-path semantics per m105 — repoint, never delete).
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelAction x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelAction repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelSignal x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelSignal repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelRisk x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelRisk repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelWork x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelWork repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelNarrative x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelNarrative repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelPersonAffiliation repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2d. Project-side org references.
UPDATE x SET ProponentCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.ProponentCanonicalOrgId;
PRINT 'MPI.Proponent repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ArchitectCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.ArchitectCanonicalOrgId;
PRINT 'MPI.Architect repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET StructuralEngineerCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.StructuralEngineerCanonicalOrgId;
PRINT 'MPI.StructEng repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET GeneralContractorCanonicalOrgId = v.SurvivorId
FROM opportunities.MajorProjectsInventory x JOIN #VarMap v ON v.VariantId = x.GeneralContractorCanonicalOrgId;
PRINT 'MPI.GC repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET TargetCanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN #VarMap v ON v.VariantId = x.TargetCanonicalOrgId;
PRINT 'IntelProjectAction.Target repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'IntelProjectKeyPerson repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2e. Pipeline / commercial references.
UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.Opportunities x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'Opportunities.Buyer repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET AwardedToCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityAwards x JOIN #VarMap v ON v.VariantId = x.AwardedToCanonicalOrgId;
PRINT 'Awards.AwardedTo repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET AwardingCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityAwards x JOIN #VarMap v ON v.VariantId = x.AwardingCanonicalOrgId;
PRINT 'Awards.Awarding repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BidderCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityBids x JOIN #VarMap v ON v.VariantId = x.BidderCanonicalOrgId;
PRINT 'Bids.Bidder repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ResolvedCanonicalOrgId = v.SurvivorId
FROM opportunities.OpportunityInterestedFirms x JOIN #VarMap v ON v.VariantId = x.ResolvedCanonicalOrgId;
PRINT 'InterestedFirms repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.CrmEngagements x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'CrmEngagements repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET BuyerCanonicalOrgId = v.SurvivorId
FROM opportunities.KorPursuits x JOIN #VarMap v ON v.VariantId = x.BuyerCanonicalOrgId;
PRINT 'KorPursuits.Buyer repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET LostToCanonicalOrgId = v.SurvivorId
FROM opportunities.KorPursuits x JOIN #VarMap v ON v.VariantId = x.LostToCanonicalOrgId;
PRINT 'KorPursuits.LostTo repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ApplicantCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.ApplicantCanonicalOrgId;
PRINT 'Permit.Applicant repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET ContractorCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.ContractorCanonicalOrgId;
PRINT 'Permit.Contractor repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET OwnerCanonicalOrgId = v.SurvivorId
FROM opportunities.BuildingPermit x JOIN #VarMap v ON v.VariantId = x.OwnerCanonicalOrgId;
PRINT 'Permit.Owner repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET CanonicalOrgId = v.SurvivorId
FROM opportunities.BdResearchTriggers x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'BdResearchTriggers repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- News mentions: avoid duplicating (NewsArticleId, survivor) pairs; surplus
-- variant mentions are duplicate links, safe to delete.
UPDATE x SET CanonicalOrgId = v.SurvivorId
FROM opportunities.NewsArticleOrgMention x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention s
                  WHERE s.NewsArticleId = x.NewsArticleId AND s.CanonicalOrgId = v.SurvivorId);
PRINT 'NewsMentions repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
DELETE x FROM opportunities.NewsArticleOrgMention x JOIN #VarMap v ON v.VariantId = x.CanonicalOrgId;
PRINT 'NewsMentions duplicate links removed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Displacement briefs: UNIQUE(ArchitectCanonicalOrgId) — move only when the
-- survivor has none, and only one variant brief per survivor.
WITH AdbCandidates AS (
    SELECT b.Id, v.SurvivorId,
           ROW_NUMBER() OVER (PARTITION BY v.SurvivorId ORDER BY b.Id DESC) AS rn
    FROM opportunities.ArchitectDisplacementBriefs b
    JOIN #VarMap v ON v.VariantId = b.ArchitectCanonicalOrgId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs s
                      WHERE s.ArchitectCanonicalOrgId = v.SurvivorId)
)
UPDATE b SET ArchitectCanonicalOrgId = c.SurvivorId
FROM opportunities.ArchitectDisplacementBriefs b
JOIN AdbCandidates c ON c.Id = b.Id AND c.rn = 1;
PRINT 'DisplacementBriefs repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2f. Mark every variant retired with the corrected reason (70022 was active).
UPDATE o SET RetiredAtUtc = COALESCE(o.RetiredAtUtc, sysdatetimeoffset()),
             RetiredReason = N'm116: duplicate of survivor ' + CAST(v.SurvivorId AS nvarchar(12)) + N' (real firm, name variant)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg o JOIN #VarMap v ON v.VariantId = o.Id;
PRINT 'Variants marked as duplicates: ' + CAST(@@ROWCOUNT AS varchar(10));

DROP TABLE #VarMap;
COMMIT TRAN;
PRINT 'm116 committed.';
GO

-- Verify: survivors active with their enrichment; variants retired as dups.
SELECT o.Id, LEFT(o.DisplayName, 45) AS DisplayName,
       CASE WHEN o.RetiredAtUtc IS NULL THEN 'ACTIVE' ELSE 'RETIRED' END AS State,
       (SELECT COUNT(*) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId = o.Id) AS EnrichRows
FROM opportunities.CanonicalOrg o
WHERE o.Id IN (31, 85, 70022, 14910, 54375, 38969, 38972, 69095, 69758, 68976)
ORDER BY o.Id;
SELECT COUNT(*) AS RemainingMultiEntityRetired
FROM opportunities.CanonicalOrg
WHERE RetiredAtUtc IS NOT NULL AND RetiredReason LIKE N'%multi-entity%';
GO
