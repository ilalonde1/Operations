/*
    KorOpportunitiesDb migration 352: session-written Director briefs and import provenance.
    The CLI imports; the App reads and acknowledges. Additive, guarded, no data moves.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.DirectorBrief', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DirectorBrief (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Opportunities_DirectorBrief PRIMARY KEY CLUSTERED,
        WeekOf DATE NOT NULL,
        Section NVARCHAR(32) NOT NULL CONSTRAINT CK_Opportunities_DirectorBrief_Section CHECK (Section IN (N'ThisWeek', N'Pursuits', N'Accounts', N'Market', N'Asks')),
        Headline NVARCHAR(512) NOT NULL,
        Body NVARCHAR(MAX) NOT NULL,
        Recommendation NVARCHAR(MAX) NULL,
        Citations NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Opportunities_DirectorBrief_Citations DEFAULT N'[]',
        WrittenBy NVARCHAR(100) NOT NULL,
        ImportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_Opportunities_DirectorBrief_ImportedAt DEFAULT sysdatetimeoffset(),
        ImportedBy NVARCHAR(100) NOT NULL,
        AcknowledgedAtUtc DATETIMEOFFSET NULL,
        AcknowledgedBy NVARCHAR(100) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Opportunities_DirectorBrief_WeekSection' AND object_id = OBJECT_ID(N'opportunities.DirectorBrief'))
    CREATE INDEX IX_Opportunities_DirectorBrief_WeekSection ON opportunities.DirectorBrief (WeekOf DESC, Section);
GO

IF OBJECT_ID(N'opportunities.DirectorBriefImport', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DirectorBriefImport (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Opportunities_DirectorBriefImport PRIMARY KEY CLUSTERED,
        WeekOf DATE NOT NULL,
        FileName NVARCHAR(260) NOT NULL,
        FileSha256 CHAR(64) NOT NULL,
        ExportSha256 CHAR(64) NOT NULL,
        ImportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_Opportunities_DirectorBriefImport_ImportedAt DEFAULT sysdatetimeoffset(),
        ImportedBy NVARCHAR(100) NOT NULL,
        SectionsWritten INT NOT NULL,
        Forced BIT NOT NULL
    );
END
GO
