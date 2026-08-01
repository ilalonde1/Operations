USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 226: final person/affiliation hygiene.
  A) De Cotiis person dedup - two distinct people, each with duplicate rows:
     Michael De Cotiis @ Pinnacle {survivor 1399, loser 13506 "Mike"};
     Donato/Don De Cotiis @ Amacon {survivor 902, losers 10640, 7340 "Don"}.
     Loser affiliations are duplicates of the survivor's -> retire loser affs +
     loser persons (kept separate: Michael != Donato).
  B) Live orphan affiliations on retired JV-string design-arch orgs:
     repoint to the live survivor where one exists (Arcadis 75987->153 IBI Group;
     CEI 69948->4552), else retire (Arney Fender 69887, Aedas 71197 - no live lead).
*/
BEGIN TRAN;

-- A) De Cotiis: retire duplicate loser affiliations, then loser persons.
UPDATE opportunities.IntelPersonAffiliation
   SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'De Cotiis person dedup (migration 226)', IsCurrent=0
 WHERE IntelPersonId IN (13506,10640,7340) AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPerson
   SET RetiredAtUtc=sysdatetimeoffset(),
       RetiredReason=N'Duplicate of Michael De Cotiis (1399) / Donato De Cotiis (902) (migration 226)',
       UpdatedAtUtc=sysdatetimeoffset()
 WHERE Id IN (13506,10640,7340);

-- B) Orphan repoints. Recompute affiliation NaturalKey for the new org; if the
--    target already has the same person+title affiliation, retire the orphan instead.
DECLARE @repoint TABLE (FromOrg bigint, ToOrg bigint);
INSERT INTO @repoint VALUES (75987,153),(69948,4552);

-- retire orphans that would collide at the target (same person already affiliated there)
UPDATE a SET a.RetiredAtUtc=sysdatetimeoffset(), a.RetiredReason=N'Orphan on retired org; target already has person (migration 226)', a.IsCurrent=0
FROM opportunities.IntelPersonAffiliation a
JOIN @repoint r ON r.FromOrg=a.CanonicalOrgId
WHERE a.RetiredAtUtc IS NULL
  AND EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation b WHERE b.IntelPersonId=a.IntelPersonId AND b.CanonicalOrgId=r.ToOrg AND b.RetiredAtUtc IS NULL);

-- repoint the rest to the survivor + recompute NaturalKey
UPDATE a
   SET a.CanonicalOrgId=r.ToOrg,
       a.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(a.IntelPersonId AS varchar(20)),'|',CAST(r.ToOrg AS varchar(20)),'|',
         REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
           LOWER(LTRIM(RTRIM(ISNULL(a.Title,N'')))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2),
       a.UpdatedAtUtc=sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation a
JOIN @repoint r ON r.FromOrg=a.CanonicalOrgId
WHERE a.RetiredAtUtc IS NULL;

-- retire orphans with no live lead org (Arney Fender Katsalidis 69887, Aedas 71197)
UPDATE opportunities.IntelPersonAffiliation
   SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Orphan on retired JV-string design-arch org; no live lead (migration 226)', IsCurrent=0
 WHERE CanonicalOrgId IN (69887,71197) AND RetiredAtUtc IS NULL;

PRINT 'Migration 226: De Cotiis dedup + orphan affiliation cleanup applied.';
COMMIT TRAN;
GO
