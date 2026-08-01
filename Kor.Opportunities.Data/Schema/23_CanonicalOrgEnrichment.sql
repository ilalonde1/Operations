/*
    Kor.OpportunitiesDb migration 23.
    Tracking table for multi-source enrichment of CanonicalOrg rows.
    One row per (CanonicalOrgId, ProviderName). Idempotent.
*/
IF OBJECT_ID(N'opportunities.CanonicalOrgEnrichment', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CanonicalOrgEnrichment (
        Id                bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CanonicalOrgId    bigint           NOT NULL,
        ProviderName      nvarchar(60)     NOT NULL,
        Status            nvarchar(20)     NOT NULL, -- 'pending' | 'ok' | 'failed' | 'no_data' | 'blocked'
        LastRefreshAtUtc  datetimeoffset   NULL,     -- when this provider last succeeded
        LastAttemptAtUtc  datetimeoffset   NULL,
        NextRefreshAtUtc  datetimeoffset   NULL,     -- when scheduler should retry
        Attempts          int              NOT NULL DEFAULT 0,
        ErrorMessage      nvarchar(2000)   NULL,
        ResultJson        nvarchar(max)    NULL,     -- optional raw payload the provider returned
        Notes             nvarchar(max)    NULL,
        CreatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_CanonicalOrgEnrichment_CanonicalOrg
            FOREIGN KEY (CanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id)
    );

    CREATE UNIQUE INDEX UX_CanonicalOrgEnrichment_OrgProvider
        ON opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName);

    CREATE INDEX IX_CanonicalOrgEnrichment_NextRefresh
        ON opportunities.CanonicalOrgEnrichment (ProviderName, NextRefreshAtUtc)
        WHERE Status IN ('pending','ok','failed');
END;
GO

PRINT 'Migration 23 complete.';
GO
