/*
002_LearningLayerAndCommandCenter.sql

Everything the learning layer and the Command Center need, in one migration, so nothing is
rebuilt later (docs/KOR-NetworkOps-Design-2026-09-28.md §8). Run as sa in SSMS on
KOR-APP01\SQLEXPRESS after 001. Idempotent.

What each table is for:
  DeviceFacts         what a PC IS, over time: one row per (fact, value) with first/last seen and
                      when it was superseded -- GPU driver, BIOS, Office build, RAM, app versions.
                      Current value = the row not superseded; a change = a new row. This is the
                      history and the change tracking ("started after the driver update").
  Metrics             numbers over time per PC (drive free space, SSD wear, hang/crash counts,
                      boot time ...) -- the trend lines that let problems be predicted before they
                      happen. Narrow on purpose: one number per row.
  Insights            what the fleet comparison found: "gpu-hangs: 9 of 10 PCs with hw.model =
                      ThinkStation P340, against 6 of 19 without". One active row per (problem, fact, value).
  FindingResolutions  when a finding clears, what changed on that PC around it -- the evidence
                      the fix ranking learns from.
  JobTriggers         "run a health check on this PC now" from the Command Center; the service
                      claims and runs it (the FileSync.JobTriggers pattern).
  Actions             every action taken on a PC, who/what/when/before/after (for remediations).
  DeviceNotes         your notes on a PC.
  Findings (+cols)    acknowledge / snooze a finding you already know about, with a note.

After: the service (networkops_app) reads and writes all of it; the Command Center login
(networkops_ui) reads everything and may only queue triggers, write notes and acknowledge.
*/

USE master;
SET NOCOUNT ON;
DECLARE @UiPassword nvarchar(128) = N'<<PASTE UI PASSWORD HERE>>';
IF @UiPassword LIKE N'<<%'
BEGIN
    RAISERROR('Set @UiPassword first. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END;
DECLARE @CreateUiLogin nvarchar(max) =
    N'CREATE LOGIN [networkops_ui] WITH PASSWORD = N''' + REPLACE(@UiPassword, N'''', N'''''') +
    N''', DEFAULT_DATABASE = [KorNetworkOps], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'networkops_ui')
    EXEC (@CreateUiLogin);
GO

USE KorNetworkOps;
GO
-- Filtered indexes need these ON. SSMS sets them by default; sqlcmd does not -- so say it here
-- instead of depending on which tool runs the script (caught rehearsing on LocalDB, 2026-09-28).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @here sysname = DB_NAME();
IF @here <> N'KorNetworkOps'
BEGIN
    RAISERROR('Not in KorNetworkOps (in %s). Nothing below will run.', 16, 1, @here);
    SET NOEXEC ON;
END;
IF OBJECT_ID(N'NetworkOps.Findings', N'U') IS NULL
BEGIN
    RAISERROR('Run 001 first: NetworkOps.Findings does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.DeviceFacts', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.DeviceFacts
        (
            DeviceFactId    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_DeviceFacts PRIMARY KEY,
            DeviceId        int            NOT NULL CONSTRAINT FK_NetworkOps_DeviceFacts_Devices REFERENCES NetworkOps.Devices(DeviceId),
            Fact            varchar(64)    NOT NULL,
            Value           nvarchar(400)  NOT NULL,
            FirstSeenUtc    datetime2(0)   NOT NULL,
            LastSeenUtc     datetime2(0)   NOT NULL,
            SupersededUtc   datetime2(0)   NULL
        );
        CREATE UNIQUE INDEX UX_NetworkOps_DeviceFacts_Current ON NetworkOps.DeviceFacts(DeviceId, Fact) WHERE SupersededUtc IS NULL;
        CREATE INDEX IX_NetworkOps_DeviceFacts_FactValue ON NetworkOps.DeviceFacts(Fact, SupersededUtc) INCLUDE (DeviceId, Value);
    END;

    IF OBJECT_ID(N'NetworkOps.Metrics', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Metrics
        (
            MetricId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Metrics PRIMARY KEY,
            DeviceId        int            NOT NULL CONSTRAINT FK_NetworkOps_Metrics_Devices REFERENCES NetworkOps.Devices(DeviceId),
            Metric          varchar(48)    NOT NULL,   -- e.g. disk.free.gb, ssd.wear.pct, gpu.hangs.14d
            Subject         nvarchar(128)  NOT NULL CONSTRAINT DF_NetworkOps_Metrics_Subject DEFAULT (N''),   -- e.g. 'C', a disk name, a process
            CollectedUtc    datetime2(0)   NOT NULL,
            Value           float          NOT NULL
        );
        CREATE INDEX IX_NetworkOps_Metrics_Series ON NetworkOps.Metrics(DeviceId, Metric, Subject, CollectedUtc) INCLUDE (Value);
        CREATE INDEX IX_NetworkOps_Metrics_Collected ON NetworkOps.Metrics(CollectedUtc);
    END;

    IF OBJECT_ID(N'NetworkOps.Insights', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Insights
        (
            InsightId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Insights PRIMARY KEY,
            RuleFamily       varchar(64)    NOT NULL,
            Fact             varchar(64)    NOT NULL,
            Value            nvarchar(400)  NOT NULL,
            AffectedWith     int            NOT NULL,
            TotalWith        int            NOT NULL,
            AffectedWithout  int            NOT NULL,
            TotalWithout     int            NOT NULL,
            Strength         float          NOT NULL,
            Summary          nvarchar(1000) NOT NULL,
            FirstSeenUtc     datetime2(0)   NOT NULL,
            LastSeenUtc      datetime2(0)   NOT NULL,
            ClearedUtc       datetime2(0)   NULL
        );
        CREATE UNIQUE INDEX UX_NetworkOps_Insights_Active ON NetworkOps.Insights(RuleFamily, Fact, Value) WHERE ClearedUtc IS NULL;
    END;

    IF OBJECT_ID(N'NetworkOps.FindingResolutions', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.FindingResolutions
        (
            FindingId         bigint         NOT NULL CONSTRAINT PK_NetworkOps_FindingResolutions PRIMARY KEY
                                             CONSTRAINT FK_NetworkOps_FindingResolutions_Findings REFERENCES NetworkOps.Findings(FindingId),
            DeviceId          int            NOT NULL,
            RuleKey           nvarchar(160)  NOT NULL,
            ClearedUtc        datetime2(0)   NOT NULL,
            Rebooted          bit            NOT NULL,
            ChangedFactsJson  nvarchar(max)  NULL,     -- facts that changed between the last open sighting and the clear
            ActionIdsJson     nvarchar(400)  NULL,     -- actions taken on the PC in that window
            Summary           nvarchar(1000) NOT NULL
        );
        CREATE INDEX IX_NetworkOps_FindingResolutions_Rule ON NetworkOps.FindingResolutions(RuleKey, ClearedUtc);
    END;

    IF OBJECT_ID(N'NetworkOps.JobTriggers', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.JobTriggers
        (
            TriggerId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_JobTriggers PRIMARY KEY,
            JobName         varchar(64)    NOT NULL,
            DeviceName      nvarchar(64)   NULL,      -- NULL = the whole fleet
            RequestedBy     nvarchar(128)  NOT NULL,
            RequestedUtc    datetime2(0)   NOT NULL CONSTRAINT DF_NetworkOps_JobTriggers_Requested DEFAULT (SYSUTCDATETIME()),
            ClaimedUtc      datetime2(0)   NULL,
            ClaimedBy       nvarchar(64)   NULL,
            CompletedUtc    datetime2(0)   NULL,
            Status          varchar(16)    NOT NULL CONSTRAINT DF_NetworkOps_JobTriggers_Status DEFAULT ('Pending'),
            Result          nvarchar(1000) NULL,
            CONSTRAINT CK_NetworkOps_JobTriggers_Status CHECK (Status IN ('Pending','Running','Done','Failed','Cancelled'))
        );
        CREATE INDEX IX_NetworkOps_JobTriggers_Pending ON NetworkOps.JobTriggers(Status, RequestedUtc);
    END;

    IF OBJECT_ID(N'NetworkOps.Actions', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Actions
        (
            ActionId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Actions PRIMARY KEY,
            DeviceId        int            NULL CONSTRAINT FK_NetworkOps_Actions_Devices REFERENCES NetworkOps.Devices(DeviceId),
            Kind            varchar(48)    NOT NULL,
            RequestedBy     nvarchar(128)  NOT NULL,
            RequestedUtc    datetime2(0)   NOT NULL CONSTRAINT DF_NetworkOps_Actions_Requested DEFAULT (SYSUTCDATETIME()),
            CompletedUtc    datetime2(0)   NULL,
            Status          varchar(16)    NOT NULL CONSTRAINT DF_NetworkOps_Actions_Status DEFAULT ('Requested'),
            Detail          nvarchar(2000) NULL,
            BeforeJson      nvarchar(max)  NULL,
            AfterJson       nvarchar(max)  NULL
        );
        CREATE INDEX IX_NetworkOps_Actions_Device ON NetworkOps.Actions(DeviceId, RequestedUtc DESC);
    END;

    IF OBJECT_ID(N'NetworkOps.DeviceNotes', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.DeviceNotes
        (
            NoteId          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_DeviceNotes PRIMARY KEY,
            DeviceId        int            NOT NULL CONSTRAINT FK_NetworkOps_DeviceNotes_Devices REFERENCES NetworkOps.Devices(DeviceId),
            Author          nvarchar(128)  NOT NULL,
            CreatedUtc      datetime2(0)   NOT NULL CONSTRAINT DF_NetworkOps_DeviceNotes_Created DEFAULT (SYSUTCDATETIME()),
            Body            nvarchar(4000) NOT NULL
        );
        CREATE INDEX IX_NetworkOps_DeviceNotes_Device ON NetworkOps.DeviceNotes(DeviceId, CreatedUtc DESC);
    END;

    IF COL_LENGTH(N'NetworkOps.Findings', N'AcknowledgedUtc') IS NULL
        ALTER TABLE NetworkOps.Findings ADD
            AcknowledgedUtc  datetime2(0)   NULL,
            AcknowledgedBy   nvarchar(128)  NULL,
            SnoozedUntilUtc  datetime2(0)   NULL,
            AckNote          nvarchar(1000) NULL;

    IF OBJECT_ID(N'NetworkOps.DeviceFacts', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.Metrics', N'U') IS NULL
       OR OBJECT_ID(N'NetworkOps.Insights', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.FindingResolutions', N'U') IS NULL
       OR OBJECT_ID(N'NetworkOps.JobTriggers', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.Actions', N'U') IS NULL
       OR OBJECT_ID(N'NetworkOps.DeviceNotes', N'U') IS NULL OR COL_LENGTH(N'NetworkOps.Findings', N'AcknowledgedUtc') IS NULL
        RAISERROR('Post-create assertion failed: a 002 object is missing.', 16, 1);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- The Command Center's user: reads everything; may only queue triggers, write notes, and
-- acknowledge/snooze findings. It cannot change findings' substance, devices or history.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'networkops_ui')
    CREATE USER [networkops_ui] FOR LOGIN [networkops_ui] WITH DEFAULT_SCHEMA = NetworkOps;
GRANT SELECT ON SCHEMA::NetworkOps TO [networkops_ui];
GRANT INSERT ON NetworkOps.JobTriggers TO [networkops_ui];
GRANT UPDATE (Status) ON NetworkOps.JobTriggers TO [networkops_ui];          -- cancel a pending trigger
GRANT INSERT ON NetworkOps.DeviceNotes TO [networkops_ui];
GRANT UPDATE (AcknowledgedUtc, AcknowledgedBy, SnoozedUntilUtc, AckNote) ON NetworkOps.Findings TO [networkops_ui];
GO

-- Verify
SELECT s.name + N'.' + t.name AS [Table] FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = N'NetworkOps' ORDER BY t.name;
SELECT name AS [User] FROM sys.database_principals WHERE name IN (N'networkops_app', N'networkops_ui');
SET NOEXEC OFF;
GO
