USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 229: enrich the Maple Ridge "Recreation Ready" program.
  - Hammond Aquatics (6819) + Albion Arena (6820): real cost estimates, build
    years, scope + the Oct-2026 referendum gate + Fast+Epp presumptive-incumbent
    flag (SE seat kept OPEN). Architect (HCMA 8799) link already present.
  - Add verified City of Maple Ridge decision-makers (Hunter {f}{last}).
  - Backfill 3 HCMA contacts that had null emails (Hunter {f}.{last}).
  Sources: Maple Ridge News 2026-06-19, mapleridge.ca, Daily Hive, KOR Deltek.
*/
DECLARE @City bigint = 72158;   -- City of Maple Ridge
DECLARE @Provider nvarchar(60) = N'MapleRidgeRecEnrichment';
BEGIN TRAN;

-- 1) Hammond Aquatics & Recreation Centre (id 6819)
UPDATE opportunities.MajorProjectsInventory
 SET EstimatedCostCad = 227000000,
     EstimatedCostText = N'$227M (preliminary design estimate, incl. contingencies)',
     StartYear = 2029,
     Stage = N'Planning (referendum Oct 2026)',
     ScheduleNotes = N'HCMA feasibility done Dec 2025; schematic design + funding strategy due Jul 2026 (Council 23/30 Jun). Construction start ~2029. Gated by Oct 2026 referendum (part of $393M parks/rec borrow). Presumptive incumbent SE: Fast + Epp (HCMA aquatic-typology partner, mass-timber/CLT); SE formally selected post-referendum (~2027) - seat tracked OPEN.',
     ProjectDescription = N'122,000 sf / 2 storeys (aquatic hall ~65,000 sf) at Hammond Community Park. 37.5m 8-lane lap pool (movable bulkhead), leisure pool + lazy river + waterslide, 2 hot pools, cold plunge, steam/sauna; full gym, fitness, multipurpose + arts/culture, cafe; above + below-grade parking.',
     UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = 6819;

-- 2) Albion Arena Expansion (id 6820)
UPDATE opportunities.MajorProjectsInventory
 SET EstimatedCostCad = 143000000,
     EstimatedCostText = N'$143M (preliminary estimate)',
     StartYear = 2028,
     Stage = N'Planning (referendum Oct 2026)',
     ScheduleNotes = N'Twin-rink ice arena expansion at Albion Fairgrounds. Construction start ~2028. Same Oct 2026 referendum gate / $393M program. Timber arena - Fast + Epp typology; SE seat OPEN. KOR arena credentials (Nelson Arena, Rogers Arena) apply.',
     UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = 6820;

-- 3) Enrichment anchor for the City org
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT @City AS OrgId) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @enr bigint = (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId=@City AND ProviderName=@Provider);

-- 4) Verified City of Maple Ridge decision-makers
DECLARE @people TABLE (PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Conf tinyint, Note nvarchar(300));
INSERT INTO @people VALUES
 (N'Valoree Richmond', N'Director of Parks and Recreation', N'vrichmond@mapleridge.ca', 91, N'Lead operational owner of the Recreation Ready program.'),
 (N'Cidalia Martin',   N'Director of Recreation',          N'cmartin@mapleridge.ca',  94, N'Recreation services lead.'),
 (N'Steve Faltas',     N'Director of Engineering',          N'sfaltas@mapleridge.ca',  94, N'Capital/infrastructure - relevant to delivery + SE procurement.'),
 (N'Catherine Nolan',  N'Finance Director',                 N'cnolan@mapleridge.ca',   93, N'Owns the funding strategy / borrowing plan.'),
 (N'James Stiver',     N'Director of Planning',             N'jstiver@mapleridge.ca',  91, NULL);

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
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(@City AS varchar(20)),'|',
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK
  FROM @people p
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2))
MERGE opportunities.IntelPersonAffiliation AS T
USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=@City
WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title), IsCurrent=1, LastSeenAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@Provider, @enr, N'High', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonId, @City, S.Title, 1);

-- 5) Backfill 3 HCMA contacts that had null emails (Hunter-verified)
UPDATE opportunities.IntelPerson SET Email=N'c.grobe@hcma.ca', EmailSource=N'Hunter', EmailConfidence=94, EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE DisplayName=N'Corey Grobe' AND Email IS NULL;
UPDATE opportunities.IntelPerson SET Email=N'e.harris@hcma.ca', EmailSource=N'Hunter', EmailConfidence=98, EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE DisplayName=N'Eli Harris' AND Email IS NULL;
UPDATE opportunities.IntelPerson SET Email=N'r.wilson@hcma.ca', EmailSource=N'Hunter', EmailConfidence=96, EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE DisplayName=N'Rob Wilson' AND Email IS NULL;

PRINT 'Migration 229: Maple Ridge rec program enriched (costs, notes, 5 City contacts, 3 HCMA email backfills).';
COMMIT TRAN;
GO
