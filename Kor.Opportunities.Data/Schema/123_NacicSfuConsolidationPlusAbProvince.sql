-- 123_NacicSfuConsolidationPlusAbProvince.sql
-- BD Reports build follow-through (2026-06-10): the report smokes surfaced
-- two duplicate pairs and a Province mislabel.
--
-- Validated against prod before writing:
--   * Part A — Province: the CapitalPlans importer stamped its file-level
--     Province='BC' constant on every row (BdResearchImport Program.cs:3426,
--     fixed in the same commit). 38 active CAPPLAN rows carry Province='BC'
--     with Alberta markets (RegionName Edmonton=19 / Calgary=10 /
--     'Other AB'=9). RegionName is a faithful copy of the research file's
--     market field for CAPPLAN rows.
--   * Part B — live merges (m117/m122 template), both survivors ACTIVE:
--       6472 -> 7036  NACIC Edmonton. 6472's own honing korAngle says
--                     "See refresh-project-7036.json"; 7036 has the correct
--                     AB/Edmonton geography and 16 active intel rows vs 5.
--                     6472's unique enrichments (ProjectBrief,
--                     PrimeConsultantResearch) repoint; its honing row
--                     collides and stays behind. Survivor keeps its $90M
--                     cost (6472's $159M is the research file's figure and
--                     equals SACIC's budget — suspect; COALESCE fill cannot
--                     overwrite a present survivor value).
--       3975 -> 3071  SFU School of Medicine (Surrey Centre Block). 3071
--                     has the first-pass ProjectBrief 3975 lacks plus the
--                     newer PrimeConsultantResearch; both honing/primes
--                     rows collide and stay behind.
--     NaturalKey overlap between pair members checked: zero on all four
--     IntelProject* tables — blind repoint cannot violate UQ_*_NaturalKey.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- ---------------------------------------------------------------------------
-- Part A: Alberta CAPPLAN rows mislabeled Province='BC' (importer constant).
-- Keyed on RegionName because MunicipalityName is NULL on CapitalPlans
-- imports and SourceKey owner slugs vary. Covers retired rows too — the
-- label is wrong regardless of lifecycle state.
-- ---------------------------------------------------------------------------
UPDATE m SET Province = N'AB', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
WHERE m.SourceKey LIKE N'CAPPLAN-%' AND m.Province = N'BC'
  AND m.RegionName IN (N'Edmonton', N'Calgary', N'Other AB');
PRINT 'CAPPLAN Province BC->AB: ' + CAST(@@ROWCOUNT AS varchar(10));

-- City-named regions double as the municipality when none was imported.
UPDATE m SET MunicipalityName = m.RegionName, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
WHERE m.SourceKey LIKE N'CAPPLAN-%' AND m.Province = N'AB'
  AND m.MunicipalityName IS NULL AND m.RegionName IN (N'Edmonton', N'Calgary');
PRINT 'CAPPLAN MunicipalityName backfilled: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part B: live merges (victim -> ACTIVE survivor), m117/m122 template.
-- ---------------------------------------------------------------------------
DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES (6472, 7036), (3975, 3071);

IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50124, 'm123: a mapped survivor is missing or retired — abort.', 1;
IF EXISTS (SELECT 1 FROM @Map mp JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId
           WHERE v.RetiredAtUtc IS NOT NULL)
    THROW 50125, 'm123: a mapped victim is already retired — re-validate before re-running.', 1;

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

WITH IpCandidates AS (
    SELECT x.Id, mp.SurvivorId,
           ROW_NUMBER() OVER (PARTITION BY mp.SurvivorId, x.SourceProviderName
                              ORDER BY x.LastSeenAtUtc DESC, x.Id DESC) AS rn
    FROM opportunities.IntelProject x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
    WHERE x.RetiredAtUtc IS NULL
      AND NOT EXISTS (SELECT 1 FROM opportunities.IntelProject s
                      WHERE s.MajorProjectsInventoryId = mp.SurvivorId
                        AND s.SourceProviderName = x.SourceProviderName AND s.RetiredAtUtc IS NULL)
)
UPDATE x SET MajorProjectsInventoryId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN IpCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'IntelProject repointed: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm123: superseded — survivor already has a live row from this provider',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProject retired (collision): ' + CAST(@@ROWCOUNT AS varchar(10));

WITH EnrichCandidates AS (
    SELECT x.Id, mp.SurvivorId, mp.VictimId,
           ROW_NUMBER() OVER (PARTITION BY mp.SurvivorId, x.ProviderName
                              ORDER BY x.LastRefreshAtUtc DESC, x.Id DESC) AS rn
    FROM opportunities.MajorProjectEnrichment x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment s
                      WHERE s.MajorProjectsInventoryId = mp.SurvivorId AND s.ProviderName = x.ProviderName)
)
UPDATE x SET MajorProjectsInventoryId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset(),
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m123: repointed from duplicate MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
FROM opportunities.MajorProjectEnrichment x JOIN EnrichCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'Enrichment repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

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

UPDATE v SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm123: duplicate of survivor MPI ' + CAST(mp.SurvivorId AS nvarchar(12)) + N' (BD Reports build smoke finding)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory v JOIN @Map mp ON mp.VictimId = v.Id;
PRINT 'Live-merge victims retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm123 committed.';
GO

-- Verify: no active CAPPLAN row labels an Alberta market as BC; both victims
-- retired; no active intel on retired rows anywhere.
SELECT COUNT(*) AS AbMarketsStillBc
FROM opportunities.MajorProjectsInventory
WHERE SourceKey LIKE N'CAPPLAN-%' AND Province = N'BC'
  AND RegionName IN (N'Edmonton', N'Calgary', N'Other AB');
SELECT Id, RetiredAtUtc FROM opportunities.MajorProjectsInventory WHERE Id IN (6472, 3975);
SELECT COUNT(*) AS ActiveIntelOnRetired
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
GO
