-- Migration 299 (2026-09-03): two more ArcGIS development-application sources.
--
-- Both verified live with tools/ArcGisProbe on 2026-09-03 before being written
-- here. The numbers below are that probe's output, not an expectation.
--
--   Coquitlam   483 applications, 2006-01-09 -> 2026-09-01, 384 pass the
--               relevance gate. Its layer carries APPLICANT — the developer by
--               name ("Ledingham McAllister", "H & S Sidhu Construction Ltd.")
--               — which no tender feed we ingest gives us.
--   Maple Ridge 886 rows -> 849 applications (11 rows carry no reference file
--               and are dropped), 2009-07-30 -> 2026-08-27. Thin: the layer has
--               no project prose, only ApplicationType + WorkProposed + status
--               boilerplate, so only 39 of 849 pass the gate today. Seeded
--               anyway because its Pre-Application Review rows are the earliest
--               signal of the three cities — but see the gate finding in
--               docs/codex/CODEX-EARLY-SIGNAL-ARCGIS-ADAPTER.md before reading
--               anything into that 39.
--
-- ⚠ SEEDED DISABLED, same as migration 298 — the Worker must ship SourceType 20
-- first. Enable with:
--     UPDATE opportunities.OpportunitySources
--     SET IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
--     WHERE Name IN (N'Coquitlam_DevelopmentApplications',
--                    N'MapleRidge_DevelopmentApplications');
USE [KorOpportunitiesDb];
GO

-- ---------------------------------------------------------------- Coquitlam
DECLARE @coq uniqueidentifier;

SELECT @coq = Id
FROM opportunities.OpportunitySources
WHERE Name = N'Coquitlam_DevelopmentApplications';

IF @coq IS NULL
BEGIN
    SET @coq = NEWID();

    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES
        (@coq,
         N'Coquitlam_DevelopmentApplications',
         20,
         N'https://services2.arcgis.com/Q6Lq3evZUGfPrN7o/arcgis/rest/services/Development_Information_Demo/FeatureServer/0',
         0,
         86400,
         120,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField', N'PROJECT_NUMBER'),
    (N'arcgis.titleField',       N'ADDRESS'),
    (N'arcgis.buyerOverride',    N'City of Coquitlam'),
    (N'arcgis.statusField',      N'PROJECT_STATUS'),
    (N'arcgis.descriptionField', N'PROJECT_DESCRIPTION'),
    (N'arcgis.applicantField',   N'APPLICANT'),
    (N'arcgis.postedDateField',  N'SUBMISSION_DATE'),
    (N'arcgis.addressFields',    N'ADDRESS'),
    -- No per-application permalink is published; the city's own development
    -- projects page is the landing spot (verified 200 on 2026-09-03).
    (N'arcgis.fallbackUrl',      N'https://www.coquitlam.ca/993/Development-Projects'),
    (N'arcgis.cityOverride',     N'Coquitlam'),
    (N'arcgis.provinceOverride', N'BC'),
    (N'arcgis.pageSize',         N'2000'),
    (N'arcgis.maxPagesPerRun',   N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @coq AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@coq, s.[Key], s.ValueJson, sysdatetimeoffset());

-- -------------------------------------------------------------- Maple Ridge
DECLARE @mr uniqueidentifier;

SELECT @mr = Id
FROM opportunities.OpportunitySources
WHERE Name = N'MapleRidge_DevelopmentApplications';

IF @mr IS NULL
BEGIN
    SET @mr = NEWID();

    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES
        (@mr,
         N'MapleRidge_DevelopmentApplications',
         20,
         N'https://geoservices.mapleridge.ca/server/rest/services/DataCatalog/PlanningDevelopment/MapServer/1',
         0,
         86400,
         120,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',   N'ReferenceFile'),
    (N'arcgis.titleField',         N'Name'),
    (N'arcgis.buyerOverride',      N'City of Maple Ridge'),
    (N'arcgis.typeField',          N'ApplicationType'),
    -- Three fields because no single one tells the story: WorkProposed is the
    -- land use, SubType the instrument, Description the workflow stage.
    (N'arcgis.descriptionFields',  N'WorkProposed,SubType,Description'),
    (N'arcgis.postedDateField',    N'InDate'),
    (N'arcgis.addressFields',      N'House,Street'),
    -- The city's own Land Development Application Viewer, linked from
    -- mapleridge.ca/build-do-business/build-develop-permits/land-development.
    (N'arcgis.fallbackUrl',        N'https://apps.vertigisstudio.com/web/?app=8b409970fec048b0940b60fe1e225e39'),
    (N'arcgis.cityOverride',       N'Maple Ridge'),
    (N'arcgis.provinceOverride',   N'BC'),
    (N'arcgis.pageSize',           N'2000'),
    (N'arcgis.maxPagesPerRun',     N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @mr AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@mr, s.[Key], s.ValueJson, sysdatetimeoffset());
GO

PRINT 'Migration 299: Coquitlam + Maple Ridge development-application sources seeded (DISABLED until the Worker deploy).';
GO
