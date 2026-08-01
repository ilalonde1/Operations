USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 210: emails for the Onni decision-makers (migration 209).
  Mark Spector: mspector@onni.com — Apollo verified AND Hunter-corroborated (asis).
  Apriano Meola: ameola@onni.com — Hunter (81); fits Onni's verified {initial}{last}
  pattern (mspector, rdecotiis), so preferred over Apollo's inconsistent apriano@.
  Email COALESCEd.
*/

BEGIN TRAN;

DECLARE @work TABLE (PersonName nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint);
INSERT INTO @work VALUES
 (N'Mark Spector',  N'mspector@onni.com', N'asis',   85),
 (N'Apriano Meola', N'ameola@onni.com',   N'Hunter', 81);

;WITH w AS (
  SELECT *, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
    AS VARCHAR(8000))),2) AS NK
  FROM @work)
UPDATE p
SET Email=COALESCE(p.Email,w.Email), EmailSource=COALESCE(p.EmailSource,w.Src), EmailConfidence=COALESCE(p.EmailConfidence,w.Conf),
    EmailCheckedAtUtc=sysdatetimeoffset(), LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
FROM opportunities.IntelPerson p JOIN w ON w.NK=p.NaturalKey;
PRINT CONCAT('Onni contacts email-enriched: ', @@ROWCOUNT);

COMMIT TRAN;
GO
