USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 204: Hunter email-finder for execs Apollo missed (migration 202).
  EmailSource reflects trust: 'Hunter' for results backed by sources (high
  confidence), 'PatternInferred' for pattern guesses with 0 sources. Email is
  COALESCEd (never clobber). The Onni De Cotiis addresses use Hunter's
  hyphenated "de-cotiis" guess which is UNVERIFIED (inconsistent with how Hunter
  rendered Beau Jarvis) — flagged in Notes; treat as a starting point for outreach.
*/

BEGIN TRAN;

DECLARE @work TABLE (PersonName nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));
INSERT INTO @work VALUES
 (N'Gino Canori',         N'gcanori@related.com',        N'Hunter',          96, NULL),
 (N'Nicholas Vanderboom', N'nvanderboom@related.com',    N'Hunter',          98, NULL),
 (N'Jason Kennedy',       N'jkennedy@carmelpartners.com',N'Hunter',          99, NULL),
 (N'Ron Zeff',            N'rzeff@carmelpartners.com',   N'Hunter',          95, NULL),
 (N'Beau Jarvis',         N'bjarvis@onni.com',           N'PatternInferred', 80, N'Hunter pattern (0 sources).'),
 (N'Rossano De Cotiis',   N'rde-cotiis@onni.com',        N'PatternInferred', 72, N'Hunter pattern (0 sources); de-cotiis hyphenation unverified.'),
 (N'Morris De Cotiis',    N'mde-cotiis@onni.com',        N'PatternInferred', 72, N'Hunter pattern (0 sources); de-cotiis hyphenation unverified.'),
 (N'Giulio De Cotiis',    N'gde-cotiis@onni.com',        N'PatternInferred', 72, N'Hunter pattern (0 sources); de-cotiis hyphenation unverified.'),
 (N'Paolo De Cotiis',     N'pde-cotiis@onni.com',        N'PatternInferred', 72, N'Hunter pattern (0 sources); de-cotiis hyphenation unverified.');

;WITH w AS (
  SELECT *,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      AS VARCHAR(8000))),2) AS NK
  FROM @work
)
UPDATE p
SET Email           = COALESCE(p.Email, w.Email),
    EmailSource     = COALESCE(p.EmailSource, w.Src),
    EmailConfidence = COALESCE(p.EmailConfidence, w.Conf),
    EmailCheckedAtUtc = sysdatetimeoffset(),
    Notes           = COALESCE(p.Notes, w.Note),
    LastSeenAtUtc   = sysdatetimeoffset(),
    UpdatedAtUtc    = sysdatetimeoffset()
FROM opportunities.IntelPerson p JOIN w ON w.NK = p.NaturalKey;
PRINT CONCAT('Hunter-enriched execs: ', @@ROWCOUNT);

SELECT p.DisplayName, p.Email, p.EmailSource, p.EmailConfidence
FROM @work wk JOIN opportunities.IntelPerson p
  ON p.NaturalKey = CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
         LOWER(LTRIM(RTRIM(wk.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2)
ORDER BY p.EmailSource, p.DisplayName;

COMMIT TRAN;
GO
