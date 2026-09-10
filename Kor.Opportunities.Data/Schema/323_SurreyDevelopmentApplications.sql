-- Migration 323 (2026-09-10): CITY OF SURREY development applications
-- (SourceType 20, ArcGIS).
--
-- Surrey is the second-largest city in British Columbia, 568,322 people, and we
-- held no early signal for it at all. Its applications layer is public and
-- unauthenticated on the city's own ArcGIS Online organisation:
--
--   https://services5.arcgis.com/YRpe0VKTJytZSSIB/ArcGIS/rest/services/
--       Development Applications/FeatureServer/0
--
-- Measured 2026-09-10: 13,848 rows total, 1,267 in an active status. For scale,
-- the entire Vancouver Island programme is ~3,000 applications across fourteen
-- feeds. This one layer is the largest single addition we have made.
--
-- ⚠ THE SERVICE NAME CONTAINS A SPACE and the org's own directory advertises it
--    with the path segment "ArcGIS" capitalised. Requesting
--    .../arcgis/rest/services/Development_Applications/... returns
--    {"error":{"code":400,"message":"Invalid URL"}} — a 200-with-an-error-body,
--    not an HTTP failure. Use the URL exactly as written above.
--
-- ⚠ THERE IS NO DATE FIELD. The layer carries OBJECTID, PROJECT_NO,
--    DESCRIPTION, STATUS, WEBLINK and APPLICATION_DOCUMENTS_WEBLINK, and
--    nothing else. arcgis.postedDateField is therefore not set — it is optional
--    in the provider, and inventing a date from PROJECT_NO's leading year would
--    put a wrong-but-plausible date on every record, which sorts to the top of
--    "newest first". The file number carries the year for a human to read.
--
-- ⚠ THERE IS NO ADDRESS FIELD EITHER, so arcgis.titleField is DESCRIPTION —
--    the only human-readable text on the row. Descriptions are substantial:
--    "Rezoning from A-2 to IB-1 and IB-2 / General Development Permit to permit
--    the development of four industrial buildings".
--
-- requiredStatuses keeps the 1,267 that are still moving and drops Concluded,
-- Closed, Cancelled, Rejected and the post-approval construction states. By the
-- time a file reads "Construction" the structural engineer was chosen long ago.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @sy uniqueidentifier;
SELECT @sy = Id FROM opportunities.OpportunitySources WHERE Name = N'Surrey_DevelopmentApplications';

IF @sy IS NULL
BEGIN
    SET @sy = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@sy, N'Surrey_DevelopmentApplications', 20,
            N'https://services5.arcgis.com/YRpe0VKTJytZSSIB/ArcGIS/rest/services/Development Applications/FeatureServer/0',
            1, 86400, 180, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 20,
        BaseUrl = N'https://services5.arcgis.com/YRpe0VKTJytZSSIB/ArcGIS/rest/services/Development Applications/FeatureServer/0',
        IsEnabled = 1, RequestTimeoutSeconds = 180, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @sy;
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'PROJECT_NO'),
    (N'arcgis.titleField',        N'DESCRIPTION'),
    (N'arcgis.descriptionFields', N'DESCRIPTION,STATUS'),
    (N'arcgis.statusField',       N'STATUS'),
    (N'arcgis.buyerOverride',     N'City of Surrey'),
    (N'arcgis.cityOverride',      N'Surrey'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.requiredStatuses',  N'Initial Review,Under Review,In Process,Referrals,Project Scoping,Resubmission Required,Filed,Project Detailing,SA Draft,Conditional Approval'),
    (N'arcgis.fallbackUrl',       N'https://www.surrey.ca/city-services/planning-development'),
    (N'arcgis.outFields',         N'PROJECT_NO,DESCRIPTION,STATUS,WEBLINK,APPLICATION_DOCUMENTS_WEBLINK'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'5')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @sy AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@sy, s.[Key], s.ValueJson, sysdatetimeoffset());

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), @sy, N'Pending', sysdatetimeoffset(), N'migration-323'
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.IngestionTriggers
    WHERE OpportunitySourceId = @sy AND Status = N'Pending');

SELECT Name, SourceType, IsEnabled FROM opportunities.OpportunitySources WHERE Id = @sy;
GO
