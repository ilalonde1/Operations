USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 220: ITC Construction Group decision-makers for the KOR/ITC BC teaming
  pursuit (email FW: KOR/ITC Collaboration, 2026-06-19). ITC Construction Group
  (id 70926) is an existing KOR Deltek client. Warm relationships: Omar Alcazar
  (KOR) <-> Manit Saiyadh (ITC); John Markulin (KOR) <-> Brad Burnett (ITC Pres).
  Contacts from Hunter domain-search itc-group.com (pattern {f}{last}) + Apollo
  (Manit verified). Affiliations carry titles + the relationship note.
*/

DECLARE @Provider nvarchar(60) = N'ItcCollaboration';
DECLARE @ITC bigint = 70926;

BEGIN TRAN;

DECLARE @people TABLE (PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));
INSERT INTO @people VALUES
 (N'Brad Burnett',    N'President',                              N'bburnett@itc-group.com',  N'Hunter', 80, N'PRIMARY OUTREACH TARGET - John Markulin (KOR) knows him. Top decision-maker.'),
 (N'Manit Saiyadh',   N'Construction Manager',                  N'msaiyadh@itc-group.com',  N'asis',   85, N'Omar Alcazar (KOR) relationship; took Omar''s call re: BC Housing teaming, June 2026.'),
 (N'David Carlton',   N'Senior VP, Preconstruction',            N'dcarlton@itc-group.com',  N'Hunter', 80, N'Teaming/bid decision-maker - preconstruction assembles the consultant team (incl. SE) on bids.'),
 (N'Josh Muise',      N'Preconstruction Director',              N'jmuise@itc-group.com',    N'Hunter', 80, NULL),
 (N'Aaron Plamondon', N'Preconstruction Manager',               N'aplamondon@itc-group.com',N'Hunter', 80, NULL),
 (N'Mathias Graf',    N'Senior VP, Operations',                 N'mgraf@itc-group.com',     N'Hunter', 80, NULL),
 (N'Vincent Lee',     N'VP, Finance',                           N'vlee@itc-group.com',      N'Hunter', 80, NULL),
 (N'Jaret Holden',    N'Project Director',                      N'jholden@itc-group.com',   N'Hunter', 80, NULL),
 (N'Jason Arnold',    N'Project Director',                      N'jarnold@itc-group.com',   N'Hunter', 80, NULL),
 (N'Kerry McCormick', N'Project Director',                      N'kmccormick@itc-group.com',N'Hunter', 80, NULL);

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT @ITC AS OrgId) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @enr bigint = (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId=@ITC AND ProviderName=@Provider);

;WITH src AS (
  SELECT p.PersonName, p.Email, p.Src, p.Conf, p.Note, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip
  FROM @people p)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Email, Src, Conf, Note, Lowered, Strip, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset(),
   Email=COALESCE(T.Email,S.Email), EmailSource=COALESCE(T.EmailSource,S.Src), EmailConfidence=COALESCE(T.EmailConfidence,S.Conf), Notes=COALESCE(T.Notes,S.Note)
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations, Email, EmailSource, EmailConfidence, EmailCheckedAtUtc, Notes)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1, S.Email, S.Src, S.Conf, sysdatetimeoffset(), S.Note);

;WITH aff AS (
  SELECT ip.Id AS PersonId, p.Title,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(@ITC AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=@ITC
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, @ITC, S.Title, 1);

PRINT 'Migration 220: ITC Construction Group contacts ingested.';
COMMIT TRAN;
GO
