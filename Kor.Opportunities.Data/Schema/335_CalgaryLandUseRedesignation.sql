-- Migration 335 (2026-10-08): Calgary Land Use Redesignation — the first Alberta
-- early-signal feed, and the richest one found anywhere in this pass.
--
-- WHY THIS ONE FIRST, of Calgary's three endpoints. It is REZONING, which is earlier in
-- the cycle than a development permit, and every row is a change of buildable form, so it
-- needs none of the big-ticket filtering Development Permits will need (193,127 rows,
-- mostly signs, home occupations and single dwellings — see the wiring plan). The relevance
-- gate learned Calgary's district codes in 8c3d80a1: 6 of 400 recent rows kept before,
-- 238 after, zero regressions.
--
-- MEASURED ON THE SLICE THIS ACTUALLY INGESTS (newest 1,000, back to 2023-07-19):
--     permitnum    1000 of 1000 (100%)   <- so the url template always resolves
--     description   981 of 1000  (98%)
--     address       972 of 1000  (97%)
--     proposedlud   941 of 1000  (94%)
--     applicant     819 of 1000  (82%)   <- against 43% across the whole early-signal layer
-- 82% named is the entire point. An application that names nobody is a permit notice; one
-- that names O2 PLANNING AND DESIGN or SPARKS PLANNING GROUP is a relationship lead.
--
-- ⛔ A ROW WITH NO URL IS DROPPED, and this feed publishes no url field.
--    GenericJsonOpportunityProvider: `if (IsNullOrWhiteSpace(title) || IsNullOrWhiteSpace(url))
--    { dropped++; continue; }`. So json.urlTemplate is MANDATORY here, not a nicety —
--    without it Calgary ingests exactly zero rows while reporting a clean run.
--
-- ⚠ THE URL IS THE VERIFIED ONE, NOT THE PRETTY ONE. Calgary's development map
--    (dmap.calgary.ca/?find=LOC2026-0208) looks like the obvious deep link and returns
--    HTTP 200 — but it returns an IDENTICAL 1,324-byte shell for a nonsense reference, so
--    `?find=` is resolved client-side and cannot be verified over HTTP. That is the same
--    soft-200 shape that wasted time on View Royal today. The template below was checked
--    instead: it returns HTTP 200, 532 bytes, and CONTAINS the permit reference.
--    Swapping in the map link is a ten-second browser check somebody can do later.
--
-- ⭐ json.descriptionPath = proposedlud IS DELIBERATE AND DOES DOUBLE DUTY. The gate reads
--    title + description only, and the Codex gate work measured that 202 of 600 coded rows
--    (33.7%) do not repeat the district code in their description text — a ceiling the
--    vocabulary change could not reach. Mapping the description to proposedlud puts the
--    code in front of the gate for 94% of the slice, and json.applicantPath prefixes
--    "Applicant: {X}. " ahead of it, so one field carries both the lead and the signal.
--    The scope sentence becomes the title; the street address goes to json.locationPath.
--
-- ⚠ SEEDED DISABLED, and this one really does wait. json.applicantPath is NEW code
--    (933f8cc3) and is not yet on KOR-APP01. Enabling before the Worker deploy gives a
--    feed with nobody's name on it — the low-value shape, from the publisher that offers
--    the high-value one. Enable AFTER the deploy:
--        UPDATE opportunities.OpportunitySources
--        SET IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
--        WHERE Name = N'Calgary_LandUseRedesignation';
--
-- WHAT THIS DOES NOT COVER:
--   * 19 of 1,000 rows have no description and are dropped as title-less. Honest loss.
--   * §K does not count Calgary. The footprint roster (migration 333) is BC's 56
--     municipalities; Alberta coverage is not measured there and should not be read off it.
--   * It is one of Calgary's three endpoints. Development Permits and DP Public Notices
--     are separate and are NOT this migration.
--   * Nothing here filters by status. `statuscurrent` carries Submitted/Approved/etc. and
--     a decided redesignation is still useful context, so everything in the slice ingests
--     and the gate decides.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @cal uniqueidentifier;
SET @cal = NULL;
SELECT @cal = Id FROM opportunities.OpportunitySources
WHERE Name = N'Calgary_LandUseRedesignation';

IF @cal IS NULL
BEGIN
    SET @cal = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@cal, N'Calgary_LandUseRedesignation', 2,
            -- 1,000 newest reaches back to 2023-07-19; the feed updates daily and holds
            -- 8,998 rows all the way to 1982, which we have no reason to carry.
            N'https://data.calgary.ca/resource/33vi-ew4s.json?$limit=1000&$order=applieddate%20DESC',
            0,       -- see the deploy note above: json.applicantPath is not on the server yet
            86400,   -- daily
            120,
            sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 2,
        BaseUrl = N'https://data.calgary.ca/resource/33vi-ew4s.json?$limit=1000&$order=applieddate%20DESC',
        UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @cal;   -- IsEnabled deliberately untouched on re-run
END;

-- Field names read off the live endpoint 2026-10-08, not guessed:
--   address, applicant, applieddate, description, fromlud, latitude, locationaddresses,
--   locationcount, longitude, multipoint, permitnum, permittype, proposedlud, statuscurrent
MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'json.titlePath',             N'description'),
    (N'json.descriptionPath',       N'proposedlud'),
    (N'json.applicantPath',         N'applicant'),
    (N'json.locationPath',          N'address'),
    (N'json.externalReferencePath', N'permitnum'),
    (N'json.postedDatePath',        N'applieddate'),
    (N'json.urlTemplate',           N'https://data.calgary.ca/resource/33vi-ew4s?permitnum={permitnum}'),
    (N'json.buyerOverride',         N'City of Calgary'),
    (N'json.cityOverride',          N'Calgary'),
    (N'json.provinceOverride',      N'AB')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @cal AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@cal, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT 'source (expect IsEnabled = 0 until the Worker deploy)' AS Section,
       Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @cal;

SELECT 'config' AS Section, [Key], ValueJson
FROM opportunities.OpportunitySourceMappings
WHERE OpportunitySourceId = @cal ORDER BY [Key];
GO
