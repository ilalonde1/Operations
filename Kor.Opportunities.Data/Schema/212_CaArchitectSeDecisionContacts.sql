USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 212: architect SE-selection decision-makers (research 2026-06-18).
  The person who picks/recommends the structural engineer at each prime-consultant
  firm. Names + titles + affiliations here; only firm-site-VERIFIED emails inline
  (SCB, SmithGroup). Pattern/missing emails are Apollo/Hunter-verified in a
  follow-up migration. Also flags Simon Ha's Steinberg Hart affiliation as not
  current (he left Jan 2024 per research).
*/

DECLARE @Provider nvarchar(60) = N'CaArchitectResearch';

BEGIN TRAN;

-- Create BDE Architecture (Carmel's Admirals Cove architect), not yet in graph.
DECLARE @bde bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'bdearchitecture' AND RetiredAtUtc IS NULL);
IF @bde IS NULL
BEGIN
  INSERT INTO opportunities.CanonicalOrg (DisplayName, Kind, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'BDE Architecture', N'Architect', sysdatetimeoffset(), sysdatetimeoffset());
  SET @bde = SCOPE_IDENTITY();
END;

DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint);
INSERT INTO @people VALUES
 -- TCA Architects (76956) — pipeline key; displace IMEG
 (76956, N'Radziah Loh',     N'Principal-In-Charge / Technical Director (Oakland)', NULL,NULL,NULL),
 (76956, N'Teresa Ruiz',     N'Principal, VP Business Development (Oakland)',       NULL,NULL,NULL),
 (76956, N'Douglas Oliver',  N'Associate Principal, Studio Director (Oakland)',    NULL,NULL,NULL),
 (76956, N'Eric Olsen',      N'VP of Design, PIC (Los Angeles)',                   NULL,NULL,NULL),
 (76956, N'Steve Hutson',    N'Principal, VP Technical Design / QM',               NULL,NULL,NULL),
 -- SCB (75180) — Onni LA wedge; firm-verified emails
 (75180, N'Ben Wrigley',     N'Principal, West Coast Urban Residential lead',      N'ben.wrigley@scb.com',N'asis',82),
 (75180, N'Francesco Mozzati',N'Principal',                                        N'francesco.mozzati@scb.com',N'asis',82),
 (75180, N'Strachan Forgan', N'Principal & Executive Director',                    N'strachan.forgan@scb.com',N'asis',82),
 -- Carrier Johnson (112)
 (112,   N'Alex Gutierrez',  N'Associate Principal, Design Director (San Diego)',  NULL,NULL,NULL),
 (112,   N'Frank Landry',    N'Associate Principal, Technical Director (San Diego)',NULL,NULL,NULL),
 (112,   N'David Huchteman',  N'CEO',                                              NULL,NULL,NULL),
 -- BDE Architecture
 (@bde,  N'Megan Aasen',     N'Managing Principal',                                NULL,NULL,NULL),
 (@bde,  N'Joseph Estefanos',N'Architect (project lead)',                          NULL,NULL,NULL),
 (@bde,  N'Magda Esperanzate',N'Project Manager',                                  NULL,NULL,NULL),
 -- SmithGroup (68872) — Sutter teaming; firm-verified emails
 (68872, N'Vince Avallone',  N'VP, Director of Medical Planning (SF)',             N'vince.avallone@smithgroup.com',N'asis',82),
 (68872, N'Tyler Krehlik',   N'VP, West Region Practice Director (SF)',            N'tyler.krehlik@smithgroup.com',N'asis',82),
 (68872, N'Heather Chung',   N'VP, West Region Director (SF)',                     N'heather.chung@smithgroup.com',N'asis',82),
 (68872, N'Daniel Cusick',   N'Principal, LA Health Studio Leader',                NULL,NULL,NULL),
 -- HGA (68874) — Sutter Emeryville teaming
 (68874, N'Victoria Vicente',N'Western Region Healthcare Practice Leader',         NULL,NULL,NULL),
 (68874, N'Craig McInroy',   N'Healthcare Practice Leader, Bay Area',              NULL,NULL,NULL),
 (68874, N'Karva Sykes',     N'Principal / AVP (Sutter Santa Clara PM)',           NULL,NULL,NULL),
 (68874, N'Greg Osecheck',   N'VP, Healthcare Practice Leader (Sacramento)',       NULL,NULL,NULL),
 -- Secondary additions at firms already in graph
 (68637, N'Michael Miller',  N'Managing Partner, LA',                              NULL,NULL,NULL),
 (68631, N'J.F. Finn III',   N'Principal, Global Mixed-Use Practice Leader',       NULL,NULL,NULL),
 (68631, N'Duncan Paterson', N'Principal, Mixed-Use Studio Leader',                NULL,NULL,NULL),
 (76890, N'Wil Wong',        N'Principal (high-density studio)',                   NULL,NULL,NULL),
 (76890, N'Brad Golba',      N'Principal / Shareholder',                           NULL,NULL,NULL),
 (76952, N'Pieter Berger',   N'Principal, Director of Design',                     NULL,NULL,NULL),
 (76952, N'Matthew McLarand', N'President',                                        NULL,NULL,NULL);

MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @people) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

;WITH src AS (
  SELECT DISTINCT p.PersonName, p.Email, p.Src, p.Conf, LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip,
    (SELECT MIN(e.Id) FROM opportunities.CanonicalOrgEnrichment e WHERE e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider) AS EnrichmentId
  FROM @people p)
MERGE opportunities.IntelPerson AS T
USING (SELECT PersonName, Email, Src, Conf, Lowered, Strip, EnrichmentId, CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(Strip AS VARCHAR(8000))),2) AS NK FROM src) AS S
   ON T.NaturalKey=S.NK
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(), Corroborations=T.Corroborations+1, UpdatedAtUtc=sysdatetimeoffset(),
   Email=COALESCE(T.Email,S.Email), EmailSource=COALESCE(T.EmailSource,S.Src), EmailConfidence=COALESCE(T.EmailConfidence,S.Conf)
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Corroborations, Email, EmailSource, EmailConfidence, EmailCheckedAtUtc)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), S.PersonName, S.Lowered, 1, S.Email, S.Src, S.Conf, CASE WHEN S.Email IS NOT NULL THEN sysdatetimeoffset() END);

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

-- Simon Ha left Steinberg Hart (Jan 2024) — mark affiliation not current.
UPDATE a SET IsCurrent=0, Notes=N'Left Steinberg Hart ~Jan 2024 (per research 2026-06-18); confirm current firm before outreach.', UpdatedAtUtc=sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation a JOIN opportunities.IntelPerson p ON p.Id=a.IntelPersonId
WHERE p.NormalizedName=N'simon ha' AND a.CanonicalOrgId=68637;

PRINT 'Migration 212: architect SE-selection contacts ingested; Simon Ha flagged.';
COMMIT TRAN;
GO
