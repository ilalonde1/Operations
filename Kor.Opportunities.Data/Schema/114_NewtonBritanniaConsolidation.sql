SET XACT_ABORT ON;
GO

-- Newton Community Centre + Britannia Community Centre consolidations.
-- Identified via 2026-06-09 bc-ab-recreational-honing batch-001.
--
-- Newton Community Centre (Surrey, $310.6M — largest civic project in
-- City of Surrey history):
--   Survivor: 4589 "Newton Community Centre (New Build)"
--             (richest Intel rows: 5 actions / 6 signals / 5 risks /
--             5 people; ProjectStage='CapitalPlan')
--   Retire: 4221 "Newton Community Centre (incl. Newton Library)"
--           5288 "Surrey Newton Community Centre and Library"
--           6782 "Newton Community Centre"
--
-- Britannia Community Centre Redevelopment (Vancouver, $300M):
--   Survivor: 6483 "Britannia Community Centre Redevelopment"
--             (4 actions / 5 signals / 4 risks;
--             ProjectStage='CapitalPlan')
--   Retire: 4196 "Britannia aquatic + community centre + ice rink (renewal)"
--
-- Pattern: identical to m110 (UHNBC 9 dupes -> 1) and m111 (Vernon
-- Jubilee Psychiatric 5 dupes -> 1).

BEGIN TRAN;

-- ==========================================================
-- 1. Newton Community Centre consolidation
-- ==========================================================
DECLARE @newtonSurvivor BIGINT = 4589;
DECLARE @newtonDupes TABLE (Id BIGINT);
INSERT INTO @newtonDupes VALUES (4221),(5288),(6782);

UPDATE opportunities.IntelProjectAction SET MajorProjectsInventoryId = @newtonSurvivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @newtonDupes);
PRINT 'Newton IntelProjectAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectSignal SET MajorProjectsInventoryId = @newtonSurvivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @newtonDupes);
PRINT 'Newton IntelProjectSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectRisk SET MajorProjectsInventoryId = @newtonSurvivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @newtonDupes);
PRINT 'Newton IntelProjectRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectKeyPerson SET MajorProjectsInventoryId = @newtonSurvivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @newtonDupes);
PRINT 'Newton IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of Newton CC survivor 4589 (m114 rec-honing)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id IN (SELECT Id FROM @newtonDupes) AND RetiredAtUtc IS NULL;
PRINT 'Newton dupes retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- 2. Britannia Community Centre consolidation
-- ==========================================================
DECLARE @britSurvivor BIGINT = 6483;
DECLARE @britDupe BIGINT = 4196;

UPDATE opportunities.IntelProjectAction SET MajorProjectsInventoryId = @britSurvivor
WHERE MajorProjectsInventoryId = @britDupe;
PRINT 'Britannia IntelProjectAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectSignal SET MajorProjectsInventoryId = @britSurvivor
WHERE MajorProjectsInventoryId = @britDupe;
PRINT 'Britannia IntelProjectSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectRisk SET MajorProjectsInventoryId = @britSurvivor
WHERE MajorProjectsInventoryId = @britDupe;
PRINT 'Britannia IntelProjectRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectKeyPerson SET MajorProjectsInventoryId = @britSurvivor
WHERE MajorProjectsInventoryId = @britDupe;
PRINT 'Britannia IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of Britannia CC survivor 6483 (m114 rec-honing)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @britDupe AND RetiredAtUtc IS NULL;
PRINT 'Britannia dupe retired';

PRINT 'Migration 114 complete.';

COMMIT TRAN;
GO

SELECT Id, ProjectName, Province, MunicipalityName, ProjectStage, RetiredAtUtc, RetiredReason
FROM opportunities.MajorProjectsInventory
WHERE Id IN (4221, 4589, 5288, 6782, 4196, 6483)
ORDER BY Id;
GO
