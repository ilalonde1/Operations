-- Migration 310 (2026-09-04): Qualicum Beach.
--
-- Correcting migration 308's comment, which said Qualicum Beach "publishes a web
-- scene" and nothing else. That was wrong. Their public ArcGIS ONLINE org holds
-- only sidewalks and irrigation, which is what an item search sees — but the town
-- runs its OWN VertiGIS Studio (ex-Geocortex) server at gis.qualicumbeach.com,
-- and the "Development Tracker" button on their site points into it.
--
-- The path was: the tracker page's "Try it Now" href gives an app id, the app's
-- item data on their internal portal is titled "Development Tracker (Public)",
-- and their ArcGIS Server root at /server/rest/services (NOT /arcgis/rest/...,
-- which 404s) lists a DevelopmentTracker folder with one FeatureServer.
--
--   145 applications, ALL DATED (ApplicationDate), current to 2026-07-07.
--   Fields: FileNumber, Status, CivicAddress, ApplicationDate, Details,
--   LegalDescription, PID, ExistingZoningCode, ExistingLandUse, ApplicationType.
--   maxRecordCount 2000, pagination supported.
--
--   Details is real scope text — "Construction of a Multi-Family building in
--   Uptown Commercial DPA", "Rezone property to amend the subdivision district
--   to allow for two parcels (Rural Rental Cottage Development)".
--   ⚠ No applicant field.
--
-- ⛔ STILL NOT WIRED, with the reason for each:
--   PARKSVILLE       publishes a quarterly PDF, not a feed —
--                    parksville.ca/cms/wpattachments/wpID41atID12760.pdf — but it
--                    is the RICHEST source in these markets because it carries a
--                    named APPLICANT per row. Current issue dated 14 Jan 2026
--                    names Momentum Design Build, Radcliffe Development
--                    Corporation (a KOR client, 79-unit condominium at 440 Island
--                    Hwy W — our project 31128), Continuum Architecture, Common
--                    Ground Consulting and Daryoush Firouzli Architecture.
--                    Needs a PDF-table reader or a scheduled human pull.
--   RDN              publishes development applications as ENGAGEMENT PROJECTS on
--                    getinvolved.rdn.ca, and /projects.json is a public feed: 184
--                    projects of which 53 are development or zoning applications
--                    (11 live), 38 of the 53 naming an applicant or owner in the
--                    description, with file numbers like PL2026-028 and full
--                    legal descriptions. Needs a small EngagementHQ adapter.
--                    ⚠ These are RDN ELECTORAL AREAS (Nanoose, Errington, Bowser
--                    and so on) — NOT the City of Parksville or the Town of
--                    Qualicum Beach, which do their own planning.
--   NORTH COWICHAN   has an "active development applications" database and a
--                    building-permit database, but neither exposes a feed, a
--                    Drupal view endpoint or an embedded map service. Needs a
--                    human look at the page in a browser.
--   COMOX VALLEY RD  runs a Drupal view named permits_applications with an
--                    AJAX endpoint at /views/ajax and a community filter.
--                    Reachable, but it is the regional district, not Courtenay
--                    or Comox town, both of which are already wired.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @qb uniqueidentifier;
SET @qb = NULL;
SELECT @qb = Id FROM opportunities.OpportunitySources WHERE Name = N'QualicumBeach_DevelopmentApplications';

IF @qb IS NULL
BEGIN
    SET @qb = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@qb, N'QualicumBeach_DevelopmentApplications', 20,
            N'https://gis.qualicumbeach.com/server/rest/services/DevelopmentTracker/Development_Application_Public/FeatureServer/0',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'FileNumber'),
    (N'arcgis.titleField',        N'CivicAddress'),
    (N'arcgis.buyerOverride',     N'Town of Qualicum Beach'),
    (N'arcgis.typeField',         N'ApplicationType'),
    (N'arcgis.statusField',       N'Status'),
    (N'arcgis.descriptionFields', N'Details,ApplicationType,ExistingLandUse,ExistingZoningCode,LegalDescription'),
    (N'arcgis.postedDateField',   N'ApplicationDate'),
    (N'arcgis.addressFields',     N'CivicAddress'),
    (N'arcgis.fallbackUrl',       N'https://qualicumbeach.com/building-development/devtracker/'),
    (N'arcgis.cityOverride',      N'Qualicum Beach'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'5')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @qb AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@qb, s.[Key], s.ValueJson, sysdatetimeoffset());

INSERT INTO opportunities.IngestionTriggers
    (Id, OpportunitySourceId, Status, RequestedBy, RequestedAtUtc, ReclaimedCount)
SELECT NEWID(), @qb, N'Pending', N'claude-migration-310', sysdatetimeoffset(), 0
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                  WHERE t.OpportunitySourceId = @qb AND t.Status = N'Pending');

SELECT Name, SourceType, IsEnabled, LEFT(BaseUrl, 100) AS BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @qb;
GO
