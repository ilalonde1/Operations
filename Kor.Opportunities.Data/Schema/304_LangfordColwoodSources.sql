-- Migration 304 (2026-09-04): Langford and Colwood — the last two unwired
-- Greater Victoria permit markets.
--
-- Both turned out to be config rows on adapters that already exist. Neither
-- needed new code.
--
-- LANGFORD runs its OWN ArcGIS Server at arcex.langford.ca — which is why an
-- ArcGIS Online item search found nothing earlier: the layer lives inside an
-- Experience Builder app, not as a searchable item. Found by reading the app's
-- item data and pulling the service urls out of it.
--   https://arcex.langford.ca/server/rest/services/Development_Tracker/Development_Applications/MapServer/0
--   398 records, maxRecordCount 2000, supportsPagination true.
--   ⭐ It carries TotalValuation — CONSTRUCTION VALUE, which no other municipal
--   feed we have publishes at all, so until now nothing could be ranked by size.
--   Also TypeOfBuilding ("Apartment - Condominium"), Zoning and Status.
--
-- COLWOOD runs Tempest/Prospero, the same tracker as Victoria, Saanich and View
-- Royal, so the scraper added in migration 303 reads it unchanged. Verified live
-- with tools/ScraperProbe --prospero before seeding: 51 applications, 51 of 51
-- dated, newest 2025-12-05, including "a 6 storey, 87-unit apartment building",
-- "a 29-unit townhouse development" and "25 3-bedroom dwellings across 5
-- attached-housing style buildings".
--   ⚠ Colwood's own site says its OurCity portal holds permits issued before
--   1 Jan 2025. That is true of BUILDING permits; the DEVELOPMENT applications
--   above are current, which is what the probe confirmed.
--
-- Also raises Saanich's page budget. Its first run read 1,200 applications and
-- reported "DEGRADED: Prospero pagination truncated at 60 page(s) — the tracker
-- offered more", which is the warning doing its job. 200 pages is ~4,000.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

-- ── Langford (ArcGIS, SourceType 20) ────────────────────────────────────────
DECLARE @lang uniqueidentifier;
SELECT @lang = Id FROM opportunities.OpportunitySources WHERE Name = N'Langford_DevelopmentApplications';

IF @lang IS NULL
BEGIN
    SET @lang = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@lang, N'Langford_DevelopmentApplications', 20,
            N'https://arcex.langford.ca/server/rest/services/Development_Tracker/Development_Applications/MapServer/0',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.externalRefField',    N'PermitNumber'),
    (N'arcgis.titleField',          N'Full_Address'),
    (N'arcgis.buyerOverride',       N'City of Langford'),
    (N'arcgis.typeField',           N'Type'),
    (N'arcgis.descriptionFields',   N'Description,PermitDescription,TypeOfBuilding,Zoning'),
    (N'arcgis.postedDateField',     N'Entered'),
    (N'arcgis.addressFields',       N'Full_Address'),
    (N'arcgis.estimatedValueField', N'TotalValuation'),
    (N'arcgis.fallbackUrl',         N'https://langford.ca/city-hall/mapping/'),
    (N'arcgis.cityOverride',        N'Langford'),
    (N'arcgis.provinceOverride',    N'BC'),
    (N'arcgis.pageSize',            N'2000'),
    (N'arcgis.maxPagesPerRun',      N'10')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @lang AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@lang, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ── Colwood (Tempest/Prospero, SourceType 21) ───────────────────────────────
DECLARE @col uniqueidentifier;
SELECT @col = Id FROM opportunities.OpportunitySources WHERE Name = N'Colwood_DevelopmentApplications';

IF @col IS NULL
BEGIN
    SET @col = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@col, N'Colwood_DevelopmentApplications', 21,
            N'https://services.colwood.ca/TLive/OurCity/Prospero/Search.aspx',
            1, 86400, 300, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'prospero.buyer',            N'City of Colwood'),
    (N'prospero.cityOverride',     N'Colwood'),
    (N'prospero.provinceOverride', N'BC'),
    (N'playwright.maxPages',       N'60')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @col AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@col, s.[Key], s.ValueJson, sysdatetimeoffset());

-- ── Saanich asked for more pages; give it more pages ────────────────────────
UPDATE m
SET m.ValueJson = N'200', m.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.OpportunitySourceMappings m
JOIN opportunities.OpportunitySources s ON s.Id = m.OpportunitySourceId
WHERE s.Name = N'Saanich_DevelopmentApplications' AND m.[Key] = N'playwright.maxPages';

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), s.Id, 'Pending', SYSDATETIMEOFFSET(), 'westshore-first-run'
FROM opportunities.OpportunitySources s
WHERE s.Name IN (N'Langford_DevelopmentApplications', N'Colwood_DevelopmentApplications',
                 N'Saanich_DevelopmentApplications', N'ViewRoyal_DevelopmentApplications')
  AND NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                  WHERE t.OpportunitySourceId = s.Id AND t.Status IN ('Pending','InProgress'));
GO

PRINT 'Migration 304: Langford + Colwood seeded; Saanich page budget raised; West Shore queued.';
GO
