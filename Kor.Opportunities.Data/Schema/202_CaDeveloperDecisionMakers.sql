USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 202: ingest named SE-decision-makers at the most CA-active developers
  (research 2026-06-18, docs/ca-research-raw/ca-developer-se-selection-2026-06-18.md).
  Same IntelPerson contract as migration 199. Affiliations carry titles and are
  matched on (person, org) so existing contacts (John Wayland, Will Cipes) get a
  titled affiliation rather than a duplicate.
*/

DECLARE @Commit bit = 1;
DECLARE @Provider nvarchar(60) = N'CaDeveloperResearch';

BEGIN TRAN;

DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200));
INSERT INTO @people (OrgId, PersonName, Title) VALUES
 -- Onni Group (38949) — Vancouver-HQ, self-GC; family controls SE selection
 (38949, N'Rossano De Cotiis',   N'President'),
 (38949, N'Morris De Cotiis',    N'Construction & Property Oversight'),
 (38949, N'Giulio De Cotiis',    N'Construction & Property Oversight'),
 (38949, N'Beau Jarvis',         N'VP Development (Los Angeles)'),
 (38949, N'Paolo De Cotiis',     N'International'),
 -- Holland Partner Group (68644) — Deltek-linked; Holland Construction self-performs
 (68644, N'Tom Warren',          N'President, Development'),
 (68644, N'Greg Thomas',         N'President, Holland Construction'),
 (68644, N'John Wayland',        N'Executive Managing Director, NorCal Development'),
 -- Related California (68645) — owner-controlled SE selection
 (68645, N'Ann Silverberg',      N'President & CEO, Affordable (PNW mandate)'),
 (68645, N'Phoebe Yee',          N'EVP Design (market-rate SE gatekeeper)'),
 (68645, N'Gino Canori',         N'CEO'),
 (68645, N'Nicholas Vanderboom', N'COO'),
 -- Carmel Partners (68641) — vertically integrated; SE internal
 (68641, N'Adam Mayer',          N'SVP Development & Capital Projects'),
 (68641, N'Jason Kennedy',       N'Director, Development'),
 (68641, N'Ron Zeff',            N'Founder & CEO'),
 (68641, N'Will Cipes',          N'SVP Development, SoCal'),
 -- Sutter Health (68855) — IPD; owner co-selects SE at inception
 (68855, N'Larry Kollerer',      N'Executive Director, Facility & Property Services'),
 (68855, N'Warner Thomas',       N'CEO'),
 (68855, N'Kevin Manemann',      N'COO'),
 -- Brookfield Residential (53589) — self-perform GC on flagship
 (53589, N'Adrian Foley',        N'CEO'),
 (53589, N'Josh Roden',          N'President, NorCal'),
 (53589, N'Nicole Burdette',     N'President, SoCal'),
 -- Crescent Heights (68642) — developer-owner; SE at principal level
 (68642, N'Bruce Menin',         N'Principal');

-- 1) Enrichment parent per org.
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @people) AS S
   ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @Provider
WHEN NOT MATCHED THEN
  INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

-- 2) Upsert people (NaturalKey = SHA1 of full-stripped lowercase name).
;WITH src AS (
  SELECT DISTINCT
    p.PersonName,
    LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId = p.OrgId AND e.ProviderName = @Provider) AS EnrichmentId
  FROM @people p
)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Lowered, Strip, EnrichmentId,
              CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(Strip AS VARCHAR(8000))), 2) AS NK FROM src) AS S
   ON T.NaturalKey = S.NK
WHEN MATCHED THEN
  UPDATE SET LastSeenAtUtc = sysdatetimeoffset(), Corroborations = T.Corroborations + 1, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
          FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK,
          sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(),
          S.PersonName, S.Lowered, 1);

-- 3) Upsert affiliations matched on (person, org) so existing contacts aren't duplicated.
;WITH aff AS (
  SELECT
    ip.Id AS PersonId, p.OrgId, p.Title,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId = p.OrgId AND e.ProviderName = @Provider) AS EnrichmentId,
    CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
      CONCAT(CAST(ip.Id AS varchar(20)), '|', CAST(p.OrgId AS varchar(20)), '|',
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
          LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      ) AS VARCHAR(8000))), 2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      AS VARCHAR(8000))), 2)
)
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S
   ON T.IntelPersonId = S.PersonId AND T.CanonicalOrgId = S.OrgId
WHEN MATCHED THEN
  UPDATE SET Title = COALESCE(T.Title, S.Title), IsCurrent = 1, LastSeenAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
          FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK,
          sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(),
          S.PersonId, S.OrgId, S.Title, 1);

SELECT o.DisplayName AS Org, ip.DisplayName AS Person, a.Title, ip.Email
FROM @people p
JOIN opportunities.CanonicalOrg o ON o.Id = p.OrgId
JOIN opportunities.IntelPerson ip ON ip.NormalizedName = LOWER(LTRIM(RTRIM(p.PersonName)))
LEFT JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = ip.Id AND a.CanonicalOrgId = p.OrgId
ORDER BY o.DisplayName, ip.DisplayName;

IF @Commit = 1 BEGIN COMMIT TRAN; PRINT 'Migration 202 COMMITTED.'; END
ELSE BEGIN ROLLBACK TRAN; PRINT 'Migration 202 DRY-RUN rolled back.'; END
GO
