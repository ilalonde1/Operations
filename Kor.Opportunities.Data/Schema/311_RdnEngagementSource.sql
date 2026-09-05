-- Migration 311 (2026-09-04): Regional District of Nanaimo, via EngagementHQ
-- (SourceType 22, new adapter).
--
-- The RDN's own "Current Development Applications" page is a one-line redirect:
-- "all zoning and official community plan amendment applications will use our
-- Get Involved RDN platform". getinvolved.rdn.ca/projects.json is public and
-- unauthenticated: 184 projects, of which 53 match the application title pattern
-- and 11 are live. 38 of the 53 name an applicant or owner in the description,
-- which is better than any mid-Island municipal ArcGIS layer manages.
--
-- ⚠ SCOPE: these are RDN ELECTORAL AREAS — Nanoose Bay, Errington, Coombs,
--    Bowser, Spider Lake, Horne Lake. NOT the City of Parksville or the Town of
--    Qualicum Beach, which run their own planning and are handled separately
--    (Qualicum is wired in migration 310; Parksville publishes a PDF only).
--
-- ⚠ includeArchived is FALSE. 42 of the 53 are archived, meaning decided — by
--    which point the structural engineer was chosen. The 11 live ones are the
--    early signal this programme exists to catch. Flip the key to backfill.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @rdn uniqueidentifier;
SET @rdn = NULL;
SELECT @rdn = Id FROM opportunities.OpportunitySources WHERE Name = N'RDN_DevelopmentApplications';

IF @rdn IS NULL
BEGIN
    SET @rdn = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@rdn, N'RDN_DevelopmentApplications', 22,
            N'https://www.getinvolved.rdn.ca',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 22, BaseUrl = N'https://www.getinvolved.rdn.ca',
        IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @rdn;
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'engagementhq.projectsUrl',      N'https://www.getinvolved.rdn.ca/projects.json'),
    (N'engagementhq.buyerOverride',    N'Regional District of Nanaimo'),
    (N'engagementhq.provinceOverride', N'BC'),
    (N'engagementhq.includeArchived',  N'false')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @rdn AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@rdn, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @rdn;
GO
