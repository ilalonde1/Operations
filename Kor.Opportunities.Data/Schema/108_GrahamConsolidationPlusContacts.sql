SET XACT_ABORT ON;
GO

-- Graham Design Builders LP consolidation + Alex Trifunov contact.
-- Triggered by 2026-06-08 BD intelligence brief work — Graham has
-- emerged as a $3.4B+ BC healthcare design-build pipeline GC and is
-- a strategic KOR relationship target. Pre-construction Manager
-- Alex Trifunov (Vancouver office) is the named entry point.
--
-- Steps:
-- 1. Pick canonical survivor = Id 8361 "GRAHAM Design Builders LP"
-- 2. Repoint FKs from dirty-suffixed dupes (72237, 72457) to survivor
-- 3. Retire dupes
-- 4. Update survivor with rich notes
-- 5. Mint Alex Trifunov as IntelPerson with affiliation to survivor

DECLARE @survivor BIGINT = 8361;
DECLARE @dupe1 BIGINT = 72237;  -- "Graham Design Builders LP (confirmed)"
DECLARE @dupe2 BIGINT = 72457;  -- "Graham Design Builders LP (Progressive Design-Build; Phase 2 under Construction Management contract)"

BEGIN TRAN;

-- Step 1: Repoint MPI proponent/architect/GC/StructEng FKs
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId = @survivor
WHERE ProponentCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'MPI Proponent FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = @survivor
WHERE ArchitectCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'MPI Architect FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory SET GeneralContractorCanonicalOrgId = @survivor
WHERE GeneralContractorCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'MPI GC FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory SET StructuralEngineerCanonicalOrgId = @survivor
WHERE StructuralEngineerCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'MPI StructEng FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Step 2: Repoint OpportunityAwards Awarding + AwardedTo
UPDATE opportunities.OpportunityAwards SET AwardingCanonicalOrgId = @survivor
WHERE AwardingCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'Awards Awarding FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.OpportunityAwards SET AwardedToCanonicalOrgId = @survivor
WHERE AwardedToCanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'Awards AwardedTo FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Step 3: Repoint Intel relational FKs
UPDATE opportunities.IntelSignal SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelAction SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelWork SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelWork FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelRisk SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelPersonAffiliation SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelPersonAffiliation FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectKeyPerson SET CanonicalOrgId = @survivor
WHERE CanonicalOrgId IN (@dupe1, @dupe2);
PRINT 'IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Step 4: Soft-retire the dupes
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Merged into Id 8361 GRAHAM Design Builders LP via m108 Graham consolidation'
WHERE Id IN (@dupe1, @dupe2) AND RetiredAtUtc IS NULL;
PRINT 'Dupes retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Step 5: Update survivor with BD context
UPDATE opportunities.CanonicalOrg
SET Kind = N'GC',
    Notes = COALESCE(Notes, N'') + CHAR(13) + CHAR(10) +
        N'[BD intel 2026-06-08] $3.4B+ active BC healthcare design-build pipeline: Richmond Hospital Yurkovich Pavilion ($1.96B, with HDR + Entuitive), Cariboo Memorial Hospital ($366M, with Stantec), Dawson Creek and District Hospital ($590M, with HDR), Stuart Lake Hospital. Calgary HQ, BC offices Vancouver/Coquitlam/Abbotsford/Kamloops/Kelowna. ReNew Canada Top 100 Infrastructure Projects 2026. KOR target: Alex Trifunov (Pre-construction Manager, Vancouver office) for next-pursuit positioning.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @survivor;
PRINT 'Survivor 8361 updated with BD context';

-- Step 6: Mint Alex Trifunov as IntelPerson with affiliation
DECLARE @personId BIGINT;
DECLARE @now DATETIMEOFFSET = sysdatetimeoffset();
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPerson WHERE DisplayName = N'Alex Trifunov' AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPerson (DisplayName, NormalizedName, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, Notes, Corroborations,
        SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (N'Alex Trifunov', N'alex trifunov', @now, @now, @now, @now,
        N'Pre-construction Manager, Graham Design Builders LP, Vancouver office. Strategic KOR BD target — pre-construction role is where structural sub-consultant conversations happen on Graham''s $3.4B+ BC healthcare pipeline. Added 2026-06-08 from web research (Glassdoor + Graham web profiles).',
        1, N'KorBdResearch-Manual-2026-06-08', N'Manual-2026-06-08', 0.80, N'alex-trifunov-graham-vancouver');
    SET @personId = SCOPE_IDENTITY();
    PRINT 'Alex Trifunov IntelPerson minted with Id: ' + CONVERT(varchar(20), @personId);
END
ELSE
BEGIN
    SELECT @personId = Id FROM opportunities.IntelPerson WHERE DisplayName = N'Alex Trifunov' AND RetiredAtUtc IS NULL;
    PRINT 'Alex Trifunov IntelPerson already exists with Id: ' + CONVERT(varchar(20), @personId);
END

-- Step 7: Add current affiliation
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation
               WHERE IntelPersonId = @personId AND CanonicalOrgId = @survivor AND IsCurrent = 1 AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPersonAffiliation (IntelPersonId, CanonicalOrgId, Title, IsCurrent, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
        SourceProviderName, SourceEnrichmentId, SourceConfidence)
    VALUES (@personId, @survivor, N'Pre-construction Manager (Vancouver office)', 1, @now, @now, @now, @now,
        N'KorBdResearch-Manual-2026-06-08', N'Manual-2026-06-08', 0.80);
    PRINT 'Alex Trifunov affiliation to Graham added';
END
ELSE
    PRINT 'Alex Trifunov affiliation already exists';

PRINT 'Migration 108 complete.';

COMMIT TRAN;
GO

-- Verify
SELECT 'Survivor Graham canonical' AS K, Id, DisplayName, Kind, RetiredAtUtc FROM opportunities.CanonicalOrg WHERE Id = 8361
UNION ALL
SELECT 'Retired dupes', Id, DisplayName, Kind, RetiredAtUtc FROM opportunities.CanonicalOrg WHERE Id IN (72237, 72457);

SELECT 'Alex Trifunov IntelPerson' AS K, Id AS PersonId, DisplayName, Notes FROM opportunities.IntelPerson WHERE DisplayName = N'Alex Trifunov' AND RetiredAtUtc IS NULL;
SELECT 'Affiliation' AS K, pa.IntelPersonId, pa.CanonicalOrgId, pa.Title, pa.IsCurrent FROM opportunities.IntelPersonAffiliation pa
INNER JOIN opportunities.IntelPerson p ON p.Id = pa.IntelPersonId WHERE p.DisplayName = N'Alex Trifunov' AND pa.RetiredAtUtc IS NULL;
GO
