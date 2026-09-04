-- Migration 298 (2026-09-03): ArcGIS development-application sources.
--
-- WHY: BC Stats discontinued the Major Projects Inventory (last issue Q3 2025,
-- page removed 30 June 2026), so the province has no forward-pipeline feed. The
-- earlier signal is the municipal development-permit / rezoning APPLICATION —
-- it names the site, the purpose and the applicant's agent months before a
-- tender exists, which is when the structural engineer is actually chosen.
--
-- SourceType 20 = ArcGisFeatureService. One adapter serves the whole ArcGIS Hub
-- platform, so a new municipality is this file plus arcgis.* mapping rows.
--
-- ⚠ SEEDED DISABLED. The provider ships in the Worker; enabling a source whose
-- provider the running binary does not have produces failed runs. Flip
-- IsEnabled = 1 AFTER the Worker deploy:
--     UPDATE opportunities.OpportunitySources
--     SET IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
--     WHERE Name = N'Victoria_DevelopmentApplications';
--
-- Verified live 2026-09-03 with tools/ArcGisProbe: 258 feature rows collapse to
-- 146 applications (one application is one row per parcel), dated 2007-06-08 to
-- 2026-08-27.
USE [KorOpportunitiesDb];
GO

DECLARE @victoria uniqueidentifier;

SELECT @victoria = Id
FROM opportunities.OpportunitySources
WHERE Name = N'Victoria_DevelopmentApplications';

IF @victoria IS NULL
BEGIN
    SET @victoria = NEWID();

    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES
        (@victoria,
         N'Victoria_DevelopmentApplications',
         20,
         N'https://maps.victoria.ca/server/rest/services/OpenData/OpenData_PlanningAndDevelopment/MapServer/3',
         0,       -- see the deploy note above
         86400,   -- daily; the layer changes a few times a week
         120,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         0);      -- QuartzManaged = 0: the cron scheduler queues it on CrawlDelaySeconds
END;

-- Field mapping. Names are the layer's own, read from
-- .../MapServer/3?f=json on 2026-09-03.
MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'FOLDER_NUMBER'),
    (N'arcgis.titleField',        N'SUBJECT'),
    (N'arcgis.buyerOverride',     N'City of Victoria'),
    (N'arcgis.typeField',         N'AppType'),
    (N'arcgis.statusField',       N'STATUS'),
    (N'arcgis.descriptionField',  N'PURPOSE'),
    (N'arcgis.postedDateField',   N'CREATED_DATE'),
    (N'arcgis.addressFields',     N'HOUSE,STREET'),
    (N'arcgis.requiredStatuses',  N'ACTIVE,ON HOLD'),
    (N'arcgis.contactNameField',  N'CityContact'),
    (N'arcgis.contactEmailField', N'email'),
    (N'arcgis.contactPhoneField', N'phone'),
    (N'arcgis.detailUrlTemplate', N'https://tender.victoria.ca/webapps/ourcity/prospero/details.aspx?folderNumber={ref}'),
    (N'arcgis.cityOverride',      N'Victoria'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @victoria AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@victoria, s.[Key], s.ValueJson, sysdatetimeoffset());
GO

PRINT 'Migration 298: Victoria_DevelopmentApplications seeded (DISABLED until the Worker carrying SourceType 20 is deployed).';
GO
