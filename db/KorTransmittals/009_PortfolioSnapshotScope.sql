/*
009_PortfolioSnapshotScope.sql  (2026-10-18)

Gives dbo.FinancialPortfolioSnapshot a Scope column so the watchlist and all-active portfolio trends never mix.

Why: the Financials screen wrote one row a day from whatever scope it showed (default: the watchlist, 67 to 137
projects), and the morning brief, which looks at all 812 active projects, read that series as its own. On 17 Oct it
printed a Delivery Risk trend of 16 critical above a drilldown of 72 critical projects.

What it does (idempotent; every step is guarded):
  - adds Scope NVARCHAR(16) NOT NULL DEFAULT N'Watchlist'. The 103 existing rows (2025-11-23 to 2026-10-17, 0 duplicate
    dates) take the default, which is what they were.
  - CHECK Scope IN ('Watchlist', 'AllActive')
  - drops the date-only uniqueness (live: index UX_FinancialPortfolioSnapshot_Date) and makes (SnapshotDate, Scope) unique

The batch below is SqlFinancialPortfolioSnapshotStore.EnsureSchemaSql verbatim; PortfolioTrendScopeTests fails if the two
drift apart. After this runs, the app's EnsureSchemaAsync finds every step done and needs no DDL rights.

Run in KorTransmittals as sa (transmittals_app has datareader/datawriter only, no DDL).
Check afterwards:
  SELECT Scope, COUNT(*) FROM dbo.FinancialPortfolioSnapshot GROUP BY Scope;   -- Watchlist 103
*/
USE KorTransmittals;
GO

IF OBJECT_ID('dbo.FinancialPortfolioSnapshot', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FinancialPortfolioSnapshot
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialPortfolioSnapshot PRIMARY KEY,
        SnapshotDate DATE NOT NULL,
        HealthyCount INT NOT NULL,
        WatchCount INT NOT NULL,
        CriticalCount INT NOT NULL,
        TotalProjects INT NOT NULL,
        CreatedUtc DATETIME NOT NULL CONSTRAINT DF_FinancialPortfolioSnapshot_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
END

IF COL_LENGTH('dbo.FinancialPortfolioSnapshot', 'Scope') IS NULL
    ALTER TABLE dbo.FinancialPortfolioSnapshot
        ADD Scope NVARCHAR(16) NOT NULL CONSTRAINT DF_FinancialPortfolioSnapshot_Scope DEFAULT N'Watchlist';

IF OBJECT_ID('dbo.CK_FinancialPortfolioSnapshot_Scope', 'C') IS NULL
    EXEC(N'ALTER TABLE dbo.FinancialPortfolioSnapshot ADD CONSTRAINT CK_FinancialPortfolioSnapshot_Scope CHECK (Scope IN (N''Watchlist'', N''AllActive''));');

IF OBJECT_ID('dbo.UQ_FinancialPortfolioSnapshot_SnapshotDate', 'UQ') IS NOT NULL
    ALTER TABLE dbo.FinancialPortfolioSnapshot DROP CONSTRAINT UQ_FinancialPortfolioSnapshot_SnapshotDate;

-- The live table (measured 18 Oct 2026) is unique on date through this index, not the constraint above.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FinancialPortfolioSnapshot_Date' AND object_id = OBJECT_ID('dbo.FinancialPortfolioSnapshot'))
    DROP INDEX UX_FinancialPortfolioSnapshot_Date ON dbo.FinancialPortfolioSnapshot;

IF OBJECT_ID('dbo.UQ_FinancialPortfolioSnapshot_SnapshotDate_Scope', 'UQ') IS NULL
    EXEC(N'ALTER TABLE dbo.FinancialPortfolioSnapshot ADD CONSTRAINT UQ_FinancialPortfolioSnapshot_SnapshotDate_Scope UNIQUE (SnapshotDate, Scope);');
GO

SELECT Scope, COUNT(*) AS Rows FROM dbo.FinancialPortfolioSnapshot GROUP BY Scope;
GO
