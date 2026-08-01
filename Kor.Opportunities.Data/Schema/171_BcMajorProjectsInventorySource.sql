USE [KorOpportunitiesDb];
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'BC_MajorProjectsInventory')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(),
         N'BC_MajorProjectsInventory',
         18,
         N'https://catalogue.data.gov.bc.ca/api/3/action/package_show?id=ea4c0bdb-a63f-49a4-b14a-09c1560aad0b',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0);
END;
GO

PRINT 'Migration 171: BC Major Projects Inventory source seeded.';
GO
