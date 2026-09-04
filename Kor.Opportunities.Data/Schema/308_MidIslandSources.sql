-- Migration 308 (2026-09-04): the mid- and north-Island markets Rory Beirne asked
-- for — Courtenay, Campbell River and Comox.
--
-- All three are config rows on the ArcGIS adapter (SourceType 20). No new code.
--
-- Found the same way migration 304 found Langford: an ArcGIS Online item search
-- returns nothing for these cities because the layers live inside a web app, not
-- as searchable items. Reading each app's item data gives up the service url.
--
--   COURTENAY   ArcGIS Online, Instant Apps.
--               services3.arcgis.com/PwS5hVLYsEN2U36s .../Development_Tracker
--               Layer 2 = Development_Applications, 511 records, maxRecordCount
--               2000. Field names are Tempest-shaped (FOLDER_NUMBER, FolderType,
--               PURPOSE) — Tempest published through ArcGIS.
--               ⚠ CONTACT_EMAIL is the CITY's own building@courtenay.ca, not the
--               applicant's, so it is NOT mapped to a contact field. Mapping it
--               would put a municipal inbox on 511 opportunities.
--
--   CAMPBELL RIVER  Its OWN ArcGIS Portal at gisportal.campbellriver.ca, found
--               through the DevelApps webmap. 65 records, pagination supported.
--               ⭐ It carries APPLICANT — a named firm per application ("Parkway
--               Properties Ltd.", "WestUrban Developments Ltd"), which Langford,
--               Colwood and Nanaimo all lack. Only Saanich and Victoria otherwise
--               name the applicant.
--               ⚠ No date field of any kind on this layer. Like Nanaimo, these
--               rows cannot be aged — excluded from any "last N months" figure.
--
--   COMOX       Two layers on one view service, wired as two sources because the
--               field names differ (AppNumber vs Permit_Number):
--                 layer 0 Planning_Permit_Locations  20 records
--                 layer 1 Building_Permit_Locations  52 records
--               ⚠ Also undated.
--
-- ⛔ NOT WIRED, and this is a finding rather than an omission: PARKSVILLE,
--    QUALICUM BEACH and DUNCAN / NORTH COWICHAN publish no application feed at
--    all. Checked: each city's own site, their ArcGIS Online orgs, their map
--    pages' embedded apps, and seven candidate Tempest OurCity urls (all 404).
--    Parksville and North Cowichan publish zoning and OCP layers only; Qualicum
--    Beach publishes a web scene. For those three the only route is the CVRD /
--    RDN building-permit records and council agendas, which are a human pull.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

-- ── Courtenay ───────────────────────────────────────────────────────────────
DECLARE @cty uniqueidentifier;
SET @cty = NULL;
SELECT @cty = Id FROM opportunities.OpportunitySources WHERE Name = N'Courtenay_DevelopmentApplications';

IF @cty IS NULL
BEGIN
    SET @cty = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@cty, N'Courtenay_DevelopmentApplications', 20,
            N'https://services3.arcgis.com/PwS5hVLYsEN2U36s/arcgis/rest/services/Development_Tracker/FeatureServer/2',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'FOLDER_NUMBER'),
    (N'arcgis.titleField',        N'SUBJECT'),
    (N'arcgis.buyerOverride',     N'City of Courtenay'),
    (N'arcgis.typeField',         N'FolderTypeSubject'),
    (N'arcgis.statusField',       N'FolderStatus'),
    (N'arcgis.descriptionFields', N'PURPOSE,SUBJECT,FolderCategory,FolderType'),
    (N'arcgis.postedDateField',   N'CREATED_DATE'),
    (N'arcgis.addressFields',     N'FULL_ADDRESS'),
    (N'arcgis.fallbackUrl',       N'https://www.courtenay.ca/business-and-building/planning-and-land-use/current-development-applications'),
    (N'arcgis.cityOverride',      N'Courtenay'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @cty AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@cty, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ── Campbell River ──────────────────────────────────────────────────────────
DECLARE @cr uniqueidentifier;
SET @cr = NULL;
SELECT @cr = Id FROM opportunities.OpportunitySources WHERE Name = N'CampbellRiver_DevelopmentApplications';

IF @cr IS NULL
BEGIN
    SET @cr = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@cr, N'CampbellRiver_DevelopmentApplications', 20,
            N'https://gisportal.campbellriver.ca/arcgis/rest/services/AllDevelopmentApplications/FeatureServer/0',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'AppNumber'),
    (N'arcgis.titleField',        N'Address'),
    (N'arcgis.buyerOverride',     N'City of Campbell River'),
    (N'arcgis.typeField',         N'Type'),
    (N'arcgis.statusField',       N'Status'),
    (N'arcgis.applicantField',    N'Applicant'),
    (N'arcgis.descriptionFields', N'Descr,Details,Type,Address'),
    (N'arcgis.addressFields',     N'Address'),
    (N'arcgis.fallbackUrl',       N'https://www.campbellriver.ca/building-development/development-applications'),
    (N'arcgis.cityOverride',      N'Campbell River'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'1000'),
    (N'arcgis.maxPagesPerRun',    N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @cr AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@cr, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ── Comox, planning permits ─────────────────────────────────────────────────
DECLARE @cxp uniqueidentifier;
SET @cxp = NULL;
SELECT @cxp = Id FROM opportunities.OpportunitySources WHERE Name = N'Comox_PlanningPermits';

IF @cxp IS NULL
BEGIN
    SET @cxp = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@cxp, N'Comox_PlanningPermits', 20,
            N'https://services6.arcgis.com/3Y9RPK8WUZjQtJe7/arcgis/rest/services/Building_and_Planning_Permits_Status_(view)/FeatureServer/0',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'AppNumber'),
    (N'arcgis.titleField',        N'Address'),
    (N'arcgis.buyerOverride',     N'Town of Comox'),
    (N'arcgis.typeField',         N'PermitType'),
    (N'arcgis.statusField',       N'Status'),
    (N'arcgis.descriptionFields', N'Proposal,Description,PermitType,Address'),
    (N'arcgis.addressFields',     N'Address'),
    (N'arcgis.fallbackUrl',       N'https://www.comox.ca/development'),
    (N'arcgis.cityOverride',      N'Comox'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'5')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @cxp AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@cxp, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ── Comox, building permits ─────────────────────────────────────────────────
DECLARE @cxb uniqueidentifier;
SET @cxb = NULL;
SELECT @cxb = Id FROM opportunities.OpportunitySources WHERE Name = N'Comox_BuildingPermits';

IF @cxb IS NULL
BEGIN
    SET @cxb = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@cxb, N'Comox_BuildingPermits', 20,
            N'https://services6.arcgis.com/3Y9RPK8WUZjQtJe7/arcgis/rest/services/Building_and_Planning_Permits_Status_(view)/FeatureServer/1',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',  N'Permit_Number'),
    (N'arcgis.titleField',        N'Address'),
    (N'arcgis.buyerOverride',     N'Town of Comox'),
    (N'arcgis.typeField',         N'PermitType'),
    (N'arcgis.statusField',       N'Status'),
    (N'arcgis.descriptionFields', N'Proposal,Description,PermitType,Address'),
    (N'arcgis.addressFields',     N'Address'),
    (N'arcgis.fallbackUrl',       N'https://www.comox.ca/development'),
    (N'arcgis.cityOverride',      N'Comox'),
    (N'arcgis.provinceOverride',  N'BC'),
    (N'arcgis.pageSize',          N'2000'),
    (N'arcgis.maxPagesPerRun',    N'5')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @cxb AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@cxb, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT Name, SourceType, IsEnabled, LEFT(BaseUrl, 96) AS BaseUrl
FROM opportunities.OpportunitySources
WHERE Name IN (N'Courtenay_DevelopmentApplications', N'CampbellRiver_DevelopmentApplications',
               N'Comox_PlanningPermits', N'Comox_BuildingPermits')
ORDER BY Name;
GO
