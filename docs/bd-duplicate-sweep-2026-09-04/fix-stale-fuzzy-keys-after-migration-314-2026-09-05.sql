SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Stale FuzzyNormalizedName repair, 2026-09-05, after migration 314.
-- Migration 314 rewrote DisplayName for the four line-feed names but not their fuzzy key,
-- so the stored key still carried the line feed. Same guarded shape as the earlier repair.
--
-- CanonicalOrgResolver.NormalizeForFuzzyMatch now folds every '&' and every spaced '+' to
-- ' and ' (Codex duplicate-sweep fix 1). Every stored key computed before that is stale for
-- a name containing either: 4 rows (0 with '&', 0 with ' + '). A stale key is invisible
-- to the write-time duplicate gate, so until this runs the next reference to any of these
-- firms can mint a twin.
--
-- Values are taken verbatim from ExpectedFuzzy in the integrity report's own
-- org_fuzzy_key_stale CSV (report stamp 20260905-045424), which computes them with the real
-- normalizer. They are NOT re-derived here.
--
-- ORDER: run tools/BdCanonicalDedup --pairs ampersand-fold-merge-2026-09-05.csv --commit FIRST
-- (the five pairs the old key kept apart), then this. Repairing keys first only turns those
-- five into fuzzy-key collisions. Merged losers are deleted rows and fall through the guard.
--
-- Deploy the Worker that carries the new normalizer in the same sitting: between repair and
-- deploy the two normalizers disagree and either side can mint a twin.
--
-- 71528 MCW Group of Companies is DELIBERATELY excluded. It stores 'mcw' as an override; the
-- report flags it as stale and it is not. This is exactly why --backfill-fuzzy-key is not used.

IF OBJECT_ID('tempdb..#fix') IS NOT NULL DROP TABLE #fix;
CREATE TABLE #fix (Id bigint PRIMARY KEY, StoredFuzzy nvarchar(400), ExpectedFuzzy nvarchar(400));
INSERT INTO #fix (Id, StoredFuzzy, ExpectedFuzzy) VALUES
 (75103, N'ministryofcitizensservices ministryoftransportationandtransit', N'ministryofcitizensservicesministryoftransportationandtransit'),
 (272014, N'bcparksprovincialservicesbranch bcparksandconservationofficerservicedivision capitalinvestmentprogram ministryofenvironmentandparks', N'bcparksprovincialservicesbranchbcparksandconservationofficerservicedivisioncapitalinvestmentprogramministryofenvironmentandparks'),
 (473331, N'fraserhealthauthority providencehealthcare', N'fraserhealthauthorityprovidencehealthcare'),
 (902252, N'ministryofenvironmentandparks ministryofforests', N'ministryofenvironmentandparksministryofforests');

SELECT 'rows found live with the stored key still in place' AS Section, COUNT(*) AS N
FROM opportunities.CanonicalOrg co JOIN #fix f ON f.Id = co.Id
WHERE co.RetiredAtUtc IS NULL AND (co.FuzzyNormalizedName LIKE N'%' + NCHAR(10) + N'%' OR co.FuzzyNormalizedName LIKE N'%' + NCHAR(13) + N'%');

BEGIN TRANSACTION;
UPDATE co
SET co.FuzzyNormalizedName = f.ExpectedFuzzy,
    co.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
JOIN #fix f ON f.Id = co.Id
WHERE co.RetiredAtUtc IS NULL
  AND (co.FuzzyNormalizedName LIKE N'%' + NCHAR(10) + N'%' OR co.FuzzyNormalizedName LIKE N'%' + NCHAR(13) + N'%')   -- guard: the stored key still carries the line feed; the CSV renders it as a space, so equality cannot be used
  AND co.FuzzyNormalizedName <> f.ExpectedFuzzy;
SELECT 'rows updated' AS Section, @@ROWCOUNT AS N;
COMMIT TRANSACTION;

SELECT 'any of these keys now collide with another LIVE row?' AS Section;
SELECT co.FuzzyNormalizedName, COUNT(*) AS LiveRows,
       LEFT(STRING_AGG(CAST(co.DisplayName AS nvarchar(max)), ' | '), 90) AS Names
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.FuzzyNormalizedName IN (SELECT ExpectedFuzzy FROM #fix)
GROUP BY co.FuzzyNormalizedName
HAVING COUNT(*) > 1;
