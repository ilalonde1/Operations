SET XACT_ABORT ON;
GO

-- Backfill MajorProjectsInventory.ProjectCategoryName for the 1,832 NULL
-- rows via keyword patterns on ProjectName + ProponentName. Order matters:
-- most-specific patterns first, generic fallbacks last. Each UPDATE filters
-- WHERE ProjectCategoryName IS NULL so a row hit by an earlier pass isn't
-- re-touched.

BEGIN TRAN;

DECLARE @schoolsK12 int, @postSec int, @healthcare int, @transit int,
        @utilities int, @publicServices int, @residential int, @commercial int,
        @hospitality int, @cultural int, @industrial int, @indigenous int,
        @municipal int;

-- ========== ProjectName-based (most specific) ==========

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Schools - K-12'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Elementary%'
    OR ProjectName LIKE N'%Secondary School%' OR ProjectName LIKE N'%Secondary Addition%' OR ProjectName LIKE N'%Secondary Replacement%'
    OR ProjectName LIKE N'%Middle School%'
    OR ProjectName LIKE N'%High School%'
    OR ProjectName LIKE N'%School District%' OR ProjectName LIKE N'%Public School%'
    OR ProjectName LIKE N'%Catholic School%'
    OR ProjectName LIKE N'% School%' AND ProjectName NOT LIKE N'%Old School%'
  );
SET @schoolsK12 = @@ROWCOUNT;
PRINT 'Schools - K-12: ' + CONVERT(varchar(20), @schoolsK12);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Post-Secondary'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%University%' OR ProjectName LIKE N'%College%'
    OR ProjectName LIKE N'%Polytechnic%' OR ProjectName LIKE N'%Campus%'
    OR ProjectName LIKE N'%Institute%'
  );
SET @postSec = @@ROWCOUNT;
PRINT 'Post-Secondary: ' + CONVERT(varchar(20), @postSec);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Healthcare'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Hospital%' OR ProjectName LIKE N'%Health Centre%' OR ProjectName LIKE N'%Health Center%'
    OR ProjectName LIKE N'%Medical Centre%' OR ProjectName LIKE N'%Medical Center%'
    OR ProjectName LIKE N'%Care Centre%' OR ProjectName LIKE N'%Care Center%' OR ProjectName LIKE N'%Cancer Centre%'
    OR ProjectName LIKE N'%Health Services%' OR ProjectName LIKE N'%Long-Term Care%' OR ProjectName LIKE N'%Long Term Care%'
    OR ProjectName LIKE N'%Mental Health%' OR ProjectName LIKE N'%Psychiatric%'
    OR ProjectName LIKE N'%Clinic%'
  );
SET @healthcare = @@ROWCOUNT;
PRINT 'Healthcare: ' + CONVERT(varchar(20), @healthcare);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Transportation'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Bridge%' OR ProjectName LIKE N'%Highway%'
    OR ProjectName LIKE N'%Transit%' OR ProjectName LIKE N'%SkyTrain%' OR ProjectName LIKE N'%LRT%'
    OR ProjectName LIKE N'%Bus Rapid%' OR ProjectName LIKE N'%Subway%' OR ProjectName LIKE N'%Rail%'
    OR ProjectName LIKE N'%Tunnel%' OR ProjectName LIKE N'%Interchange%' OR ProjectName LIKE N'%Overpass%'
    OR ProjectName LIKE N'%Airport%' OR ProjectName LIKE N'%Terminal%' OR ProjectName LIKE N'%Port%'
    OR ProjectName LIKE N'%Ferry%' OR ProjectName LIKE N'%Marine Terminal%'
  );
SET @transit = @@ROWCOUNT;
PRINT 'Transportation: ' + CONVERT(varchar(20), @transit);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Utilities (incl sewage treatment)'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Wastewater%' OR ProjectName LIKE N'%Sewer%' OR ProjectName LIKE N'%Sewage%'
    OR ProjectName LIKE N'%Water Treatment%' OR ProjectName LIKE N'%Pump Station%' OR ProjectName LIKE N'%Reservoir%'
    OR ProjectName LIKE N'%Water Main%' OR ProjectName LIKE N'%Substation%' OR ProjectName LIKE N'%Power Station%'
    OR ProjectName LIKE N'%Hydro%' OR ProjectName LIKE N'%Wind Farm%' OR ProjectName LIKE N'%Solar Farm%'
    OR ProjectName LIKE N'%Pipeline%' OR ProjectName LIKE N'%Refinery%'
  );
SET @utilities = @@ROWCOUNT;
PRINT 'Utilities: ' + CONVERT(varchar(20), @utilities);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Public Services'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Fire Hall%' OR ProjectName LIKE N'%Fire Station%' OR ProjectName LIKE N'%Police Station%'
    OR ProjectName LIKE N'%Police Detachment%' OR ProjectName LIKE N'%RCMP%'
    OR ProjectName LIKE N'%Community Centre%' OR ProjectName LIKE N'%Community Center%'
    OR ProjectName LIKE N'%Recreation%' OR ProjectName LIKE N'%Aquatic Centre%' OR ProjectName LIKE N'%Aquatic Center%'
    OR ProjectName LIKE N'%Fieldhouse%' OR ProjectName LIKE N'%Sport%Complex%'
    OR ProjectName LIKE N'%Arena%' OR ProjectName LIKE N'%Stadium%'
    OR ProjectName LIKE N'%Courthouse%' OR ProjectName LIKE N'%Correctional%' OR ProjectName LIKE N'%Prison%'
  );
SET @publicServices = @@ROWCOUNT;
PRINT 'Public Services: ' + CONVERT(varchar(20), @publicServices);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Cultural'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Library%' OR ProjectName LIKE N'%Museum%' OR ProjectName LIKE N'%Gallery%'
    OR ProjectName LIKE N'%Theatre%' OR ProjectName LIKE N'%Theater%' OR ProjectName LIKE N'%Performance%Hall%'
    OR ProjectName LIKE N'%Concert%Hall%' OR ProjectName LIKE N'%Auditorium%'
  );
SET @cultural = @@ROWCOUNT;
PRINT 'Cultural: ' + CONVERT(varchar(20), @cultural);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Hospitality'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Hotel%' OR ProjectName LIKE N'%Resort%'
    OR ProjectName LIKE N'%Hostel%' OR ProjectName LIKE N'%Inn %' OR ProjectName LIKE N'% Inn'
  );
SET @hospitality = @@ROWCOUNT;
PRINT 'Hospitality: ' + CONVERT(varchar(20), @hospitality);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Indigenous'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%First Nation%' OR ProjectName LIKE N'%Indigenous%'
    OR ProjectName LIKE N'%Aboriginal%' OR ProjectName LIKE N'%Metis%'
    OR ProjectName LIKE N'%Cultural Centre%' OR ProjectName LIKE N'%Healing Lodge%'
  );
SET @indigenous = @@ROWCOUNT;
PRINT 'Indigenous: ' + CONVERT(varchar(20), @indigenous);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Industrial'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Warehouse%' OR ProjectName LIKE N'%Distribution Centre%' OR ProjectName LIKE N'%Distribution Center%'
    OR ProjectName LIKE N'%Industrial Park%' OR ProjectName LIKE N'%Manufacturing%' OR ProjectName LIKE N'%Plant%'
    OR ProjectName LIKE N'%Factory%' OR ProjectName LIKE N'%Mine%' OR ProjectName LIKE N'%Mill %' OR ProjectName LIKE N'% Mill'
    OR ProjectName LIKE N'%LNG%' OR ProjectName LIKE N'%Oil and Gas%'
  );
SET @industrial = @@ROWCOUNT;
PRINT 'Industrial: ' + CONVERT(varchar(20), @industrial);

UPDATE opportunities.MajorProjectsInventory
SET ProjectCategoryName = N'Residential/Commercial'
WHERE RetiredAtUtc IS NULL
  AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0)
  AND (
       ProjectName LIKE N'%Condominium%' OR ProjectName LIKE N'%Condo %' OR ProjectName LIKE N'%Condo' OR ProjectName LIKE N'% Condo%'
    OR ProjectName LIKE N'%Residential%' OR ProjectName LIKE N'%Apartments%' OR ProjectName LIKE N'%Apartment %'
    OR ProjectName LIKE N'%Townhouse%' OR ProjectName LIKE N'%Townhomes%' OR ProjectName LIKE N'%Rowhouse%'
    OR ProjectName LIKE N'%Tower %' OR ProjectName LIKE N'% Tower%' OR ProjectName LIKE N'%Towers%'
    OR ProjectName LIKE N'%Mixed-Use%' OR ProjectName LIKE N'%Mixed Use%'
    OR ProjectName LIKE N'%Housing%' OR ProjectName LIKE N'%Affordable Housing%'
    OR ProjectName LIKE N'%Lowrise%' OR ProjectName LIKE N'%Highrise%' OR ProjectName LIKE N'%Mid-Rise%'
    OR ProjectName LIKE N'%Lodge%'
    OR ProjectName LIKE N'%Office Tower%' OR ProjectName LIKE N'%Office Building%' OR ProjectName LIKE N'%Office Complex%'
    OR ProjectName LIKE N'%Retail%' OR ProjectName LIKE N'%Shopping%' OR ProjectName LIKE N'%Mall%'
  );
SET @residential = @@ROWCOUNT;
PRINT 'Residential/Commercial: ' + CONVERT(varchar(20), @residential);

-- ========== ProponentName-based fallback for remaining nulls ==========

UPDATE m
SET m.ProjectCategoryName = N'Schools - K-12'
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(m.ProjectCategoryName))) = 0)
  AND (
       m.ProponentName LIKE N'%School District%' OR m.ProponentName LIKE N'%(SD%'
    OR m.ProponentName LIKE N'%Public Schools%' OR m.ProponentName LIKE N'%School Board%'
    OR m.ProponentName LIKE N'%Catholic Schools%' OR m.ProponentName LIKE N'%Catholic District%'
  );
PRINT 'Schools - K-12 (proponent fallback): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m
SET m.ProjectCategoryName = N'Post-Secondary'
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(m.ProjectCategoryName))) = 0)
  AND (
       m.ProponentName LIKE N'%University%' OR m.ProponentName LIKE N'%College%'
    OR m.ProponentName LIKE N'%Polytechnic%' OR m.ProponentName LIKE N'%Institute of Technology%'
  );
PRINT 'Post-Secondary (proponent fallback): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m
SET m.ProjectCategoryName = N'Healthcare'
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(m.ProjectCategoryName))) = 0)
  AND (
       m.ProponentName LIKE N'%Health Authority%' OR m.ProponentName LIKE N'%Health Services%'
    OR m.ProponentName LIKE N'%Health Region%' OR m.ProponentName LIKE N'%Fraser Health%'
    OR m.ProponentName LIKE N'%Interior Health%' OR m.ProponentName LIKE N'%Vancouver Coastal%'
    OR m.ProponentName LIKE N'%Northern Health%' OR m.ProponentName LIKE N'%Island Health%'
    OR m.ProponentName LIKE N'%Providence Health%' OR m.ProponentName LIKE N'%Provincial Health%'
  );
PRINT 'Healthcare (proponent fallback): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m
SET m.ProjectCategoryName = N'Indigenous'
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(m.ProjectCategoryName))) = 0)
  AND (
       m.ProponentName LIKE N'%First Nation%' OR m.ProponentName LIKE N'%Band Council%'
    OR m.ProponentName LIKE N'%Tribal Council%' OR m.ProponentName LIKE N'%Metis%'
    OR m.ProponentName LIKE N'%Indigenous%' OR m.ProponentName LIKE N'%Westbank%'
    OR m.ProponentName LIKE N'%Squamish Nation%' OR m.ProponentName LIKE N'%Musqueam%'
  );
PRINT 'Indigenous (proponent fallback): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m
SET m.ProjectCategoryName = N'Municipal'
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(m.ProjectCategoryName))) = 0)
  AND (
       m.ProponentName LIKE N'City of %' OR m.ProponentName LIKE N'Town of %'
    OR m.ProponentName LIKE N'District of %' OR m.ProponentName LIKE N'Village of %'
    OR m.ProponentName LIKE N'Township of %' OR m.ProponentName LIKE N'Municipality of %'
    OR m.ProponentName LIKE N'Regional District%' OR m.ProponentName LIKE N'Metro Vancouver%'
  );
SET @municipal = @@ROWCOUNT;
PRINT 'Municipal: ' + CONVERT(varchar(20), @municipal);

-- ========== Final summary ==========

DECLARE @stillNull int;
SELECT @stillNull = COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND (ProjectCategoryName IS NULL OR LEN(LTRIM(RTRIM(ProjectCategoryName))) = 0);
PRINT 'Active MPI rows still missing ProjectCategoryName: ' + CONVERT(varchar(20), @stillNull);

PRINT 'Migration 77 category backfill complete.';

COMMIT TRAN;
GO
