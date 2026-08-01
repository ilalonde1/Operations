/*
194_FixDedupOrphansAnd300First.sql
WHAT: (A) Re-point the IntelPersonAffiliation rows orphaned by the 2026-06-18 BdCanonicalDedup
          run — the tool does NOT repoint IntelPersonAffiliation, so affils on the 11 retired
          loser orgs were left pointing at retired orgs. (B) Correct 300 S First St: the Glotman
          SE attribution was an address mix-up (belongs to the adjacent 35 S Second Westbank
          tower) — 300 S First's SE is unnamed (re-hone round 2).
WHY:  Data-integrity fix surfaced by the post-dedup audit (orphan check = 11) + seat re-hone.
HOW:  Re-point loser->survivor, recompute affiliation NaturalKey (SHA1 of PersonId|OrgId|normTitle);
      retire orphans that would collide with an existing live survivor affil.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

DECLARE @map TABLE (Loser bigint PRIMARY KEY, Survivor bigint);
INSERT INTO @map VALUES (76885,68631),(75706,38930),(76792,68910),(76944,70108),(76784,68610),
 (75704,38924),(76794,68631),(76737,68631),(75705,68613),(75199,68613),(76888,75180);

-- stage the orphaned live affils with their would-be survivor NaturalKey
DECLARE @o TABLE (AffId bigint, Survivor bigint, NewKey char(40));
INSERT INTO @o (AffId, Survivor, NewKey)
SELECT a.Id, m.Survivor,
  CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
     CAST(a.IntelPersonId AS VARCHAR(20)) + '|' + CAST(m.Survivor AS VARCHAR(20)) + '|' +
     REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
       LOWER(LTRIM(RTRIM(COALESCE(a.Title,N'')))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N'')
   AS VARCHAR(8000))), 2)
FROM opportunities.IntelPersonAffiliation a JOIN @map m ON m.Loser = a.CanonicalOrgId
WHERE a.RetiredAtUtc IS NULL;

-- retire orphans whose survivor-key already exists on a different live affil (true duplicate)
UPDATE a SET RetiredAtUtc = @now, RetiredReason = N'dedup-merge 2026-06-18: duplicate of survivor affiliation', UpdatedAtUtc = @now
FROM opportunities.IntelPersonAffiliation a
JOIN @o o ON o.AffId = a.Id
WHERE EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation x WHERE x.NaturalKey = o.NewKey AND x.Id <> a.Id AND x.RetiredAtUtc IS NULL);

-- re-point the survivors of the rest
UPDATE a SET CanonicalOrgId = o.Survivor, NaturalKey = o.NewKey, UpdatedAtUtc = @now
FROM opportunities.IntelPersonAffiliation a
JOIN @o o ON o.AffId = a.Id
WHERE a.RetiredAtUtc IS NULL;

-- (B) 300 S First St (8273): drop the wrong Glotman SE attribution (address mix-up)
UPDATE opportunities.MajorProjectsInventory
  SET StructuralEngineerName = NULL, StructuralEngineerCanonicalOrgId = NULL,
      ArchitectName = N'Steinberg Hart',
      ProjectDescription = COALESCE(ProjectDescription + N' ', N'') + N'[re-hone r2 2026-06-18: Glotman SE attribution was WRONG (address mix-up with the adjacent 35 S Second St Westbank tower). 300 S First SE is unnamed; Steinberg Hart is architect of record.]',
      UpdatedAtUtc = @now
  WHERE Province = N'CA' AND SourceKey = N'ca-research-2026-06:300-s-first-sj';

COMMIT;

-- verify: zero remaining orphans across all retired orgs
SELECT (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a JOIN opportunities.CanonicalOrg o ON o.Id=a.CanonicalOrgId WHERE a.RetiredAtUtc IS NULL AND o.RetiredAtUtc IS NOT NULL) AS OrphanAffilsRemaining,
       (SELECT StructuralEngineerName FROM opportunities.MajorProjectsInventory WHERE SourceKey=N'ca-research-2026-06:300-s-first-sj') AS First300_SE;
