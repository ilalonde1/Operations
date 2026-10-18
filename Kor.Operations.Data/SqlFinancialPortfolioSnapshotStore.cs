#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    /// <summary>
    /// Which projects a portfolio snapshot counted. Until 18 Oct 2026 there was no scope column: the Financials screen
    /// wrote its watchlist (67 to 137 projects) and the morning brief, on all 812 active, read that series as its own.
    /// </summary>
    public static class PortfolioSnapshotScope
    {
        public const string Watchlist = "Watchlist";
        public const string AllActive = "AllActive";
    }

    public sealed class SqlFinancialPortfolioSnapshotStore
    {
        private readonly string _cs;

        public SqlFinancialPortfolioSnapshotStore(string? connectionString = null)
        {
            _cs = connectionString ?? ResolveTransmittalsConnectionString();
        }

        private static string ResolveTransmittalsConnectionString()
        {
            var cs =
                ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittalsDb]?.ConnectionString ??
                ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittals]?.ConnectionString;

            if (!string.IsNullOrWhiteSpace(cs))
                return cs;

            foreach (ConnectionStringSettings s in ConfigurationManager.ConnectionStrings)
            {
                if (!string.IsNullOrWhiteSpace(s.ConnectionString))
                    return s.ConnectionString;
            }

            return "";
        }

        // Idempotent. Rows written before the Scope column existed came from the screen's default watchlist, so the
        // column's default labels them Watchlist. Later steps run through EXEC because the column may not exist when
        // the batch compiles.
        public const string EnsureSchemaSql = @"
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
    EXEC(N'ALTER TABLE dbo.FinancialPortfolioSnapshot ADD CONSTRAINT UQ_FinancialPortfolioSnapshot_SnapshotDate_Scope UNIQUE (SnapshotDate, Scope);');";

        public const string InsertSql = @"
DECLARE @d date = @SnapshotDate;
IF NOT EXISTS (SELECT 1 FROM dbo.FinancialPortfolioSnapshot WHERE SnapshotDate = @d AND Scope = @Scope)
BEGIN
    INSERT INTO dbo.FinancialPortfolioSnapshot(SnapshotDate, Scope, HealthyCount, WatchCount, CriticalCount, TotalProjects, CreatedUtc)
    VALUES(@d, @Scope, @Healthy, @Watch, @Critical, @Total, SYSUTCDATETIME());
    SELECT 1;
END
ELSE
BEGIN
    SELECT 0;
END";

        public const string LoadSql = @"
SELECT SnapshotDate, HealthyCount, WatchCount, CriticalCount, TotalProjects
FROM dbo.FinancialPortfolioSnapshot
WHERE SnapshotDate >= @StartDate AND Scope = @Scope
ORDER BY SnapshotDate;";

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(EnsureSchemaSql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        public async Task<bool> TryInsertSnapshotAsync(DateTime snapshotDateLocal, string scope, int healthyCount, int watchCount, int criticalCount, int totalProjects, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(InsertSql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@SnapshotDate", snapshotDateLocal.Date);
                cmd.Parameters.Add("@Scope", SqlDbType.NVarChar, 16).Value = scope;
                cmd.Parameters.AddWithValue("@Healthy", healthyCount);
                cmd.Parameters.AddWithValue("@Watch", watchCount);
                cmd.Parameters.AddWithValue("@Critical", criticalCount);
                cmd.Parameters.AddWithValue("@Total", totalProjects);

                var v = await cmd.ExecuteScalarAsync(innerCt);
                return Convert.ToInt32(v) == 1;
            }, ct);
        }

        public sealed record SnapshotRow(DateTime SnapshotDate, int HealthyCount, int WatchCount, int CriticalCount, int TotalProjects);

        public async Task<List<SnapshotRow>> LoadSnapshotsAsync(DateTime startDateLocal, string scope, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<SnapshotRow>(128);
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(LoadSql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@StartDate", startDateLocal.Date);
                cmd.Parameters.Add("@Scope", SqlDbType.NVarChar, 16).Value = scope;

                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                {
                    var d = r.GetDateTime(0);
                    var healthy = r.GetInt32(1);
                    var watch = r.GetInt32(2);
                    var critical = r.GetInt32(3);
                    var total = r.GetInt32(4);
                    list.Add(new SnapshotRow(d.Date, healthy, watch, critical, total));
                }
                return list;
            }, ct);
        }
    }
}
