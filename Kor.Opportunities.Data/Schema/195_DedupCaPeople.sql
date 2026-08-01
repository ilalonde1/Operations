/*
195_DedupCaPeople.sql
WHAT: Merge 4 verified duplicate IntelPerson rows (same person, name/spelling variants on the
      same personal email) into survivors. CA-relevant only.
WHY:  Graph-perfection step 1a. NOTE: most shared-email "dups" are FALSE (different people on a
      firm's generic info@/office@ address) — those are deliberately NOT touched. Only true
      same-person variants merged.
HOW:  Re-point IntelPersonAffiliation loser->survivor (recompute NaturalKey; retire collisions),
      COALESCE survivor contact fields, retire losers. Pairs hand-verified 2026-06-18.
      13530->13491 Chase Ronge/Rongé (MVE); 13509->13535 Kevin Heinley/Heinly (Gensler);
      13548->13574 Bill/William Shopoff; 9853->31 William (Bill) Fain (Johnson Fain).
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

DECLARE @pairs TABLE (Loser bigint PRIMARY KEY, Survivor bigint);
INSERT INTO @pairs VALUES (13530,13491),(13509,13535),(13548,13574),(9853,31);

-- COALESCE survivor contact fields from loser + bump corroborations
UPDATE s SET
  s.Email = COALESCE(s.Email, l.Email),
  s.LinkedinUrl = COALESCE(s.LinkedinUrl, l.LinkedinUrl),
  s.Corroborations = s.Corroborations + l.Corroborations,
  s.LastSeenAtUtc = @now, s.UpdatedAtUtc = @now
FROM opportunities.IntelPerson s
JOIN @pairs pr ON pr.Survivor = s.Id
JOIN opportunities.IntelPerson l ON l.Id = pr.Loser;

-- stage loser affils with survivor-key
DECLARE @a TABLE (AffId bigint, Survivor bigint, NewKey char(40));
INSERT INTO @a (AffId, Survivor, NewKey)
SELECT af.Id, pr.Survivor,
  CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
     CAST(pr.Survivor AS VARCHAR(20)) + '|' + CAST(af.CanonicalOrgId AS VARCHAR(20)) + '|' +
     REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
       LOWER(LTRIM(RTRIM(COALESCE(af.Title,N'')))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N'')
   AS VARCHAR(8000))), 2)
FROM opportunities.IntelPersonAffiliation af JOIN @pairs pr ON pr.Loser = af.IntelPersonId
WHERE af.RetiredAtUtc IS NULL;

-- retire loser affils that collide with an existing live survivor affil
UPDATE af SET RetiredAtUtc = @now, RetiredReason = N'person-dedup 2026-06-18: dup of survivor affiliation', UpdatedAtUtc = @now
FROM opportunities.IntelPersonAffiliation af JOIN @a a ON a.AffId = af.Id
WHERE EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation x WHERE x.NaturalKey = a.NewKey AND x.Id <> af.Id AND x.RetiredAtUtc IS NULL);

-- re-point the rest to the survivor
UPDATE af SET IntelPersonId = a.Survivor, NaturalKey = a.NewKey, UpdatedAtUtc = @now
FROM opportunities.IntelPersonAffiliation af JOIN @a a ON a.AffId = af.Id
WHERE af.RetiredAtUtc IS NULL;

-- fix the correct spelling on the Chase Rongé survivor
UPDATE opportunities.IntelPerson SET DisplayName = N'Chase Rongé', UpdatedAtUtc = @now WHERE Id = 13491;

-- retire the loser people
UPDATE opportunities.IntelPerson SET RetiredAtUtc = @now, RetiredReason = N'person-dedup 2026-06-18: merged into survivor', UpdatedAtUtc = @now
WHERE Id IN (SELECT Loser FROM @pairs);

COMMIT;

SELECT p.Id, p.DisplayName, p.Email, p.Corroborations,
  (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.IntelPersonId=p.Id AND a.RetiredAtUtc IS NULL) AS Affils
FROM opportunities.IntelPerson p WHERE p.Id IN (13491,13535,13574,31) ORDER BY p.Id;
SELECT COUNT(*) AS LiveLosers FROM opportunities.IntelPerson WHERE Id IN (13530,13509,13548,9853) AND RetiredAtUtc IS NULL;
