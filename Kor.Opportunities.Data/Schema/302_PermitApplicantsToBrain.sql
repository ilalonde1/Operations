-- Migration 302 (2026-09-04): decompose the Victoria permit harvest into the Brain.
--
-- The Prospero extractor gives us, for each of 116 live City of Victoria
-- development applications, the APPLICANT'S OWN AGENT — the developer, their
-- architect or their planning consultant — by name, email and phone. That is
-- market intelligence sitting in the opportunities tables where no dossier, brief
-- or search can reach it. This lifts it into CanonicalOrg / IntelPerson /
-- IntelPersonAffiliation / OrgFact, which is where the Brain looks.
--
-- Firm names and kinds come from Apollo enrichment of the applicant email domains
-- (see docs/island-pipeline/victoria-applicant-firms-2026-09-04.json), NOT from
-- guessing a company name out of a domain.
--
-- IDEMPOTENT and MERGE-SAFE. 15 CanonicalOrg rows already carry these domains, so
-- an org is resolved before it is created:
--   1. exact WebsiteDomain match  (lowest Id wins when several exist)
--   2. exact DisplayName match    (never LIKE — "Chard" once matched
--                                  "Richard & Co. Architecture")
--   3. otherwise insert
-- FuzzyNormalizedName is set explicitly on insert because it is NOT computed;
-- leaving it empty groups the row with unrelated orgs.
--
-- Free-mail domains are deliberately excluded: a gmail address is a person acting
-- for themselves, not a firm. Those people still exist as applicants in the
-- opportunity rows; they simply do not mint an organisation.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @provider nvarchar(100) = N'PermitApplicants';
DECLARE @createdBy nvarchar(100) = N'claude-island-permits-2026-09-04';
DECLARE @now datetimeoffset = sysdatetimeoffset();

-- ── The firms, as Apollo returned them ──────────────────────────────────────
DECLARE @firms TABLE (
    Domain      nvarchar(200) PRIMARY KEY,
    DisplayName nvarchar(400) NOT NULL,
    Kind        nvarchar(50)  NOT NULL,
    Website     nvarchar(400) NULL,
    City        nvarchar(100) NULL
);

INSERT INTO @firms (Domain, DisplayName, Kind, Website, City) VALUES
 (N'aryze.ca',                 N'Aryze Developments',                 N'Developer', N'http://www.aryze.ca',                 N'Victoria'),
 (N'daustudio.ca',             N'D''Ambrosio Architecture + Urbanism', N'Architect', N'http://www.daustudio.ca',             N'Victoria'),
 (N'cascadiaarchitects.ca',    N'Cascadia Architects',                N'Architect', N'http://www.cascadiaarchitects.ca',    N'Victoria'),
 (N'charch.ca',                N'Colin Harper Architect',             N'Architect', N'http://www.charch.ca',                N'Victoria'),
 (N'mjmarchitect.ca',          N'MJM Architect Inc.',                 N'Architect', N'http://www.mjmarchitect.ca',          N'Victoria'),
 (N'studio531.ca',             N'STUDIO 531 architects',              N'Architect', N'http://www.studio531.ca',             N'Victoria'),
 (N'foldarchitects.com',       N'Fold Architects Inc',                N'Architect', N'http://www.foldarchitects.com',       N'Victoria'),
 (N'lintottarchitect.ca',      N'Christine Lintott Architects Inc.',  N'Architect', N'http://www.lintottarchitect.ca',      N'Victoria'),
 (N'finlaysonbonet.ca',        N'Finlayson Bonet Architecture',       N'Architect', N'http://www.finlaysonbonet.ca',        N'Saanichton'),
 (N'kiloarchitecture.com',     N'Kilo Architecture Inc.',             N'Architect', N'http://www.kiloarchitecture.com',     N'Victoria'),
 (N'wiserprojects.com',        N'Wiser Projects',                     N'Architect', N'http://www.wiserprojects.com',        N'Victoria'),
 (N'barefootplanning.com',     N'Barefoot Planning + Design',         N'Architect', N'http://www.barefootplanning.com',     N'Saanich'),
 (N'northland.ca',             N'Northland Properties',               N'Developer', N'http://www.northland.ca',             N'Vancouver'),
 (N'townline.com',             N'Townline',                           N'Developer', N'http://www.townline.com',             N'Vancouver'),
 (N'intracorphomes.com',       N'Intracorp Homes',                    N'Developer', N'http://www.intracorphomes.com',       N'Vancouver'),
 (N'relianceproperties.ca',    N'Reliance Properties Ltd.',           N'Developer', N'http://www.relianceproperties.ca',    N'Vancouver'),
 (N'starlightinvest.com',      N'Starlight Investments',              N'Developer', N'http://www.starlightinvest.com',      N'Toronto'),
 (N'primexinvestments.com',    N'Primex Investments Ltd.',            N'Developer', N'http://www.primexinvestments.com',    N'Vancouver'),
 (N'gwlra.com',                N'GWL Realty Advisors',                N'Investor',  N'http://www.gwlrealtyadvisors.com',    N'Toronto'),
 (N'bayviewplace.com',         N'Bayview Place',                      N'Developer', N'http://www.bayviewplace.com',         N'Victoria'),
 (N'urbanthrive.ca',           N'Urban Thrive Developments',          N'Developer', N'http://www.urbanthrive.ca',           N'Victoria'),
 (N'korsdevelopment.com',      N'Kors Development Services Inc',      N'Developer', N'http://www.korsdevelopment.com',      NULL),
 (N'cittagroup.com',           N'Città Group',                   N'GC',        N'http://www.cittagroup.com',           N'Victoria'),
 (N'gericconstruction.com',    N'Mike Geric Construction Ltd',        N'GC',        N'http://www.gericconstruction.com',    N'Saanich'),
 (N'hutchinsoncontracting.ca', N'Hutchinson Contracting Ltd.',        N'GC',        N'http://www.hutchinsoncontracting.ca', N'Victoria'),
 (N'blendprojects.co',         N'Blend Projects',                     N'GC',        N'http://www.blendprojects.co',         N'Victoria'),
 (N'ledcor.com',               N'Ledcor',                             N'GC',        N'http://www.ledcor.com',               N'Vancouver'),
 (N'casman.net',               N'Casman Group',                       N'GC',        N'http://www.casman.ca',                NULL),
 (N'zebragroup.ca',            N'zebra design group',                 N'Designer',  N'http://www.zebragroup.ca',            N'Oak Bay'),
 (N'evokebuildings.com',       N'Evoke Buildings Engineering',        N'Competitor',N'http://www.evokebuildings.com',       N'Vancouver'),
 (N'williamwright.ca',         N'William Wright Commercial Real Estate Services', N'Vendor', N'http://www.williamwright.ca', N'Vancouver'),
 -- Not matched in Apollo; names taken from the applicant string itself, which is
 -- the firm name in these cases. Flagged by the absence of a Website.
 (N'islandviewgroup.ca',       N'Islandview Group',                   N'Developer', NULL, N'Victoria'),
 (N'makoladev.com',            N'M''akola Development Services',      N'Developer', NULL, N'Victoria'),
 (N'sakuradevelopments.com',   N'Sakura Developments',                N'Developer', NULL, N'Victoria'),
 (N'lowegroup.ca',             N'Lowe Group',                         N'Developer', NULL, N'Victoria'),
 (N'tirproperties.com',        N'TIR Properties',                     N'Developer', NULL, N'Victoria'),
 (N'gustavsoncapital.co',      N'Gustavson Capital',                  N'Developer', NULL, N'Victoria'),
 (N'readyms.ca',               N'Ready Management Services',          N'Developer', NULL, N'Victoria'),
 (N'mygns.ca',                 N'Glenlyon Norfolk School',            N'Buyer',     NULL, N'Victoria'),
 (N'thebcma.com',              N'BC Muslim Association',              N'Buyer',     NULL, N'Victoria'),
 (N'bc.anglican.ca',           N'Anglican Diocese of British Columbia',N'Buyer',    NULL, N'Victoria'),
 (N'chabadvi.org',             N'Chabad of Vancouver Island',         N'Buyer',     NULL, N'Victoria');

-- ── The people, straight from the live harvest ──────────────────────────────
IF OBJECT_ID('tempdb..#agents') IS NOT NULL DROP TABLE #agents;

SELECT DISTINCT
       o.Id                                                                        AS OpportunityId,
       LTRIM(RTRIM(o.BuyerContactName))                                            AS AgentName,
       LOWER(LTRIM(RTRIM(o.BuyerContactEmail)))                                    AS Email,
       LOWER(SUBSTRING(o.BuyerContactEmail, CHARINDEX('@', o.BuyerContactEmail) + 1, 200)) AS Domain,
       o.BuyerContactPhone                                                         AS Phone,
       o.Name                                                                      AS ApplicationName
INTO #agents
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE s.Name = N'Victoria_DevelopmentApplications'
  AND o.BuyerContactEmail LIKE '%@%'
  AND o.BuyerContactName IS NOT NULL;

-- ── 1. Resolve or create each org ───────────────────────────────────────────
IF OBJECT_ID('tempdb..#orgmap') IS NOT NULL DROP TABLE #orgmap;
CREATE TABLE #orgmap (Domain nvarchar(200) PRIMARY KEY, CanonicalOrgId bigint NOT NULL, WasCreated bit NOT NULL);

INSERT INTO #orgmap (Domain, CanonicalOrgId, WasCreated)
SELECT f.Domain, x.Id, 0
FROM @firms f
CROSS APPLY (
    SELECT TOP 1 co.Id
    FROM opportunities.CanonicalOrg co
    WHERE co.RetiredAtUtc IS NULL
      AND (co.WebsiteDomain = f.Domain OR co.DisplayName = f.DisplayName)
    ORDER BY CASE WHEN co.WebsiteDomain = f.Domain THEN 0 ELSE 1 END, co.Id
) x;

DECLARE @newOrgs TABLE (Domain nvarchar(200), Id bigint);

MERGE opportunities.CanonicalOrg AS t
USING (SELECT f.* FROM @firms f WHERE NOT EXISTS (SELECT 1 FROM #orgmap m WHERE m.Domain = f.Domain)) AS s
ON 1 = 0
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Kind, DisplayName, Website, WebsiteDomain, FuzzyNormalizedName, Notes,
            KorProjectsCount, CreatedAtUtc, UpdatedAtUtc)
    VALUES (s.Kind, s.DisplayName, s.Website, s.Domain,
            LOWER(REPLACE(REPLACE(REPLACE(REPLACE(s.DisplayName, '&', ''), '.', ''), ',', ''), ' ', '')),
            N'Created from City of Victoria development-permit Application Contacts, ' + CONVERT(varchar(10), @now, 23),
            0, @now, @now)
    OUTPUT s.Domain, inserted.Id INTO @newOrgs (Domain, Id);

INSERT INTO #orgmap (Domain, CanonicalOrgId, WasCreated)
SELECT Domain, Id, 1 FROM @newOrgs;

-- Backfill the website anchor on orgs that already existed without one. The
-- anchor is what ResearchIdentityGate uses to refuse a drifting refresh, so an
-- org without one is the shape that produced the Continuum defect.
UPDATE co
SET co.WebsiteDomain = f.Domain,
    co.Website = COALESCE(co.Website, f.Website),
    co.UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
JOIN #orgmap m ON m.CanonicalOrgId = co.Id
JOIN @firms f ON f.Domain = m.Domain
WHERE m.WasCreated = 0 AND co.WebsiteDomain IS NULL;

-- ── 2. An enrichment row per org (IntelPerson FKs to it) ────────────────────
INSERT INTO opportunities.CanonicalOrgEnrichment
    (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, Notes, CreatedAtUtc, UpdatedAtUtc)
SELECT m.CanonicalOrgId, @provider, N'Succeeded', @now, @now, 1,
       N'City of Victoria development-permit Application Contacts', @now, @now
FROM #orgmap m
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
    WHERE e.CanonicalOrgId = m.CanonicalOrgId AND e.ProviderName = @provider);

-- ── 3. People ───────────────────────────────────────────────────────────────
IF OBJECT_ID('tempdb..#people') IS NOT NULL DROP TABLE #people;

SELECT a.Email,
       MAX(a.AgentName) AS DisplayName,
       MAX(a.Phone)     AS Phone,
       a.Domain,
       m.CanonicalOrgId,
       e.Id             AS EnrichmentId,
       CONVERT(char(40), HASHBYTES('SHA1', a.Email), 2) AS NaturalKey
INTO #people
FROM #agents a
JOIN #orgmap m ON m.Domain = a.Domain
JOIN opportunities.CanonicalOrgEnrichment e
     ON e.CanonicalOrgId = m.CanonicalOrgId AND e.ProviderName = @provider
GROUP BY a.Email, a.Domain, m.CanonicalOrgId, e.Id;

MERGE opportunities.IntelPerson AS t
USING #people AS s
ON t.NaturalKey = s.NaturalKey
WHEN MATCHED THEN UPDATE SET
    LastSeenAtUtc = @now,
    UpdatedAtUtc  = @now,
    Corroborations = t.Corroborations + 1,
    Email      = COALESCE(t.Email, s.Email),
    Phone      = COALESCE(t.Phone, s.Phone),
    EmailSource = COALESCE(t.EmailSource, N'PermitApplication')
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
            FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
            DisplayName, NormalizedName, Email, Phone, Corroborations,
            EmailSource, EmailConfidence, Notes)
    VALUES (@provider, s.EnrichmentId, N'High', s.NaturalKey,
            @now, @now, @now, @now,
            s.DisplayName, LOWER(s.DisplayName), s.Email, s.Phone, 1,
            N'PermitApplication', 100,
            N'Named as the Application Contact on a City of Victoria development application.');

-- ── 4. Affiliations ─────────────────────────────────────────────────────────
MERGE opportunities.IntelPersonAffiliation AS t
USING (
    SELECT ip.Id AS IntelPersonId, p.CanonicalOrgId, p.EnrichmentId,
           CONVERT(char(40), HASHBYTES('SHA1',
               CONVERT(varchar(40), ip.Id) + '|' + CONVERT(varchar(40), p.CanonicalOrgId)), 2) AS NaturalKey
    FROM #people p
    JOIN opportunities.IntelPerson ip ON ip.NaturalKey = p.NaturalKey
) AS s
ON t.IntelPersonId = s.IntelPersonId AND t.CanonicalOrgId = s.CanonicalOrgId
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now, IsCurrent = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
            FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
            IntelPersonId, CanonicalOrgId, IsCurrent, Notes)
    VALUES (@provider, s.EnrichmentId, N'High', s.NaturalKey,
            @now, @now, @now, @now,
            s.IntelPersonId, s.CanonicalOrgId, 1,
            N'Acted for this firm on a City of Victoria development application.');

-- ── 5. One fact per org: what it is actually doing right now ────────────────
;WITH perOrg AS (
    SELECT m.CanonicalOrgId,
           COUNT(DISTINCT a.OpportunityId) AS Applications,
           STRING_AGG(CAST(a.ApplicationName AS nvarchar(max)), N'; ')
               WITHIN GROUP (ORDER BY a.ApplicationName) AS Apps
    FROM #agents a
    JOIN #orgmap m ON m.Domain = a.Domain
    GROUP BY m.CanonicalOrgId
)
MERGE opportunities.OrgFact AS t
USING (
    SELECT CanonicalOrgId,
           CONVERT(char(40), HASHBYTES('SHA1',
               CONVERT(varchar(40), CanonicalOrgId) + '|LiveVictoriaApplications'), 2) AS NaturalKey,
           N'Named as the applicant''s contact on ' + CAST(Applications AS nvarchar(10))
             + N' live City of Victoria development application'
             + CASE WHEN Applications = 1 THEN N'' ELSE N's' END
             + N' as at 2026-09-04: ' + LEFT(Apps, 3000) AS Body
    FROM perOrg
) AS s
ON t.NaturalKey = s.NaturalKey
WHEN MATCHED THEN UPDATE SET Body = s.Body, ObservedAtUtc = @now
WHEN NOT MATCHED BY TARGET THEN
    INSERT (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef,
            ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
    VALUES (s.NaturalKey, s.CanonicalOrgId, N'MarketFocus', s.Body,
            N'https://tender.victoria.ca/webapps/ourcity/prospero/',
            N'Victoria_DevelopmentApplications',
            @now, N'High', @now, @createdBy);

-- ── Report ──────────────────────────────────────────────────────────────────
SELECT (SELECT COUNT(*) FROM #orgmap)                       AS OrgsResolved,
       (SELECT COUNT(*) FROM #orgmap WHERE WasCreated = 1)  AS OrgsCreated,
       (SELECT COUNT(*) FROM #orgmap WHERE WasCreated = 0)  AS OrgsMatchedExisting,
       (SELECT COUNT(*) FROM #people)                       AS PeopleProcessed,
       (SELECT COUNT(DISTINCT OpportunityId) FROM #agents)  AS ApplicationsCovered;
GO

PRINT 'Migration 302: Victoria permit applicants decomposed to the Brain.';
GO
