/*
    Kor.OpportunitiesDb migration 295.

    Closes the IndustryEvents ingest gap found 2026-08-24: the table had a
    reaper (DataRetirementJob) but no feeder. Its 83 rows came from two manual
    loads (2026-05-28, 2026-06-21) and nothing since, so association calendars
    -- ICBA in particular -- were never represented at all.

    Adds:
      1. opportunities.IndustryEventSource -- one row per association calendar
         the worker polls. Sources live HERE, not in a markdown list.
      2. Provenance on opportunities.IndustryEvents (which source produced a
         row, when it was last seen in that source's feed).

    Idempotent. Seeding is done in code by IndustryEventSourceBootstrapHostedService
    so a fresh database self-populates on worker start.
*/

-- 1. Source catalogue -------------------------------------------------------
IF OBJECT_ID(N'opportunities.IndustryEventSource', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IndustryEventSource
    (
        Id bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_IndustryEventSource PRIMARY KEY,
        Name nvarchar(200) NOT NULL,              -- short handle, e.g. 'ICBA'
        Organizer nvarchar(300) NOT NULL,         -- written onto every event this source yields
        CalendarUrl nvarchar(800) NOT NULL,       -- the page/feed actually fetched
        SiteUrl nvarchar(800) NULL,
        ParserKey nvarchar(60) NOT NULL,          -- 'icba-cards' | 'ics' | 'jsonld'
        Region nvarchar(40) NULL,                 -- 'CA-BC' | 'CA-AB' | ...
        DefaultMarket nvarchar(100) NULL,
        DefaultEventType nvarchar(40) NULL,
        KorRelevance nvarchar(1000) NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_IndustryEventSource_IsActive DEFAULT 1,
        CrawlDelaySeconds int NOT NULL
            CONSTRAINT DF_IndustryEventSource_CrawlDelay DEFAULT 86400,
        LastPolledAtUtc datetimeoffset NULL,
        LastErrorMessage nvarchar(1000) NULL,
        LastEventCount int NULL,
        CreatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_IndustryEventSource_CreatedAtUtc DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_IndustryEventSource_UpdatedAtUtc DEFAULT sysdatetimeoffset()
    );

    CREATE UNIQUE INDEX UX_IndustryEventSource_CalendarUrl
        ON opportunities.IndustryEventSource (CalendarUrl);
END;
GO

-- 2. Provenance on the events table ----------------------------------------
IF COL_LENGTH(N'opportunities.IndustryEvents', N'IndustryEventSourceId') IS NULL
BEGIN
    ALTER TABLE opportunities.IndustryEvents
        ADD IndustryEventSourceId bigint NULL;
END;
GO

IF COL_LENGTH(N'opportunities.IndustryEvents', N'LastSeenAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.IndustryEvents
        ADD LastSeenAtUtc datetimeoffset NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_IndustryEvents_IndustryEventSource'
)
BEGIN
    ALTER TABLE opportunities.IndustryEvents
        ADD CONSTRAINT FK_IndustryEvents_IndustryEventSource
            FOREIGN KEY (IndustryEventSourceId)
            REFERENCES opportunities.IndustryEventSource (Id);
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_IndustryEvents_SourceId'
      AND object_id = OBJECT_ID(N'opportunities.IndustryEvents')
)
BEGIN
    CREATE INDEX IX_IndustryEvents_SourceId
        ON opportunities.IndustryEvents (IndustryEventSourceId)
        WHERE IndustryEventSourceId IS NOT NULL;
END;
GO

PRINT '295_IndustryEventSources complete';
GO
