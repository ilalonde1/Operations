USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 207: email enrichment for the LA/SD architect contacts (migration 206).
  EmailSource: 'asis' = Apollo email_status=verified; 'Hunter' = Hunter result with
  >=1 source; 'PatternInferred' = Hunter pattern (0 sources). Email COALESCEd.
  Gordon Carrier and Kara Dunne not found (left without email).
*/

BEGIN TRAN;

DECLARE @work TABLE (PersonName nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint);
INSERT INTO @work VALUES
 -- Apollo verified
 (N'Jay Longo',       N'jay.longo@scb.com',             N'asis', 85),
 (N'Claudia Escala',  N'cce@carrierjohnson.com',        N'asis', 85),
 (N'Marin Gertler',   N'mlg@carrierjohnson.com',        N'asis', 85),
 (N'Duane Hagewood',  N'dlh@carrierjohnson.com',        N'asis', 85),
 (N'Greg Verabian',   N'gverabian@hksinc.com',          N'asis', 85),
 (N'Scott Hunter',    N'shunter@hksinc.com',            N'asis', 85),
 (N'Ricardo Rabines', N'ricardo@safdierabines.com',     N'asis', 85),
 (N'Taal Safdie',     N'taal@safdierabines.com',        N'asis', 85),
 -- Hunter, source-backed
 (N'Joseph O. Wong',  N'jwong@jwdainc.com',             N'Hunter', 95),
 (N'Simon Ha',        N'sha@steinberghart.com',         N'Hunter', 96),
 (N'Fredrik Nilsson', N'fnilsson@steinberghart.com',    N'Hunter', 97),
 (N'John Adams',      N'john_adams@gensler.com',        N'Hunter', 83),
 (N'Kelly Farrell',   N'kelly_farrell@gensler.com',     N'Hunter', 80),
 (N'Carl McLarand',   N'cmclarand@mve-architects.com',  N'Hunter', 99),
 (N'Tom Hsieh',       N'tom.hsieh@acmartin.com',        N'Hunter', 96),
 -- Hunter pattern (0 sources, but consistent with firm pattern)
 (N'Matthew Cobo',    N'matthew.cobo@acmartin.com',     N'PatternInferred', 90);

;WITH w AS (
  SELECT *,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      AS VARCHAR(8000))),2) AS NK
  FROM @work
)
UPDATE p
SET Email = COALESCE(p.Email, w.Email),
    EmailSource = COALESCE(p.EmailSource, w.Src),
    EmailConfidence = COALESCE(p.EmailConfidence, w.Conf),
    EmailCheckedAtUtc = sysdatetimeoffset(),
    LastSeenAtUtc = sysdatetimeoffset(),
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p JOIN w ON w.NK = p.NaturalKey;
PRINT CONCAT('Architect contacts email-enriched: ', @@ROWCOUNT);

COMMIT TRAN;
GO
