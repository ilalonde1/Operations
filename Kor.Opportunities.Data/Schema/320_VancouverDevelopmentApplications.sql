-- Migration 320 (2026-09-10): CITY OF VANCOUVER development and rezoning
-- applications, via EngagementHQ (SourceType 22).
--
-- This was a hole. Fourteen municipal application feeds were wired and the
-- largest development market in the province was not one of them, because
-- Vancouver was looked for in the two places that do not have it:
--
--   * opendata.vancouver.ca carries ISSUED building permits (51,969 rows,
--     refreshed daily). Issued is the wrong end of the job — by then the
--     structural engineer has been chosen, stamped and paid.
--   * maps.vancouver.ca/server/rest/services has 180 services across 19
--     folders. One of them, VanMapViewer/Licenses_and_Permits, is issued
--     permits again. There is no pending-application layer on the ArcGIS.
--
-- The applications are on shapeyourcity.ca — Vancouver's EngagementHQ tenant,
-- the same platform the RDN uses (migration 311). development.vancouver.ca is
-- a stale 2020 index whose every entry links there.
--
-- Measured 2026-09-10 against the live feed: 2,036 projects, of which 1,840
-- match the application title pattern and 93 are live. 46 of the 93 were filed
-- in 2026. 26 carry a DP file number; the remaining 67 are REZONINGS, which is
-- the earliest signal this programme can get — before a development permit
-- exists at all. Vancouver General Hospital Campus, East Fraser Lands and
-- 1166 W Pender are in the live set.
--
-- What makes this feed better than any other we hold: the descriptions are
-- written to a house style that opens with the applicant.
--     "Arcadis Architects has applied to the City of Vancouver…"
--     "Gradual Architecture has applied to the City of Vancouver…"
-- 25 of the 93 name the design firm outright. That is the architect's-sub play
-- readable straight off the feed, and no municipal ArcGIS layer we hold does it.
--
-- ⚠ includeArchived is FALSE, as for the RDN. 1,747 archived Vancouver
--    applications carry an architect and an address and would be a fine
--    research corpus, but they are DECIDED — putting them in the pipeline
--    would be 1,747 leads that are not leads. Flip the key to backfill.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @van uniqueidentifier;
SET @van = NULL;
SELECT @van = Id FROM opportunities.OpportunitySources WHERE Name = N'Vancouver_DevelopmentApplications';

IF @van IS NULL
BEGIN
    SET @van = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@van, N'Vancouver_DevelopmentApplications', 22,
            N'https://shapeyourcity.ca',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 22, BaseUrl = N'https://shapeyourcity.ca',
        IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @van;
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'engagementhq.projectsUrl',      N'https://shapeyourcity.ca/projects.json'),
    (N'engagementhq.buyerOverride',    N'City of Vancouver'),
    (N'engagementhq.cityOverride',     N'Vancouver'),
    (N'engagementhq.provinceOverride', N'BC'),
    (N'engagementhq.includeArchived',  N'false')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @van AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@van, s.[Key], s.ValueJson, sysdatetimeoffset());

-- titleRegex, fileNumberRegex and applicantRegex are deliberately NOT set. The
-- provider defaults now cover the BC municipal DP-2026-00529 / RZ- style and
-- the "X has applied to" clause, verified against both live feeds: the RDN's
-- 10 of 11 file numbers are unchanged by the broadened pattern, and Vancouver
-- goes from 0 to 26. A per-source override here would be a second place to
-- maintain the same knowledge.

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), @van, N'Pending', sysdatetimeoffset(), N'migration-320'
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.IngestionTriggers
    WHERE OpportunitySourceId = @van AND Status = N'Pending');

SELECT Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @van;
GO
