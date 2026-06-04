IF COL_LENGTH(N'opportunities.OpportunitySources', N'ConfigJson') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'BcBid_Engineering')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, ConfigJson)
    SELECT
        N'BcBid_Engineering',
        8,
        BaseUrl,
        1,
        CrawlDelaySeconds,
        RequestTimeoutSeconds,
        N'{"bcbid.keyword":"engineering"}'
    FROM opportunities.OpportunitySources
    WHERE Name = N'BcBid';
END;
GO

PRINT 'Migration 63: BcBid_Engineering source seeded from existing BcBid row.';
GO
