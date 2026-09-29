/*
003_WatchdogLogin.sql

The dead-man watcher's login. `netops watchdog` runs on KOR-FS01 every 10 minutes and reads ONE
thing: the newest row of NetworkOps.ServiceHeartbeat. So its login can read that table and nothing
else -- not the findings, not the PC inventory, and it can write nothing. FS01 is a file server that
many people's machines talk to; whatever credential sits on it should be worth as little as possible.

Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 001 (002 is not needed for this). Idempotent.
Set @WatchPassword first; the same value goes in the KOR_NETWORKOPS_WATCHDB machine variable on FS01.
*/

USE master;
SET NOCOUNT ON;
DECLARE @WatchPassword nvarchar(128) = N'<<PASTE WATCH PASSWORD HERE>>';
IF @WatchPassword LIKE N'<<%'
BEGIN
    RAISERROR('Set @WatchPassword first. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END;
DECLARE @CreateWatchLogin nvarchar(max) =
    N'CREATE LOGIN [networkops_watch] WITH PASSWORD = N''' + REPLACE(@WatchPassword, N'''', N'''''') +
    N''', DEFAULT_DATABASE = [KorNetworkOps], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'networkops_watch')
    EXEC (@CreateWatchLogin);
GO

USE KorNetworkOps;
GO
DECLARE @here sysname = DB_NAME();
IF @here <> N'KorNetworkOps'
BEGIN
    RAISERROR('Not in KorNetworkOps (in %s). Nothing below will run.', 16, 1, @here);
    SET NOEXEC ON;
END;
IF OBJECT_ID(N'NetworkOps.ServiceHeartbeat', N'U') IS NULL
BEGIN
    RAISERROR('Run 001 first: NetworkOps.ServiceHeartbeat does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'networkops_watch')
    CREATE USER [networkops_watch] FOR LOGIN [networkops_watch] WITH DEFAULT_SCHEMA = NetworkOps;
GRANT SELECT ON NetworkOps.ServiceHeartbeat TO [networkops_watch];
GO

-- Verify: exactly one permission, SELECT on the heartbeat.
SELECT pr.name AS [User], pe.permission_name AS [Permission], OBJECT_SCHEMA_NAME(pe.major_id) + N'.' + OBJECT_NAME(pe.major_id) AS [On]
FROM sys.database_permissions pe JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pr.name = N'networkops_watch' AND pe.class = 1;
SET NOEXEC OFF;
GO
