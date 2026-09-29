/*
000_CleanupMasterFromFailedFirstRun.sql

One-off repair, 2026-09-28. The first run of 001 failed to create KorNetworkOps (an illegal
EXEC()), and because its database guard shared the failed USE's batch, the rest of the script ran
in MASTER: schema NetworkOps and its five tables now sit, empty, in master. This removes exactly
those -- only in master, only the NetworkOps schema, and only while every table is still empty.

Run as sa in SSMS BEFORE re-running 001.
*/
USE master;
GO
IF DB_NAME() <> N'master' BEGIN RAISERROR('This repair runs ONLY in master.', 16, 1); SET NOEXEC ON; END;
GO
SET NOCOUNT ON;
IF SCHEMA_ID(N'NetworkOps') IS NULL
BEGIN
    PRINT 'master has no NetworkOps schema - nothing to clean.';
    SET NOEXEC ON;
END;
GO
DECLARE @rows bigint =
      ISNULL((SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id
              WHERE SCHEMA_NAME(t.schema_id) = N'NetworkOps' AND p.index_id IN (0, 1)), 0);
IF @rows > 0
BEGIN
    RAISERROR('NetworkOps tables in master hold %I64d rows - not dropping anything. Tell Claude.', 16, 1, @rows);
    SET NOEXEC ON;
END;
GO
DROP TABLE IF EXISTS NetworkOps.Findings;
DROP TABLE IF EXISTS NetworkOps.Observations;
DROP TABLE IF EXISTS NetworkOps.Devices;
DROP TABLE IF EXISTS NetworkOps.JobRuns;
DROP TABLE IF EXISTS NetworkOps.ServiceHeartbeat;
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'NetworkOps'))
    DROP SCHEMA NetworkOps;
GO
SELECT CASE WHEN SCHEMA_ID(N'NetworkOps') IS NULL THEN 'master is clean' ELSE 'NetworkOps schema STILL in master' END AS [Result];
SET NOEXEC OFF;
GO
