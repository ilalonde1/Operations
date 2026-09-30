/*
004_RackPower.sql

The rack's power, for the UPS watcher and the shutdown chain (Kor.Operations.NetworkOps.Service,
the Power folder). Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 002. Idempotent. No logins change: the
schema-wide grants in 001 (networkops_app) and 002 (networkops_ui, read) already cover new tables.

What each table is for:
  PowerReadings   every UPS card read, kept once a minute and on every change (mains <-> battery,
                  answering <-> silent): the history behind the Command Center's power panel, and
                  the evidence after an outage of what the UPSes said and when.
  PowerEvents     what the watcher decided and did: each change of verdict (normal / degraded /
                  shutdown), each chain start, every chain step and its outcome, dry run or real.
                  After an outage this is the timeline; after a rehearsal it is the proof.
*/

USE KorNetworkOps;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @here sysname = DB_NAME();
IF @here <> N'KorNetworkOps'
BEGIN
    RAISERROR('Not in KorNetworkOps (in %s). Nothing below will run.', 16, 1, @here);
    SET NOEXEC ON;
END;
IF OBJECT_ID(N'NetworkOps.JobTriggers', N'U') IS NULL
BEGIN
    RAISERROR('Run 002 first: NetworkOps.JobTriggers does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.PowerReadings', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.PowerReadings
        (
            ReadingId         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_PowerReadings PRIMARY KEY,
            Ups               varchar(32)    NOT NULL,   -- the configured name, e.g. 'Eaton 5PX'
            AtUtc             datetime2(0)   NOT NULL,
            Reachable         bit            NOT NULL,
            Source            varchar(12)    NOT NULL,   -- Mains | Battery | Bypass | Off | Unknown
            SecondsOnBattery  int            NULL,
            MinutesRemaining  int            NULL,
            ChargePercent     tinyint        NULL,
            LoadPercent       tinyint        NULL,
            BatteryLow        bit            NOT NULL,
            ReplaceBattery    bit            NOT NULL,
            Error             nvarchar(400)  NULL
        );
        CREATE INDEX IX_NetworkOps_PowerReadings_Ups ON NetworkOps.PowerReadings(Ups, AtUtc);
        CREATE INDEX IX_NetworkOps_PowerReadings_At ON NetworkOps.PowerReadings(AtUtc);
    END;

    IF OBJECT_ID(N'NetworkOps.PowerEvents', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.PowerEvents
        (
            EventId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_PowerEvents PRIMARY KEY,
            AtUtc     datetime2(0)    NOT NULL CONSTRAINT DF_NetworkOps_PowerEvents_At DEFAULT (SYSUTCDATETIME()),
            ChainId   uniqueidentifier NULL,       -- groups one chain run's steps
            Kind      varchar(16)     NOT NULL,   -- Verdict | ChainStart | ChainStep | ChainEnd | Plan
            DryRun    bit             NOT NULL,
            Ok        bit             NOT NULL,
            Text      nvarchar(4000)  NOT NULL
        );
        CREATE INDEX IX_NetworkOps_PowerEvents_At ON NetworkOps.PowerEvents(AtUtc);
    END;

    COMMIT;
    SELECT 'PowerReadings' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.PowerReadings
    UNION ALL SELECT 'PowerEvents', COUNT(*) FROM NetworkOps.PowerEvents;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
