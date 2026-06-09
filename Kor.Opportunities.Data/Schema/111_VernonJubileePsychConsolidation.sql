SET XACT_ABORT ON;
GO

-- Vernon Jubilee Hospital New Inpatient Psychiatric Unit consolidation.
-- Identified via 2026-06-08 bc-ab-hospitals-honing pt2 — 5 duplicate
-- MPI rows for the same Interior Health project. Same pattern as UHNBC
-- m110.
--
-- Survivor: Id 7031 "Vernon Jubilee Hospital — New Inpatient Psychiatric
-- Unit" (richest existing enrichment, ~$110-140M, 44 single-occupancy
-- rooms, Sylvia Weir / Lorne Sisley contacts already in Intel rows from
-- earlier manual research).
--
-- Dupes to merge: 3133, 4320, 4410, 4489.

BEGIN TRAN;

DECLARE @survivor BIGINT = 7031;
DECLARE @dupes TABLE (Id BIGINT);
INSERT INTO @dupes VALUES (3133),(4320),(4410),(4489);

-- Repoint all Intel* FKs
UPDATE opportunities.IntelProjectAction SET MajorProjectsInventoryId = @survivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @dupes);
PRINT 'IntelProjectAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectSignal SET MajorProjectsInventoryId = @survivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @dupes);
PRINT 'IntelProjectSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectRisk SET MajorProjectsInventoryId = @survivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @dupes);
PRINT 'IntelProjectRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectKeyPerson SET MajorProjectsInventoryId = @survivor
WHERE MajorProjectsInventoryId IN (SELECT Id FROM @dupes);
PRINT 'IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Soft-retire the 4 dupes
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of Vernon Jubilee Psych survivor 7031 (m111 honing-pt2)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id IN (SELECT Id FROM @dupes) AND RetiredAtUtc IS NULL;
PRINT 'Vernon Jubilee dupes retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 111 complete.';

COMMIT TRAN;
GO

SELECT Id, ProjectName, Province, MunicipalityName, RetiredAtUtc, RetiredReason
FROM opportunities.MajorProjectsInventory
WHERE Id IN (7031, 3133, 4320, 4410, 4489)
ORDER BY Id;
GO
