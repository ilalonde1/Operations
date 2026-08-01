USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 225: decompose / clean the 90 JV-string IntelPerson rows.
  - Explode 4 genuine multi-person name-lists into individuals, re-affiliated to
    the correct org (the JYOM partners row was wrongly affiliated to Pinnacle ->
    re-homed to JYOM Architecture id 55064).
  - Rename the De Cotiis nickname row (Don / Donato -> Donato).
  - Soft-retire the remaining ~85 junk rows (dual-title placeholders like
    'Mayor / CAO', org-as-person like 'Bird Construction / Concert Infrastructure')
    via RetiredAtUtc + reason (reversible). None had personal emails.
*/
DECLARE @Provider nvarchar(60) = N'PersonDecomposition';
BEGIN TRAN;

-- Enrichment anchors for the 4 target orgs (SourceEnrichmentId is NOT NULL).
DECLARE @orgs TABLE (OrgId bigint);
INSERT INTO @orgs VALUES (301),(69767),(55064),(39815);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

-- Individuals to create from the name-lists.
DECLARE @new TABLE (FullName nvarchar(200), OrgId bigint, Title nvarchar(200));
INSERT INTO @new VALUES
 (N'Bruce Boychuk', 301, N'Chief Financial Officer'),
 (N'Jason Cheung',  301, N'Chief Financial Officer'),
 (N'Richard Lai',    69767, N'Partner'),
 (N'Myles Craig',    69767, N'Partner'),
 (N'Wes Wilson',     69767, N'Partner'),
 (N'Avery Guthrie',  69767, N'Partner'),
 (N'Tomer Diamant',  69767, N'Partner'),
 (N'Eric Elliott Lai',   55064, N'Partner, JYOM Architecture'),
 (N'Kandice Emmie Kwok', 55064, N'Partner, JYOM Architecture'),
 (N'Mireille Brunet',  39815, N'Owner (2nd generation)'),
 (N'Chantal Brunet',   39815, N'Owner (2nd generation)'),
 (N'Stephanie Brunet', 39815, N'Owner (2nd generation)');

-- Create the individuals.
;WITH src AS (
  SELECT n.FullName, n.OrgId, n.Title, LOWER(LTRIM(RTRIM(n.FullName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(n.FullName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip,
    e.Id AS EnrId
  FROM @new n
  JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=n.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPerson AS T
USING (SELECT FullName, OrgId, Title, Lowered, Strip, EnrId, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations)
  VALUES (@Provider, S.EnrId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.FullName, S.Lowered, 1);

-- Affiliate each individual to its org.
;WITH aff AS (
  SELECT ip.Id AS PersonId, n.OrgId, n.Title, e.Id AS EnrId,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(n.OrgId AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(n.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @new n
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(n.FullName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2)
  JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=n.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, S.EnrId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, S.OrgId, S.Title, 1);

-- Rename the nickname-pair (single person).
UPDATE opportunities.IntelPerson SET DisplayName=N'Donato De Cotiis', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=10640;

-- Retire the 4 exploded source rows + their (now-superseded) affiliations.
UPDATE opportunities.IntelPersonAffiliation SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Person row exploded into individuals (migration 225)', IsCurrent=0 WHERE IntelPersonId IN (5773,8189,10380,10795) AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPerson SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Exploded into individual person rows (migration 225)', UpdatedAtUtc=sysdatetimeoffset() WHERE Id IN (5773,8189,10380,10795);

-- Soft-retire the remaining JV-string junk person rows (dual-title placeholders /
-- org-as-person; no personal emails). Reversible.
DECLARE @junk TABLE (Id bigint);
INSERT INTO @junk SELECT Id FROM opportunities.IntelPerson WHERE RetiredAtUtc IS NULL AND DisplayName LIKE '% / %';
UPDATE opportunities.IntelPersonAffiliation SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'JV-string junk person row (migration 225)', IsCurrent=0 WHERE IntelPersonId IN (SELECT Id FROM @junk) AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPerson SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'JV-string junk: dual-title placeholder or org-as-person, no real individual (migration 225)', UpdatedAtUtc=sysdatetimeoffset() WHERE Id IN (SELECT Id FROM @junk);

DECLARE @junkCount int = (SELECT COUNT(*) FROM @junk);
PRINT 'Migration 225: exploded 4 name-lists into 12 individuals, renamed 1, retired ' + CAST(@junkCount AS varchar(10)) + ' junk rows.';
COMMIT TRAN;
GO
