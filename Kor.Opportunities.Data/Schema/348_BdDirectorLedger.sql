/*
    Kor.OpportunitiesDb migration 348: BD Director targets, bounded asks and decisions.
    Written by the BD Director window through SqlBdDirectorLedgerStore.
    Read by the Director window; the BD Dashboard reads open asks for the signed-in
    person and records their Done/Declined responses through the same store.
    Additive only, no data moves.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.BdTarget', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdTarget (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdTarget PRIMARY KEY CLUSTERED,
        Metric NVARCHAR(40) NOT NULL CONSTRAINT CK_BdTarget_Metric CHECK (Metric IN (N'LivePursuits', N'PipelineFee', N'AccountsWithLivePursuit', N'PursuitsOpened', N'BuyersTouched')),
        Scope NVARCHAR(20) NOT NULL CONSTRAINT CK_BdTarget_Scope CHECK (Scope IN (N'Firm', N'Region', N'Person')),
        ScopeKey NVARCHAR(150) NULL,
        PeriodStartUtc DATETIMEOFFSET NOT NULL,
        PeriodEndUtc DATETIMEOFFSET NOT NULL,
        TargetValue DECIMAL(18,2) NOT NULL,
        Note NVARCHAR(500) NULL,
        SetBy NVARCHAR(150) NOT NULL,
        SetAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdTarget_SetAt DEFAULT sysdatetimeoffset(),
        RetiredAtUtc DATETIMEOFFSET NULL,
        RetiredBy NVARCHAR(150) NULL,
        CONSTRAINT CK_BdTarget_Period CHECK (PeriodEndUtc > PeriodStartUtc)
    );
END
GO

IF OBJECT_ID(N'opportunities.BdAsk', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdAsk (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdAsk PRIMARY KEY CLUSTERED,
        ToStaffKey NVARCHAR(150) NOT NULL,
        EngagementId BIGINT NULL CONSTRAINT FK_BdAsk_Pursuit REFERENCES opportunities.CrmEngagements(Id),
        CanonicalOrgId BIGINT NULL CONSTRAINT FK_BdAsk_Account REFERENCES opportunities.CanonicalOrg(Id),
        Ask NVARCHAR(500) NOT NULL,
        WhyNow NVARCHAR(1000) NULL,
        Minutes INT NOT NULL CONSTRAINT CK_BdAsk_Minutes CHECK (Minutes BETWEEN 5 AND 180),
        DueUtc DATETIMEOFFSET NOT NULL,
        AskedBy NVARCHAR(150) NOT NULL,
        AskedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdAsk_AskedAt DEFAULT sysdatetimeoffset(),
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_BdAsk_Status DEFAULT N'Open'
            CONSTRAINT CK_BdAsk_Status CHECK (Status IN (N'Open', N'Done', N'Declined', N'Withdrawn')),
        ClosedAtUtc DATETIMEOFFSET NULL,
        ClosedBy NVARCHAR(150) NULL,
        ClosedNote NVARCHAR(500) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdAsk_ToStaffKey_Status' AND object_id = OBJECT_ID(N'opportunities.BdAsk'))
BEGIN
    CREATE INDEX IX_BdAsk_ToStaffKey_Status ON opportunities.BdAsk (ToStaffKey, Status) INCLUDE (DueUtc);
END
GO

IF OBJECT_ID(N'opportunities.BdDecision', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdDecision (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdDecision PRIMARY KEY CLUSTERED,
        Kind NVARCHAR(20) NOT NULL CONSTRAINT CK_BdDecision_Kind CHECK (Kind IN (N'Go', N'NoGo', N'Drop', N'Reassign', N'Price', N'Target', N'Other')),
        EngagementId BIGINT NULL CONSTRAINT FK_BdDecision_Pursuit REFERENCES opportunities.CrmEngagements(Id),
        CanonicalOrgId BIGINT NULL CONSTRAINT FK_BdDecision_Account REFERENCES opportunities.CanonicalOrg(Id),
        Decision NVARCHAR(500) NOT NULL,
        Rationale NVARCHAR(2000) NOT NULL,
        ExpectedOutcome NVARCHAR(500) NOT NULL,
        ReviewAtUtc DATETIMEOFFSET NOT NULL,
        DecidedBy NVARCHAR(150) NOT NULL,
        DecidedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdDecision_DecidedAt DEFAULT sysdatetimeoffset(),
        Outcome NVARCHAR(20) NOT NULL CONSTRAINT DF_BdDecision_Outcome DEFAULT N'Pending'
            CONSTRAINT CK_BdDecision_Outcome CHECK (Outcome IN (N'Pending', N'Right', N'Wrong', N'Unclear')),
        OutcomeNote NVARCHAR(1000) NULL,
        ScoredAtUtc DATETIMEOFFSET NULL,
        ScoredBy NVARCHAR(150) NULL
    );
END
GO
