SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Stale FuzzyNormalizedName repair, 2026-09-04.
--
-- Values are taken verbatim from ExpectedFuzzy in the integrity report's own
-- org_fuzzy_key_stale CSV, which computes them with the real
-- CanonicalOrgResolver.NormalizeForFuzzyMatch. They are NOT re-derived here.
--
-- 16 of the 19 rows were inserted by the Island who's-who SQL on 2026-09-04 with
-- a hand-computed key that kept the legal suffix ('hutchinsoncontractingltd'
-- instead of 'hutchinsoncontracting') or with no key at all. A row with the wrong
-- fuzzy key is invisible to the write-time duplicate gate, which is how it minted
-- four duplicates of rows that already existed. Those four are merged.
--
-- 504241 and 658682 are pre-existing rows with empty keys, fixed here for the
-- same reason.
--
-- ⛔ 71528 MCW Group of Companies is DELIBERATELY excluded. It stores 'mcw' as an
-- override; the report flags it as stale and it is not. This is exactly why the
-- global --backfill-fuzzy-key was not used.

IF OBJECT_ID('tempdb..#fix') IS NOT NULL DROP TABLE #fix;
CREATE TABLE #fix (Id int PRIMARY KEY, ExpectedFuzzy nvarchar(400));
INSERT INTO #fix (Id, ExpectedFuzzy) VALUES
 (504241, N'innascoredevelopments'),
 (658682, N'namdargroup'),
 (927760, N'knappettindustries'),
 (927762, N'barefootplanningdesign'),
 (927767, N'hutchinsoncontracting'),
 (927769, N'korsdevelopmentservices'),
 (927770, N'mjmarchitect'),
 (927790, N'abbarcharchitecture'),
 (927791, N'calidservices'),
 (927795, N'leesassociates'),
 (927797, N'hausenprojects'),
 (927803, N'nesarchitecture'),
 (927804, N'oemarchitectureoffice'),
 (927805, N'polarislandsurveying'),
 (927806, N'robertblaneydesign'),
 (927807, N'sebaconstruction');

SELECT 'before' AS Section;
SELECT COUNT(*) AS RowsToFix FROM #fix f
JOIN opportunities.CanonicalOrg co ON co.Id = f.Id
WHERE co.RetiredAtUtc IS NULL AND ISNULL(co.FuzzyNormalizedName, N'') <> f.ExpectedFuzzy;

UPDATE co SET FuzzyNormalizedName = f.ExpectedFuzzy
FROM opportunities.CanonicalOrg co
JOIN #fix f ON f.Id = co.Id
WHERE co.RetiredAtUtc IS NULL AND ISNULL(co.FuzzyNormalizedName, N'') <> f.ExpectedFuzzy;

SELECT 'rows updated' AS Section;
SELECT @@ROWCOUNT AS Updated;

SELECT 'any of these keys now collide with another LIVE row?' AS Section;
SELECT co.FuzzyNormalizedName, COUNT(*) AS LiveRows,
       LEFT(STRING_AGG(CAST(co.DisplayName AS nvarchar(max)), ' | '), 90) AS Names
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.FuzzyNormalizedName IN (SELECT ExpectedFuzzy FROM #fix)
GROUP BY co.FuzzyNormalizedName
HAVING COUNT(*) > 1;
