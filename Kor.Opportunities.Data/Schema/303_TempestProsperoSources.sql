-- Migration 303 (2026-09-04): Tempest/Prospero listing sources — Saanich and
-- View Royal.
--
-- WHY THESE TWO. Saanich is the biggest permit market in Greater Victoria (Q1
-- 2026 housing starts 195, against the City of Victoria's 117) and was entirely
-- unwired: no ArcGIS layer, no open-data feed. Its only public route is the
-- Tempest tracker. View Royal runs the same system.
--
-- SourceType 21 = TempestProspero. The detail pages were already covered —
-- VictoriaProsperoLiveDetailExtractor matches on the PATH, so the applicant's
-- own agent enriches through the same implementation that serves Victoria.
--
-- Verified live before seeding, via tools/ScraperProbe --prospero:
-- Saanich returned 100 distinct applications over 5 pages, 100 of 100 dated,
-- with real scope text ("BLASTING FOR HOUSEPLEX - 4 UNITS", "TO INSTALL THREE
-- PORTABLE PADEL COURTS", "REZONE FROM A-1 RURAL TO A-2 RURAL TO ALLOW A SECOND
-- DETACHED SINGLE FAMILY DWELLING").
--
-- ⚠ playwright.maxPages is 60 (about 1,200 applications at 20 a page). If the
-- tracker offers more the scraper raises a DEGRADED run warning naming this key,
-- rather than silently truncating.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @rows TABLE (Name nvarchar(200), BaseUrl nvarchar(400), Buyer nvarchar(200), City nvarchar(100));
INSERT INTO @rows VALUES
 (N'Saanich_DevelopmentApplications',
  N'https://online.saanich.ca/Tempest/OurCity/Prospero/Search.aspx',
  N'District of Saanich', N'Saanich'),
 (N'ViewRoyal_DevelopmentApplications',
  N'https://www.viewroyal.ca/webapps/ourcity/prospero/search.aspx',
  N'Town of View Royal', N'View Royal');

DECLARE @name nvarchar(200), @url nvarchar(400), @buyer nvarchar(200), @city nvarchar(100), @id uniqueidentifier;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name, BaseUrl, Buyer, City FROM @rows;
OPEN c;
FETCH NEXT FROM c INTO @name, @url, @buyer, @city;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @id = Id FROM opportunities.OpportunitySources WHERE Name = @name;

    IF @id IS NULL
    BEGIN
        SET @id = NEWID();
        INSERT INTO opportunities.OpportunitySources
            (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
             CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
        VALUES (@id, @name, 21, @url, 1, 86400, 300, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
    END;

    MERGE opportunities.OpportunitySourceMappings AS t
    USING (VALUES
        (N'prospero.buyer',            @buyer),
        (N'prospero.cityOverride',     @city),
        (N'prospero.provinceOverride', N'BC'),
        (N'playwright.maxPages',       N'60')
    ) AS s([Key], ValueJson)
    ON t.OpportunitySourceId = @id AND t.[Key] = s.[Key]
    WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
        THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
    WHEN NOT MATCHED BY TARGET
        THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
             VALUES (@id, s.[Key], s.ValueJson, sysdatetimeoffset());

    INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
    SELECT NEWID(), @id, 'Pending', SYSDATETIMEOFFSET(), 'prospero-first-run'
    WHERE NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                      WHERE t.OpportunitySourceId = @id AND t.Status IN ('Pending','InProgress'));

    FETCH NEXT FROM c INTO @name, @url, @buyer, @city;
END;

CLOSE c;
DEALLOCATE c;
GO

PRINT 'Migration 303: Saanich + View Royal Tempest/Prospero sources seeded and queued.';
GO
