-- Migration 307 (2026-09-04): decompose the Saanich + Colwood permit applicants
-- into the Brain, the same way migration 302 did for Victoria.
--
-- SCOPED TO BD-RELEVANT PERMIT TYPES ONLY. Saanich's tracker publishes the whole
-- permit spectrum, and KOR is a structural engineering firm: rezonings,
-- development permits, commercial permits, subdivisions and variances are ours;
-- plumbing, tree, fireplace and boulevard permits are not. Those are excluded by
-- the gate now, and the 439 already ingested were removed.
--
-- Firm names and kinds come from Apollo enrichment
-- (docs/island-pipeline/island-firms-enriched-2026-09-04.json); 8 domains were
-- ALREADY HELD in the Brain and are resolved to those rows rather than re-created.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @provider nvarchar(100) = N'PermitApplicants';
DECLARE @createdBy nvarchar(100) = N'claude-island-permits-2026-09-04';
DECLARE @now datetimeoffset = sysdatetimeoffset();

DECLARE @firms TABLE (Domain nvarchar(200) PRIMARY KEY, DisplayName nvarchar(400), Kind nvarchar(50), Website nvarchar(400));
INSERT INTO @firms VALUES
 (N'rjc.ca',                   N'RJC Engineers',                              N'Competitor', N'https://www.rjc.ca'),
 (N'senseengineering.com',     N'Sense Engineering',                          N'Competitor', N'https://www.senseengineering.com'),
 (N'mccuaig.net',              N'McCuaig & Associates Engineering Ltd.',      N'Competitor', N'https://www.mccuaig.net'),
 (N'lhra.ca',                  N'Low Hammond Rowe Architects Inc.',           N'Architect',  N'https://www.lhra.ca'),
 (N'dhk.ca',                   N'dHKarchitects',                              N'Architect',  N'https://www.dhk.ca'),
 (N'hcma.ca',                  N'hcma architecture + design',                 N'Architect',  N'https://www.hcma.ca'),
 (N'mgba.com',                 N'Mallen Gowing Berzins Architecture (MGBA)',  N'Architect',  N'https://www.mgba.com'),
 (N'abbarch.com',              N'ABBARCH Architecture Inc.',                  N'Architect',  N'https://www.abbarch.com'),
 (N'numberten.com',            N'Number TEN Architectural Group',             N'Architect',  N'https://www.numberten.com'),
 (N'continuumarchitecture.ca', N'Continuum Architecture Inc.',                N'Architect',  N'https://www.continuumarchitecture.ca'),
 (N'cityspaces.ca',            N'CitySpaces Consulting',                      N'Architect',  N'https://www.cityspaces.ca'),
 (N'westplanconsulting.ca',    N'Westplan Consulting Group',                  N'Architect',  N'https://www.westplanconsulting.ca'),
 (N'elac.ca',                  N'LEES + Associates',                          N'Architect',  N'https://www.elac.ca'),
 (N'oemarchitectureoffice.com',N'OEM Architecture Office Inc.',               N'Architect',  N'https://www.oemarchitectureoffice.com'),
 (N'nesarch.ca',               N'NES Architecture Ltd.',                      N'Architect',  NULL),
 (N'hillelarch.ca',            N'Karen Hillel Architect',                     N'Architect',  NULL),
 (N'jrtw.ca',                  N'JRTW Planning Services',                     N'Architect',  NULL),
 (N'aragon.ca',                N'Aragon Properties Ltd.',                     N'Developer',  N'https://www.aragon.ca'),
 (N'greystar.com',             N'Greystar',                                   N'Developer',  N'https://www.greystar.com'),
 (N'shape.ca',                 N'SHAPE',                                      N'Developer',  N'https://www.shape.ca'),
 (N'woodsmere.ca',             N'Woodsmere Holdings Corp.',                   N'Developer',  N'https://www.woodsmere.ca'),
 (N'ivlm.ca',                  N'Island View Land Management',                N'Developer',  N'https://www.ivlm.ca'),
 (N'maxcellent.ca',            N'Maxcellent Group',                           N'Developer',  N'https://www.maxcellent.ca'),
 (N'amica.ca',                 N'Amica Senior Lifestyles',                    N'Buyer',      N'https://www.amica.ca'),
 (N'habitatvictoria.com',      N'Habitat for Humanity Victoria',              N'Buyer',      N'https://www.habitatvictoria.com'),
 (N'crd.bc.ca',                N'Capital Regional District',                  N'Buyer',      N'https://www.crd.bc.ca'),
 (N'sebaconstruction.com',     N'Seba Construction Ltd.',                     N'GC',         N'https://www.sebaconstruction.com'),
 (N'hausen.ca',                N'Hausen Projects Inc.',                       N'GC',         N'https://www.hausen.ca'),
 (N'luxuriahomes.ca',          N'Luxuria Homes',                              N'GC',         N'https://www.luxuriahomes.ca'),
 (N'robertblaneydesign.com',   N'Robert Blaney Design Inc',                   N'Designer',   N'https://www.robertblaneydesign.com'),
 (N'calid.ca',                 N'Calid Services Ltd.',                        N'Vendor',     N'https://www.calid.ca'),
 (N'plsi.ca',                  N'Polaris Land Surveying Inc',                 N'Vendor',     N'https://www.plsi.ca'),
 (N'cypresslandservices.com',  N'Cypress Land Services',                      N'Vendor',     N'https://www.cypresslandservices.com');

-- BD-relevant permit types only.
DECLARE @bd TABLE (T nvarchar(80) PRIMARY KEY);
INSERT INTO @bd VALUES
 (N'REZONING'),(N'DEVELOPMENT PERMIT'),(N'DEVELOPMENT PERMIT AMENDMENT'),(N'COMMERCIAL PERMIT'),
 (N'SUBDIVISION'),(N'DEVELOPMENT VARIANCE PERMIT'),(N'BOARD OF VARIANCE'),(N'STRATA'),
 (N'HERITAGE REGISTRY'),(N'TEMPORARY USE PERMIT'),(N'AGRICULTURAL LAND RESERVE');

IF OBJECT_ID('tempdb..#agents') IS NOT NULL DROP TABLE #agents;
SELECT DISTINCT
       o.Id AS OpportunityId,
       LTRIM(RTRIM(o.BuyerContactName)) AS AgentName,
       LOWER(LTRIM(RTRIM(o.BuyerContactEmail))) AS Email,
       LOWER(SUBSTRING(o.BuyerContactEmail, CHARINDEX('@', o.BuyerContactEmail) + 1, 200)) AS Domain,
       o.BuyerContactPhone AS Phone,
       o.Name AS ApplicationName
INTO #agents
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE s.Name IN (N'Saanich_DevelopmentApplications', N'Colwood_DevelopmentApplications')
  AND o.BuyerContactEmail LIKE '%@%'
  AND o.BuyerContactName IS NOT NULL
  AND (LEFT(o.Name, CHARINDEX(N' —', o.Name + N' —') - 1) IN (SELECT T FROM @bd)
       OR o.Name LIKE N'DEVELOPMENT%');

DELETE FROM #agents WHERE Domain NOT IN (SELECT Domain FROM @firms);

-- Resolve or create, exactly as 302: WebsiteDomain, then exact DisplayName, never LIKE.
IF OBJECT_ID('tempdb..#orgmap') IS NOT NULL DROP TABLE #orgmap;
CREATE TABLE #orgmap (Domain nvarchar(200) PRIMARY KEY, CanonicalOrgId bigint NOT NULL, WasCreated bit NOT NULL);

INSERT INTO #orgmap (Domain, CanonicalOrgId, WasCreated)
SELECT f.Domain, x.Id, 0
FROM @firms f
CROSS APPLY (
    SELECT TOP 1 co.Id FROM opportunities.CanonicalOrg co
    WHERE co.RetiredAtUtc IS NULL AND (co.WebsiteDomain = f.Domain OR co.DisplayName = f.DisplayName)
    ORDER BY CASE WHEN co.WebsiteDomain = f.Domain THEN 0 ELSE 1 END, co.Id
) x;

-- ⚠ ONE ROW AT A TIME, NOT ONE MERGE. CanonicalOrg carries a unique index
-- UX_CanonicalOrg_LiveNormalizedName on the COMPUTED NormalizedName column, so a
-- name that normalizes onto an existing live org raises Msg 2601 — and a single
-- MERGE means that one collision rolls back ALL the inserts. It did: 20 firms,
-- 0 created, one bad row. Per-row with TRY/CATCH, a collision skips just itself
-- and is reported instead of silently taking the batch down with it.
DECLARE @dom nvarchar(200), @nm nvarchar(400), @kd nvarchar(50), @ws nvarchar(400), @newId bigint;
DECLARE fc CURSOR LOCAL FAST_FORWARD FOR
    SELECT f.Domain, f.DisplayName, f.Kind, f.Website
    FROM @firms f WHERE NOT EXISTS (SELECT 1 FROM #orgmap m WHERE m.Domain = f.Domain);
OPEN fc;
FETCH NEXT FROM fc INTO @dom, @nm, @kd, @ws;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @newId = NULL;
    BEGIN TRY
        INSERT INTO opportunities.CanonicalOrg
            (Kind, DisplayName, Website, WebsiteDomain, FuzzyNormalizedName, Notes, KorProjectsCount, CreatedAtUtc, UpdatedAtUtc)
        VALUES (@kd, @nm, @ws, @dom,
                LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@nm,'&',''),'.',''),',',''),' ','')),
                N'Created from Saanich/Colwood development-permit Application Contacts, 2026-09-04',
                0, @now, @now);
        SET @newId = SCOPE_IDENTITY();
        INSERT INTO #orgmap (Domain, CanonicalOrgId, WasCreated) VALUES (@dom, @newId, 1);
    END TRY
    BEGIN CATCH
        -- Normalized-name collision with a live org under a different display
        -- name. Attach to that org rather than creating a second row for one
        -- real firm — which is the defect this whole pass exists to avoid.
        DECLARE @existing bigint =
            (SELECT TOP 1 co.Id FROM opportunities.CanonicalOrg co
             WHERE co.RetiredAtUtc IS NULL
               AND co.NormalizedName = (SELECT TOP 1 x.NormalizedName FROM opportunities.CanonicalOrg x WHERE x.DisplayName = @nm)
             ORDER BY co.Id);
        PRINT CONCAT('  [name collision] ', @dom, ' / ', @nm, ' -> ', ISNULL(CONVERT(varchar(20), @existing), 'UNRESOLVED'), ' : ', ERROR_MESSAGE());
        IF @existing IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #orgmap m WHERE m.Domain = @dom)
            INSERT INTO #orgmap (Domain, CanonicalOrgId, WasCreated) VALUES (@dom, @existing, 0);
    END CATCH;
    FETCH NEXT FROM fc INTO @dom, @nm, @kd, @ws;
END;
CLOSE fc;
DEALLOCATE fc;

UPDATE co SET co.WebsiteDomain = f.Domain, co.Website = COALESCE(co.Website, f.Website), co.UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
JOIN #orgmap m ON m.CanonicalOrgId = co.Id
JOIN @firms f ON f.Domain = m.Domain
WHERE m.WasCreated = 0 AND co.WebsiteDomain IS NULL;

INSERT INTO opportunities.CanonicalOrgEnrichment
    (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, Notes, CreatedAtUtc, UpdatedAtUtc)
SELECT m.CanonicalOrgId, @provider, N'Succeeded', @now, @now, 1,
       N'Saanich/Colwood development-permit Application Contacts', @now, @now
FROM #orgmap m
WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = m.CanonicalOrgId AND e.ProviderName = @provider);

IF OBJECT_ID('tempdb..#people') IS NOT NULL DROP TABLE #people;
SELECT a.Email, MAX(a.AgentName) AS DisplayName, MAX(a.Phone) AS Phone, a.Domain,
       m.CanonicalOrgId, e.Id AS EnrichmentId,
       CONVERT(char(40), HASHBYTES('SHA1', a.Email), 2) AS NaturalKey
INTO #people
FROM #agents a
JOIN #orgmap m ON m.Domain = a.Domain
JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = m.CanonicalOrgId AND e.ProviderName = @provider
GROUP BY a.Email, a.Domain, m.CanonicalOrgId, e.Id;

MERGE opportunities.IntelPerson AS t
USING #people AS s ON t.NaturalKey = s.NaturalKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now,
    Corroborations = t.Corroborations + 1, Email = COALESCE(t.Email, s.Email), Phone = COALESCE(t.Phone, s.Phone)
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
            CreatedAtUtc, UpdatedAtUtc, DisplayName, NormalizedName, Email, Phone, Corroborations,
            EmailSource, EmailConfidence, Notes)
    VALUES (@provider, s.EnrichmentId, N'High', s.NaturalKey, @now, @now, @now, @now,
            s.DisplayName, LOWER(s.DisplayName), s.Email, s.Phone, 1, N'PermitApplication', 100,
            N'Named as the Application Contact on a Saanich or Colwood development application.');

MERGE opportunities.IntelPersonAffiliation AS t
USING (
    SELECT ip.Id AS IntelPersonId, p.CanonicalOrgId, p.EnrichmentId,
           CONVERT(char(40), HASHBYTES('SHA1', CONVERT(varchar(40), ip.Id) + '|' + CONVERT(varchar(40), p.CanonicalOrgId)), 2) AS NaturalKey
    FROM #people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey = p.NaturalKey
) AS s
ON t.IntelPersonId = s.IntelPersonId AND t.CanonicalOrgId = s.CanonicalOrgId
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now, IsCurrent = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
            CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, IsCurrent, Notes)
    VALUES (@provider, s.EnrichmentId, N'High', s.NaturalKey, @now, @now, @now, @now,
            s.IntelPersonId, s.CanonicalOrgId, 1, N'Acted on a Saanich or Colwood development application.');

;WITH perOrg AS (
    SELECT m.CanonicalOrgId, COUNT(DISTINCT a.OpportunityId) AS Applications,
           STRING_AGG(CAST(a.ApplicationName AS nvarchar(max)), N'; ') WITHIN GROUP (ORDER BY a.ApplicationName) AS Apps
    FROM #agents a JOIN #orgmap m ON m.Domain = a.Domain
    GROUP BY m.CanonicalOrgId
)
MERGE opportunities.OrgFact AS t
USING (
    SELECT CanonicalOrgId,
           CONVERT(char(40), HASHBYTES('SHA1', CONVERT(varchar(40), CanonicalOrgId) + '|IslandPermitApplications'), 2) AS NaturalKey,
           N'Named as the applicant''s contact on ' + CAST(Applications AS nvarchar(10))
             + N' live Saanich/Colwood development application' + CASE WHEN Applications = 1 THEN N'' ELSE N's' END
             + N' as at 2026-09-04: ' + LEFT(Apps, 3000) AS Body
    FROM perOrg
) AS s
ON t.NaturalKey = s.NaturalKey
WHEN MATCHED THEN UPDATE SET Body = s.Body, ObservedAtUtc = @now
WHEN NOT MATCHED BY TARGET THEN
    INSERT (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
    VALUES (s.NaturalKey, s.CanonicalOrgId, N'MarketFocus', s.Body,
            N'https://online.saanich.ca/Tempest/OurCity/Prospero/', N'Saanich/Colwood_DevelopmentApplications',
            @now, N'High', @now, @createdBy);

SELECT (SELECT COUNT(*) FROM #orgmap) AS OrgsResolved,
       (SELECT COUNT(*) FROM #orgmap WHERE WasCreated = 1) AS OrgsCreated,
       (SELECT COUNT(*) FROM #orgmap WHERE WasCreated = 0) AS MatchedExisting,
       (SELECT COUNT(*) FROM #people) AS People,
       (SELECT COUNT(DISTINCT OpportunityId) FROM #agents) AS ApplicationsCovered;
GO

PRINT 'Migration 307: Saanich + Colwood permit applicants decomposed to the Brain.';
GO
