SET XACT_ABORT ON;
GO

BEGIN TRAN;

DECLARE @loFixed int;
DECLARE @okFixed int;
DECLARE @vaFixed int;
DECLARE @nonCanonicalRemaining int;

-- LO = truncated "Lower Mainland", OK = "Okanagan", VA = "Vancouver Island".
-- All three are BC sub-regions that the buggy 2-char truncation in
-- BdResearchImport.NormalizeProvince surfaced as bogus "province codes".
UPDATE opportunities.MajorProjectsInventory
SET Province = N'BC'
WHERE Province = N'LO';

SET @loFixed = @@ROWCOUNT;
PRINT 'LO->BC rows fixed: ' + CONVERT(varchar(20), @loFixed);

UPDATE opportunities.MajorProjectsInventory
SET Province = N'BC'
WHERE Province = N'OK';

SET @okFixed = @@ROWCOUNT;
PRINT 'OK->BC rows fixed: ' + CONVERT(varchar(20), @okFixed);

UPDATE opportunities.MajorProjectsInventory
SET Province = N'BC'
WHERE Province = N'VA';

SET @vaFixed = @@ROWCOUNT;
PRINT 'VA->BC rows fixed: ' + CONVERT(varchar(20), @vaFixed);

SELECT DISTINCT Province
INTO #NonCanonicalProvinces
FROM opportunities.MajorProjectsInventory
WHERE Province IS NOT NULL
  AND Province NOT IN (
      N'AB', N'BC', N'MB', N'NB', N'NL', N'NS', N'NT', N'NU', N'ON', N'PE', N'QC', N'SK', N'YT',
      N'WA', N'OR', N'CA', N'NV', N'AZ', N'TX', N'NY', N'FL', N'IL', N'MA', N'CO', N'GA'
  );

SELECT @nonCanonicalRemaining = COUNT(*)
FROM #NonCanonicalProvinces;

PRINT 'Non-canonical Provinces remaining: ' + CONVERT(varchar(20), @nonCanonicalRemaining);

SELECT Province
FROM #NonCanonicalProvinces
ORDER BY Province;

COMMIT TRAN;
GO

PRINT 'Migration 69 R95c province junk fix complete.';
GO
