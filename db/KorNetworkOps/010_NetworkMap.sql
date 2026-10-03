/*
010_NetworkMap.sql

The port map: what is plugged in where, by name and person (Ian, 2026-10-02: "which DEVICES by name (and user) ... see it
clearly and then once it's perfected, be able to create documentation from it"). Run as sa in SSMS on
KOR-APP01\SQLEXPRESS after 009. Idempotent. No logins change: the schema-wide grants in 001 (networkops_app) and 002
(networkops_ui, read) already cover new tables.

What the tables are for:
  NetworkPlacements  one row per device (MAC) the network knows: where it is (a switch port, also seen on a port, an access
                     point, or unplaced), its name and where the name came from, its IP, the fleet PC it is and that PC's
                     person. Written by the service after every rack sweep (Core/Network/NetworkMaps: the rules), so the
                     map survives a restart and the documentation is generated from rows, not from a screen.
  NetworkMoves       a device that sat on one port and is now on another: from, to, when. The network's history -- a
                     desk moved, a cable swapped, a PC that appeared on the wrong switch.
The service builds the live map in memory too: the app's Network window works before this has run; history does not.
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
IF OBJECT_ID(N'NetworkOps.MeshNodes', N'U') IS NULL
BEGIN
    RAISERROR('Run 006 first: NetworkOps.MeshNodes does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.NetworkPlacements', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.NetworkPlacements
        (
            Mac           char(17)       NOT NULL CONSTRAINT PK_NetworkOps_NetworkPlacements PRIMARY KEY,   -- aa:bb:cc:dd:ee:ff
            Name          nvarchar(200)  NOT NULL,
            NameSource    varchar(32)    NOT NULL,   -- NetworkOps agent | UniFi device | rack | DHCP | UniFi client | maker | MAC only
            Placement     varchar(16)    NOT NULL,   -- port | also-seen | wireless | unplaced
            SwitchName    nvarchar(200)  NULL,       -- the switch (port, also-seen) or access point (wireless)
            Port          int            NULL,
            Ip            varchar(45)    NULL,
            Pc            nvarchar(100)  NULL,       -- the fleet PC it is
            UserName      nvarchar(200)  NULL,       -- that PC's person
            UserSource    varchar(32)    NULL,       -- usual | signed in now | last signed in
            Maker         nvarchar(100)  NULL,
            SeenUtc       datetime2(0)   NULL,       -- when the switch saw it connect / the controller last recorded it
            FirstSeenUtc  datetime2(0)   NOT NULL,   -- first time NetworkOps placed it
            UpdatedUtc    datetime2(0)   NOT NULL
        );
        CREATE INDEX IX_NetworkOps_NetworkPlacements_Port ON NetworkOps.NetworkPlacements(SwitchName, Port);
    END;

    IF OBJECT_ID(N'NetworkOps.NetworkMoves', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.NetworkMoves
        (
            MoveId      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_NetworkMoves PRIMARY KEY,
            Mac         char(17)       NOT NULL,
            Name        nvarchar(200)  NOT NULL,
            FromSwitch  nvarchar(200)  NULL,
            FromPort    int            NULL,
            ToSwitch    nvarchar(200)  NULL,
            ToPort      int            NULL,
            AtUtc       datetime2(0)   NOT NULL
        );
        CREATE INDEX IX_NetworkOps_NetworkMoves_At ON NetworkOps.NetworkMoves(AtUtc DESC);
    END;

    COMMIT;
    SELECT 'NetworkPlacements' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.NetworkPlacements
    UNION ALL SELECT 'NetworkMoves', COUNT(*) FROM NetworkOps.NetworkMoves;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
