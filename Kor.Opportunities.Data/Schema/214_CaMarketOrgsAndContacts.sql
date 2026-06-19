USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 214: Sacramento/East Bay developer + architect decision-makers and
  the orgs that host them (research 2026-06-18). Creates the few orgs not yet in
  the graph (most competitor SEs already exist: Miyamoto, Tipping, Rutherford+
  Chekene, SGH, Murphy Burr Curry). Element Structural added. Contacts via the
  IntelPerson contract; emails Apollo/Hunter-verified in the next migration.
*/

DECLARE @Provider nvarchar(60) = N'CaMarketResearch';

BEGIN TRAN;

-- Create missing orgs (get-or-create by NormalizedName).
DECLARE @element bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'elementstructuralengineers' AND RetiredAtUtc IS NULL);
IF @element IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'Element Structural Engineers',N'Competitor',sysdatetimeoffset(),sysdatetimeoffset()); SET @element=SCOPE_IDENTITY(); END;
DECLARE @owow bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'owow' AND RetiredAtUtc IS NULL);
IF @owow IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'oWOW',N'Developer',sysdatetimeoffset(),sysdatetimeoffset()); SET @owow=SCOPE_IDENTITY(); END;
DECLARE @panoramic bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'panoramicinterests' AND RetiredAtUtc IS NULL);
IF @panoramic IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'Panoramic Interests',N'Developer',sysdatetimeoffset(),sysdatetimeoffset()); SET @panoramic=SCOPE_IDENTITY(); END;
DECLARE @urbancore bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'urbancoredevelopment' AND RetiredAtUtc IS NULL);
IF @urbancore IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'UrbanCore Development',N'Developer',sysdatetimeoffset(),sysdatetimeoffset()); SET @urbancore=SCOPE_IDENTITY(); END;
DECLARE @lowney bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'lowneyarchitecture' AND RetiredAtUtc IS NULL);
IF @lowney IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'Lowney Architecture',N'Architect',sysdatetimeoffset(),sysdatetimeoffset()); SET @lowney=SCOPE_IDENTITY(); END;
DECLARE @c2k bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'c2karchitecture' AND RetiredAtUtc IS NULL);
IF @c2k IS NULL BEGIN INSERT INTO opportunities.CanonicalOrg(DisplayName,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES(N'C2K Architecture',N'Architect',sysdatetimeoffset(),sysdatetimeoffset()); SET @c2k=SCOPE_IDENTITY(); END;
DECLARE @skk bigint = 76872, @lpa bigint = 68930;

DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200));
INSERT INTO @people VALUES
 (@skk,       N'Sotiris Kolokotronis', N'Founder & President (SKK Developments)'),
 (@skk,       N'Marisa Kolokotronis',  N'Senior VP (SKK Developments)'),
 (@owow,      N'Andy Ball',            N'President (oWOW)'),
 (@owow,      N'Jeremy Harris',        N'Director of Development (oWOW)'),
 (@panoramic, N'Patrick Kennedy',      N'Owner / Developer (Panoramic Interests)'),
 (@urbancore, N'Michael E. Johnson',   N'President & CEO (UrbanCore Development)'),
 (@lowney,    N'Ken Lowney',           N'Principal (Lowney Architecture)'),
 (@lpa,       N'Brady Smith',          N'President (LPA Design Studios)'),
 (@c2k,       N'Kevin Sauser',         N'Principal (C2K Architecture)'),
 (@c2k,       N'John Wright',          N'Principal (C2K Architecture)');

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @people) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

;WITH src AS (
  SELECT DISTINCT p.PersonName, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider) AS EnrichmentId
  FROM @people p)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Lowered, Strip, EnrichmentId, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1);

;WITH aff AS (
  SELECT ip.Id AS PersonId, p.OrgId, p.Title,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider) AS EnrichmentId,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, S.OrgId, S.Title, 1);

PRINT 'Migration 214: Sacramento/East Bay orgs + decision-makers ingested.';
COMMIT TRAN;
GO
