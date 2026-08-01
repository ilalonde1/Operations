/*
    Kor.OpportunitiesDb migration 08.
    Adds opportunities.OpportunityAwards - the competitive intelligence
    layer. One row per awarded contract scraped from procurement portals
    (BC Bid Contract Awards tab is the first source).

    Idempotent: re-runs cleanly. Safe to apply via SSMS.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC ('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

IF OBJECT_ID('opportunities.OpportunityAwards', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OpportunityAwards
    (
        Id                       BIGINT IDENTITY(1,1) NOT NULL,
        ExternalReference        NVARCHAR(200)     NOT NULL,
        OpportunitySourceId      UNIQUEIDENTIFIER  NOT NULL,
        Title                    NVARCHAR(400)     NOT NULL,
        SolicitationType         NVARCHAR(150)     NULL,
        AwardingOrganization     NVARCHAR(300)     NOT NULL,
        AwardedToOrganization    NVARCHAR(300)     NOT NULL,
        ContractValue            DECIMAL(18,2)     NULL,
        ContractCurrency         CHAR(3)           NOT NULL CONSTRAINT DF_OppAwards_Currency DEFAULT ('CAD'),
        AwardedAtUtc             DATETIMEOFFSET    NULL,
        IssuingLocation          NVARCHAR(300)     NULL,
        SupplierAddress          NVARCHAR(500)     NULL,
        ContactEmail             NVARCHAR(200)     NULL,
        ContractNumber           NVARCHAR(150)     NULL,
        SourceUrl                NVARCHAR(800)     NOT NULL,
        RawJson                  NVARCHAR(MAX)     NULL,
        CreatedAtUtc             DATETIMEOFFSET    NOT NULL CONSTRAINT DF_OppAwards_CreatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc             DATETIMEOFFSET    NOT NULL CONSTRAINT DF_OppAwards_UpdatedAt DEFAULT (sysdatetimeoffset()),
        IngestionRunId           UNIQUEIDENTIFIER  NULL,
        RowVersion               ROWVERSION        NOT NULL,
        CONSTRAINT PK_OpportunityAwards PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_OpportunityAwards_Source FOREIGN KEY (OpportunitySourceId)
            REFERENCES opportunities.OpportunitySources(Id)
    );

    -- Dedup: (source, external ref) is the natural unique key. Re-scraping
    -- the same award is idempotent.
    CREATE UNIQUE INDEX UX_OppAwards_SourceRef
        ON opportunities.OpportunityAwards (OpportunitySourceId, ExternalReference);

    CREATE INDEX IX_OppAwards_AwardedAt
        ON opportunities.OpportunityAwards (AwardedAtUtc DESC)
        INCLUDE (AwardedToOrganization, AwardingOrganization, ContractValue);

    CREATE INDEX IX_OppAwards_Winner
        ON opportunities.OpportunityAwards (AwardedToOrganization, AwardedAtUtc DESC);
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.OpportunityAwards TO opportunities_app;
GO
