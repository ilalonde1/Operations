USE [KorOpportunitiesDb];
GO
SET XACT_ABORT ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.AwardProgram', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.AwardProgram
    (
        Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AwardProgram PRIMARY KEY,
        NaturalKey           nvarchar(300) NOT NULL,
        AwardingBody         nvarchar(300) NOT NULL,
        ProgramName          nvarchar(300) NOT NULL,
        CycleYear            int NULL,
        Category             nvarchar(200) NULL,
        Discipline           nvarchar(100) NULL,
        Region               nvarchar(50) NULL,
        EligibilitySummary   nvarchar(max) NULL,
        SubmissionDeadline   date NULL,
        EntryFee             nvarchar(100) NULL,
        Url                  nvarchar(1000) NULL,
        SourceProvider       nvarchar(100) NOT NULL CONSTRAINT DF_AwardProgram_SourceProvider DEFAULT (N'AwardProgramFinder'),
        FirstSeenAtUtc       datetimeoffset NOT NULL CONSTRAINT DF_AwardProgram_FirstSeen DEFAULT (sysdatetimeoffset()),
        LastSeenAtUtc        datetimeoffset NOT NULL CONSTRAINT DF_AwardProgram_LastSeen DEFAULT (sysdatetimeoffset()),
        RetiredAtUtc         datetimeoffset NULL,
        RetiredReason        nvarchar(300) NULL,
        CreatedAtUtc         datetimeoffset NOT NULL CONSTRAINT DF_AwardProgram_Created DEFAULT (sysdatetimeoffset()),
        UpdatedAtUtc         datetimeoffset NOT NULL CONSTRAINT DF_AwardProgram_Updated DEFAULT (sysdatetimeoffset()),
        CONSTRAINT UQ_AwardProgram_NaturalKey UNIQUE (NaturalKey)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'opportunities.AwardProgram') AND name = N'IX_AwardProgram_Upcoming')
BEGIN
    CREATE INDEX IX_AwardProgram_Upcoming
        ON opportunities.AwardProgram (SubmissionDeadline, Region, Discipline)
        INCLUDE (AwardingBody, ProgramName, Category, EntryFee, Url)
        WHERE RetiredAtUtc IS NULL;
END
GO

PRINT 'Migration 258: AwardProgram catalog table created.';
GO
