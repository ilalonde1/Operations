SET XACT_ABORT ON;
GO

-- Re-point wrong FK resolutions in MajorProjectsInventory across all four
-- role columns. Pattern: MPI.<Role>Name says one entity (e.g., "Interior
-- Health Authority") but MPI.<Role>CanonicalOrgId points to an entirely
-- unrelated canonical (e.g., "Standards Council of Canada"). Likely the
-- original scraper had a column-shift or the resolver picked the wrong
-- match.
--
-- For each suspect row: try exact CanonicalOrg.DisplayName match against
-- the role string. If a correct canonical exists, re-point the FK. If
-- nothing matches, NULL the FK (leave the role string so the proponent
-- drain can pick it up).

BEGIN TRAN;

-- ========== ProponentCanonicalOrgId ==========

DECLARE @propRepointed int, @propNulled int;

UPDATE m
SET m.ProponentCanonicalOrgId = correct.Id
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.ProponentCanonicalOrgId
INNER JOIN opportunities.CanonicalOrg correct ON correct.DisplayName = LTRIM(RTRIM(m.ProponentName)) AND correct.Id <> current_canon.Id
WHERE m.RetiredAtUtc IS NULL
  AND m.ProponentName IS NOT NULL AND LEN(m.ProponentName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.ProponentName, 6) + N'%'
  AND m.ProponentName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @propRepointed = @@ROWCOUNT;
PRINT 'Proponent FKs re-pointed to correct canonical: ' + CONVERT(varchar(20), @propRepointed);

UPDATE m
SET m.ProponentCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.ProponentCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.ProponentName IS NOT NULL AND LEN(m.ProponentName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.ProponentName, 6) + N'%'
  AND m.ProponentName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @propNulled = @@ROWCOUNT;
PRINT 'Proponent FKs nulled (no match found): ' + CONVERT(varchar(20), @propNulled);

-- ========== ArchitectCanonicalOrgId ==========

DECLARE @archRepointed int, @archNulled int;

UPDATE m
SET m.ArchitectCanonicalOrgId = correct.Id
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.ArchitectCanonicalOrgId
INNER JOIN opportunities.CanonicalOrg correct ON correct.DisplayName = LTRIM(RTRIM(m.ArchitectName)) AND correct.Id <> current_canon.Id
WHERE m.RetiredAtUtc IS NULL
  AND m.ArchitectName IS NOT NULL AND LEN(m.ArchitectName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.ArchitectName, 6) + N'%'
  AND m.ArchitectName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @archRepointed = @@ROWCOUNT;
PRINT 'Architect FKs re-pointed: ' + CONVERT(varchar(20), @archRepointed);

UPDATE m
SET m.ArchitectCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.ArchitectCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.ArchitectName IS NOT NULL AND LEN(m.ArchitectName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.ArchitectName, 6) + N'%'
  AND m.ArchitectName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @archNulled = @@ROWCOUNT;
PRINT 'Architect FKs nulled: ' + CONVERT(varchar(20), @archNulled);

-- ========== StructuralEngineerCanonicalOrgId ==========

DECLARE @seRepointed int, @seNulled int;

UPDATE m
SET m.StructuralEngineerCanonicalOrgId = correct.Id
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.StructuralEngineerCanonicalOrgId
INNER JOIN opportunities.CanonicalOrg correct ON correct.DisplayName = LTRIM(RTRIM(m.StructuralEngineerName)) AND correct.Id <> current_canon.Id
WHERE m.RetiredAtUtc IS NULL
  AND m.StructuralEngineerName IS NOT NULL AND LEN(m.StructuralEngineerName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.StructuralEngineerName, 6) + N'%'
  AND m.StructuralEngineerName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @seRepointed = @@ROWCOUNT;
PRINT 'StructEng FKs re-pointed: ' + CONVERT(varchar(20), @seRepointed);

UPDATE m
SET m.StructuralEngineerCanonicalOrgId = NULL
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg current_canon ON current_canon.Id = m.StructuralEngineerCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.StructuralEngineerName IS NOT NULL AND LEN(m.StructuralEngineerName) >= 4
  AND current_canon.DisplayName NOT LIKE N'%' + LEFT(m.StructuralEngineerName, 6) + N'%'
  AND m.StructuralEngineerName NOT LIKE N'%' + LEFT(current_canon.DisplayName, 6) + N'%';
SET @seNulled = @@ROWCOUNT;
PRINT 'StructEng FKs nulled: ' + CONVERT(varchar(20), @seNulled);

PRINT 'Migration 78 wrong-FK repair complete.';

COMMIT TRAN;
GO
