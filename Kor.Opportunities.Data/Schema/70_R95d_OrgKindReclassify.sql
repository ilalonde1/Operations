SET XACT_ABORT ON;
GO

BEGIN TRAN;

DECLARE @unknownToArchitect int;
DECLARE @unknownToGc int;
DECLARE @unknownToDeveloper int;
DECLARE @unknownToCompetitor int;
DECLARE @unknownToBuyerSchool int;
DECLARE @unknownToBuyerPostSecondary int;
DECLARE @unknownToBuyerHealth int;
DECLARE @unknownToBuyerMunicipality int;
DECLARE @unknownToBuyerIndigenous int;
DECLARE @vendorToArchitect int;
DECLARE @vendorToGc int;
DECLARE @vendorToCompetitor int;
DECLARE @vendorToDeveloper int;

UPDATE opportunities.CanonicalOrg
SET Kind = N'Architect'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%architect%'
      OR DisplayName LIKE N'%architects%'
      OR DisplayName LIKE N'%architecture%'
  );
SET @unknownToArchitect = @@ROWCOUNT;
PRINT 'Unknown -> Architect: ' + CONVERT(varchar(20), @unknownToArchitect) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'GC'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%construction%'
      OR DisplayName LIKE N'%contractors%'
      OR DisplayName LIKE N'%contracting%'
      OR DisplayName LIKE N'%builders%'
      OR DisplayName LIKE N'%general contractor%'
  );
SET @unknownToGc = @@ROWCOUNT;
PRINT 'Unknown -> GC: ' + CONVERT(varchar(20), @unknownToGc) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Developer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%developments%'
      OR DisplayName LIKE N'%developments inc%'
      OR DisplayName LIKE N'%properties%'
      OR DisplayName LIKE N'%real estate%'
      OR DisplayName LIKE N'%developer%'
  );
SET @unknownToDeveloper = @@ROWCOUNT;
PRINT 'Unknown -> Developer: ' + CONVERT(varchar(20), @unknownToDeveloper) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Competitor'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%engineering%'
      OR DisplayName LIKE N'%engineers%'
      OR DisplayName LIKE N'%consulting engineers%'
  );
SET @unknownToCompetitor = @@ROWCOUNT;
PRINT 'Unknown -> Competitor: ' + CONVERT(varchar(20), @unknownToCompetitor) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%school district%'
      OR DisplayName LIKE N'%(SD%'
      OR DisplayName LIKE N'%public schools%'
      OR DisplayName LIKE N'%school board%'
  );
SET @unknownToBuyerSchool = @@ROWCOUNT;
PRINT 'Unknown -> Buyer (school districts): ' + CONVERT(varchar(20), @unknownToBuyerSchool) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%university%'
      OR DisplayName LIKE N'%college%'
      OR DisplayName LIKE N'%polytechnic%'
      OR DisplayName LIKE N'%institute of technology%'
  );
SET @unknownToBuyerPostSecondary = @@ROWCOUNT;
PRINT 'Unknown -> Buyer (post-secondary): ' + CONVERT(varchar(20), @unknownToBuyerPostSecondary) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%health authority%'
      OR DisplayName LIKE N'%health services%'
      OR DisplayName LIKE N'%hospital%'
      OR DisplayName LIKE N'%health region%'
  );
SET @unknownToBuyerHealth = @@ROWCOUNT;
PRINT 'Unknown -> Buyer (health): ' + CONVERT(varchar(20), @unknownToBuyerHealth) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'City of %'
      OR DisplayName LIKE N'Town of %'
      OR DisplayName LIKE N'District of %'
      OR DisplayName LIKE N'Village of %'
      OR DisplayName LIKE N'Township of %'
      OR DisplayName LIKE N'Municipality of %'
      OR DisplayName LIKE N'Regional District%'
  );
SET @unknownToBuyerMunicipality = @@ROWCOUNT;
PRINT 'Unknown -> Buyer (municipality): ' + CONVERT(varchar(20), @unknownToBuyerMunicipality) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown'
  AND (
      DisplayName LIKE N'%first nation%'
      OR DisplayName LIKE N'%band council%'
      OR DisplayName LIKE N'%tribal council%'
      OR DisplayName LIKE N'%metis%'
  );
SET @unknownToBuyerIndigenous = @@ROWCOUNT;
PRINT 'Unknown -> Buyer (Indigenous): ' + CONVERT(varchar(20), @unknownToBuyerIndigenous) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Architect'
WHERE Kind = N'Vendor'
  AND (
      DisplayName LIKE N'%architect%'
      OR DisplayName LIKE N'%architects%'
      OR DisplayName LIKE N'%architecture%'
  );
SET @vendorToArchitect = @@ROWCOUNT;
PRINT 'Vendor -> Architect: ' + CONVERT(varchar(20), @vendorToArchitect) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'GC'
WHERE Kind = N'Vendor'
  AND (
      DisplayName LIKE N'%construction%'
      OR DisplayName LIKE N'%contractors%'
      OR DisplayName LIKE N'%contracting%'
      OR DisplayName LIKE N'%builders%'
      OR DisplayName LIKE N'%general contractor%'
  );
SET @vendorToGc = @@ROWCOUNT;
PRINT 'Vendor -> GC: ' + CONVERT(varchar(20), @vendorToGc) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Competitor'
WHERE Kind = N'Vendor'
  AND (
      DisplayName LIKE N'%engineering%'
      OR DisplayName LIKE N'%engineers%'
      OR DisplayName LIKE N'%consulting engineers%'
  );
SET @vendorToCompetitor = @@ROWCOUNT;
PRINT 'Vendor -> Competitor: ' + CONVERT(varchar(20), @vendorToCompetitor) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Developer'
WHERE Kind = N'Vendor'
  AND (
      DisplayName LIKE N'%developments%'
      OR DisplayName LIKE N'%properties%'
      OR DisplayName LIKE N'%real estate%'
  );
SET @vendorToDeveloper = @@ROWCOUNT;
PRINT 'Vendor -> Developer: ' + CONVERT(varchar(20), @vendorToDeveloper) + ' rows';

SELECT Kind, COUNT(*) AS Cnt
INTO #PostState
FROM opportunities.CanonicalOrg
GROUP BY Kind;

SELECT *
FROM #PostState
ORDER BY Cnt DESC;

COMMIT TRAN;
GO

PRINT 'Migration 70 R95d mechanical org-Kind reclassification complete.';
GO
