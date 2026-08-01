SET XACT_ABORT ON;
GO

-- Promote the 139 'Unknown' CanonicalOrgs the proponent drain ingester
-- minted tonight. The ingester defaults to Kind='Unknown' for new orgs
-- it creates from a Sonnet-researched proponentName (it has no kind
-- signal at write time). This migration runs the same R95d-1 keyword
-- pass to promote them so they qualify for org enrichment and surface
-- correctly in briefs as Architect / Developer / Buyer / etc.
--
-- Scoped to CreatedAtUtc >= 3 hours ago (the drain window) so we don't
-- re-process the 7K legacy Unknown rows already handled in migration 70.

BEGIN TRAN;

DECLARE @architects int, @gcs int, @developers int, @buyers int;

-- Architect — has architect keyword
UPDATE opportunities.CanonicalOrg
SET Kind = N'Architect'
WHERE Kind = N'Unknown' AND RetiredAtUtc IS NULL
  AND CreatedAtUtc >= DATEADD(HOUR, -3, sysdatetimeoffset())
  AND (DisplayName LIKE N'%Architect%' OR DisplayName LIKE N'%Architecture%' OR DisplayName LIKE N'%Architects%');
SET @architects = @@ROWCOUNT;
PRINT 'Drain Unknown -> Architect: ' + CONVERT(varchar(20), @architects);

-- GC — has construction/builders/contracting keyword
UPDATE opportunities.CanonicalOrg
SET Kind = N'GC'
WHERE Kind = N'Unknown' AND RetiredAtUtc IS NULL
  AND CreatedAtUtc >= DATEADD(HOUR, -3, sysdatetimeoffset())
  AND (DisplayName LIKE N'%Construction%' OR DisplayName LIKE N'%Builders%' OR DisplayName LIKE N'%Contracting%'
       OR DisplayName LIKE N'%Contractors%');
SET @gcs = @@ROWCOUNT;
PRINT 'Drain Unknown -> GC: ' + CONVERT(varchar(20), @gcs);

-- Developer — has developer/properties/holdings/homes/group/corp/inc keywords
-- Order matters — already-architect/GC rows are excluded by the Kind filter above.
UPDATE opportunities.CanonicalOrg
SET Kind = N'Developer'
WHERE Kind = N'Unknown' AND RetiredAtUtc IS NULL
  AND CreatedAtUtc >= DATEADD(HOUR, -3, sysdatetimeoffset())
  AND (DisplayName LIKE N'%Properties%' OR DisplayName LIKE N'%Holdings%' OR DisplayName LIKE N'%Developments%'
       OR DisplayName LIKE N'%Developer%' OR DisplayName LIKE N'%Property%' OR DisplayName LIKE N'%Real Estate%'
       OR DisplayName LIKE N'%Homes%' OR DisplayName LIKE N'%Capital Partners%' OR DisplayName LIKE N'%Investment%'
       OR DisplayName LIKE N'%Realty%' OR DisplayName LIKE N'%Estates%');
SET @developers = @@ROWCOUNT;
PRINT 'Drain Unknown -> Developer: ' + CONVERT(varchar(20), @developers);

-- Buyer — Society/Authority/Foundation/Hospital/University/College/First Nation/Housing
UPDATE opportunities.CanonicalOrg
SET Kind = N'Buyer'
WHERE Kind = N'Unknown' AND RetiredAtUtc IS NULL
  AND CreatedAtUtc >= DATEADD(HOUR, -3, sysdatetimeoffset())
  AND (DisplayName LIKE N'%Society%' OR DisplayName LIKE N'%Authority%' OR DisplayName LIKE N'%Foundation%'
       OR DisplayName LIKE N'%Co-op%' OR DisplayName LIKE N'%Cooperative%' OR DisplayName LIKE N'%Housing Society%'
       OR DisplayName LIKE N'%Hospital%' OR DisplayName LIKE N'%University%' OR DisplayName LIKE N'%College%'
       OR DisplayName LIKE N'%First Nation%' OR DisplayName LIKE N'%Band Council%'
       OR DisplayName LIKE N'%Health Authority%' OR DisplayName LIKE N'%School District%');
SET @buyers = @@ROWCOUNT;
PRINT 'Drain Unknown -> Buyer: ' + CONVERT(varchar(20), @buyers);

-- Final summary
SELECT 'Drain canonicals by Kind (post-reclassify)' AS Stat, Kind, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg
WHERE CreatedAtUtc >= DATEADD(HOUR, -3, sysdatetimeoffset())
  AND RetiredAtUtc IS NULL
GROUP BY Kind
ORDER BY Cnt DESC;

PRINT 'Migration 85 drain-created Kind reclassification complete. Total promoted: '
    + CONVERT(varchar(20), @architects + @gcs + @developers + @buyers);

COMMIT TRAN;
GO
