IF OBJECT_ID(N'opportunities.IndustryEvents', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IndustryEvents
    (
        Id bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_IndustryEvents PRIMARY KEY,
        Name nvarchar(300) NULL,
        Organizer nvarchar(300) NULL,
        EventType nvarchar(40) NULL,
        StartDate date NULL,
        EndDate date NULL,
        Recurrence nvarchar(200) NULL,
        City nvarchar(200) NULL,
        Market nvarchar(100) NULL,
        Format nvarchar(300) NULL,
        SectorsThemes nvarchar(max) NULL,
        Audience nvarchar(500) NULL,
        TargetsPresent nvarchar(max) NULL,
        RegistrationUrl nvarchar(1000) NULL,
        CostNote nvarchar(300) NULL,
        KorRelevance nvarchar(1000) NULL,
        SourceNote nvarchar(500) NULL,
        SourceKey nvarchar(64) NOT NULL,
        RetiredAtUtc datetimeoffset NULL,
        RetiredReason nvarchar(200) NULL,
        CreatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_IndustryEvents_CreatedAtUtc DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_IndustryEvents_UpdatedAtUtc DEFAULT sysdatetimeoffset()
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_IndustryEvents_SourceKey'
      AND object_id = OBJECT_ID(N'opportunities.IndustryEvents')
)
BEGIN
    CREATE UNIQUE INDEX UX_IndustryEvents_SourceKey
        ON opportunities.IndustryEvents (SourceKey);
END;
GO

PRINT '56_IndustryEvents complete';
GO
