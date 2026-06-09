SET XACT_ABORT ON;
GO

-- Post-Secondary first-pass honing data corrections.
-- Surfaced by 2026-06-08 bc-ab-postsecondary drain.
--
-- Three discrete corrections (per Sonnet flags + Ian audit):
--   1. MPI 6615 — Province bug: BC -> AB + City -> Calgary
--      (University of Calgary - Multidisciplinary Science project
--      is in Calgary, not BC).
--   2. MPIs 3833 + 6977 — duplicate "NAIT Advanced Skills Centre"
--      rows. Consolidate to survivor 6977 (has Stage='CapitalPlan').
--      Sonnet research surfaced $560M project cost.
--   3. MPI 4428 — UVic 510-bed Student Housing. Per BC Budget 2026,
--      construction delayed to 2034. Add to KorSeatOpening as
--      schedule signal.
--
-- Note: MPI 502 "MacEwan Student Centre Expansion / U of Calgary"
-- was a false-positive in Sonnet's flags. MacEwan Hall (Student
-- Centre) at U of Calgary is named after John W.G. MacEwan,
-- former Lt Governor of Alberta — distinct from MacEwan University
-- in Edmonton. Data is correct; no change applied.

BEGIN TRAN;

-- ==========================================================
-- 1. MPI 6615 — Province + City fix
-- ==========================================================
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB',
    MunicipalityName = N'Calgary',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 6615 AND RetiredAtUtc IS NULL;
PRINT 'MPI 6615 Province+City corrected: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- 2. NAIT Advanced Skills Centre duplicate consolidation
--    Survivor: 6977 (Stage='CapitalPlan')
--    Retire: 3833
-- ==========================================================
DECLARE @naitSurvivor BIGINT = 6977;
DECLARE @naitDupe BIGINT = 3833;

UPDATE opportunities.IntelProjectAction SET MajorProjectsInventoryId = @naitSurvivor
WHERE MajorProjectsInventoryId = @naitDupe;
PRINT 'IntelProjectAction FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectSignal SET MajorProjectsInventoryId = @naitSurvivor
WHERE MajorProjectsInventoryId = @naitDupe;
PRINT 'IntelProjectSignal FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectRisk SET MajorProjectsInventoryId = @naitSurvivor
WHERE MajorProjectsInventoryId = @naitDupe;
PRINT 'IntelProjectRisk FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.IntelProjectKeyPerson SET MajorProjectsInventoryId = @naitSurvivor
WHERE MajorProjectsInventoryId = @naitDupe;
PRINT 'IntelProjectKeyPerson FKs repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of NAIT Advanced Skills Centre survivor 6977 (m113)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @naitDupe AND RetiredAtUtc IS NULL;
PRINT 'MPI 3833 retired as NAIT dup';

UPDATE opportunities.MajorProjectsInventory
SET EstimatedCostCad = 560000000,
    EstimatedCostText = N'$560M',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @naitSurvivor AND RetiredAtUtc IS NULL
  AND EstimatedCostCad IS NULL;
PRINT 'MPI 6977 cost set to $560M: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- 3. MPI 4428 UVic Student Housing — 2034 delay note
-- ==========================================================
UPDATE opportunities.MajorProjectsInventory
SET KorSeatOpening = N'[BD 2026-06-08 PostSec honing] DELAYED to 2034 per BC Budget 2026. 510-bed Student Housing. UVic capital plan signal — monitor only, no near-term action.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 4428 AND RetiredAtUtc IS NULL;
PRINT 'MPI 4428 UVic delay note added: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 113 complete.';

COMMIT TRAN;
GO

-- Verify
SELECT Id, ProjectName, Province, MunicipalityName,
    COALESCE(EstimatedCostText, CAST(EstimatedCostCad AS NVARCHAR(64))) AS Cost,
    ProjectStage, LEFT(COALESCE(KorSeatOpening, ''), 80) AS KorNote,
    RetiredReason
FROM opportunities.MajorProjectsInventory
WHERE Id IN (6615, 3833, 6977, 4428)
ORDER BY Id;
GO
