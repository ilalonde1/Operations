/*
    Kor.OpportunitiesDb migration 11.

    Extends HistoricalOpportunities with detail-page enrichment columns
    (BC Bid internal id, detail URL, full description, commodities, amendment
    count, award winner/value) and adds a new HistoricalOpportunityDocuments
    table for the upcoming document-downloader pass.

    Idempotent. Safe to re-run.
*/

-- 1. Enrichment columns on HistoricalOpportunities ------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'BcBidInternalId' AND Object_ID = Object_ID(N'opportunities.HistoricalOpportunities'))
BEGIN
    ALTER TABLE opportunities.HistoricalOpportunities
        ADD BcBidInternalId       nvarchar(50)        NULL,
            DetailUrl             nvarchar(2000)      NULL,
            FullDescription       nvarchar(max)       NULL,
            Commodities           nvarchar(1000)      NULL,
            AmendmentCount        int                 NULL,
            AwardedToOrganization nvarchar(300)       NULL,
            AwardedValue          decimal(18,2)       NULL,
            AwardedCurrency       nvarchar(3)         NULL,
            AwardedAtUtc          datetimeoffset(3)   NULL,
            DetailScrapedAtUtc    datetimeoffset(3)   NULL;
END;
GO

-- Indices to make filtering on enrichment fast in the Competition Info UI.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HistOpp_BcBidInternalId')
    CREATE INDEX IX_HistOpp_BcBidInternalId ON opportunities.HistoricalOpportunities (BcBidInternalId) WHERE BcBidInternalId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HistOpp_AwardedToOrganization')
    CREATE INDEX IX_HistOpp_AwardedToOrganization ON opportunities.HistoricalOpportunities (AwardedToOrganization) WHERE AwardedToOrganization IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HistOpp_AwardedAt')
    CREATE INDEX IX_HistOpp_AwardedAt ON opportunities.HistoricalOpportunities (AwardedAtUtc DESC) WHERE AwardedAtUtc IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HistOpp_DetailScrapedAt')
    CREATE INDEX IX_HistOpp_DetailScrapedAt ON opportunities.HistoricalOpportunities (DetailScrapedAtUtc) WHERE DetailScrapedAtUtc IS NULL;
GO

-- 2. HistoricalOpportunityDocuments table ---------------------------------------------
IF OBJECT_ID('opportunities.HistoricalOpportunityDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.HistoricalOpportunityDocuments
    (
        Id                          bigint              IDENTITY(1,1) NOT NULL,
        HistoricalOpportunityId     bigint              NOT NULL,
        FileName                    nvarchar(500)       NOT NULL,
        SourceUrl                   nvarchar(2000)      NOT NULL,
        LocalPath                   nvarchar(2000)      NULL,
        Sha256                      varbinary(32)       NULL,
        SizeBytes                   bigint              NULL,
        ContentType                 nvarchar(100)       NULL,
        DownloadAttemptCount        int                 NOT NULL CONSTRAINT DF_HistDocs_AttemptCount DEFAULT (0),
        LastAttemptAtUtc            datetimeoffset(3)   NULL,
        LastAttemptError            nvarchar(1000)      NULL,
        DiscoveredAtUtc             datetimeoffset(3)   NOT NULL CONSTRAINT DF_HistDocs_DiscoveredAt DEFAULT (sysdatetimeoffset()),
        DownloadedAtUtc             datetimeoffset(3)   NULL,

        CONSTRAINT PK_HistDocs PRIMARY KEY (Id),
        CONSTRAINT FK_HistDocs_HistOpp FOREIGN KEY (HistoricalOpportunityId)
            REFERENCES opportunities.HistoricalOpportunities (Id) ON DELETE CASCADE
    );

    -- Dedup: same (opportunity, source URL) is the natural unique key.
    CREATE UNIQUE INDEX UX_HistDocs_OppUrl
        ON opportunities.HistoricalOpportunityDocuments (HistoricalOpportunityId, SourceUrl);

    -- Download queue: rows where LocalPath IS NULL are pending.
    CREATE INDEX IX_HistDocs_Pending
        ON opportunities.HistoricalOpportunityDocuments (DiscoveredAtUtc)
        WHERE LocalPath IS NULL;

    -- Lookup by sha (de-dup across different opportunities pointing at the same file).
    CREATE INDEX IX_HistDocs_Sha
        ON opportunities.HistoricalOpportunityDocuments (Sha256)
        WHERE Sha256 IS NOT NULL;
END;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.HistoricalOpportunityDocuments TO opportunities_app;
GO

-- 3. Backfill DetailUrl + BcBidInternalId from existing observations -----------------
--    Some observations may already have the correct per-row URL (the in-flight scrape
--    with the URL-fix is landing them as we speak). This pulls one row's URL into the
--    opportunity. Re-runs are safe: only updates rows where DetailUrl is still NULL.
;WITH best_obs AS (
    SELECT obs.HistoricalOpportunityId,
           obs.Url,
           ROW_NUMBER() OVER (PARTITION BY obs.HistoricalOpportunityId ORDER BY obs.IngestedAtUtc DESC) AS rn
    FROM   opportunities.HistoricalOpportunityObservations obs
    WHERE  obs.HistoricalOpportunityId IS NOT NULL
      AND  obs.Url LIKE '%/page.aspx/en/bpm/process_manage_extranet/%'
)
UPDATE h
SET    DetailUrl        = b.Url,
       -- Extract the trailing numeric segment as the BC Bid internal ID.
       BcBidInternalId  = REVERSE(LEFT(REVERSE(b.Url), CHARINDEX('/', REVERSE(b.Url)) - 1))
FROM   opportunities.HistoricalOpportunities h
JOIN   best_obs b ON b.HistoricalOpportunityId = h.Id AND b.rn = 1
WHERE  h.DetailUrl IS NULL;
GO

PRINT 'Migration 11 complete.';
GO
