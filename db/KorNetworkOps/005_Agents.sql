/*
005_Agents.sql

The endpoint agent (Kor.Operations.NetworkOps.Agent): one row per PC it is installed on. Run as sa in
SSMS on KOR-APP01\SQLEXPRESS after 004. Idempotent. No logins change: the schema-wide grants in 001
(networkops_app) and 002 (networkops_ui, read) already cover a new table.

What the table is for:
  Agents   which PCs have the agent, the SHA-256 of the secret the installer gave each one (the secret
           itself exists only on that PC, readable by SYSTEM and Administrators), what version was
           installed, and when it last called in. The service refuses an agent whose secret does not
           hash to its row, and an agent on a removed row.

Installs, upgrades and removals are recorded in NetworkOps.Actions like every fix, so the audit stays
in one place.
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
IF OBJECT_ID(N'NetworkOps.PowerEvents', N'U') IS NULL
BEGIN
    RAISERROR('Run 004 first: NetworkOps.PowerEvents does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.Agents', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.Agents
        (
            DeviceId        int            NOT NULL CONSTRAINT PK_NetworkOps_Agents PRIMARY KEY
                                           CONSTRAINT FK_NetworkOps_Agents_Devices REFERENCES NetworkOps.Devices(DeviceId),
            SecretSha256    binary(32)     NOT NULL,
            Version         varchar(32)    NOT NULL,   -- what the installer put there
            InstalledUtc    datetime2(0)   NOT NULL,
            InstalledBy     nvarchar(128)  NOT NULL,
            LastContactUtc  datetime2(0)   NULL,       -- written at most every few minutes, not on every call
            LastVersion     varchar(32)    NULL,       -- what the agent itself reports
            LastAddress     varchar(45)    NULL,
            RemovedUtc      datetime2(0)   NULL
        );
    END;

    COMMIT;
    SELECT 'Agents' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.Agents;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
