-- Migration 363 (2026-10-16): BC Housing project pages (SourceType 23, new adapter
-- BcHousingProjectsProvider).
--
-- bchousing.org/projects-partners/projects lists the projects BC Housing is
-- developing, and its sitemap names every page: 62 on the day. Each page carries the
-- address, the homes, the operator (M'akola, Lookout, a co-op) and a dated timeline
-- whose steps are marked completed / ongoing / pending. 59 of the 62 have one.
--
-- Why: the permit feeds carry the same sites as bare addresses with the city as
-- buyer ("564 Fifth Street", City of Nanaimo). BC Housing's page names the owner,
-- the operator and when the building permit is due, which is the BD signal.
--
-- ⚠ Seeded IsEnabled = 0, per the Worker rule: a source whose SourceType the running
--    binary has no provider for just fails. Enable it after the Worker deploy that
--    carries BcHousingProjectsProvider. Until then tools/IngestSourceOnce runs it.
-- Weekly (604800 s): the pages move a timeline step every few months.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @bch uniqueidentifier;
SET @bch = NULL;
SELECT @bch = Id FROM opportunities.OpportunitySources WHERE Name = N'BCHousing_Projects';

IF @bch IS NULL
BEGIN
    SET @bch = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@bch, N'BCHousing_Projects', 23,
            N'https://www.bchousing.org/projects-partners/projects',
            0, 604800, 300, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 23, BaseUrl = N'https://www.bchousing.org/projects-partners/projects',
        UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @bch;
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'bchousing.sitemapUrl', N'https://www.bchousing.org/sitemap.xml'),
    (N'bchousing.delayMs',    N'1000')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @bch AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@bch, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @bch;
GO
