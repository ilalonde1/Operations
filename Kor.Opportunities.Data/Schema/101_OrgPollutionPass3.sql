SET XACT_ABORT ON;
GO

-- Org pollution pass 3 — patterns m96/m98 missed.
-- All targets verified mpi=0 (zero MPI FK refs) before retiring so
-- nothing real gets lost. These are pure research-artifact rows:
-- parenthetical-scope descriptors, multi-entity "Ltd. and" JV
-- shorthand, and Stantec Architecture office-descriptor variants.

BEGIN TRAN;

DECLARE @retired int;

-- Parenthetical-scope descriptors that escaped m96/m98
-- ("(in-house)", "(consultant)", "(structural)", "(prime)",
-- "(architect of record)", "(design lead)")
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m101: parenthetical-scope research artifact'
WHERE RetiredAtUtc IS NULL
  AND (DisplayName LIKE N'%(in-house%' OR DisplayName LIKE N'%(consultant%'
       OR DisplayName LIKE N'%(structural%' OR DisplayName LIKE N'%(architect of record%'
       OR DisplayName LIKE N'%(prime%' OR DisplayName LIKE N'%(design lead%')
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m
                  WHERE m.RetiredAtUtc IS NULL
                    AND (m.ArchitectCanonicalOrgId = Id OR m.GeneralContractorCanonicalOrgId = Id
                         OR m.ProponentCanonicalOrgId = Id OR m.StructuralEngineerCanonicalOrgId = Id));
SET @retired = @@ROWCOUNT;
PRINT 'Parenthetical-scope retired: ' + CONVERT(varchar(20), @retired);

-- "Ltd. and ..." / "Inc. and ..." multi-entity JV shorthand
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m101: multi-entity Ltd./Inc.-and-joined JV shorthand (real partners exist as separate canonicals)'
WHERE RetiredAtUtc IS NULL
  AND (DisplayName LIKE N'% Ltd. and %' OR DisplayName LIKE N'% Inc. and %'
       OR DisplayName LIKE N'% Ltd and %' OR DisplayName LIKE N'% Inc and %'
       OR DisplayName LIKE N'% LTD. AND %' OR DisplayName LIKE N'% INC. AND %'
       OR DisplayName LIKE N'% in Joint Venture%' OR DisplayName LIKE N'% IN JOINT VENTURE%')
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m
                  WHERE m.RetiredAtUtc IS NULL
                    AND (m.ArchitectCanonicalOrgId = Id OR m.GeneralContractorCanonicalOrgId = Id
                         OR m.ProponentCanonicalOrgId = Id OR m.StructuralEngineerCanonicalOrgId = Id));
SET @retired = @@ROWCOUNT;
PRINT 'Ltd./Inc.-and-joined retired: ' + CONVERT(varchar(20), @retired);

-- Stantec Architecture office-descriptor variants (all mpi=0,
-- office regional descriptors that aren't real distinct entities)
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m101: Stantec Architecture office-descriptor research artifact'
WHERE RetiredAtUtc IS NULL
  AND DisplayName LIKE N'Stantec Architecture%'
  AND DisplayName <> N'Stantec Architecture' AND DisplayName <> N'Stantec Architecture Ltd.'
  AND DisplayName NOT LIKE N'%/%' AND DisplayName NOT LIKE N'% + %'
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m
                  WHERE m.RetiredAtUtc IS NULL
                    AND (m.ArchitectCanonicalOrgId = Id OR m.GeneralContractorCanonicalOrgId = Id
                         OR m.ProponentCanonicalOrgId = Id OR m.StructuralEngineerCanonicalOrgId = Id));
SET @retired = @@ROWCOUNT;
PRINT 'Stantec Architecture office artifacts retired: ' + CONVERT(varchar(20), @retired);

SELECT 'Total retired CanonicalOrg now' AS Stat, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NOT NULL;

PRINT 'Migration 101 pollution pass 3 complete.';

COMMIT TRAN;
GO
