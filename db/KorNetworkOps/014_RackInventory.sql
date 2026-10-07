/*
014_RackInventory.sql

The rack inventory -- WHICH pieces of infrastructure NetworkOps reads and how to reach each (collector,
address, cert pin, Mesh name, UPS card, per-volume thresholds, SSH host-key pins). Until now this was the
only runtime state NetworkOps kept in a JSON file (the service's appsettings.json "Rack" block) while every
other thing it knows -- devices, observations, findings, power, agents -- lived here in SQL. Ian, 2026-10-07:
"get the rack in the DB!!! Why wouldn't it be? Professional means consistent." So here it is.

Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 001. Idempotent: creates the tables if missing and seeds
them ONCE (only when RackInventory is empty), so re-running changes nothing. The schema-wide grants in 001
(networkops_app) and 002 (networkops_ui, read) already cover new tables in the NetworkOps schema.

Seeded from the appsettings.json "Rack" block as it stood on 2026-10-07 -- 22 devices, the UniFi controller's
3 SSH host-key pins. The service reads this table at startup; if it is missing or empty it falls back to the
appsettings list, so deploying the DB-reading build before this runs never blanks the rack sweep. A test
(RackInventorySeedMatchesAppsettingsTests) asserts this seed and that block stay identical.

Two things are deliberately NOT moved here yet and stay in appsettings: the Ups[] SNMP cards (MIB + auth) and
the PowerChain shutdown plan. They are the power subsystem's config, not the device inventory, and are a
separate, deliberate move.
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
IF OBJECT_ID(N'NetworkOps.Devices', N'U') IS NULL
BEGIN
    RAISERROR('Run 001 first: NetworkOps.Devices does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- One row per rack device: the reach-config the sweep needs. Name matches NetworkOps.Devices.Name (the
    -- runtime row the sweep upserts). VolumeFreeWarnPct defaults to 10 (the app's own default); Address/MeshName/
    -- UpsName/CertSha256 are NULL when the device has none (the loader reads NULL as the empty string the app used).
    IF OBJECT_ID(N'NetworkOps.RackInventory', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.RackInventory
        (
            RackDeviceId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_RackInventory PRIMARY KEY,
            Name               nvarchar(64)  NOT NULL CONSTRAINT UQ_NetworkOps_RackInventory_Name UNIQUE,
            Kind               varchar(24)   NOT NULL,   -- Host | Storage | Backup | UPS | Network | Internet | Server
            Collector          varchar(24)   NOT NULL,   -- Esxi | Synology | Veeam | Ups | UniFi | CoreSwitch | Firewall | Printer | Internet | WindowsServer | MeshServer
            Address            nvarchar(64)  NULL,
            MeshName           nvarchar(64)  NULL,
            UpsName            nvarchar(64)  NULL,
            CertSha256         char(64)      NULL,
            VolumeFreeWarnPct  int           NOT NULL CONSTRAINT DF_NetworkOps_RackInventory_VolFree DEFAULT (10),
            Enabled            bit           NOT NULL CONSTRAINT DF_NetworkOps_RackInventory_Enabled DEFAULT (1),
            SortOrder          int           NOT NULL CONSTRAINT DF_NetworkOps_RackInventory_SortOrder DEFAULT (0)
        );
    END;

    -- SSH host-key pins for a collector that reaches a device over SSH (the UniFi controller). A device that
    -- presents any other host key is refused. One row per (device, pin).
    IF OBJECT_ID(N'NetworkOps.RackInventoryHostKey', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.RackInventoryHostKey
        (
            RackDeviceId  int           NOT NULL
                CONSTRAINT FK_NetworkOps_RackInventoryHostKey_Device REFERENCES NetworkOps.RackInventory(RackDeviceId) ON DELETE CASCADE,
            HostKey       varchar(100)  NOT NULL,
            CONSTRAINT PK_NetworkOps_RackInventoryHostKey PRIMARY KEY (RackDeviceId, HostKey)
        );
    END;

    -- Seed ONCE, from appsettings.json "Rack" as of 2026-10-07. SortOrder preserves the file's order (the
    -- Command Center lists the rack in it). Only NAS01 and Synology02 override VolumeFreeWarnPct (0 = a thick
    -- LUN whose volume is full by design); everything else takes the column default of 10.
    IF NOT EXISTS (SELECT 1 FROM NetworkOps.RackInventory)
    BEGIN
        INSERT NetworkOps.RackInventory (Name, Kind, Collector, Address, MeshName, UpsName, CertSha256, VolumeFreeWarnPct, SortOrder)
        VALUES
            (N'ESXi host .10 (production)',               'Host',     'Esxi',          N'192.168.1.10',  NULL,         NULL,            NULL, 10,  1),
            (N'ESXi host .16 (standby)',                  'Host',     'Esxi',          N'192.168.1.16',  NULL,         NULL,            NULL, 10,  2),
            (N'UC3200 SAN',                               'Storage',  'Synology',      N'192.168.1.12',  NULL,         NULL,            NULL, 10,  3),
            (N'NAS01 (Veeam repository)',                 'Storage',  'Synology',      N'192.168.1.15',  NULL,         NULL,            NULL,  0,  4),
            (N'Synology02 (Veeam repository)',            'Storage',  'Synology',      N'192.168.1.105', NULL,         NULL,            NULL,  0,  5),
            (N'Veeam backups (BK01)',                     'Backup',   'Veeam',         N'192.168.1.18',  N'KOR-BK01',  NULL,            '40B17CB016BC4DF092F0FA39E4112E2A485598D868B92E14E669034D4FCB4EFE', 10, 6),
            (N'Eaton 5PX UPS',                            'UPS',      'Ups',           NULL,             NULL,         N'Eaton 5PX',    NULL, 10,  7),
            (N'APC SRT1500 UPS',                          'UPS',      'Ups',           NULL,             NULL,         N'APC SRT1500',  NULL, 10,  8),
            (N'UniFi network',                            'Network',  'UniFi',         N'192.168.1.26',  NULL,         NULL,            '4A3CE816B00E665DD8B7D7FB14A804EB48202BF2EA79E660287B08D74E66183A', 10, 9),
            (N'Core switch (EdgeSwitch 10G)',             'Network',  'CoreSwitch',    N'192.168.1.11',  NULL,         NULL,            NULL, 10, 10),
            (N'Firewall (Netgate pfSense)',               'Network',  'Firewall',      N'192.168.1.1',   NULL,         NULL,            NULL, 10, 11),
            (N'Canon iR-ADV C5840 (copier)',              'Printer',  'Printer',       N'192.168.1.8',   NULL,         NULL,            NULL, 10, 12),
            (N'Canon TZ-30000 (plotter)',                 'Printer',  'Printer',       N'192.168.1.5',   NULL,         NULL,            NULL, 10, 13),
            (N'Brother HL-L6200DW',                       'Printer',  'Printer',       N'192.168.1.14',  NULL,         NULL,            NULL, 10, 14),
            (N'Brother HL-5450DN',                        'Printer',  'Printer',       N'192.168.1.156', NULL,         NULL,            NULL, 10, 15),
            (N'HP LaserJet 5200',                         'Printer',  'Printer',       N'192.168.1.220', NULL,         NULL,            NULL, 10, 16),
            (N'Internet (Netgate + Shaw)',                'Internet', 'Internet',      NULL,             NULL,         NULL,            NULL, 10, 17),
            (N'KOR-APP01 (apps, SQL, NetworkOps)',        'Server',   'WindowsServer', N'KOR-APP01',     NULL,         NULL,            NULL, 10, 18),
            (N'KOR-DC01 (domain controller, DNS, DHCP)',  'Server',   'WindowsServer', N'KOR-DC01',      NULL,         NULL,            NULL, 10, 19),
            (N'KOR-FS01 (file server)',                   'Server',   'WindowsServer', N'KOR-FS01',      N'Kor-FS01',  NULL,            NULL, 10, 20),
            (N'KOR-RDS01 (remote desktop)',               'Server',   'WindowsServer', N'KOR-RDS01',     N'Kor-RDS01', NULL,            NULL, 10, 21),
            (N'KOR-MESH01 (remote control, MeshCentral)', 'Server',   'MeshServer',    N'192.168.1.27',  NULL,         NULL,            NULL, 10, 22);

        DECLARE @unifi int = (SELECT RackDeviceId FROM NetworkOps.RackInventory WHERE Name = N'UniFi network');
        INSERT NetworkOps.RackInventoryHostKey (RackDeviceId, HostKey) VALUES
            (@unifi, 'SHA256:EqihW9l+4pvRH63kcz5ReUDliFQuuPDwAmhGseUKDT8'),
            (@unifi, 'SHA256:GhUGDvjFQHsnoIClbU8WAHboQxapk9tT/fZZJ0FMkog'),
            (@unifi, 'SHA256:RwXrc5Nv/c6hkKC/fDPJU6NOf+5OAhoggWnrRjiQnKY');
    END;

    IF OBJECT_ID(N'NetworkOps.RackInventory', N'U') IS NULL OR OBJECT_ID(N'NetworkOps.RackInventoryHostKey', N'U') IS NULL
        RAISERROR('Post-create assertion failed: a RackInventory table is missing.', 16, 1);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Verify
SELECT COUNT(*) AS [RackInventory rows] FROM NetworkOps.RackInventory;
SELECT COUNT(*) AS [HostKey rows] FROM NetworkOps.RackInventoryHostKey;
SELECT Name, Kind, Collector, Address, MeshName, UpsName, VolumeFreeWarnPct FROM NetworkOps.RackInventory ORDER BY SortOrder;
SET NOEXEC OFF;
GO
