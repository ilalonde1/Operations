SET XACT_ABORT ON;
GO

BEGIN TRAN;

DECLARE @tradeRenovationToGc int;
DECLARE @tradeElectricalToSub int;
DECLARE @tradeMechanicalToSub int;
DECLARE @tradePlumbingToSub int;
DECLARE @tradeRoofingToSub int;
DECLARE @tradeHvacToSub int;
DECLARE @tradeDrywallToSub int;
DECLARE @tradeConcreteToSub int;
DECLARE @tradeExcavationToSub int;
DECLARE @tradeFinishingToSub int;
DECLARE @vendorHoldingsToDeveloper int;
DECLARE @categorySchools int;
DECLARE @categoryPostSecondary int;
DECLARE @categoryHealthcare int;
DECLARE @categoryInfrastructure int;
DECLARE @categoryHousing int;
DECLARE @categoryMunicipal int;

UPDATE opportunities.CanonicalOrg
SET Kind = N'GC'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%restoration%'
      OR DisplayName LIKE N'%renovation%'
      OR DisplayName LIKE N'%remodel%'
  );
SET @tradeRenovationToGc = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> GC (renovation/restoration): ' + CONVERT(varchar(20), @tradeRenovationToGc) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%electric%'
      OR DisplayName LIKE N'%electrical%'
      OR DisplayName LIKE N'%electricians%'
  );
SET @tradeElectricalToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (electrical): ' + CONVERT(varchar(20), @tradeElectricalToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND DisplayName LIKE N'%mechanical%';
SET @tradeMechanicalToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (mechanical): ' + CONVERT(varchar(20), @tradeMechanicalToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND DisplayName LIKE N'%plumbing%';
SET @tradePlumbingToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (plumbing): ' + CONVERT(varchar(20), @tradePlumbingToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%roofing%'
      OR DisplayName LIKE N'%roofer%'
  );
SET @tradeRoofingToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (roofing): ' + CONVERT(varchar(20), @tradeRoofingToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%hvac%'
      OR DisplayName LIKE N'%heating%cooling%'
      OR DisplayName LIKE N'%refrigeration%'
  );
SET @tradeHvacToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (hvac): ' + CONVERT(varchar(20), @tradeHvacToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%drywall%'
      OR DisplayName LIKE N'%framing%'
      OR DisplayName LIKE N'%insulation%'
  );
SET @tradeDrywallToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (drywall/framing/insulation): ' + CONVERT(varchar(20), @tradeDrywallToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%concrete%'
      OR DisplayName LIKE N'%forming%'
      OR DisplayName LIKE N'%foundation%'
  );
SET @tradeConcreteToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (concrete/forming/foundation): ' + CONVERT(varchar(20), @tradeConcreteToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%excavation%'
      OR DisplayName LIKE N'%drilling%'
      OR DisplayName LIKE N'%earthwork%'
      OR DisplayName LIKE N'%demolition%'
      OR DisplayName LIKE N'%site work%'
      OR DisplayName LIKE N'%sitework%'
  );
SET @tradeExcavationToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (excavation/site work): ' + CONVERT(varchar(20), @tradeExcavationToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Subcontractor'
WHERE Kind IN (N'Vendor', N'Unknown')
  AND (
      DisplayName LIKE N'%painting%'
      OR DisplayName LIKE N'%flooring%'
      OR DisplayName LIKE N'%glazing%'
      OR DisplayName LIKE N'%glass%'
      OR DisplayName LIKE N'%cabinet%'
      OR DisplayName LIKE N'%millwork%'
  );
SET @tradeFinishingToSub = @@ROWCOUNT;
PRINT 'Vendor/Unknown -> Subcontractor (finishing trades): ' + CONVERT(varchar(20), @tradeFinishingToSub) + ' rows';

UPDATE opportunities.CanonicalOrg
SET Kind = N'Developer'
WHERE Kind = N'Vendor'
  AND (
      DisplayName LIKE N'%holdings%'
      OR DisplayName LIKE N'%holding co%'
      OR DisplayName LIKE N'%holding ltd%'
  );
SET @vendorHoldingsToDeveloper = @@ROWCOUNT;
PRINT 'Vendor -> Developer (holdings): ' + CONVERT(varchar(20), @vendorHoldingsToDeveloper) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Schools - K-12'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-schooldistrict%'
      OR SourceKey LIKE N'CAPPLAN-edmontoncatholicscho%'
      OR SourceKey LIKE N'CAPPLAN-edmontonpublicschool%'
      OR SourceKey LIKE N'CAPPLAN-calgaryboardofeducat%'
      OR SourceKey LIKE N'CAPPLAN-northvancouverschool%'
      OR SourceKey LIKE N'CAPPLAN-abbotsfordschooldist%'
      OR SourceKey LIKE N'CAPPLAN-langleyschooldistric%'
  );
SET @categorySchools = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Schools - K-12): ' + CONVERT(varchar(20), @categorySchools) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Post-Secondary'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-universityof%'
      OR SourceKey LIKE N'CAPPLAN-britishcolumbiainsti%'
  );
SET @categoryPostSecondary = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Post-Secondary): ' + CONVERT(varchar(20), @categoryPostSecondary) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Healthcare'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-albertahealthservice%'
      OR SourceKey LIKE N'CAPPLAN-vancouvercoastalheal%'
      OR SourceKey LIKE N'CAPPLAN-fraserhealth%'
      OR SourceKey LIKE N'CAPPLAN-interiorhealt%'
      OR SourceKey LIKE N'CAPPLAN-islandhealth%'
  );
SET @categoryHealthcare = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Healthcare): ' + CONVERT(varchar(20), @categoryHealthcare) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Infrastructure'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-albertainfrastructur%'
      OR SourceKey LIKE N'CAPPLAN-infrastructure%'
  );
SET @categoryInfrastructure = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Infrastructure): ' + CONVERT(varchar(20), @categoryInfrastructure) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Housing'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-makolahousingsociety%'
      OR SourceKey LIKE N'CAPPLAN-mapleridgepittmeadow%'
  );
SET @categoryHousing = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Housing): ' + CONVERT(varchar(20), @categoryHousing) + ' rows';

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Municipal'
WHERE (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
      SourceKey LIKE N'CAPPLAN-cityofkelown%'
      OR SourceKey LIKE N'CAPPLAN-cityof%'
  );
SET @categoryMunicipal = @@ROWCOUNT;
PRINT 'ProjectCategoryName backfilled (Municipal): ' + CONVERT(varchar(20), @categoryMunicipal) + ' rows';

SELECT Kind, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg
GROUP BY Kind
ORDER BY Cnt DESC;

SELECT COUNT(*) AS ProjectCategoryNameNonNullCount
FROM opportunities.MajorProjectsInventory
WHERE ProjectCategoryName IS NOT NULL
  AND LEN(LTRIM(RTRIM(ProjectCategoryName))) > 0;

PRINT 'Migration 71 R95-final close-out complete.';

COMMIT TRAN;
GO
