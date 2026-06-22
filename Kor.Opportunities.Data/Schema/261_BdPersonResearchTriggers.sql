/* Kor.OpportunitiesDb migration 261: durable ad-hoc person-research queue.
   Mirrors BdResearchTriggers, keyed to IntelPerson instead of CanonicalOrg. */

IF OBJECT_ID(N'opportunities.BdPersonResearchTriggers', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdPersonResearchTriggers
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT DF_BdPersonResearchTriggers_Id DEFAULT (NEWID()),
        IntelPersonId   bigint           NOT NULL,
        ProviderName    nvarchar(64)     NOT NULL,
        Status          nvarchar(32)     NOT NULL CONSTRAINT DF_BdPersonResearchTriggers_Status DEFAULT (N'Pending'),
        RequestedBy     nvarchar(150)    NOT NULL,
        RequestedAtUtc  datetimeoffset   NOT NULL CONSTRAINT DF_BdPersonResearchTriggers_RequestedAt DEFAULT (sysdatetimeoffset()),
        ClaimedAtUtc    datetimeoffset   NULL,
        ClaimedBy       nvarchar(200)    NULL,
        ClaimToken      uniqueidentifier NULL,
        ReclaimedCount  int              NOT NULL CONSTRAINT DF_BdPersonResearchTriggers_ReclaimedCount DEFAULT (0),
        CompletedAtUtc  datetimeoffset   NULL,
        ErrorSummary    nvarchar(2000)   NULL,
        InputTokens     bigint           NULL,
        OutputTokens    bigint           NULL,
        CONSTRAINT PK_BdPersonResearchTriggers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_BdPersonResearchTriggers_IntelPerson FOREIGN KEY (IntelPersonId)
            REFERENCES opportunities.IntelPerson(Id),
        CONSTRAINT CK_BdPersonResearchTriggers_Status
            CHECK (Status IN (N'Pending', N'InProgress', N'Completed', N'Failed', N'Cancelled'))
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_BdPersonResearchTriggers_StatusRequestedAt'
      AND object_id = OBJECT_ID(N'opportunities.BdPersonResearchTriggers'))
BEGIN
    CREATE INDEX IX_BdPersonResearchTriggers_StatusRequestedAt
        ON opportunities.BdPersonResearchTriggers (Status, RequestedAtUtc)
        INCLUDE (IntelPersonId, ProviderName);
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.BdPersonResearchTriggers TO opportunities_app;
GO
