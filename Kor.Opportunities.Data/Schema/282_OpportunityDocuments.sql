/*
    Kor.OpportunitiesDb migration 282.

    Live-opportunity RFP documents captured by the Phase-2 detail-enricher
    (BC Bid / Bids&Tenders / MERX / Bonfire). Distinct from
    opportunities.OpportunityFiles, which holds USER-attached pursuit files on a
    LAN share; this table holds SOURCE-published document references (the RFP
    PDFs, terms of reference, addenda) discovered on an opportunity's detail page.

    Additive + idempotent + non-destructive. FK is ON DELETE NO ACTION to match
    the soft-retire (RetiredAtUtc) doctrine — opportunities are archived, never
    hard-deleted, so no cascade is needed and NO ACTION refuses to silently drop
    document rows. A unique (OpportunityId, DocumentUrl) index makes the
    enricher's writes idempotent: re-fetching a detail page cannot create
    duplicate document rows.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'opportunities' AND t.name = N'OpportunityDocuments')
BEGIN
    CREATE TABLE opportunities.OpportunityDocuments
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL,
        OpportunityId   BIGINT              NOT NULL,
        DocumentName    NVARCHAR(400)       NULL,
        DocumentUrl     NVARCHAR(1000)      NOT NULL,
        ContentType     NVARCHAR(200)       NULL,
        SourcePortal    NVARCHAR(40)        NULL,   -- BCBID / BIDSTENDERS / MERX / BONFIRE
        LocalPath       NVARCHAR(1000)      NULL,   -- set once the file is downloaded (optional)
        FetchedAtUtc    DATETIMEOFFSET      NOT NULL CONSTRAINT DF_Opp_Docs_FetchedAt DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_Opp_Docs PRIMARY KEY (Id),
        CONSTRAINT FK_Opp_Docs_Opportunity FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_Opp_Docs_Opp_Url
        ON opportunities.OpportunityDocuments (OpportunityId, DocumentUrl);

    CREATE INDEX IX_Opp_Docs_OpportunityId
        ON opportunities.OpportunityDocuments (OpportunityId);
END;
GO

PRINT 'Migration 282 complete.';
GO
