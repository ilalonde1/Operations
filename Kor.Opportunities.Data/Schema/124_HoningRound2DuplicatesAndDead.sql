-- 124_HoningRound2DuplicatesAndDead.sql
-- 2026-06-10 honing-projects batch-003 follow-through: the 99-verdict pass
-- flagged 10 DUPLICATEs (each naming its twin) and 19 DEADs.
--
-- Validated against prod before writing:
--   * Part A — 9 live merges (m117/m122/m123 template), every survivor
--     verified ACTIVE; the three name-only twins resolved to ids by exact
--     project-name match (4111 -> 6971 Dr. Anne Anderson HS Addition EPSB,
--     4112 -> 6972 Hawks Ridge K-6 EPSB; both confirmed active, the older
--     6317 Anne Anderson row is retired-Completed and NOT the twin).
--     NaturalKey overlap between every pair: zero on all four IntelProject*
--     tables — blind repoint cannot violate UQ_*_NaturalKey.
--   * Part B — 4115 (Castle Downs 10-12) duplicates 7090, which m118
--     retired as honing-verdict DEAD -> dies with its twin (m122 Part-B
--     rule): retire + children retire.
--   * Part C — the 19 active MPIs whose honing verdict is DEAD (all from
--     batch-003; verified active DEAD count = exactly 19) retire per the
--     m118 pattern. Selected BY CONDITION, not hardcoded ids, so a re-run
--     after further ingests stays correct.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- ---------------------------------------------------------------------------
-- Part A: live merges (victim -> ACTIVE survivor).
-- ---------------------------------------------------------------------------
DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES
    (3341, 3340),  -- 268 Nelson's Court -> Brewery District 'Seven'
    (3404, 3403),  -- Fort and Quadra -> 1520 Blanshard Street
    (4111, 6971),  -- Dr. Anne Anderson 10-12 Addition -> EPSB Addition
    (4112, 6972),  -- Hawks Ridge K-6 (NW) -> Hawks Ridge K-6 School (EPSB)
    (4113, 6973),  -- Aster K-9 (SE) -> Aster K-9 School (EPSB)
    (4114, 7088),  -- Stillwater K-9 (W) -> EPSB New K-9 in Stillwater
    (4215, 6634),  -- Symons Valley Library -> Symons Valley Centre Library
    (4230, 7072),  -- Walker Fire Station No. 32 -> Walker Fire Station #32
    (6698, 6649);  -- Calgary Housing Bridgeland Place (update) -> primary

IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50126, 'm124: a mapped survivor is missing or retired — abort.', 1;
IF EXISTS (SELECT 1 FROM @Map mp JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId
           WHERE v.RetiredAtUtc IS NOT NULL)
    THROW 50127, 'm124: a mapped victim is already retired — re-validate before re-running.', 1;

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
             RetiredReason = N'm124: superseded — survivor already has a live row from this provider',
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
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m124: repointed from duplicate MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
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
             RetiredReason = N'm124: duplicate of survivor MPI ' + CAST(mp.SurvivorId AS nvarchar(12)) + N' (honing batch-003 DUPLICATE verdict)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory v JOIN @Map mp ON mp.VictimId = v.Id;
PRINT 'Live-merge victims retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part B: duplicate of a DEAD twin — dies with it (m122 Part-B rule).
-- 4115 Castle Downs 10-12 (N) duplicates 7090, retired by m118 as DEAD.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE Id = 7090 AND RetiredAtUtc IS NULL)
    THROW 50128, 'm124: twin 7090 is unexpectedly ACTIVE — re-classify 4115 as a live merge instead.', 1;

UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: duplicate of MPI 7090, whose honing verdict is DEAD',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m WHERE m.Id = 4115 AND m.RetiredAtUtc IS NULL;
PRINT 'Dead-twin duplicate retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part C: active MPIs whose honing verdict is DEAD retire (m118 pattern).
-- Condition-selected; children retire per the lifecycle rule (m115).
-- ---------------------------------------------------------------------------
DECLARE @Dead TABLE (Id bigint PRIMARY KEY);
INSERT INTO @Dead (Id)
SELECT m.Id
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'), JSON_VALUE(e.ResultJson,'$.verdict')) = N'DEAD';
DECLARE @deadCount int;
SELECT @deadCount = COUNT(*) FROM @Dead;
PRINT 'DEAD-verdict actives selected: ' + CAST(@deadCount AS varchar(10));

UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: honing verdict DEAD — fully awarded or unviable per Sonnet honing (batch-003)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m JOIN @Dead d ON d.Id = m.Id;
PRINT 'DEAD actives retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Children retire with their parents (Part B + Part C victims).
DECLARE @Gone TABLE (Id bigint PRIMARY KEY);
INSERT INTO @Gone (Id) SELECT Id FROM @Dead;
INSERT INTO @Gone (Id) VALUES (4115);

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: parent MPI retired (DEAD verdict or duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProjectAction retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: parent MPI retired (DEAD verdict or duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProjectSignal retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: parent MPI retired (DEAD verdict or duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProjectRisk retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: parent MPI retired (DEAD verdict or duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProjectKeyPerson retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm124: parent MPI retired (DEAD verdict or duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProject retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm124 committed.';
GO

-- Verify: no active DUPLICATE or DEAD honing verdicts remain; no active intel
-- on retired rows.
SELECT COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'), JSON_VALUE(e.ResultJson,'$.verdict')) AS Verdict, COUNT(*) AS Actives
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'), JSON_VALUE(e.ResultJson,'$.verdict')) IN (N'DEAD', N'DUPLICATE')
GROUP BY COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'), JSON_VALUE(e.ResultJson,'$.verdict'));
SELECT COUNT(*) AS ActiveIntelOnRetired
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
GO
