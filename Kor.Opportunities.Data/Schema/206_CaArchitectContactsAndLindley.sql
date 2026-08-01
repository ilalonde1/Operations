USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 206: prime-consultant path - ingest named contacts at the LA/SD
  architecture firms KOR should team with (research 2026-06-18,
  docs/ca-research-raw/ca-la-sd-architect-strategy-2026-06-18.md), create the one
  missing firm (HKS), and record KOR's SD beachhead (The Lindley, where KOR is SE
  of record for JWDA/Toll Brothers) as a queryable project. Same IntelPerson
  contract as migration 202; affiliations matched on (person,org).
*/

DECLARE @Provider nvarchar(60) = N'CaArchitectResearch';

BEGIN TRAN;

-- Create HKS (the one target firm not yet in the graph).
DECLARE @hks bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName = N'hks' AND RetiredAtUtc IS NULL);
IF @hks IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrg (DisplayName, Kind, CreatedAtUtc, UpdatedAtUtc)
    VALUES (N'HKS', N'Architect', sysdatetimeoffset(), sysdatetimeoffset());
    SET @hks = SCOPE_IDENTITY();
END;

DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200));
INSERT INTO @people (OrgId, PersonName, Title) VALUES
 (48,    N'Joseph O. Wong',  N'Founder & President'),                                  -- JWDA (KOR client: The Lindley)
 (75180, N'Jay Longo',       N'Principal & Managing Director, LA'),                    -- SCB (Onni gateway)
 (112,   N'Gordon Carrier',  N'Executive Chairman & Co-Founder'),                      -- Carrier Johnson
 (112,   N'Claudia Escala',  N'President, San Diego'),
 (112,   N'Marin Gertler',   N'Chief Design Officer'),
 (112,   N'Duane Hagewood',  N'Principal'),
 (68637, N'Simon Ha',        N'Managing Partner LA & Mixed-Use Practice Leader'),      -- Steinberg Hart
 (68637, N'Fredrik Nilsson', N'Design Director'),
 (68631, N'Kevin Heinly',    N'Co-Regional Managing Principal'),                       -- Gensler (exists)
 (68631, N'John Adams',      N'Co-Regional Managing Principal'),
 (68631, N'Kelly Farrell',   N'Lifestyle Sector Leader'),
 (@hks,  N'Greg Verabian',   N'Partner, Regional Practice Director (Mixed-Use)'),      -- HKS
 (@hks,  N'Scott Hunter',    N'Partner, FAIA'),
 (76890, N'Keith McCloskey', N'Principal'),                                            -- KTGY (exists)
 (76970, N'Eric Naslund',    N'Principal'),                                            -- Studio E (exists)
 (68910, N'Ricardo Rabines', N'Co-Founder'),                                          -- Safdie Rabines
 (68910, N'Taal Safdie',     N'Co-Founder'),
 (76952, N'Carl McLarand',   N'Chairman & CEO'),                                       -- MVE + Partners
 (76952, N'Kara Dunne',      N'Managing Principal'),
 (70108, N'Tom Hsieh',       N'President & CEO'),                                      -- AC Martin / Togawa Smith Martin
 (70108, N'Matthew Cobo',    N'Associate Principal');

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @people) AS S
   ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @Provider
WHEN NOT MATCHED THEN
  INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

;WITH src AS (
  SELECT DISTINCT p.PersonName,
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
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = sysdatetimeoffset(), Corroborations = T.Corroborations + 1, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1);

;WITH aff AS (
  SELECT ip.Id AS PersonId, p.OrgId, p.Title,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId = p.OrgId AND e.ProviderName = @Provider) AS EnrichmentId,
    CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey = CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2)
)
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId = S.PersonId AND T.CanonicalOrgId = S.OrgId
WHEN MATCHED THEN UPDATE SET Title = COALESCE(T.Title, S.Title), IsCurrent = 1, LastSeenAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, S.OrgId, S.Title, 1);

-- Beachhead: record The Lindley (KOR = SE of record) as a queryable CA project.
IF NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE Province=N'CA' AND SourceKey=N'korref:the-lindley')
BEGIN
    INSERT INTO opportunities.MajorProjectsInventory
        (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ArchitectName, ArchitectCanonicalOrgId,
         MunicipalityName, CompletionYear, ScheduleNotes, SourceUrl, RawJson)
    VALUES
        (N'CA', N'korref:the-lindley', N'The Lindley', N'Residential High-Rise', N'Built - KOR SE of record',
         N'Toll Brothers Apartment Living', N'JWDA (Joseph Wong Design Associates)', 48,
         N'San Diego', 2025,
         N'37-story, 363 units; GC Swinerton; opened Mar 2025. KOR Structural = structural engineer of record. KOR''s San Diego beachhead with JWDA.',
         N'https://korstructural.com', N'{"source":"research-2026-06-18","korRole":"SE of record"}');
END;

PRINT 'Migration 206: architect contacts + HKS + The Lindley beachhead recorded.';
COMMIT TRAN;
GO
