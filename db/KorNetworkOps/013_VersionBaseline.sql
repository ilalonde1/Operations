/*
013_VersionBaseline.sql

CLASS 1 -- "every component records its version, and nothing compares it to the vendor's current secure build or
end-of-support date." This is the store that fixes that: one row per product with the minimum SAFE build, the vendor
end-of-support date, and WHEN A HUMAN LAST REVIEWED the row. Core/Rack/VersionBaselineRules compares each recorded
version fact (esxi.version, veeam.version, os.build+os.caption, fw.model) against it in the rack sweep, and nags
(baseline.stale) when a row goes un-reviewed for 45 days so the list cannot rot silently.

The Veeam row's MinSecureBuild + LastReviewedUtc are AUTO-REFRESHED from veeam.com/kb2680 by a job on APP01; the rest
are reviewed by hand. Seeding is INSERT-WHERE-NOT-EXISTS so a re-run never clobbers an auto-refreshed value.

Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 012. Idempotent. No logins change (schema-wide grants in 001/002).

Known-fault seeds (the 2026-10-05 audit): VMware ESXi 7.0 is past end of general support (2025-10-02); Veeam B&R below
12.3.2.4934 carries CVE-2025-64393 (CVSS 9.4, RCE by a backup operator).
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

    IF OBJECT_ID(N'NetworkOps.VersionBaseline', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.VersionBaseline
        (
            Product          nvarchar(128) NOT NULL CONSTRAINT PK_NetworkOps_VersionBaseline PRIMARY KEY,
            MinSecureBuild   nvarchar(64)  NULL,          -- lowest safe build: an ESXi build number, or a dotted version; NULL = none set
            EndOfSupportUtc  datetime2(0)  NULL,          -- vendor end of support; in the past => version.end-of-support
            Reason           nvarchar(400) NULL,          -- CVE / lifecycle note, shown on the finding
            Source           nvarchar(200) NULL,          -- where the value came from (e.g. veeam.com/kb2680)
            LastReviewedUtc  datetime2(0)  NOT NULL CONSTRAINT DF_NetworkOps_VersionBaseline_Reviewed DEFAULT (SYSUTCDATETIME()),
            UpdatedUtc       datetime2(0)  NOT NULL CONSTRAINT DF_NetworkOps_VersionBaseline_Updated  DEFAULT (SYSUTCDATETIME())
        );
    END;

    -- Seed (never overwrites an existing row, so the Veeam auto-refresh survives a re-run).
    INSERT INTO NetworkOps.VersionBaseline (Product, MinSecureBuild, EndOfSupportUtc, Reason, Source)
    SELECT v.Product, v.MinSecureBuild, v.EndOfSupportUtc, v.Reason, v.Source
    FROM (VALUES
        (N'VMware ESXi 7.0',               NULL,            '2025-10-02', N'vSphere 7 end of general support -- no more security fixes',                         N'broadcom lifecycle'),
        (N'Veeam Backup & Replication',    N'12.3.2.4934',  NULL,         N'CVE-2025-64393 (CVSS 9.4): remote code execution by a backup operator',               N'veeam.com/kb2680'),
        (N'Windows Server 2019',           N'10.0.17763',   '2029-01-09', NULL,                                                                                   N'microsoft lifecycle'),
        (N'Windows Server 2022',           N'10.0.20348',   '2031-10-14', NULL,                                                                                   N'microsoft lifecycle'),
        (N'Windows Server 2025',           N'10.0.26100',   '2034-10-10', NULL,                                                                                   N'microsoft lifecycle'),
        (N'pfSense Plus',                  N'25.07.1',      NULL,         N'seed = the build running at review; raise as Netgate ships security releases',         N'netgate')
    ) AS v(Product, MinSecureBuild, EndOfSupportUtc, Reason, Source)
    WHERE NOT EXISTS (SELECT 1 FROM NetworkOps.VersionBaseline b WHERE b.Product = v.Product);

    COMMIT;
    SELECT Product, MinSecureBuild, EndOfSupportUtc, LastReviewedUtc FROM NetworkOps.VersionBaseline ORDER BY Product;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
