USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 215: email enrichment (Apollo org-verified + Hunter) for the architect
  SE-decision contacts (mig 212) and Sacramento/East Bay market contacts (mig 214).
  EmailSource: asis=Apollo verified; Hunter=Hunter w/ sources; PatternInferred=
  Hunter 0-source pattern. Email COALESCEd. 28 of 32 found (Alex Gutierrez, Andy
  Ball, Jeremy Harris, John Wright not found — left without email).
*/

BEGIN TRAN;

DECLARE @w TABLE (PersonName nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint);
INSERT INTO @w VALUES
 (N'Radziah Loh',N'rloh@tca-arch.com',N'asis',85),
 (N'Teresa Ruiz',N'truiz@tca-arch.com',N'asis',85),
 (N'Douglas Oliver',N'doliver@tca-arch.com',N'asis',85),
 (N'Eric Olsen',N'eric@tca-arch.com',N'asis',85),
 (N'Steve Hutson',N'shutson@tca-arch.com',N'Hunter',96),
 (N'Frank Landry',N'fal@carrierjohnson.com',N'asis',85),
 (N'David Huchteman',N'deh@carrierjohnson.com',N'asis',85),
 (N'Megan Aasen',N'maasen@bdearch.com',N'asis',85),
 (N'Joseph Estefanos',N'jestefanos@bdearch.com',N'Hunter',98),
 (N'Magda Esperanzate',N'mesperanzate@bdearch.com',N'asis',85),
 (N'Daniel Cusick',N'daniel.cusick@smithgroup.com',N'PatternInferred',83),
 (N'Victoria Vicente',N'vvicente@hga.com',N'asis',85),
 (N'Craig McInroy',N'cmcinroy@hga.com',N'asis',85),
 (N'Karva Sykes',N'ksykes@hga.com',N'asis',85),
 (N'Greg Osecheck',N'gosecheck@hga.com',N'Hunter',97),
 (N'Michael Miller',N'mmiller@steinberg.us.com',N'PatternInferred',97),
 (N'Duncan Paterson',N'duncan_paterson@gensler.com',N'asis',85),
 (N'Wil Wong',N'wwong@ktgy.com',N'PatternInferred',95),
 (N'Brad Golba',N'bgolba@ktgy.com',N'asis',85),
 (N'Pieter Berger',N'pberger@mve-architects.com',N'Hunter',96),
 (N'Matthew McLarand',N'mmclarand@mve-architects.com',N'asis',85),
 (N'Sotiris Kolokotronis',N'sotiris@skkdevelopments.com',N'Hunter',97),
 (N'Marisa Kolokotronis',N'marisa@skkdevelopments.com',N'asis',85),
 (N'Patrick Kennedy',N'patrick@panoramicinterests.com',N'PatternInferred',98),
 (N'Michael Johnson',N'mjohnson@urbancorellc.com',N'Hunter',99),
 (N'Ken Lowney',N'ken@lowneyarch.com',N'asis',85),
 (N'Brady Smith',N'bradys@lpadesignstudios.com',N'PatternInferred',83),
 (N'Kevin Sauser',N'ksauser@c2k.com',N'asis',85);

;WITH w AS (
  SELECT *, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
    AS VARCHAR(8000))),2) AS NK FROM @w)
UPDATE p
SET Email=COALESCE(p.Email,w.Email), EmailSource=COALESCE(p.EmailSource,w.Src), EmailConfidence=COALESCE(p.EmailConfidence,w.Conf),
    EmailCheckedAtUtc=sysdatetimeoffset(), LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
FROM opportunities.IntelPerson p JOIN w ON w.NK=p.NaturalKey;
PRINT CONCAT('Migration 215: batch email-enriched: ', @@ROWCOUNT);

COMMIT TRAN;
GO
