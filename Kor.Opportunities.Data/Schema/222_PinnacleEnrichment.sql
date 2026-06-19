USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 222: Pinnacle International (id 53665, Deltek CL00333) enrichment.
  - Backfill Hunter-verified exec emails on existing Anson Kwok / John Moy.
  - Correct Michael De Cotiis email: was akwok@ (Anson Kwok's address, mis-inferred,
    conf 55) -> md@pinnacleinternational.ca (Hunter-confirmed conf 95).
  - Add the construction / project-management team (the people who engage the SE)
    + the in-house architect, with Hunter domain-search emails (pattern {f}{last}).
  Source: Hunter domain-search pinnacleinternational.ca, 2026-06-19.
*/

DECLARE @Provider nvarchar(60) = N'PinnacleEnrichment';
DECLARE @PIN bigint = 53665;

BEGIN TRAN;

-- 1) Fix the mis-inferred Michael De Cotiis email (akwok@ belongs to Anson Kwok).
UPDATE opportunities.IntelPerson
   SET Email = N'md@pinnacleinternational.ca', EmailSource = N'Hunter', EmailConfidence = 95,
       EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = 1399;

-- 2) Backfill verified exec emails on existing rows (only where currently null).
UPDATE opportunities.IntelPerson
   SET Email = N'akwok@pinnacleinternational.ca', EmailSource = N'Hunter', EmailConfidence = 98,
       EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
 WHERE DisplayName = N'Anson Kwok' AND Email IS NULL;
UPDATE opportunities.IntelPerson
   SET Email = N'jmoy@pinnacleinternational.ca', EmailSource = N'Hunter', EmailConfidence = 96,
       EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
 WHERE DisplayName = N'John Moy' AND Email IS NULL;

-- 3) Enrichment anchor row.
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT @PIN AS OrgId) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @enr bigint = (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId=@PIN AND ProviderName=@Provider);

-- 4) New construction / PM team + in-house architect (Hunter {f}{last}).
DECLARE @people TABLE (PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Conf tinyint, Note nvarchar(400));
INSERT INTO @people VALUES
 (N'Luke Griffin',    N'Project Manager',         N'lgriffin@pinnacleinternational.ca', 99, N'Construction PM - engages structural engineer on Pinnacle projects.'),
 (N'Chris Eyles',     N'Project Manager',         N'ceyles@pinnacleinternational.ca',   97, N'Construction PM.'),
 (N'Joe Meola',       N'Project Manager',         N'jmeola@pinnacleinternational.ca',   96, N'Construction PM.'),
 (N'Alireza Partovi', N'Project Manager',         N'apartovi@pinnacleinternational.ca', 96, N'Construction PM.'),
 (N'Matias Gil',      N'Construction Coordinator',N'mgil@pinnacleinternational.ca',     99, NULL),
 (N'Daniel Bellows',  N'Site Superintendent',     N'dbellows@pinnacleinternational.ca', 97, NULL),
 (N'Benny Yeo',       N'Architect (in-house)',    N'byeo@pinnacleinternational.ca',     98, N'In-house design - relevant to SE coordination.');

;WITH src AS (
  SELECT p.PersonName, p.Title, p.Email, p.Conf, p.Note, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip
  FROM @people p)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Title, Email, Conf, Note, Lowered, Strip, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset(),
   Email=COALESCE(T.Email,S.Email), EmailSource=COALESCE(T.EmailSource,N'Hunter'), EmailConfidence=COALESCE(T.EmailConfidence,S.Conf), Notes=COALESCE(T.Notes,S.Note)
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations, Email, EmailSource, EmailConfidence, EmailCheckedAtUtc, Notes)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1, S.Email, N'Hunter', S.Conf, sysdatetimeoffset(), S.Note);

-- 5) Affiliations to Pinnacle International.
;WITH aff AS (
  SELECT ip.Id AS PersonId, p.Title,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(@PIN AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=@PIN
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, @PIN, S.Title, 1);

PRINT 'Migration 222: Pinnacle International enrichment applied.';
COMMIT TRAN;
GO
