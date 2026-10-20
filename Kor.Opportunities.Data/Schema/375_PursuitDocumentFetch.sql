/*
    KorOpportunitiesDb migration 375: the log of a portal's bid documents being fetched into a pursuit (20 October 2026).

    The Worker downloads a tender's whole bid package (Bids&Tenders serves it as one zip at
    /Module/Tenders/{lang}/Tender/DownloadBidDocuments/{tender id}, signed in with the one company login) and stores each file
    on the pursuit in opportunities.OpportunityFiles. A pursuit is fetched once, then again at most daily until its deadline so
    addenda arrive. This table is how the job knows when it last fetched, and what the fetch found: a row per fetch.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.PursuitDocumentFetch', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PursuitDocumentFetch (
        Id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_PursuitDocumentFetch PRIMARY KEY CLUSTERED,
        EngagementId BIGINT NOT NULL CONSTRAINT FK_PursuitDocumentFetch_Engagement REFERENCES opportunities.CrmEngagements (Id),
        Portal NVARCHAR(40) NOT NULL,
        FetchedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_PursuitDocumentFetch_At DEFAULT SYSDATETIMEOFFSET(),
        Succeeded BIT NOT NULL,
        FilesInPackage INT NOT NULL,
        FilesStored INT NOT NULL,
        Note NVARCHAR(400) NULL
    );
    CREATE INDEX IX_PursuitDocumentFetch_Engagement ON opportunities.PursuitDocumentFetch (EngagementId, Portal, FetchedAtUtc);
END
GO
