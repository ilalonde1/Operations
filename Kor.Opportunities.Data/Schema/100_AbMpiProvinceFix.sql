SET XACT_ABORT ON;
GO

-- Province mis-coding fix surfaced by the projects/batch-005 drain
-- (2026-06-07). Sonnet flagged 5 MPI rows where Province was wrong:
--   * 3 Strathcona County, Alberta facilities listed as BC
--   * 1 Medicine Hat, Alberta facility listed as BC
--   * 1 Calgary, Alberta facility listed with "CA" instead of "AB"
--
-- Skipping the Ecole Beausoleil cost adjustment Sonnet flagged
-- ($16M → $73M) because $73M for an elementary school is
-- suspicious — Sonnet may have mis-attributed a Ministry-wide
-- investment number. Worth a human check, not a mechanical fix.

BEGIN TRAN;

-- Strathcona County, AB
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB', MunicipalityName = N'Strathcona County'
WHERE Id IN (6582, 6583, 6584) AND Province = N'BC';
PRINT 'Strathcona County province fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Medicine Hat, AB
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB', MunicipalityName = N'Medicine Hat'
WHERE Id = 6535 AND Province = N'BC';
PRINT 'Medicine Hat province fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Calgary, AB (province code "CA" -> "AB")
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB'
WHERE Id = 5316 AND Province = N'CA';
PRINT 'Calgary province code fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Confirm final state
SELECT Id, ProjectName, Province, MunicipalityName
FROM opportunities.MajorProjectsInventory
WHERE Id IN (6582, 6583, 6584, 6535, 5316)
ORDER BY Id;

PRINT 'Migration 100 AB province corrections complete.';

COMMIT TRAN;
GO
