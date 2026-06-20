USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 230: complete the HCMA (org 8799) senior decision-maker map.
  HCMA is now a confirmed active prime-consultant target (Maple Ridge Hammond
  Aquatics $227M + their 89-project civic portfolio). Add the senior
  principals/partners/directors surfaced by Hunter (hcma.ca, {f}.{last}) that
  were not yet in the graph. Tracy Liu (Dir, Community + Recreation) remains the
  primary rec/aquatic target; these round out firm leadership.
*/
DECLARE @HCMA bigint = 8799;
DECLARE @Provider nvarchar(60) = N'HcmaPrincipalsBackfill';
BEGIN TRAN;

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT @HCMA AS OrgId) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @enr bigint = (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId=@HCMA AND ProviderName=@Provider);

DECLARE @people TABLE (PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Conf tinyint);
INSERT INTO @people VALUES
 (N'Paul Fast',        N'Principal',                 N'p.fast@hcma.ca',      99),
 (N'Roger Hughes',     N'Partner',                   N'r.hughes@hcma.ca',    95),
 (N'Daniel Philippot', N'Senior Director',           N'd.philippot@hcma.ca', 97),
 (N'Dorian Resener',   N'Director of Architecture',  N'd.resener@hcma.ca',   98),
 (N'Heidi Nesbitt',    N'Principal',                 N'h.nesbitt@hcma.ca',   96);

;WITH src AS (
  SELECT p.PersonName, p.Title, p.Email, p.Conf, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip
  FROM @people p)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Title, Email, Conf, Lowered, Strip, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset(),
   Email=COALESCE(T.Email,S.Email), EmailSource=COALESCE(T.EmailSource,N'Hunter'), EmailConfidence=COALESCE(T.EmailConfidence,S.Conf)
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations, Email, EmailSource, EmailConfidence, EmailCheckedAtUtc)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1, S.Email, N'Hunter', S.Conf, sysdatetimeoffset());

;WITH aff AS (
  SELECT ip.Id AS PersonId, p.Title,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(@HCMA AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=@HCMA
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, @HCMA, S.Title, 1);

PRINT 'Migration 230: HCMA senior decision-maker map completed.';
COMMIT TRAN;
GO
