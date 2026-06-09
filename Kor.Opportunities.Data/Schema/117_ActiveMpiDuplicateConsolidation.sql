-- 117_ActiveMpiDuplicateConsolidation.sql
-- BD-Audit-2026-06-09 C9: the active MPI set carried 12 exact-name duplicate
-- clusters plus 38 honing verdict=DUPLICATE rows that nothing actioned.
-- This consolidates the 51 verifiable cases. Mapping evidence (verified
-- 2026-06-09 against prod + each honing rationale):
--   * Honing pairs: the DUPLICATE row's rationale names the canonical id
--     ("See ID X"); each target verified ACTIVE and same facility by
--     name+municipality before inclusion.
--   * Exact-name clusters: survivor = richest active Intel (m114 rule);
--     Cardston survivor 6655 confirmed by both the hospitals honing and the
--     primes research ("all consolidate to ID 6655").
-- Deliberately NOT merged (recorded here so the skip is auditable):
--   * 4589, 6483 — their "See ID" targets are their own m114-retired
--     victims (circular cross-reference); they ARE the canonicals.
--   * 4305, 5284 -> 6455 — Kelowna Activity Centres is a multi-SITE program
--     (Rutland / Mission / Glenmore); rows are different facilities.
--   * 4652 -> 5029 — Kamloops Arena Multiplex vs Memorial Arena are
--     different Build-Kamloops facilities.
--   * 6953 -> 7110 — Leduc umbrella row vs Phase 1 row (program vs phase).
--   * "Condominium Development" (2257/878/1064) + "Residential Condominium"
--     (2414/1788) — generic names, distinct projects (R95-extra deferred).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES
    -- Honing-designated pairs (dup row -> canonical row named in rationale)
    (3277, 2451),  -- Brentwood Block (Grosvenor) -> Brentwood Block Condominium
    (4291, 4570),  -- Crystal Pool Replacement -> Crystal Pool Replacement (canonical)
    (5000, 4570),  -- Crystal Pool & Fitness Centre -> same
    (4315, 5089),  -- Nanaimo South CC -> South Nanaimo Community Centre
    (4445, 4222),  -- Surrey Cultural Event Centre / Arena -> City Centre arena
    (4591, 4222),  -- Surrey City Centre Arena -> same
    (6783, 4222),  -- Surrey City Centre Arena (Centre Block) -> same
    (4594, 4236),  -- Brentwood CC (New Build) -> Brentwood/Willingdon CC
    (5289, 4236),  -- Burnaby Brentwood CC -> same
    (6780, 4236),  -- Brentwood CC -> same
    (4877, 6925),  -- Langara Family YMCA (665 Units) -> Langara YMCA Musqueam
    (5120, 4587),  -- Penticton Twin-Pad at SOEC -> Penticton Twin Pad Arena
    (5168, 6817),  -- Al Anderson Pool Hybrid Renewal -> City of Langley Al Anderson
    (5169, 4599),  -- West Richmond Pavilion (Hugh Boyd) -> West Richmond Pavilion
    (5170, 3356),  -- Richmond Capstan CC -> Capstan Community Centre
    (6798, 3356),  -- Capstan CC (exact-name cluster member) -> same
    (5178, 5001),  -- Ravensong Renovation -> Ravensong Aquatic Centre
    (5213, 6819),  -- MR Hammond Community Aquatics -> City of Maple Ridge Hammond
    (5292, 6819),  -- MR Hammond Aquatics and Rec -> same
    (5214, 6820),  -- MR Albion Fairgrounds Arena -> City of Maple Ridge Albion
    (5238, 1487),  -- REC-REATE Ph2 Rod Brind'Amour -> Campbell River Arena
    (5281, 4585),  -- Kamloops Curling and Racquet -> Curling and Racquet Complex
    (5282, 4397),  -- Kamloops Aquatics Centre -> Build Kamloops Leisure Aquatic
    (5290, 5240),  -- Coquitlam Fraser Mills CC -> Fraser Mills Community Centre
    (6931, 5240),  -- Beedie Living Fraser Mills CC -> same
    (5924, 6789),  -- Vancouver Aquatic Centre Replacement -> VAC Renewal
    (6335, 4228),  -- Rollie Miles (Design P...) -> Rollie Miles Recreation Centre
    (6479, 6702),  -- Northeast Athletic Complex -> Calgary NE Athletic (Saddle Ridge)
    (7113, 6702),  -- NE Athletic Complex Indoor Field -> same
    (6535, 6728),  -- South Side Outdoor Aquatics -> Medicine Hat South Side
    (6815, 5291),  -- Langley Willowbrook CC -> Langley Township Willowbrook
    (6821, 5216),  -- Port Moody Kyle Centre CC -> Kyle Centre Redevelopment
    (6929, 2734),  -- Bosa Solhouse 6035 -> Solhouse 6035 by Bosa
    -- Exact-name clusters (survivor = richest active Intel)
    (3923, 6655), (7034, 6655), (6474, 6655), (6510, 6655),  -- Cardston Health Centre
    (5020, 3880), (7020, 3880),                              -- East Kootenay Oncology & Renal
    (2914, 4439), (3129, 4439),                              -- Pitt Meadows Secondary
    (4436, 6843), (2865, 6843), (3082, 6843),                -- Olympic Village Elementary
    (4558, 6843), (5260, 6843),                              --   (+ name variants)
    (4415, 3128), (4300, 3128),                              -- North Langford Secondary
    (3099, 5140),                                            -- Matthew McNair Seismic
    (4952, 3096),                                            -- John G. Diefenbaker Seismic
    (5016, 7058);                                            -- Cariboo Memorial Hospital

-- Assertions: all survivors AND victims must currently be ACTIVE; no id may
-- appear as both victim and survivor.
IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50118, 'm117: a survivor is missing or retired — abort.', 1;
IF EXISTS (SELECT 1 FROM @Map mp JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId
           WHERE v.RetiredAtUtc IS NOT NULL)
    THROW 50119, 'm117: a victim is already retired — mapping is stale, abort.', 1;
IF EXISTS (SELECT 1 FROM @Map a JOIN @Map b ON a.VictimId = b.SurvivorId)
    THROW 50120, 'm117: an id appears as both victim and survivor — abort.', 1;

-- 1. Repoint live project intel (NaturalKey globally unique — no collision).
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'IntelProjectAction repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'IntelProjectSignal repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'IntelProjectRisk repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'IntelProjectKeyPerson repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelWork x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'IntelWork repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2. IntelProject (filtered unique on MPI+provider for live rows): repoint
--    the freshest non-colliding candidate per (survivor, provider); retire
--    the live remainder in place as superseded.
WITH IpCandidates AS (
    SELECT x.Id, mp.SurvivorId,
           ROW_NUMBER() OVER (PARTITION BY mp.SurvivorId, x.SourceProviderName
                              ORDER BY x.LastSeenAtUtc DESC, x.Id DESC) AS rn
    FROM opportunities.IntelProject x
    JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
    WHERE x.RetiredAtUtc IS NULL
      AND NOT EXISTS (SELECT 1 FROM opportunities.IntelProject s
                      WHERE s.MajorProjectsInventoryId = mp.SurvivorId
                        AND s.SourceProviderName = x.SourceProviderName
                        AND s.RetiredAtUtc IS NULL)
)
UPDATE x SET MajorProjectsInventoryId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN IpCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'IntelProject repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm117: superseded — survivor MPI already has a live row from this provider',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProject retired (provider collision): ' + CAST(@@ROWCOUNT AS varchar(10));

-- 3. Enrichment: unique (MPI, Provider) — collision-ranked repoint, freshest
--    candidate per (survivor, provider); remainder stays archived on victim.
WITH EnrichCandidates AS (
    SELECT x.Id, mp.SurvivorId, mp.VictimId,
           ROW_NUMBER() OVER (PARTITION BY mp.SurvivorId, x.ProviderName
                              ORDER BY x.LastRefreshAtUtc DESC, x.Id DESC) AS rn
    FROM opportunities.MajorProjectEnrichment x
    JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment s
                      WHERE s.MajorProjectsInventoryId = mp.SurvivorId
                        AND s.ProviderName = x.ProviderName)
)
UPDATE x SET MajorProjectsInventoryId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset(),
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m117: repointed from duplicate MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
FROM opportunities.MajorProjectEnrichment x
JOIN EnrichCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'MajorProjectEnrichment repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 4. CRM links (unique pair-guarded).
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId
FROM opportunities.CrmEngagementProjectLink x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink s
                  WHERE s.EngagementId = x.EngagementId AND s.MajorProjectsInventoryId = mp.SurvivorId);
PRINT 'CrmEngagementProjectLink repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 5. Backfill survivor FKs/fields the victim had and the survivor lacks
--    (cheap COALESCE pulls; never overwrite survivor data).
UPDATE s SET
    ProponentCanonicalOrgId = COALESCE(s.ProponentCanonicalOrgId, v.ProponentCanonicalOrgId),
    ArchitectCanonicalOrgId = COALESCE(s.ArchitectCanonicalOrgId, v.ArchitectCanonicalOrgId),
    GeneralContractorCanonicalOrgId = COALESCE(s.GeneralContractorCanonicalOrgId, v.GeneralContractorCanonicalOrgId),
    StructuralEngineerCanonicalOrgId = COALESCE(s.StructuralEngineerCanonicalOrgId, v.StructuralEngineerCanonicalOrgId),
    EstimatedCostCad = COALESCE(s.EstimatedCostCad, v.EstimatedCostCad),
    MunicipalityName = COALESCE(s.MunicipalityName, v.MunicipalityName),
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory s
JOIN @Map mp ON mp.SurvivorId = s.Id
JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId;
PRINT 'Survivor field backfill rows touched: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 6. Retire the victims.
UPDATE v SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm117: duplicate of survivor MPI ' + CAST(mp.SurvivorId AS nvarchar(12)) + N' (BD-Audit-2026-06-09 C9)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory v JOIN @Map mp ON mp.VictimId = v.Id;
PRINT 'Duplicate MPIs retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm117 committed.';
GO

-- Verify 1: no active intel may sit on the newly retired victims.
SELECT COUNT(*) AS ActiveIntelOnM117Victims
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i
JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
-- Verify 2: remaining exact-name duplicate clusters among active rows
-- (expect only the generic condo clusters).
SELECT LOWER(LTRIM(RTRIM(ProjectName))) AS NormName, Province, ISNULL(MunicipalityName,'?') AS Muni, COUNT(*) AS N
FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL
GROUP BY LOWER(LTRIM(RTRIM(ProjectName))), Province, ISNULL(MunicipalityName,'?') HAVING COUNT(*) > 1;
GO
