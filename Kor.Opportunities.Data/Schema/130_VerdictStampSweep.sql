-- 130_VerdictStampSweep.sql
-- Follow-through on the 2026-06-11 verdict-stamp campaign (176 verdicts) +
-- the parked m126-candidate recreation duplicates. Validated against prod:
--   * Part A — 14 live merges, every survivor verified ACTIVE; NaturalKey
--     overlap = 0 on all four IntelProject* tables. Includes double-merges
--     (5286+6811 -> 4574; 6493+6920 -> 6878).
--   * Part B — 4 recreation rows whose honing TEXT says DEAD (team locked)
--     while the verdict field says DUPLICATE, twins already m118-retired:
--     retire as DEAD (m122 die-with-twin rule generalized).
--   * Part C — all active DEAD-verdict MPIs retire by condition (m118/m124
--     pattern); 31 at validation time.
--   * Part D — 3 junk-pointer hones (4397/4585 self-referencing, 6483
--     pointing at its own m114-retired twin): backdate LastRefreshAtUtc 45
--     days so the honing selector re-picks them naturally. No fabricated
--     verdicts.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- Part A: live merges -------------------------------------------------------
DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES
    (3355, 6797), (4213, 6635), (5159, 6825), (5162, 6787), (5163, 6818),
    (5286, 4574), (6485, 6890), (6493, 6878), (6582, 7074), (6811, 4574),
    (6877, 6589), (6920, 6878), (6921, 6880), (6953, 7110);

IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50130, 'm130: a mapped survivor is missing or retired — abort.', 1;
IF EXISTS (SELECT 1 FROM @Map mp JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId
           WHERE v.RetiredAtUtc IS NOT NULL)
    THROW 50131, 'm130: a mapped victim is already retired — re-validate before re-running.', 1;

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
             RetiredReason = N'm130: superseded — survivor already has a live row from this provider',
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
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m130: repointed from duplicate MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
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
             RetiredReason = N'm130: duplicate of survivor MPI ' + CAST(mp.SurvivorId AS nvarchar(12)) + N' (verdict-stamp DUPLICATE)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory v JOIN @Map mp ON mp.VictimId = v.Id;
PRINT 'Live-merge victims retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Part B: DEAD-text recreation duplicates of dead twins ----------------------
UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm130: honing text DEAD (team locked) — duplicate of an m118-retired DEAD twin',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
WHERE m.Id IN (4305, 4589, 4652, 5284) AND m.RetiredAtUtc IS NULL;
PRINT 'Dead-twin recreation rows retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Part C: active DEAD verdicts retire by condition ---------------------------
DECLARE @Dead TABLE (Id bigint PRIMARY KEY);
INSERT INTO @Dead (Id)
SELECT m.Id
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),'')) = N'DEAD';
DECLARE @deadCount int; SELECT @deadCount = COUNT(*) FROM @Dead;
PRINT 'DEAD-verdict actives selected: ' + CAST(@deadCount AS varchar(10));

UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm130: honing verdict DEAD — fully awarded or unviable (verdict-stamp sweep)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m JOIN @Dead d ON d.Id = m.Id;
PRINT 'DEAD actives retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Children retire with Part B + C parents.
DECLARE @Gone TABLE (Id bigint PRIMARY KEY);
INSERT INTO @Gone (Id) SELECT Id FROM @Dead;
INSERT INTO @Gone (Id) SELECT v FROM (VALUES (4305), (4589), (4652), (5284)) AS t(v)
WHERE NOT EXISTS (SELECT 1 FROM @Gone g WHERE g.Id = t.v);

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'm130: parent MPI retired', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child actions retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'm130: parent MPI retired', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child signals retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'm130: parent MPI retired', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child risks retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'm130: parent MPI retired', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child key people retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'm130: parent MPI retired', UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @Gone g ON g.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL;
PRINT 'Child IntelProject retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Part D: junk-pointer hones — backdate so the selector re-picks them --------
UPDATE e SET LastRefreshAtUtc = DATEADD(DAY, -45, sysdatetimeoffset()),
             Notes = COALESCE(e.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m130: junk twin pointer (self-ref or circular) — backdated to force natural re-hone]',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectEnrichment e
WHERE e.ProviderName = N'ProjectBriefHoning' AND e.MajorProjectsInventoryId IN (4397, 4585, 6483);
PRINT 'Junk-pointer hones backdated for re-hone: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm130 committed.';
GO

-- Verify: remaining active DEAD/DUPLICATE verdicts (expect: DUPLICATE = 3,
-- the backdated re-hone trio; DEAD = 0) and zero active intel on retired.
SELECT COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),'')) AS Verdict, COUNT(*) AS Actives
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),'')) IN (N'DEAD', N'DUPLICATE')
GROUP BY COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),''));
SELECT COUNT(*) AS ActiveIntelOnRetired
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
GO
