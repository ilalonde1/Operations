/*
    Kor.OpportunitiesDb migration 274.

    CRM plan Phase 2/3 (2026-07-07). Additive only:

    1) opportunities.CrmBuyerEmailWarmth — nightly rollup of the filed-email
       corpus (KorEmailIndex, ~368k rows) per live-pursuit buyer org. Written
       by the Worker's CrmEnrichmentJob (the ONE nightly CRM enrichment job);
       read by the CRM engagement panel and the Overwatch staleness fusion.
       Aggregates only — counts, dates, one correspondent address. Never
       bodies (plan 3.1 privacy invariant).

    2) opportunities.BdUiOpens — minimal adoption instrumentation (plan 2.2c):
       one row per BD-surface open. Consumed by the kill-list review query,
       not by any UI. Fire-and-forget writes; losing a row is fine.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.CrmBuyerEmailWarmth', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.CrmBuyerEmailWarmth
    (
        CanonicalOrgId   BIGINT          NOT NULL,
        Domain           NVARCHAR(200)   NOT NULL,
        LastTouchUtc     DATETIMEOFFSET  NULL,   -- newest filed email in either direction
        LastInboundUtc   DATETIMEOFFSET  NULL,   -- newest filed email FROM the domain
        Emails90d        INT             NOT NULL CONSTRAINT DF_CrmWarmth_90d DEFAULT (0),
        EmailsAllTime    BIGINT          NOT NULL CONSTRAINT DF_CrmWarmth_All DEFAULT (0),
        TopCorrespondent NVARCHAR(320)   NULL,   -- most frequent FromEmail at the domain
        ComputedAtUtc    DATETIMEOFFSET  NOT NULL CONSTRAINT DF_CrmWarmth_At DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_CrmBuyerEmailWarmth PRIMARY KEY CLUSTERED (CanonicalOrgId),
        CONSTRAINT FK_CrmWarmth_Org FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg (Id) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'opportunities.BdUiOpens', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdUiOpens
    (
        Id          BIGINT IDENTITY(1,1) NOT NULL,
        Surface     NVARCHAR(50)         NOT NULL,  -- 'Pursuits' / 'Bazaar' / 'Overwatch' / 'Attribution'
        ByStaffId   NVARCHAR(150)        NULL,
        OpenedAtUtc DATETIMEOFFSET       NOT NULL CONSTRAINT DF_BdUiOpens_At DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_BdUiOpens PRIMARY KEY CLUSTERED (Id)
    );

    CREATE INDEX IX_BdUiOpens_Surface ON opportunities.BdUiOpens (Surface, OpenedAtUtc);
END;
GO

PRINT 'Migration 274 complete.';
GO
