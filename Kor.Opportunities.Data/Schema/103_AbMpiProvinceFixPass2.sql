SET XACT_ABORT ON;
GO

-- AB province correction pass 2. Surfaced by projects/batch-008 drain
-- (Sonnet caught 12 more BC-mislabeled AB projects).

BEGIN TRAN;

-- Edmonton, AB
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB', MunicipalityName = N'Edmonton'
WHERE Id IN (6508, 6509, 6514, 6515, 6516, 6540) AND Province = N'BC';
PRINT 'Edmonton province fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Sherwood Park (Strathcona County), AB
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB', MunicipalityName = N'Strathcona County'
WHERE Id = 6475 AND Province = N'BC';
PRINT 'Strathcona Community Hospital fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Calgary, AB (CBE schools)
UPDATE opportunities.MajorProjectsInventory
SET Province = N'AB', MunicipalityName = N'Calgary'
WHERE Id IN (6596, 6597, 6598, 6599, 6600) AND Province = N'BC';
PRINT 'Calgary CBE schools fix: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Confirm
SELECT Id, ProjectName, Province, MunicipalityName FROM opportunities.MajorProjectsInventory
WHERE Id IN (6475, 6508, 6509, 6514, 6515, 6516, 6540, 6596, 6597, 6598, 6599, 6600)
ORDER BY Id;

PRINT 'Migration 103 AB province pass 2 complete.';

COMMIT TRAN;
GO
