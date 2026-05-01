-- =============================================================================
-- FileSync control plane schema for Kor.Operations.FileSync.Service.
--
-- Runs against the KorTransmittals database (KOR-APP01\SQLEXPRESS).
-- Idempotent: every CREATE / INSERT is guarded so repeat runs are safe.
--
-- Architecture: the .NET service writes status/heartbeat/run rows here;
-- the FileSync Command Center window inside Kor.Operations.App reads from
-- and writes to the same tables. No HTTP API; this DB is the IPC channel.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Schema
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'FileSync')
BEGIN
    EXEC('CREATE SCHEMA FileSync AUTHORIZATION dbo;');
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.Jobs  -- one row per registered job; canonical config.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'Jobs')
BEGIN
    CREATE TABLE FileSync.Jobs
    (
        JobName                 varchar(64)         NOT NULL PRIMARY KEY,
        DisplayName             nvarchar(128)       NOT NULL,
        Mode                    varchar(16)         NOT NULL CONSTRAINT DF_FileSync_Jobs_Mode DEFAULT ('Shadow'),
        CronExpression          varchar(64)         NULL,
        Enabled                 bit                 NOT NULL CONSTRAINT DF_FileSync_Jobs_Enabled DEFAULT (1),
        LastConfigChangedAt     datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_Jobs_LastChangedAt DEFAULT (sysdatetimeoffset()),
        LastConfigChangedBy     nvarchar(128)       NOT NULL CONSTRAINT DF_FileSync_Jobs_LastChangedBy DEFAULT (suser_sname()),
        Notes                   nvarchar(512)       NULL,
        CONSTRAINT CK_FileSync_Jobs_Mode CHECK (Mode IN ('Shadow', 'Live'))
    );
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.JobKnobs  -- name/value bag for per-job tunables.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'JobKnobs')
BEGIN
    CREATE TABLE FileSync.JobKnobs
    (
        JobName             varchar(64)         NOT NULL,
        KnobName            varchar(64)         NOT NULL,
        KnobValue           nvarchar(1024)      NULL,
        UpdatedAt           datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_JobKnobs_UpdatedAt DEFAULT (sysdatetimeoffset()),
        UpdatedBy           nvarchar(128)       NOT NULL CONSTRAINT DF_FileSync_JobKnobs_UpdatedBy DEFAULT (suser_sname()),
        CONSTRAINT PK_FileSync_JobKnobs PRIMARY KEY (JobName, KnobName),
        CONSTRAINT FK_FileSync_JobKnobs_Jobs FOREIGN KEY (JobName)
            REFERENCES FileSync.Jobs (JobName) ON DELETE CASCADE
    );
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.JobRuns  -- one row per execution.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'JobRuns')
BEGIN
    CREATE TABLE FileSync.JobRuns
    (
        RunId               bigint              NOT NULL IDENTITY(1,1) PRIMARY KEY,
        JobName             varchar(64)         NOT NULL,
        StartedAt           datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_JobRuns_StartedAt DEFAULT (sysdatetimeoffset()),
        CompletedAt         datetimeoffset(3)   NULL,
        Status              varchar(16)         NOT NULL CONSTRAINT DF_FileSync_JobRuns_Status DEFAULT ('Running'),
        Mode                varchar(16)         NOT NULL,
        TriggerSource       varchar(16)         NOT NULL,
        TriggeredBy         nvarchar(128)       NULL,
        Summary             nvarchar(max)       NULL,
        ErrorMessage        nvarchar(max)       NULL,
        ErrorStack          nvarchar(max)       NULL,
        HostName            nvarchar(128)       NOT NULL CONSTRAINT DF_FileSync_JobRuns_HostName DEFAULT (host_name()),
        ServiceVersion      varchar(32)         NULL,
        CONSTRAINT CK_FileSync_JobRuns_Status CHECK (Status IN ('Running', 'Success', 'Failed', 'TimedOut', 'Cancelled')),
        CONSTRAINT CK_FileSync_JobRuns_Mode CHECK (Mode IN ('Shadow', 'Live')),
        CONSTRAINT CK_FileSync_JobRuns_TriggerSource CHECK (TriggerSource IN ('Cron', 'Manual', 'Watcher', 'PostRun', 'Startup')),
        CONSTRAINT FK_FileSync_JobRuns_Jobs FOREIGN KEY (JobName)
            REFERENCES FileSync.Jobs (JobName)
    );

    CREATE INDEX IX_FileSync_JobRuns_JobName_StartedAt
        ON FileSync.JobRuns (JobName, StartedAt DESC)
        INCLUDE (Status, Mode, CompletedAt);
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.JobTriggers  -- manual fire requests from the Command Center UI.
-- The service polls Pending rows, claims them, and links to JobRuns.RunId.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'JobTriggers')
BEGIN
    CREATE TABLE FileSync.JobTriggers
    (
        TriggerId           bigint              NOT NULL IDENTITY(1,1) PRIMARY KEY,
        JobName             varchar(64)         NOT NULL,
        RequestedAt         datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_JobTriggers_RequestedAt DEFAULT (sysdatetimeoffset()),
        RequestedBy         nvarchar(128)       NOT NULL CONSTRAINT DF_FileSync_JobTriggers_RequestedBy DEFAULT (suser_sname()),
        ClaimedAt           datetimeoffset(3)   NULL,
        ClaimedByHost       nvarchar(128)       NULL,
        CompletedAt         datetimeoffset(3)   NULL,
        LinkedRunId         bigint              NULL,
        Status              varchar(16)         NOT NULL CONSTRAINT DF_FileSync_JobTriggers_Status DEFAULT ('Pending'),
        Args                nvarchar(max)       NULL,
        CONSTRAINT CK_FileSync_JobTriggers_Status CHECK (Status IN ('Pending', 'Claimed', 'Completed', 'Cancelled')),
        CONSTRAINT FK_FileSync_JobTriggers_Jobs FOREIGN KEY (JobName)
            REFERENCES FileSync.Jobs (JobName) ON DELETE CASCADE,
        CONSTRAINT FK_FileSync_JobTriggers_Runs FOREIGN KEY (LinkedRunId)
            REFERENCES FileSync.JobRuns (RunId)
    );

    CREATE INDEX IX_FileSync_JobTriggers_Pending
        ON FileSync.JobTriggers (Status, JobName, RequestedAt)
        WHERE Status = 'Pending';
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.JobLogTail  -- recent log lines per job, surfaced in the Command
-- Center. Pruned by the service to keep the last ~1000 per job.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'JobLogTail')
BEGIN
    CREATE TABLE FileSync.JobLogTail
    (
        LogId           bigint              NOT NULL IDENTITY(1,1) PRIMARY KEY,
        JobName         varchar(64)         NOT NULL,
        RunId           bigint              NULL,
        Timestamp       datetimeoffset(3)   NOT NULL CONSTRAINT DF_FileSync_JobLogTail_Timestamp DEFAULT (sysdatetimeoffset()),
        Level           varchar(8)          NOT NULL,
        Message         nvarchar(max)       NOT NULL,
        CONSTRAINT CK_FileSync_JobLogTail_Level CHECK (Level IN ('TRACE', 'DEBUG', 'INFO', 'WARN', 'ERROR', 'FATAL'))
    );

    CREATE INDEX IX_FileSync_JobLogTail_JobName_Timestamp
        ON FileSync.JobLogTail (JobName, Timestamp DESC)
        INCLUDE (Level);
END;
GO

-- -----------------------------------------------------------------------------
-- FileSync.ServiceHeartbeat  -- one row per host the service runs on.
-- Updated each Worker tick; the Command Center treats stale heartbeats as
-- "service down" alerts.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = 'FileSync' AND t.name = 'ServiceHeartbeat')
BEGIN
    CREATE TABLE FileSync.ServiceHeartbeat
    (
        HostName            nvarchar(128)       NOT NULL PRIMARY KEY,
        StartedAt           datetimeoffset(3)   NOT NULL,
        LastHeartbeatAt     datetimeoffset(3)   NOT NULL,
        ServiceVersion      varchar(32)         NULL,
        GlobalMode          varchar(16)         NOT NULL,
        WatcherGen          int                 NULL,
        JobsRegistered      int                 NOT NULL CONSTRAINT DF_FileSync_ServiceHeartbeat_JobsRegistered DEFAULT (0),
        CONSTRAINT CK_FileSync_ServiceHeartbeat_GlobalMode CHECK (GlobalMode IN ('Shadow', 'Live'))
    );
END;
GO

-- =============================================================================
-- Seed: register the 6 buckets from the migration plan with their PS1 cadence.
-- All start in Shadow mode; flipped to Live per-bucket after parity is proven.
-- =============================================================================

-- Send-Weekly-PM-Deadlines : Mondays @ 05:00 (handoff §9.4)
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'WeeklyPmDeadlines')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('WeeklyPmDeadlines',
            'Send weekly PM deadlines',
            'Shadow',
            '0 0 5 ? * MON',
            'Reads Project Deadlines.xlsx; emails each PM their week.');
END;

-- File-ConcreteTestReports : monthly batch (1st @ 00:30 to leave room for Move_Reports_To_EOR at 00:00)
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'ConcreteTestReports')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('ConcreteTestReports',
            'File concrete test reports',
            'Shadow',
            '0 30 0 1 * ?',
            'Excel mapping + monthly file moves on file server + per-PM email.');
END;

-- Move_Reports_To_EOR : 1st of month @ 00:00 (handoff §9.1)
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'MoveReportsToEor')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('MoveReportsToEor',
            'Move reports to EOR',
            'Shadow',
            '0 0 0 1 * ?',
            'Distribute project Reports/ to each EOR''s SharePoint folder.');
END;

-- Move_Reports_To_ToSend : currently manual cadence (handoff §9.2). NULL cron = manual fire only.
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'MoveReportsToToSend')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('MoveReportsToToSend',
            'Move acknowledged reports to file server',
            'Shadow',
            NULL,
            'Pulls EOR-acknowledged reports back to the file server. Manual fire until a schedule is set.');
END;

-- RenameReportsUploads : runs frequently in PS1 (handoff §9.3); cadence to be set in Command Center.
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'RenameReportsUploads')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('RenameReportsUploads',
            'Rename SharePoint report uploads',
            'Shadow',
            NULL,
            'Normalize <project>/Reports/ filenames. Cadence not codified in PS1; set via Command Center.');
END;

-- Watcher : long-running hosted service, not Quartz-triggered.
IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'Watcher')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('Watcher',
            'File-server watcher (real-time sync)',
            'Shadow',
            NULL,
            'FileSystemWatcher on \\KOR-FS01\Projects\Projects. Runs as a hosted service, not on a cron.');
END;

-- =============================================================================
-- Seed: default knobs. Magic numbers from handoff §7.7 with §11.4 fix
-- (FileStabilitySleepMs bumped from 1000 to 2500 to handle Newforma's
--  multi-phase saves).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'MaxSyncMinutes')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'MaxSyncMinutes', '15');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'DebounceSeconds')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'DebounceSeconds', '5');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'LockPollSeconds')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'LockPollSeconds', '10');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'MaxLockTrackHours')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'MaxLockTrackHours', '12');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'PostRunRetrySeconds')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'PostRunRetrySeconds', '120');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'FileStabilitySleepMs')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'FileStabilitySleepMs', '2500');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'HeartbeatMinutes')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'HeartbeatMinutes', '5');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'LivenessThresholdHours')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'LivenessThresholdHours', '24');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'RestartBackoffMaxSeconds')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'RestartBackoffMaxSeconds', '300');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'ImageUploadChunkBytes')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'ImageUploadChunkBytes', '5242880');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'SimpleVsChunkedThresholdBytes')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'SimpleVsChunkedThresholdBytes', '3670016');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'PaginationTopN')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'PaginationTopN', '200');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'Watcher' AND KnobName = 'ControlFileName')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('Watcher', 'ControlFileName', 'NOT SYNCED TO ACTIVE PROJECTS.txt');

-- ConcreteTestReports sender override (PS1 sends from app-admin@; rest send from ilalonde@)
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'ConcreteTestReports' AND KnobName = 'SenderAddress')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('ConcreteTestReports', 'SenderAddress', 'app-admin@korstructural.com');

GO

-- =============================================================================
-- Runtime grants: the FileSync.Service and the Command Center window will
-- connect as the existing low-privilege transmittals_app login. They need
-- read/write on the new schema only -- never DDL.
-- =============================================================================
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'transmittals_app')
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::FileSync TO transmittals_app;
END;
GO

