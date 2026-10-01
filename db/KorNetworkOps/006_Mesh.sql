/*
006_Mesh.sql

Remote control (MeshCentral on KOR-MESH01): which NetworkOps device is which MeshCentral node. Run as sa in SSMS on
KOR-APP01\SQLEXPRESS after 005. Idempotent. No logins change: the schema-wide grants in 001 (networkops_app) and 002
(networkops_ui, read) already cover a new table.

What the table is for:
  MeshNodes   one row per device MeshCentral knows: its node id (what the Command Center's Connect button opens), the
              device group, whether its Mesh agent was connected at the last read, and when it was last connected.
              Written by the service's MeshSweep job every 5 minutes; the service also keeps the live state in memory,
              so remote control works before this table exists and the table is what survives a service restart.
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
IF OBJECT_ID(N'NetworkOps.Agents', N'U') IS NULL
BEGIN
    RAISERROR('Run 005 first: NetworkOps.Agents does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.MeshNodes', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.MeshNodes
        (
            DeviceId          int            NOT NULL CONSTRAINT PK_NetworkOps_MeshNodes PRIMARY KEY
                                             CONSTRAINT FK_NetworkOps_MeshNodes_Devices REFERENCES NetworkOps.Devices(DeviceId),
            NodeId            varchar(100)   NOT NULL,   -- e.g. node//bJ@yhUBjIF4c8rS8MThrje0...
            MeshName          nvarchar(64)   NOT NULL,   -- the name in MeshCentral (KOR-302N, Kor-APP01)
            MeshGroup         nvarchar(64)   NOT NULL,   -- KOR PCs | KOR Servers
            Connected         bit            NOT NULL,
            LastConnectedUtc  datetime2(0)   NULL,
            LastReadUtc       datetime2(0)   NOT NULL
        );
        CREATE UNIQUE INDEX UX_NetworkOps_MeshNodes_Node ON NetworkOps.MeshNodes(NodeId);
    END;

    COMMIT;
    SELECT 'MeshNodes' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.MeshNodes;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
