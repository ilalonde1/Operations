USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 223: Purpose Driven Development (id 28093, Deltek client
  c14869b6d1504dbfacc080e9ebbc6ad5) enrichment. Bare canonical before this pass.
  Woman-led real estate development MANAGER / owner's rep in Vancouver (HQ
  #502-134 Abbott St); assembles project teams (incl. SE) for non-market /
  affordable / Indigenous / women-led housing in the Lower Mainland. Flagship:
  Soroptimist House ($85M, 546 W 13th Ave, Vancouver). Contacts from Hunter
  domain-search purposedrivenroi.com (pattern {first}), 2026-06-19.
*/

DECLARE @Provider nvarchar(60) = N'PurposeDrivenEnrichment';
DECLARE @PDD bigint = 28093;

BEGIN TRAN;

-- Org profile: website + notes (preserve any existing).
UPDATE opportunities.CanonicalOrg
   SET Website = COALESCE(Website, N'https://www.purposedrivenroi.com'),
       Notes = COALESCE(Notes, N'Woman-led real estate development manager / owner''s rep (CEO Carla Guerrera). HQ #502-134 Abbott St, Vancouver. Assembles project teams (incl. structural engineer) for non-market/affordable/Indigenous/women-led housing in the Lower Mainland. Flagship: Soroptimist House ($85M, 546 W 13th Ave). Deltek record flagged USD currency - confirm with Daler.'),
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = @PDD;

-- Enrichment anchor row.
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT @PDD AS OrgId) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @enr bigint = (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId=@PDD AND ProviderName=@Provider);

DECLARE @people TABLE (PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Conf tinyint, Note nvarchar(400));
INSERT INTO @people VALUES
 (N'Carla Guerrera', N'Founder & CEO',                N'carla@purposedrivenroi.com',    98, N'Founder/CEO - relationship owner. ~$1B+ in complex mixed-use over 20 years.'),
 (N'Kyle Foot',      N'Senior Development Manager',   N'kyle@purposedrivenroi.com',     96, N'Senior Development Manager - selects the structural engineer on a given project.'),
 (N'Annelise Veen',  N'Strategic Projects Manager',   N'annelise@purposedrivenroi.com', 99, NULL),
 (N'Sydney Edwards', N'Development Coordinator',      N'sydney@purposedrivenroi.com',   97, NULL);

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

;WITH aff AS (
  SELECT ip.Id AS PersonId, p.Title,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(@PDD AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=@PDD
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, @PDD, S.Title, 1);

PRINT 'Migration 223: Purpose Driven Development enrichment applied.';
COMMIT TRAN;
GO
