/*
    KorOpportunitiesDb migration 357: the six identity rows whose names were UTF-8 read as code page 1252.

    tools/BdIntelTextRepair (2026-10-15, commit 2df19d53) repaired 1,738 intel rows and deliberately left these
    six, because their keys belong to usp_ResolveOrCreateIntelPerson and the canonical resolver. Each was
    checked by hand the same day; the stored key of every row was reproduced from its garbled name first.

      IntelPerson 20021  Stéphane Chan (KMBR)          key = name|org:54571, no twin  -> name, NormalizedName, key
      IntelPerson 20087  Sebastian Salvadó (RIOS)      key = LinkedIn, no twin        -> name, NormalizedName
      IntelPerson 20065  François Marchand (AMA)       key = LinkedIn                 -> name, NormalizedName
                         (2944 'François Marchand' carries Timothy Ankenman's LinkedIn and a shared inbox: a
                          conflated row, left for a person to split. Not touched here.)
      IntelPerson 20042  Chase Rongé, AIA, NCARB (MVE) same person as 13491, which has his email and LinkedIn
                                                                                      -> name repaired, retired as duplicate
      IntelPerson 12442  Alberta Culture Ministry — Capital Projects
                         identical to 11539 (same org 789, title, day); 11539 holds the clean key
                                                                                      -> name repaired, retired as duplicate
      CanonicalOrg 271646 SF Public Works – Bureau of Architecture
                         name and FuzzyNormalizedName (value from CanonicalOrgResolver.NormalizeForFuzzyMatch).
                         271648 'San Francisco Public Works, Bureau of Architecture' is the same body; merging is
                         a delete and goes through tools/BdCanonicalDedup, not here.

    NormalizedName and the name-based key use usp_ResolveOrCreateIntelPerson's own formula (277).
    PART B runs in one transaction; any update that does not hit exactly one row rolls everything back.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ===== PART A: the six rows as they are =====
SELECT Id, DisplayName, NormalizedName, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 'live' ELSE 'retired' END AS state
FROM opportunities.IntelPerson WHERE Id IN (12442, 20021, 20042, 20065, 20087) ORDER BY Id;
SELECT Id, DisplayName, NormalizedName, FuzzyNormalizedName FROM opportunities.CanonicalOrg WHERE Id = 271646;
GO

-- ===== PART B: apply =====
DECLARE @n INT;
DECLARE @people TABLE (Id BIGINT PRIMARY KEY, OldKey CHAR(40) NOT NULL, NewName NVARCHAR(400) NOT NULL, KeyOrgId BIGINT NULL,
                       RetireAsDuplicateOf BIGINT NULL);
INSERT INTO @people VALUES
    (20021, '9AD0495EC21780CCCC03074E6F8C7A7DCA742FBF', N'St' + NCHAR(233) + N'phane Chan', 54571, NULL),
    (20087, 'DBAD2B0F37D7D300EBAA5877D5EBBC6A1B84EF2E', N'Sebastian Salvad' + NCHAR(243), NULL, NULL),
    (20065, '10DCE65FCEF51780A7CE2FDDD42A00548B1F5F38', N'Fran' + NCHAR(231) + N'ois Marchand', NULL, NULL),
    (20042, '1D9B7101837BE83448EE523E184901786B0A8777', N'Chase Rong' + NCHAR(233) + N', AIA, NCARB', NULL, 13491),
    (12442, 'FDCC3F1C8ACA4A390898350676B6B0F939EFBFF4', N'Alberta Culture Ministry ' + NCHAR(8212) + N' Capital Projects', NULL, 11539);

-- usp_ResolveOrCreateIntelPerson's NormalizedName formula, applied to the repaired name
DECLARE @norm TABLE (Id BIGINT PRIMARY KEY, NewNorm NVARCHAR(200) NOT NULL);
INSERT INTO @norm
SELECT Id, LEFT(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(NewName))),
             N' ', N''), N'.', N''), N',', N''), N'''', N''), N'-', N''), N'&', N''), N'/', N''), N'(', N''), N')', N''), N'+', N''), 200)
FROM @people;

BEGIN TRANSACTION;

UPDATE p
   SET p.DisplayName = x.NewName,
       p.NormalizedName = n.NewNorm,
       p.NaturalKey = CASE WHEN x.KeyOrgId IS NULL THEN p.NaturalKey
                           ELSE CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(n.NewNorm + N'|org:' + CONVERT(NVARCHAR(20), x.KeyOrgId) AS VARCHAR(8000))), 2) END,
       p.RetiredAtUtc = CASE WHEN x.RetireAsDuplicateOf IS NULL THEN p.RetiredAtUtc ELSE SYSDATETIMEOFFSET() END,
       p.RetiredReason = CASE WHEN x.RetireAsDuplicateOf IS NULL THEN p.RetiredReason
                              ELSE N'Duplicate of IntelPerson ' + CONVERT(NVARCHAR(20), x.RetireAsDuplicateOf)
                                   + N'; name had been UTF-8 read as Windows-1252 (migration 357).' END
  FROM opportunities.IntelPerson p
  JOIN @people x ON x.Id = p.Id
  JOIN @norm n ON n.Id = p.Id
 WHERE p.NaturalKey = x.OldKey AND p.RetiredAtUtc IS NULL;
SET @n = @@ROWCOUNT;
IF @n <> 5 THROW 50357, 'IntelPerson: expected 5 rows; a row changed since 2026-10-15. Nothing applied.', 1;

UPDATE a
   SET a.RetiredAtUtc = SYSDATETIMEOFFSET(),
       a.RetiredReason = N'Person retired as duplicate of IntelPerson ' + CONVERT(NVARCHAR(20), x.RetireAsDuplicateOf) + N' (migration 357).'
  FROM opportunities.IntelPersonAffiliation a
  JOIN @people x ON x.Id = a.IntelPersonId AND x.RetireAsDuplicateOf IS NOT NULL
 WHERE a.RetiredAtUtc IS NULL;
SELECT @@ROWCOUNT AS affiliations_retired_with_their_duplicate_person;

UPDATE opportunities.CanonicalOrg
   SET DisplayName = N'SF Public Works ' + NCHAR(8211) + N' Bureau of Architecture',
       FuzzyNormalizedName = N'sfpublicworks' + NCHAR(8211) + N'bureauofarchitecture'
 WHERE Id = 271646
   AND FuzzyNormalizedName = N'sfpublicworks' + NCHAR(226) + NCHAR(8364) + NCHAR(8220) + N'bureauofarchitecture';
SET @n = @@ROWCOUNT;
IF @n <> 1 THROW 50357, 'CanonicalOrg 271646: expected 1 row. Nothing applied.', 1;

COMMIT TRANSACTION;
SELECT 'applied' AS result;
GO

-- ===== PART C: check =====
SELECT Id, DisplayName, NormalizedName, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 'live' ELSE 'retired' END AS state, LEFT(RetiredReason, 60) AS retired_reason
FROM opportunities.IntelPerson WHERE Id IN (12442, 20021, 20042, 20065, 20087) ORDER BY Id;
SELECT Id, DisplayName, NormalizedName, FuzzyNormalizedName FROM opportunities.CanonicalOrg WHERE Id = 271646;
SELECT COUNT(*) AS person_names_still_garbled FROM opportunities.IntelPerson
WHERE DisplayName LIKE N'%' + NCHAR(226) + NCHAR(8364) + N'%' OR DisplayName COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'[' + NCHAR(160) + N'-' + NCHAR(191) + N']%';
GO
