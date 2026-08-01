/*
    Kor.OpportunitiesDb migration 09.
    Adds opportunities.OpportunityBids for bidder-level detail scraped from
    procurement result views such as BC Bid Unverified Bid Results.

    Idempotent: re-runs cleanly. Safe to apply via SSMS.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC ('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

IF OBJECT_ID('opportunities.OpportunityBids', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OpportunityBids
    (
        Id                      BIGINT IDENTITY(1,1) NOT NULL,
        OpportunitySourceId     UNIQUEIDENTIFIER  NOT NULL,
        ExternalReference       NVARCHAR(200)     NOT NULL,
        BidderName              NVARCHAR(300)     NOT NULL,
        BidAmount               DECIMAL(18,2)     NULL,
        BidCurrency             CHAR(3)           NOT NULL CONSTRAINT DF_OpportunityBids_Currency DEFAULT ('CAD'),
        BidderRank              INT               NULL,
        BidderAddress           NVARCHAR(500)     NULL,
        SourceUrl               NVARCHAR(800)     NULL,
        RawJson                 NVARCHAR(MAX)     NULL,
        IngestionRunId          UNIQUEIDENTIFIER  NULL,
        CreatedAtUtc            DATETIMEOFFSET    NOT NULL CONSTRAINT DF_OpportunityBids_CreatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc            DATETIMEOFFSET    NOT NULL CONSTRAINT DF_OpportunityBids_UpdatedAt DEFAULT (sysdatetimeoffset()),
        RowVersion              ROWVERSION        NOT NULL,
        CONSTRAINT PK_OpportunityBids PRIMARY KEY CLUSTERED (Id)
    );

    CREATE UNIQUE INDEX UX_OpportunityBids_SourceRefBidder
        ON opportunities.OpportunityBids (OpportunitySourceId, ExternalReference, BidderName);

    CREATE INDEX IX_OpportunityBids_SourceRef
        ON opportunities.OpportunityBids (OpportunitySourceId, ExternalReference);
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.OpportunityBids TO opportunities_app;
GO
