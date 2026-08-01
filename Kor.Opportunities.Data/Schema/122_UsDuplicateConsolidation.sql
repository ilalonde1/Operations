-- 122_UsDuplicateConsolidation.sql
-- US honing follow-through (2026-06-10): the CA honing pass flagged 10
-- DUPLICATE verdicts, each naming its canonical twin ("See ID X").
-- (121 is reserved for the WPF build's BdReportAuditLog per the UI plan —
-- the gap is intentional.)
--
-- Validated against prod before writing:
--   * 2 twins are ACTIVE -> standard merge (m117 template):
--       3501 -> 3502 (City of Hope Orange County Cancer Center)
--       3526 -> 3525 (Dodger Stadium renovation)
--   * 8 twins were retired by m118 as honing-verdict DEAD (SoFi, Intuit
--     Dome, LACMA BPC, One Beverly Hills, Oschin Center, The Century,
--     WBD Second Century, Delta Sky Way) -> the duplicate rows die with
--     their twins: retire + children retire (lifecycle rule, m115).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

-- ---------------------------------------------------------------------------
-- Part A: live merges (victim -> ACTIVE survivor), m117 template.
-- ---------------------------------------------------------------------------
DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES (3501, 3502), (3526, 3525);

IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50122, 'm122: a mapped survivor is missing or retired — abort.', 1;

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
             RetiredReason = N'm122: superseded — survivor already has a live row from this provider',
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
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m122: repointed from duplicate MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
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
             RetiredReason = N'm122: duplicate of survivor MPI ' + CAST(mp.SurvivorId AS nvarchar(12)) + N' (US honing DUPLICATE verdict)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory v JOIN @Map mp ON mp.VictimId = v.Id;
PRINT 'Live-merge victims retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- ---------------------------------------------------------------------------
-- Part B: duplicates of DEAD twins — die with the twin (no repoint target
-- in the active set; children retire per the lifecycle rule).
-- ---------------------------------------------------------------------------
DECLARE @DeadDups TABLE (Id bigint PRIMARY KEY, TwinId bigint NOT NULL);
INSERT INTO @DeadDups (Id, TwinId) VALUES
    (3521, 3522), (3570, 3569), (3583, 3582), (3618, 3617),
    (3647, 3646), (3655, 3653), (3673, 3674), (3723, 3722);

IF EXISTS (SELECT 1 FROM @DeadDups d JOIN opportunities.MajorProjectsInventory t ON t.Id = d.TwinId
           WHERE t.RetiredAtUtc IS NULL)
    THROW 50123, 'm122: a Part-B twin is unexpectedly ACTIVE — re-classify as a live merge instead.', 1;

UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: duplicate of MPI ' + CAST(d.TwinId AS nvarchar(12)) + N', whose honing verdict is DEAD',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m JOIN @DeadDups d ON d.Id = m.Id
WHERE m.RetiredAtUtc IS NULL;
PRINT 'Dead-twin duplicates retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: parent MPI retired (duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN @DeadDups d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Part-B IntelProjectAction retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: parent MPI retired (duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x JOIN @DeadDups d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Part-B IntelProjectSignal retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: parent MPI retired (duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x JOIN @DeadDups d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Part-B IntelProjectRisk retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: parent MPI retired (duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN @DeadDups d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Part-B IntelProjectKeyPerson retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm122: parent MPI retired (duplicate of DEAD twin)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @DeadDups d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Part-B IntelProject retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm122 committed.';
GO

-- Verify: no active US DUPLICATE verdicts remain; no active intel on retired.
SELECT COUNT(*) AS UsDuplicateActives
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL AND m.Province IN ('CA','OR','WA')
  AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'), JSON_VALUE(e.ResultJson,'$.verdict')) = N'DUPLICATE';
SELECT COUNT(*) AS ActiveIntelOnRetired
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
GO
