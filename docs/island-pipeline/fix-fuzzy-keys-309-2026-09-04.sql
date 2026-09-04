SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Values taken verbatim from ExpectedFuzzy in the integrity report's own
-- org_fuzzy_key_stale CSV (run 20260904-234456). Not re-derived.
-- 71528 MCW is DELIBERATELY excluded: it stores 'mcw' as an override.
IF OBJECT_ID('tempdb..#k') IS NOT NULL DROP TABLE #k;
CREATE TABLE #k (Id int PRIMARY KEY, ExpectedFuzzy nvarchar(400));
INSERT INTO #k (Id, ExpectedFuzzy) VALUES
 (927810, N'bakerviewbuildingdesign'),
 (927811, N'igvhousing'),
 (927812, N'jrtarchitecture'),
 (927813, N'storeycreekgolfandrecreationsociety'),
 (927814, N'kylineconstruction'),
 (927815, N'nikolaproperties'),
 (927816, N'parkwayproperties'),
 (927817, N'highlandengineeringservices'),
 (927818, N'bigislandconstruction'),
 (927819, N'tobemprojects'),
 (927820, N'comactcanada');

UPDATE co SET FuzzyNormalizedName = k.ExpectedFuzzy
FROM opportunities.CanonicalOrg co
JOIN #k k ON k.Id = co.Id
WHERE co.RetiredAtUtc IS NULL AND ISNULL(co.FuzzyNormalizedName, N'') <> k.ExpectedFuzzy;

SELECT 'keys after' AS Section;
SELECT co.Id, LEFT(co.DisplayName, 40) AS Firm, co.FuzzyNormalizedName AS StoredFuzzy
FROM opportunities.CanonicalOrg co JOIN #k k ON k.Id = co.Id ORDER BY co.Id;

SELECT 'do any of these keys now collide with another live row?' AS Section;
SELECT co.FuzzyNormalizedName, COUNT(*) AS LiveRows,
       LEFT(STRING_AGG(CAST(co.DisplayName AS nvarchar(max)), ' | '), 80) AS Names
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL AND co.FuzzyNormalizedName IN (SELECT ExpectedFuzzy FROM #k)
GROUP BY co.FuzzyNormalizedName HAVING COUNT(*) > 1;
