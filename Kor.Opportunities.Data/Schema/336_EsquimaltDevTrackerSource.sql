-- Migration 336 (2026-10-09): Esquimalt development permit applications (SourceType 20).
--
-- HOW IT WAS FOUND, because the method is the transferable part. Esquimalt was on the
-- "probed and nothing found" list in docs/KOR-EarlySignal-CoverageGap-2026-09-10.md, which
-- had searched the ArcGIS ONLINE catalogue. Esquimalt does not publish there. It runs its
-- own server at gis.esquimalt.ca, which an item search cannot see — the same reason
-- 6 of our 10 live ArcGIS feeds were invisible to that index.
--
-- ⚠ AND THE FINGERPRINT'S SUGGESTED MAPPING WAS THE THIN ONE. tools/ArcGisFingerprint
--    classified this layer correctly but proposed `titleField = ADDRESS`, because it reads
--    a schema and does not know this practice's conventions. The layer actually carries
--    SUBJECT and PURPOSE — Esquimalt runs the same Tempest-backed DevTracker as Victoria,
--    so Victoria's mapping (migration 298) is the right one. Probed both ways: with ADDRESS
--    as the title the rows are "PLANNING — 2432 OTTER POINT RD" shaped and carry no scope;
--    with SUBJECT/PURPOSE they carry "Proposing a 4-story wood frame building with a
--    1-level underground parkade". A fingerprint proposes; the precedent decides.
--
-- VERIFIED LIVE 2026-10-09 with tools/ArcGisProbe:
--     56 feature rows -> 43 applications (one application is many parcels)
--     gate KEEP 29, gate DROP 14
--     dated 43 of 43, 2017-07-19 -> 2026-08-18
--
-- ENABLED ON INSERT: SourceType 20's provider has been deployed since migration 298 and
-- eleven sources already run through it, so nothing waits on a deploy.
--
-- WHAT THIS DOES NOT COVER:
--   * Esquimalt publishes FOUR DevTracker layers and this wires ONE — the largest and
--     richest. The others, left deliberately: Development Variance Permit Applications
--     (25 rows), Current Developments (19), DADU Development Permit Applications (16).
--     They overlap this one and each would need its own probe; one source per municipality
--     matches how the Island feeds are wired.
--   * Parcel_BPTrackerGISLINK (527 rows) is ISSUED residential building permits. Late
--     signal — the engineer is already chosen — and the fingerprint says so itself.
--   * ⛔ SOOKE WAS FOUND THE SAME WAY AND IS NOT WIRED. gis.sooke.ca publishes
--     MapService/PlanningApplications (23 rows) and TempMap/devApplications (27), and the
--     probe rejected 18 of 18: every row is "PLANNING — <address>" with an EMPTY
--     description, so the gate cannot tell a house from a tower and correctly drops them
--     all. Wiring it would manufacture a source_everything_filtered_out alert. The layer
--     needs a scope field Sooke does not publish.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @esq uniqueidentifier;
SET @esq = NULL;
SELECT @esq = Id FROM opportunities.OpportunitySources
WHERE Name = N'Esquimalt_DevelopmentApplications';

IF @esq IS NULL
BEGIN
    SET @esq = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@esq, N'Esquimalt_DevelopmentApplications', 20,
            N'https://gis.esquimalt.ca/arcgis/rest/services/SQL/Parcel_DevTrackerDP/MapServer/1',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 20,
        BaseUrl = N'https://gis.esquimalt.ca/arcgis/rest/services/SQL/Parcel_DevTrackerDP/MapServer/1',
        IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @esq;
END;

-- Field names read off .../MapServer/1?f=json on 2026-10-09:
--   ADDRESS, LEGAL, GISLINK, FOLDER_NUMBER, PROJECT_NUMBER, SUBJECT, CREATED_DATE,
--   COMPLETED_DATE, APPROVED_DATE, PURPOSE, STATUS
MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'arcgis.titleField',       N'SUBJECT'),
    (N'arcgis.descriptionField', N'PURPOSE'),
    (N'arcgis.externalRefField', N'FOLDER_NUMBER'),
    (N'arcgis.statusField',      N'STATUS'),
    (N'arcgis.postedDateField',  N'CREATED_DATE'),
    (N'arcgis.addressFields',    N'ADDRESS'),
    -- ⛔ MANDATORY, AND OMITTING IT COST A SILENT ZERO-INSERT RUN. The first attempt at
    --    this migration had no detailUrlTemplate, and the run came back Success=1 with
    --    0 inserted and 43 SKIPPED -- and NO gate-reject rows, which is what made it
    --    confusing. IngestionService.cs:123 drops a candidate with no Title OR no Url
    --    BEFORE the relevance gate, so nothing is recorded in RelevanceGateRejects and
    --    the run looks clean. Same fault class as Calgary's url-less feed in migration 335.
    --    {ref} is substituted with the external reference (FOLDER_NUMBER), URL-escaped.
    -- ⚠ This is the VERIFIED url, not the pretty one. Esquimalt runs Tempest -- Victoria's
    --    equivalent is tender.victoria.ca/webapps/ourcity/prospero/details.aspx -- and
    --    online.esquimalt.ca IS a live ASP.NET app (its 404 is an ASP.NET "resource cannot
    --    be found", so the host and app exist). But no Prospero path answered at five
    --    guesses, and View Royal proved today what guessing a Tempest path costs. The
    --    layer query below was checked: HTTP 200, 2,431 bytes, and it contains DP000272.
    --    Swapping in a Prospero link is a browser look at online.esquimalt.ca.
    (N'arcgis.detailUrlTemplate',
     N'https://gis.esquimalt.ca/arcgis/rest/services/SQL/Parcel_DevTrackerDP/MapServer/1/query?where=FOLDER_NUMBER%3D%27{ref}%27&outFields=*&f=json'),
    (N'arcgis.buyerOverride',    N'Township of Esquimalt'),
    (N'arcgis.cityOverride',     N'Esquimalt'),
    (N'arcgis.provinceOverride', N'BC')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @esq AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@esq, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT 'source' AS Section, Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @esq;

SELECT 'section K coverage after this' AS Section,
       COUNT_BIG(*) AS Municipalities,
       SUM(CASE WHEN h.HeldBy IS NOT NULL THEN 1 ELSE 0 END) AS Held
FROM opportunities.FootprintMunicipalities m
OUTER APPLY (
    SELECT TOP 1 s.Name AS HeldBy
    FROM opportunities.OpportunitySources s
    WHERE s.IsEnabled = 1 AND s.IsHistorical = 0 AND s.SourceType IN (2, 20, 21, 22)
      AND LEFT(s.Name, CHARINDEX(N'_', s.Name + N'_') - 1) = m.SourceNamePrefix
) h;
GO
