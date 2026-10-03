/*
011_NetworkPlacementsSwitchMac.sql

A switch is known by its MAC, not its name. 010 compared switch NAMES to find a device that moved ports, so renaming a
switch (Ian, 2026-10-02: BMZ-SW01 -> KOR-...) would have logged every device on it as moved. This keeps the switch's MAC
beside its name; the service compares MACs once this has run (and names only for rows written before it).
Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 010. Idempotent.
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
IF OBJECT_ID(N'NetworkOps.NetworkPlacements', N'U') IS NULL
BEGIN
    RAISERROR('Run 010 first: NetworkOps.NetworkPlacements does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'NetworkOps.NetworkPlacements', N'SwitchMac') IS NULL
    ALTER TABLE NetworkOps.NetworkPlacements ADD SwitchMac char(17) NULL;   -- the switch / access point it is on, by MAC
GO
SELECT COUNT(*) AS [Placements], SUM(CASE WHEN SwitchMac IS NULL THEN 1 ELSE 0 END) AS [Without a switch MAC yet (filled at the next sweep)]
FROM NetworkOps.NetworkPlacements;
GO
