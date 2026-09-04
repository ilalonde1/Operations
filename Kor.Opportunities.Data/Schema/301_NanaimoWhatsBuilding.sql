-- Migration 301 (2026-09-04): City of Nanaimo — "What's Building in my
-- Neighbourhood".
--
-- Nanaimo does NOT publish through ArcGIS, so the SourceType 20 adapter cannot
-- reach it. Its tracker is a custom app on Google Maps whose marker feed is a
-- flat JSON array — which the existing GenericJson provider (SourceType 2)
-- already handles, given one addition: json.urlTemplate, because Nanaimo
-- publishes no per-record permalink and a row with no url is dropped outright.
--
-- Verified live 2026-09-04: 1,287 records, 1,287 distinct FileNumbers.
--   217  Commercial / Multi / Industrial      55  Subdivision
--   235  Single / Two Family Alteration       45  Development Permit
--   205  Commercial / Multi-Res Alteration    33  Rezoning Application
--   111  Single / Two Family Dwelling         48  Demolition Permit
-- Status: 1,184 ACTIVE, 51 RECEIVED, 49 ON HOLD, 3 NEW.
--
-- The descriptions are richer than Victoria's — they name the occupant and
-- describe the structure: "ABC Recycling Ltd., Steel Recycling Facility,
-- consists of 3 buildings"; "(Convertus Canada) Addition of 593m2 of Group F2
-- processing plant ... 4 pre-cast concrete composting tunnels".
--
-- ⚠ TWO GAPS, both real and neither fatal:
--   1. NO DATE FIELD. PostedDateUtc will be null for every row, so these cannot
--      be sorted or aged by application date. FileNumber (BP######) rises over
--      time and is the only ordering available.
--   2. NO PER-RECORD PAGE. Four url shapes were tested; FilterResults returns
--      200 but does not contain the record. The url is therefore the tracker
--      page plus the file number as a fragment — unique per row, honest, and it
--      never 404s. If Nanaimo ever publishes a deep link, change the template.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

DECLARE @nanaimo uniqueidentifier;

SELECT @nanaimo = Id
FROM opportunities.OpportunitySources
WHERE Name = N'Nanaimo_WhatsBuilding';

IF @nanaimo IS NULL
BEGIN
    SET @nanaimo = NEWID();

    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES
        (@nanaimo,
         N'Nanaimo_WhatsBuilding',
         2,       -- GenericJson
         N'https://www.nanaimo.ca/whatsbuilding/GetMarkers',
         1,
         86400,
         120,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    -- The payload is a bare array, so there is no items path to walk.
    (N'json.titlePath',             N'CivicAddress'),
    (N'json.descriptionPath',       N'Description'),
    (N'json.locationPath',          N'CivicAddress'),
    (N'json.externalReferencePath', N'FileNumber'),
    (N'json.urlPath',               N'__none__'),
    (N'json.urlTemplate',           N'https://www.nanaimo.ca/whatsbuilding/AllActiveApplications#{FileNumber}'),
    (N'json.buyerOverride',          N'City of Nanaimo'),
    (N'json.cityOverride',          N'Nanaimo'),
    (N'json.provinceOverride',      N'BC')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @nanaimo AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@nanaimo, s.[Key], s.ValueJson, sysdatetimeoffset());

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), @nanaimo, 'Pending', SYSDATETIMEOFFSET(), 'nanaimo-first-run'
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                  WHERE t.OpportunitySourceId = @nanaimo AND t.Status IN ('Pending','InProgress'));
GO

PRINT 'Migration 301: Nanaimo_WhatsBuilding seeded and queued.';
GO
