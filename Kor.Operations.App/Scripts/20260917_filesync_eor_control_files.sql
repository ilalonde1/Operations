-- =============================================================================
-- FileSync.EorControlFiles -- the evidence behind the field-review round trip.
--
-- MoveReportsToEor (1st @ 00:00) writes one row per EOR folder it drops an
-- "Acknowledge and Move To Server <Month>.txt" into. MoveReportsToToSend
-- (5th @ 08:00) sweeps a folder to the file server ONLY when a row exists for
-- the period and that file is now gone. No row = nothing was asked of that
-- engineer = nothing moves.
--
-- Why (2026-09-17): the PS1 and the first port read "no control file" as
-- "acknowledged". CatchAll never gets one, so ~70 un-initialled reports a month
-- (69 Jun, 73 Jul, 84 Aug, 68 Sep 2026) went to the server as if reviewed.
--
-- Runs against KorTransmittals on KOR-APP01\SQLEXPRESS. Idempotent.
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'EorControlFiles')
BEGIN
    CREATE TABLE FileSync.EorControlFiles
    (
        PeriodKey       char(7)             NOT NULL,   -- 'yyyy-MM' of the 1st-of-month run
        EorFolder       nvarchar(255)       NOT NULL,   -- folder name under _FIELD REVIEWS TO INITIAL
        ControlFileName nvarchar(255)       NOT NULL,   -- exact name dropped, the 5th looks for THIS
        DroppedAt       datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_EorControlFiles_DroppedAt DEFAULT (sysdatetimeoffset()),
        CONSTRAINT PK_FileSync_EorControlFiles PRIMARY KEY (PeriodKey, EorFolder)
    );
END;
GO

-- The 1st-of-month run now mails whoever maintains EOR.csv the list of
-- reports it could not route. Same inbox as the 5th-of-month summary.
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'MoveReportsToEor' AND KnobName = 'CatchAllReportTo')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('MoveReportsToEor', 'CatchAllReportTo', 'admin@korstructural.com');
GO
