-- 115_RepairStrandedIntelOnRetiredMpis.sql
-- BD-Audit-2026-06-09 M1/M2: 331 active IntelProject* rows + 49 IntelProject
-- rows + ~50 enrichment rows sat on retired MPIs.
--   * 278 were stranded by merge migrations that repointed only 4 of the 8
--     referencing tables (and the R95-extra m74-family generic merge, which
--     repointed none of them).
--   * 53 were written AFTER retirement by drain ingest (now blocked at the
--     source — BdQueueDrainIngest refuses retired MPIs).
-- Policy (per audit architectural rule): children REPOINT to the survivor on
-- a merge retire; children RETIRE with the parent on a lifecycle retire.
--
-- Victim -> survivor mapping evidence (verified 2026-06-09 against prod):
--   * R95-extra "merged into survivor MPI row (dup ProjectName+Province)"
--     victims map to the single ACTIVE row with identical ProjectName +
--     Province (each verified to have exactly one such row).
--   * "Dup of <X> survivor NNNN" reasons carry the survivor id explicitly
--     (UHNBC 7010 / Vernon Jubilee 7031 / Newton 4589 / Britannia 6483 /
--     Comox Third Ice 5180).
--   * All mapped survivors verified ACTIVE; all victims verified RETIRED
--     (also asserted in-script below).
SET XACT_ABORT ON;
-- Required for UPDATEs against tables carrying filtered indexes
-- (UQ_IntelProject_ProjectProvider_Live); sqlcmd defaults it OFF.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @Map TABLE (VictimId bigint PRIMARY KEY, SurvivorId bigint NOT NULL);
INSERT INTO @Map (VictimId, SurvivorId) VALUES
    -- R95-extra m74-family merge victims -> same-name+province active survivor
    (7014, 4286), (5093, 4286),   -- Port Hardy Hospital Upgrades
    (7004, 6873),                 -- New St. Paul's Hospital CSRC
    (5145, 4569),                 -- Hazel Trembath Elementary Rebuild
    (6823, 5215),                 -- Port Coquitlam Multi-Use Sport Covered Facility
    (5151, 4437),                 -- Fleetwood Park Secondary School Addition
    (5236, 2993),                 -- Clayton Heights Secondary Addition
    (5146, 4568),                 -- Ecole Montgomery Middle Seismic Replacement
    (5149, 4552),                 -- Forsyth Road Elementary Addition
    (5155, 4560),                 -- New Westminster Downtown Elementary
    (5174, 4945),                 -- Shoreline Middle Seismic Upgrade
    (5205, 4057),                 -- R.E. Mountain Secondary Addition
    (5175, 4949),                 -- Sooke Elementary Seismic Upgrade
    (5116, 4541),                 -- Len Wood Middle Gymnasium Addition
    (5088, 4574),                 -- East Courtenay Fire Hall
    (6627, 4209),                 -- Foothills Multisport Fieldhouse
    (6630, 3838),                 -- UCalgary Multidisciplinary Science Hub
    (6984, 3912),                 -- New Stand-Alone Stollery Children's Hospital
    -- UHNBC duplicate family -> m110 survivor 7010
    (4414, 7010), (2458, 7010), (3062, 7010), (3892, 7010),
    (4782, 7010), (6594, 7010), (6512, 7010),
    -- Vernon Jubilee Psychiatric family -> m111 survivor 7031
    (3133, 7031), (4320, 7031), (4410, 7031), (4489, 7031),
    -- Newton Community Centre family -> m114 survivor 4589
    (4221, 4589), (5288, 4589), (6782, 4589),
    -- Britannia Community Centre -> m114 survivor 6483
    (4196, 6483),
    -- Comox Valley Arena 3 -> Third Ice Surface per defense honing 2026-06-08
    (5287, 5180);

-- Sanity assertions: survivors active + existing, victims retired.
IF EXISTS (SELECT 1 FROM @Map mp LEFT JOIN opportunities.MajorProjectsInventory s ON s.Id = mp.SurvivorId
           WHERE s.Id IS NULL OR s.RetiredAtUtc IS NOT NULL)
    THROW 50115, 'm115: a mapped survivor is missing or retired — mapping is stale, abort.', 1;
IF EXISTS (SELECT 1 FROM @Map mp JOIN opportunities.MajorProjectsInventory v ON v.Id = mp.VictimId
           WHERE v.RetiredAtUtc IS NULL)
    THROW 50116, 'm115: a mapped victim is not retired — mapping is stale, abort.', 1;

-- 1. Repoint the four NaturalKey-unique Intel tables. NaturalKey is globally
--    unique, so changing MajorProjectsInventoryId cannot collide.
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

-- 2. IntelProject: UQ_IntelProject_ProjectProvider_Live is filtered unique on
--    (MajorProjectsInventoryId, SourceProviderName) WHERE RetiredAtUtc IS NULL.
--    Repoint rows the survivor lacks; retire-in-place the live remainder
--    (superseded by the survivor's own row; filtered index ignores retired).
--    Two victims can map to the SAME survivor (e.g. 7014+5093 -> 4286), so a
--    plain NOT EXISTS passes both candidates and they collide with each
--    other: rank candidates per (survivor, provider) and move only the
--    freshest one.
WITH IpCandidates AS (
    SELECT x.Id,
           mp.SurvivorId,
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
FROM opportunities.IntelProject x
JOIN IpCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'IntelProject repointed (no provider collision): ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm115: superseded — survivor MPI already has a live row from this provider',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProject retired (provider collision on survivor): ' + CAST(@@ROWCOUNT AS varchar(10));

-- 3. MajorProjectEnrichment: UQ on (MajorProjectsInventoryId, ProviderName) is
--    NOT filtered — repoint only where the survivor lacks the provider; the
--    remainder stays archived on the victim (visible via the victim id).
WITH EnrichCandidates AS (
    SELECT x.Id,
           mp.SurvivorId,
           mp.VictimId,
           ROW_NUMBER() OVER (PARTITION BY mp.SurvivorId, x.ProviderName
                              ORDER BY x.LastRefreshAtUtc DESC, x.Id DESC) AS rn
    FROM opportunities.MajorProjectEnrichment x
    JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment s
                      WHERE s.MajorProjectsInventoryId = mp.SurvivorId
                        AND s.ProviderName = x.ProviderName)
)
UPDATE x SET MajorProjectsInventoryId = c.SurvivorId, UpdatedAtUtc = sysdatetimeoffset(),
             Notes = COALESCE(x.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m115: repointed from merge-victim MPI ' + CAST(c.VictimId AS nvarchar(12)) + N']'
FROM opportunities.MajorProjectEnrichment x
JOIN EnrichCandidates c ON c.Id = x.Id AND c.rn = 1;
PRINT 'MajorProjectEnrichment repointed (no provider collision): ' + CAST(@@ROWCOUNT AS varchar(10));

SELECT mp.VictimId, x.ProviderName
INTO #EnrichLeftBehind
FROM opportunities.MajorProjectEnrichment x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId;
PRINT 'MajorProjectEnrichment left archived on victims (provider collision): ' + CAST(@@ROWCOUNT AS varchar(10));
DROP TABLE #EnrichLeftBehind;

-- 4. CrmEngagementProjectLink (0 rows today; covered so the template is complete).
UPDATE x SET MajorProjectsInventoryId = mp.SurvivorId
FROM opportunities.CrmEngagementProjectLink x JOIN @Map mp ON mp.VictimId = x.MajorProjectsInventoryId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink s
                  WHERE s.EngagementId = x.EngagementId AND s.MajorProjectsInventoryId = mp.SurvivorId);
PRINT 'CrmEngagementProjectLink repointed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 5. Lifecycle-retired MPIs (everything still attached to a retired MPI after
--    the repoints above): the parent was retired for stage/scope reasons, so
--    the children retire with it instead of floating as active orphans.
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = LEFT(N'm115: parent MPI retired (' + ISNULL(m.RetiredReason, N'no reason') + N')', 200),
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x
JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectAction retired with lifecycle-retired parent: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = LEFT(N'm115: parent MPI retired (' + ISNULL(m.RetiredReason, N'no reason') + N')', 200),
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x
JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectSignal retired with lifecycle-retired parent: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = LEFT(N'm115: parent MPI retired (' + ISNULL(m.RetiredReason, N'no reason') + N')', 200),
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x
JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectRisk retired with lifecycle-retired parent: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = LEFT(N'm115: parent MPI retired (' + ISNULL(m.RetiredReason, N'no reason') + N')', 200),
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x
JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectKeyPerson retired with lifecycle-retired parent: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = LEFT(N'm115: parent MPI retired (' + ISNULL(m.RetiredReason, N'no reason') + N')', 200),
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x
JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
PRINT 'IntelProject retired with lifecycle-retired parent: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm115 committed.';
GO

-- Verify: no ACTIVE project intel may remain on a retired MPI.
SELECT 'IntelProjectAction' AS Tbl, COUNT(*) AS ActiveOnRetired
FROM opportunities.IntelProjectAction x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL
UNION ALL SELECT 'IntelProjectSignal', COUNT(*)
FROM opportunities.IntelProjectSignal x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL
UNION ALL SELECT 'IntelProjectRisk', COUNT(*)
FROM opportunities.IntelProjectRisk x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL
UNION ALL SELECT 'IntelProjectKeyPerson', COUNT(*)
FROM opportunities.IntelProjectKeyPerson x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL
UNION ALL SELECT 'IntelProject', COUNT(*)
FROM opportunities.IntelProject x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL AND x.RetiredAtUtc IS NULL;
GO
