/*
001_CreateDatabaseAndSchema.sql

Creates KorNetworkOps -- the store behind KOR NetworkOps (docs/KOR-NetworkOps-Design-2026-09-28.md) --
its NetworkOps schema, and the SQL login the service uses. Run ONCE as sa in SSMS on
KOR-APP01\SQLEXPRESS. Idempotent: running it again changes nothing that already exists.

Why its own database (Ian, 2026-09-28): SQL Express caps each DATABASE at 10 GB, and the
observation history grows daily -- it must not eat into KorTransmittals, which FileSync and the
app depend on. It will later hold the credential vault, so its login reaches nothing else.

Before running: put the password in @AppPassword below (the service reads the same value from
the KOR_NETWORKOPS_DB machine environment variable on KOR-APP01). The script refuses to run
with the placeholder. No password is ever committed to the repo.

After: the service can record devices, observations, findings, runs and its heartbeat.
*/

-- ============================================================================ part 1: master
USE master;
SET NOCOUNT ON;

DECLARE @AppPassword nvarchar(128) = N'<<PASTE PASSWORD HERE>>';

IF @AppPassword LIKE N'<<%'
BEGIN
    RAISERROR('Set @AppPassword first. Nothing was changed.', 16, 1);
    SET NOEXEC ON;   -- stop the rest of the script, including later batches
END;

IF DB_ID(N'KorNetworkOps') IS NULL
    CREATE DATABASE KorNetworkOps;

-- EXEC() takes a variable or literals, never a function call: build the statement first.
DECLARE @CreateLogin nvarchar(max) =
    N'CREATE LOGIN [networkops_app] WITH PASSWORD = N''' + REPLACE(@AppPassword, N'''', N'''''') +
    N''', DEFAULT_DATABASE = [KorNetworkOps], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'networkops_app')
    EXEC (@CreateLogin);
GO

-- ============================================================================ part 2: the database
USE KorNetworkOps;
GO
-- Filtered indexes need these ON. SSMS sets them by default; sqlcmd does not -- so say it here
-- instead of depending on which tool runs the script (caught rehearsing on LocalDB, 2026-09-28).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- In its OWN batch: when the USE above fails (database missing), SSMS carries on in the previous
-- database -- master. The guard must therefore run after the USE's batch, not inside it, and
-- NOEXEC stops every later batch, including the table creates (2026-09-28: the first attempt
-- created the NetworkOps tables in master because the guard shared the failed batch).
DECLARE @here sysname = DB_NAME();   -- RAISERROR takes variables, not function calls
IF @here <> N'KorNetworkOps'
BEGIN
    RAISERROR('Not in KorNetworkOps (in %s). Nothing below will run.', 16, 1, @here);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'NetworkOps')
    EXEC (N'CREATE SCHEMA NetworkOps AUTHORIZATION dbo;');
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- Every machine NetworkOps knows about, and how it can be reached (the channel census).
    IF OBJECT_ID(N'NetworkOps.Devices', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Devices
        (
            DeviceId          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Devices PRIMARY KEY,
            Name              nvarchar(64)  NOT NULL,
            Kind              varchar(24)   NOT NULL CONSTRAINT DF_NetworkOps_Devices_Kind DEFAULT ('Workstation'),
            Source            varchar(16)   NOT NULL CONSTRAINT DF_NetworkOps_Devices_Source DEFAULT ('AD'),
            InDirectory       bit           NOT NULL CONSTRAINT DF_NetworkOps_Devices_InDirectory DEFAULT (1),
            FirstSeenUtc      datetime2(0)  NOT NULL CONSTRAINT DF_NetworkOps_Devices_FirstSeen DEFAULT (SYSUTCDATETIME()),
            LastCensusUtc     datetime2(0)  NULL,
            LastReachableUtc  datetime2(0)  NULL,     -- freshness: silence is a finding
            AnsweredOn        varchar(45)   NULL,
            Resolved          varchar(200)  NULL,
            AdminWrite        bit           NULL,
            ServiceControl    bit           NULL,
            WinRm             bit           NULL,
            Rdp               bit           NULL,
            RemoteRegistry    varchar(20)   NULL,
            RetiredUtc        datetime2(0)  NULL,
            CONSTRAINT UQ_NetworkOps_Devices_Name UNIQUE (Name)
        );
    END;

    -- Raw probe results, one row per (device, probe, run). Kept 90 days by the service.
    IF OBJECT_ID(N'NetworkOps.Observations', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Observations
        (
            ObservationId  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Observations PRIMARY KEY,
            DeviceId       int           NOT NULL CONSTRAINT FK_NetworkOps_Observations_Devices REFERENCES NetworkOps.Devices(DeviceId),
            Probe          varchar(32)   NOT NULL,
            ProbeVersion   int           NULL,
            CollectedUtc   datetime2(0)  NOT NULL,
            Status         varchar(16)   NOT NULL,   -- Ok | Offline | Timeout | ScriptError | ...
            PayloadJson    nvarchar(max) NULL,
            Error          nvarchar(1000) NULL
        );
        CREATE INDEX IX_NetworkOps_Observations_Device ON NetworkOps.Observations(DeviceId, Probe, CollectedUtc DESC);
    END;

    -- What the rules concluded. At most ONE active finding per (device, rule): raising the same
    -- rule again updates LastSeen/Evidence; a rule that stops firing clears it.
    IF OBJECT_ID(N'NetworkOps.Findings', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Findings
        (
            FindingId         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_Findings PRIMARY KEY,
            DeviceId          int            NOT NULL CONSTRAINT FK_NetworkOps_Findings_Devices REFERENCES NetworkOps.Devices(DeviceId),
            RuleKey           nvarchar(160)  NOT NULL,
            Severity          tinyint        NOT NULL,   -- 0 Info, 1 Warning, 2 Critical
            Title             nvarchar(200)  NOT NULL,
            Evidence          nvarchar(2000) NOT NULL,
            FirstSeenUtc      datetime2(0)   NOT NULL,
            LastSeenUtc       datetime2(0)   NOT NULL,
            ClearedUtc        datetime2(0)   NULL,
            NotifiedUtc       datetime2(0)   NULL,
            NotifiedSeverity  tinyint        NULL
        );
        CREATE UNIQUE INDEX UX_NetworkOps_Findings_Active ON NetworkOps.Findings(DeviceId, RuleKey) WHERE ClearedUtc IS NULL;
        CREATE INDEX IX_NetworkOps_Findings_Open ON NetworkOps.Findings(ClearedUtc, Severity) INCLUDE (DeviceId, RuleKey, NotifiedUtc);
    END;

    -- Every job run, success or failure -- the same shape as FileSync.JobRuns.
    IF OBJECT_ID(N'NetworkOps.JobRuns', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.JobRuns
        (
            RunId           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_JobRuns PRIMARY KEY,
            JobName         varchar(64)   NOT NULL,
            Host            nvarchar(64)  NOT NULL,
            ServiceVersion  varchar(32)   NULL,
            StartedUtc      datetime2(0)  NOT NULL CONSTRAINT DF_NetworkOps_JobRuns_Started DEFAULT (SYSUTCDATETIME()),
            FinishedUtc     datetime2(0)  NULL,
            Status          varchar(16)   NOT NULL CONSTRAINT DF_NetworkOps_JobRuns_Status DEFAULT ('Running'),
            Summary         nvarchar(2000) NULL,
            Error           nvarchar(max) NULL
        );
        CREATE INDEX IX_NetworkOps_JobRuns_Job ON NetworkOps.JobRuns(JobName, StartedUtc DESC);
    END;

    -- The service's own pulse. The dead-man watcher reads THIS, from another machine.
    IF OBJECT_ID(N'NetworkOps.ServiceHeartbeat', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.ServiceHeartbeat
        (
            Host            nvarchar(64)  NOT NULL CONSTRAINT PK_NetworkOps_ServiceHeartbeat PRIMARY KEY,
            StartedUtc      datetime2(0)  NOT NULL,
            LastBeatUtc     datetime2(0)  NOT NULL,
            ServiceVersion  varchar(32)   NULL
        );
    END;

    IF OBJECT_ID(N'NetworkOps.Devices', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.Observations', N'U') IS NULL
       OR OBJECT_ID(N'NetworkOps.Findings', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.JobRuns', N'U') IS NULL
       OR OBJECT_ID(N'NetworkOps.ServiceHeartbeat', N'U') IS NULL
        RAISERROR('Post-create assertion failed: a NetworkOps table is missing.', 16, 1);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- The service's user: data rights on its own schema, nothing else, no DDL.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'networkops_app')
    CREATE USER [networkops_app] FOR LOGIN [networkops_app] WITH DEFAULT_SCHEMA = NetworkOps;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::NetworkOps TO [networkops_app];
GO

-- Verify
SELECT s.name + N'.' + t.name AS [Table], t.create_date
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = N'NetworkOps' ORDER BY t.name;
SELECT HAS_PERMS_BY_NAME(N'NetworkOps', N'SCHEMA', N'INSERT') AS [sa can insert];
SET NOEXEC OFF;
GO
