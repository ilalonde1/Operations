USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 209: decompose the Onni LA SEOR research (2026-06-18).
  Verdict: Glotman Simpson (Competitor 38926) is the verified Onni LA incumbent on
  the BUILT/approved towers (820 Olive, Olympic+Hill, Times Mirror Square). The
  pre-construction towers using SCB (not Chris Dikeakos) break that Vancouver
  pipeline => SE seat OPEN; door is SCB (Jay Longo), sponsor Mark Spector.
  - Annotate SEOR status on Onni towers; set SCB as architect on the open ones.
  - Add the open-seat towers missing from MPI (5700 Wilshire, 601 N Brand, 1360 Vine).
  - Retire a duplicate Olympic+Hill row.
  - Add Onni decision-makers Mark Spector + Apriano Meola.
*/

DECLARE @Provider nvarchar(60) = N'CaDeveloperResearch';
DECLARE @SCB bigint = 75180, @Onni bigint = 38949, @OnniCA bigint = 167;

BEGIN TRAN;

-- GS-locked (built/approved) — record incumbent so KOR does not chase them.
UPDATE opportunities.MajorProjectsInventory
SET ScheduleNotes = LEFT(N'SEOR: Glotman Simpson (incumbent, verified GS portfolio). ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id IN (8304, 8306, 3696);  -- 820 Olive, Olympic+Hill, Times Mirror Square

-- Retire the duplicate Olympic+Hill row (keep 8306 "DELIVERED 2025").
UPDATE opportunities.MajorProjectsInventory SET RetiredAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id = 3422;

-- OPEN seat already in MPI: Violet (Arts District) -> set SCB architect + open-seat note.
UPDATE opportunities.MajorProjectsInventory
SET ProponentCanonicalOrgId = @Onni,
    ArchitectName = N'Solomon Cordwell Buenz (SCB)', ArchitectCanonicalOrgId = @SCB,
    ScheduleNotes = LEFT(N'SE STATUS: OPEN (no public SEOR). Architect SCB (not Dikeakos) => Glotman pipeline broken; pursue via SCB / Jay Longo. Sponsor: Mark Spector (Onni Sr VP Dev CA). ' + COALESCE(ScheduleNotes,N''),1000),
    UpdatedAtUtc=sysdatetimeoffset()
WHERE Id = 3720;  -- Violet Avenue / Arts District (2143 E Violet)

-- OPEN-seat towers missing from MPI -> add as pursuit rows.
MERGE opportunities.MajorProjectsInventory AS T
USING (VALUES
  (N'korref:onni-5700-wilshire', N'5700 Wilshire Blvd (twin 67-story, ~2,586 units)', N'Pre-construction (EIR; filed May 2026)'),
  (N'korref:onni-601-brand',     N'601 N Brand Blvd, Glendale (twin 36-story, 858 units)', N'Pre-construction (Glendale DRB)'),
  (N'korref:onni-1360-vine',     N'1360 N Vine St, Hollywood (33-story, 429 units)', N'Pre-construction (approved 2024)')
) AS S(SourceKey, ProjectName, Stage)
ON T.Province=N'CA' AND T.SourceKey=S.SourceKey
WHEN NOT MATCHED THEN
  INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES (N'CA', S.SourceKey, S.ProjectName, N'Residential High-Rise', S.Stage, N'Onni Group', @Onni,
          N'Solomon Cordwell Buenz (SCB)', @SCB, N'Los Angeles',
          N'SE STATUS: OPEN (no public SEOR). Architect SCB (not Dikeakos) => Glotman pipeline broken; pursue via SCB / Jay Longo. Sponsor: Mark Spector (Onni Sr VP Dev CA).',
          N'https://la.urbanize.city', N'{"source":"onni-seor-research-2026-06-18","seStatus":"open"}');

-- Onni decision-makers.
DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200));
INSERT INTO @people VALUES
 (@Onni,   N'Mark Spector',   N'Senior VP, Development (California) - SE-selection sponsor'),
 (@OnniCA, N'Apriano Meola',  N'CEO, Onni Contracting (California) Inc.');

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @people) AS S
   ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
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

PRINT 'Migration 209: Onni SEOR status + open-seat targets + decision-makers recorded.';
COMMIT TRAN;
GO
