-- 273: Persist relevance-gate rejects for periodic review.
-- Completeness audit 2026-07-01: gate rejects were log-only, so systematic
-- false negatives (French postings, "Coal Harbour" word-trap, VCH health-
-- authority postings) evaporated with the log files. One row per
-- (SourceName, Title), upserted with a counter — the same posting is
-- re-rejected on every scheduled run, so append-only would explode.
IF OBJECT_ID(N'opportunities.RelevanceGateRejects', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.RelevanceGateRejects
    (
        Id                 BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_RelevanceGateRejects PRIMARY KEY,
        SourceName         NVARCHAR(200)  NOT NULL,
        Title              NVARCHAR(500)  NOT NULL,
        Buyer              NVARCHAR(300)  NULL,
        Url                NVARCHAR(2000) NULL,
        RejectReason       NVARCHAR(200)  NOT NULL,
        FirstRejectedAtUtc DATETIMEOFFSET NOT NULL
            CONSTRAINT DF_RelevanceGateRejects_First DEFAULT SYSDATETIMEOFFSET(),
        LastRejectedAtUtc  DATETIMEOFFSET NOT NULL
            CONSTRAINT DF_RelevanceGateRejects_Last DEFAULT SYSDATETIMEOFFSET(),
        RejectCount        INT            NOT NULL
            CONSTRAINT DF_RelevanceGateRejects_Count DEFAULT 1
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_RelevanceGateRejects_SourceTitle'
      AND object_id = OBJECT_ID(N'opportunities.RelevanceGateRejects'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_RelevanceGateRejects_SourceTitle
        ON opportunities.RelevanceGateRejects (SourceName, Title);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_RelevanceGateRejects_LastRejected'
      AND object_id = OBJECT_ID(N'opportunities.RelevanceGateRejects'))
BEGIN
    CREATE INDEX IX_RelevanceGateRejects_LastRejected
        ON opportunities.RelevanceGateRejects (LastRejectedAtUtc);
END;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON opportunities.RelevanceGateRejects TO opportunities_app;
GO

PRINT 'Migration 273: opportunities.RelevanceGateRejects created.';
GO
