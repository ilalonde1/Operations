SET XACT_ABORT ON;
GO

-- UHNBC Acute Care Tower consolidation + EllisDon/DIALOG linkage.
-- Triggered by 2026-06-08 EllisDon deep-dive honing which surfaced
-- UHNBC ($1.579B, Prince George, construction fall 2026) as a major
-- KOR pursuit opportunity NOT previously enriched in MPI. Pre-insert
-- audit found 9 duplicate MPI rows for UHNBC — consolidate instead
-- of insert.
--
-- Survivor: Id 7010 "University Hospital of Northern BC (UHNBC)
-- Acute Care Tower" (cleanest naming, most recent).
-- Dupes to merge: 2458, 2964, 3062, 3892, 4414, 4782, 6512, 6594.

BEGIN TRAN;

DECLARE @survivor BIGINT = 7010;
DECLARE @dupes TABLE (Id BIGINT);
INSERT INTO @dupes VALUES (2458),(2964),(3062),(3892),(4414),(4782),(6512),(6594);

-- Repoint all Intel* FKs that reference dupe MPI Ids
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

-- Note: dupe MajorProjectEnrichment rows are NOT migrated here.
-- Intel* signals/actions/risks/keypeople already point to survivor via the
-- repoint above. Dupe enrichments stay orphaned against soft-retired MPIs
-- (harmless — survivor's own enrichments come from upcoming honing passes).
-- Direct migration is blocked by FK constraints from Intel* SourceEnrichmentId.
PRINT 'Enrichment FK migration skipped (Intel data already on survivor)';

-- Soft-retire the 8 dupes
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of UHNBC survivor 7010 (m110 EllisDon-honing-driven consolidation)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id IN (SELECT Id FROM @dupes) AND RetiredAtUtc IS NULL;
PRINT 'UHNBC dupes retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Enrich survivor with EllisDon + DIALOG + Northern Health + $1.579B
DECLARE @ellisdon BIGINT = 22257;       -- EllisDon Corporation
DECLARE @dialog BIGINT = 69865;         -- DIALOG BC Architecture Engineering Interior Design Planning
DECLARE @northernHealth BIGINT = 54976; -- Northern Health

UPDATE opportunities.MajorProjectsInventory
SET ProponentName = N'Northern Health (with Province of BC funding)',
    ProponentCanonicalOrgId = @northernHealth,
    ArchitectName = N'DIALOG (BC office) — preferred proponent with EllisDon',
    ArchitectCanonicalOrgId = @dialog,
    GeneralContractorName = N'EllisDon Corporation — preferred proponent with DIALOG',
    GeneralContractorCanonicalOrgId = @ellisdon,
    EstimatedCostCad = 1579000000,
    EstimatedCostText = N'$1.579B',
    MunicipalityName = COALESCE(MunicipalityName, N'Prince George'),
    Province = N'BC',
    ProjectStage = N'Planned',
    Sector = N'Healthcare / Hospital',
    KorSeatOpening = N'[BD 2026-06-08 EllisDon honing] PURSUE. EllisDon+DIALOG preferred proponent. Construction fall 2026. DIALOG architect-led=struct sub slot may be open. Action: Daniel Murphy dmurphy@ellisdon.com or Candace MacDonald cmacdonald@ellisdon.com. $1.579B BC healthcare opp.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @survivor;
PRINT 'UHNBC survivor 7010 enriched with EllisDon + DIALOG + Northern Health + $1.579B';

PRINT 'Migration 110 complete.';

COMMIT TRAN;
GO

-- Verify
SELECT 'UHNBC survivor 7010' AS K, Id, ProjectName, Province, MunicipalityName,
       ProponentName, ArchitectName, GeneralContractorName, EstimatedCostText
FROM opportunities.MajorProjectsInventory WHERE Id = 7010;

SELECT 'UHNBC dupes retired' AS K, Id, ProjectName, RetiredReason
FROM opportunities.MajorProjectsInventory
WHERE Id IN (2458,2964,3062,3892,4414,4782,6512,6594);
GO
