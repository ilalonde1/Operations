-- Migration 325 (2026-09-23): CITY OF KAMLOOPS planning applications
-- (SourceType 20, ArcGIS). First Interior BC application feed.
--
-- Verified live with tools/ArcGisProbe before this was written, per the standard
-- migration 299 set. The numbers below are that probe's output, not an
-- expectation:
--
--   151 row(s) over 1 page collapsed to 115 application(s); 14 rows skipped
--   (no folder number). Dated 115 of 115, 2018-11-08 -> 2026-09-21.
--   Relevance gate: 42 KEEP, 73 DROP.
--
-- Found by tools/ArcGisFingerprint, which classified 137 layers across five
-- municipal servers and returned exactly one genuine development-application
-- table — this one. The other four servers' 65 keyword matches were zoning
-- overlays, and Kelowna's "applications" turned out to be Cityworks ROAD USE
-- permits. Kelowna has no public development-application layer at all.
--
-- ⚠⚠ THIS LAYER CANNOT PAGE. Its metadata says supportsPagination: false, and
--    any query carrying resultOffset is answered
--    {"code":400,"message":"Pagination is not supported."} INSIDE AN HTTP 200 —
--    so the adapter read ZERO rows from a live layer of 151 and reported a clean
--    partial. arcgis.supportsPagination = false is new in this migration's
--    companion provider change; with maxRecordCount 1000 against 151 rows, one
--    plain query returns everything. If Kamloops ever exceeds 1000 the run will
--    report DEGRADED rather than silently keep the first slice.
--
-- ⚠ arcgis.applicantField is deliberately NOT set. ArcGisFingerprint offered
--   GIS.Property.OWNERTYPE, which is the KIND of owner (individual/corporate),
--   not a name. A meaningless string on the field somebody calls is worse than
--   an empty one.
--
-- ⚠ descriptionFields carries PERMIT_SUBJECT + PERMIT_TYPE and NOT
--   PERMIT_PURPOSE: on this layer SUBJECT and PURPOSE hold the same text, so
--   including both printed every description twice ("Split Title Duplex Split
--   Title Duplex"). Dropping it moved the gate from 44 KEEP to 42 — the two
--   lost were passing on a doubled keyword, which is not a real pass.
--
-- The lane is right. The newest kept applications are an 80-unit six-storey
-- senior housing development permit, two duplexes with secondary suites, and a
-- rezoning for a home-based daycare in a two-unit residential building.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @kam uniqueidentifier;
SELECT @kam = Id FROM opportunities.OpportunitySources WHERE Name = N'Kamloops_PlanningApplications';

IF @kam IS NULL
BEGIN
    SET @kam = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@kam, N'Kamloops_PlanningApplications', 20,
            N'https://maps.kamloops.ca/arcgis/rest/services/CityMap/CityMap_PlanningDevelopment/MapServer/228',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 20,
        BaseUrl = N'https://maps.kamloops.ca/arcgis/rest/services/CityMap/CityMap_PlanningDevelopment/MapServer/228',
        IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @kam;
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',   N'GIS.GIST_TEMPEST_PLANNING_APPL.FOLDER_NUMBER'),
    (N'arcgis.titleField',         N'GIS.Property.ADDRESS'),
    (N'arcgis.statusField',        N'GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_STATUS'),
    (N'arcgis.typeField',          N'GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_TYPE'),
    (N'arcgis.postedDateField',    N'GIS.GIST_TEMPEST_PLANNING_APPL.CREATED_DATE'),
    (N'arcgis.descriptionFields',  N'GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_SUBJECT,GIS.GIST_TEMPEST_PLANNING_APPL.PERMIT_TYPE'),
    (N'arcgis.addressFields',      N'GIS.Property.ADDRESS'),
    (N'arcgis.buyerOverride',      N'City of Kamloops'),
    (N'arcgis.cityOverride',       N'Kamloops'),
    (N'arcgis.provinceOverride',   N'BC'),
    (N'arcgis.fallbackUrl',        N'https://www.kamloops.ca/property-development/development-applications'),
    (N'arcgis.supportsPagination', N'false'),
    (N'arcgis.pageSize',           N'1000'),
    (N'arcgis.maxPagesPerRun',     N'1')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @kam AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@kam, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ⚠ SEEDED DISABLED is the 299 convention when the Worker has not yet shipped
--   the code a source needs. This one needs arcgis.supportsPagination, which is
--   new. Enabled here ONLY because the same session deploys the Worker; if that
--   deploy does not happen, turn it off again — an enabled source the running
--   binary cannot read is a source that reports Success with nothing in it.
INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), @kam, N'Pending', sysdatetimeoffset(), N'migration-325'
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.IngestionTriggers
    WHERE OpportunitySourceId = @kam AND Status = N'Pending');

SELECT Name, SourceType, IsEnabled FROM opportunities.OpportunitySources WHERE Id = @kam;
GO
