-- Migration 305 (2026-09-04): repair the Saanich / View Royal mix-up that
-- migration 303 caused, and create View Royal properly.
--
-- WHAT WENT WRONG. 303 looped two municipalities with a cursor and resolved each
-- one's id like this:
--
--     SELECT @id = Id FROM opportunities.OpportunitySources WHERE Name = @name;
--     IF @id IS NULL BEGIN ...create... END
--
-- When the SELECT matches NO row it does not assign anything — @id KEEPS ITS
-- PREVIOUS VALUE. So on the View Royal iteration @id was still Saanich's id,
-- "IF @id IS NULL" was false, View Royal was never created, and View Royal's
-- mappings were merged ONTO SAANICH. Saanich then ingested 835 applications
-- labelled "Town of View Royal", in the City of "View Royal".
--
-- That is the same defect class this module opens with: one row is supposed to
-- be one real thing. Two municipalities ended up sharing one configuration.
--
-- 303 has been corrected in place (SET @id = NULL before each lookup) so a
-- re-run is safe. This migration repairs the live damage.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

-- ── 1. Saanich is Saanich ───────────────────────────────────────────────────
DECLARE @saanich uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'Saanich_DevelopmentApplications');

IF @saanich IS NOT NULL
BEGIN
    UPDATE opportunities.OpportunitySourceMappings
    SET ValueJson = N'District of Saanich', UpdatedAtUtc = sysdatetimeoffset()
    WHERE OpportunitySourceId = @saanich AND [Key] = N'prospero.buyer';

    UPDATE opportunities.OpportunitySourceMappings
    SET ValueJson = N'Saanich', UpdatedAtUtc = sysdatetimeoffset()
    WHERE OpportunitySourceId = @saanich AND [Key] = N'prospero.cityOverride';

    -- Repair the rows already written under the wrong identity. Scoped to
    -- opportunities observed from the Saanich source, so nothing that is
    -- genuinely View Royal can be caught by it.
    UPDATE o
    SET o.BuyerName = N'District of Saanich',
        o.ProjectCity = N'Saanich',
        o.UpdatedAtUtc = sysdatetimeoffset()
    FROM opportunities.Opportunities o
    WHERE o.BuyerName = N'Town of View Royal'
      AND EXISTS (SELECT 1 FROM opportunities.OpportunityObservations ob
                  WHERE ob.OpportunityId = o.Id AND ob.OpportunitySourceId = @saanich);

    UPDATE ob
    SET ob.Buyer = N'District of Saanich'
    FROM opportunities.OpportunityObservations ob
    WHERE ob.OpportunitySourceId = @saanich AND ob.Buyer = N'Town of View Royal';
END;

-- ── 2. Create View Royal, this time actually ────────────────────────────────
DECLARE @vr uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'ViewRoyal_DevelopmentApplications');

IF @vr IS NULL
BEGIN
    SET @vr = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@vr, N'ViewRoyal_DevelopmentApplications', 21,
            N'https://www.viewroyal.ca/webapps/ourcity/prospero/search.aspx',
            1, 86400, 300, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'prospero.buyer',            N'Town of View Royal'),
    (N'prospero.cityOverride',     N'View Royal'),
    (N'prospero.provinceOverride', N'BC'),
    (N'playwright.maxPages',       N'60')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @vr AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
         VALUES (@vr, s.[Key], s.ValueJson, sysdatetimeoffset());

INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), @vr, 'Pending', SYSDATETIMEOFFSET(), 'viewroyal-repair'
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IngestionTriggers t
                  WHERE t.OpportunitySourceId = @vr AND t.Status IN ('Pending','InProgress'));
GO

SELECT s.Name, m.[Key], m.ValueJson
FROM opportunities.OpportunitySourceMappings m
JOIN opportunities.OpportunitySources s ON s.Id = m.OpportunitySourceId
WHERE s.SourceType = 21 AND m.[Key] IN (N'prospero.buyer', N'prospero.cityOverride')
ORDER BY s.Name, m.[Key];
GO

PRINT 'Migration 305: Saanich/View Royal identities separated and repaired.';
GO
