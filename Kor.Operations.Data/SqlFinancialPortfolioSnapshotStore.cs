using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
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
                ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString ??
                ConfigurationManager.ConnectionStrings["KorTransmittals"]?.ConnectionString;

            if (!string.IsNullOrWhiteSpace(cs))
                return cs;

            foreach (ConnectionStringSettings s in ConfigurationManager.ConnectionStrings)
            {
                if (!string.IsNullOrWhiteSpace(s.ConnectionString))
                    return s.ConnectionString;
            }

            return "";
        }

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
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

    ALTER TABLE dbo.FinancialPortfolioSnapshot
        ADD CONSTRAINT UQ_FinancialPortfolioSnapshot_SnapshotDate UNIQUE (SnapshotDate);
END";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<bool> TryInsertSnapshotAsync(DateTime snapshotDateLocal, int healthyCount, int watchCount, int criticalCount, int totalProjects, CancellationToken ct = default)
        {
            const string sql = @"
DECLARE @d date = @SnapshotDate;
IF NOT EXISTS (SELECT 1 FROM dbo.FinancialPortfolioSnapshot WHERE SnapshotDate = @d)
BEGIN
    INSERT INTO dbo.FinancialPortfolioSnapshot(SnapshotDate, HealthyCount, WatchCount, CriticalCount, TotalProjects, CreatedUtc)
    VALUES(@d, @Healthy, @Watch, @Critical, @Total, SYSUTCDATETIME());
    SELECT 1;
END
ELSE
BEGIN
    SELECT 0;
END";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@SnapshotDate", snapshotDateLocal.Date);
            cmd.Parameters.AddWithValue("@Healthy", healthyCount);
            cmd.Parameters.AddWithValue("@Watch", watchCount);
            cmd.Parameters.AddWithValue("@Critical", criticalCount);
            cmd.Parameters.AddWithValue("@Total", totalProjects);

            var v = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(v) == 1;
        }

        public sealed record SnapshotRow(DateTime SnapshotDate, int HealthyCount, int WatchCount, int CriticalCount, int TotalProjects);

        public async Task<List<SnapshotRow>> LoadSnapshotsAsync(DateTime startDateLocal, CancellationToken ct = default)
        {
            const string sql = @"
SELECT SnapshotDate, HealthyCount, WatchCount, CriticalCount, TotalProjects
FROM dbo.FinancialPortfolioSnapshot
WHERE SnapshotDate >= @StartDate
ORDER BY SnapshotDate;";

            var list = new List<SnapshotRow>(128);
            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@StartDate", startDateLocal.Date);

            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await r.ReadAsync(ct))
            {
                var d = r.GetDateTime(0);
                var healthy = r.GetInt32(1);
                var watch = r.GetInt32(2);
                var critical = r.GetInt32(3);
                var total = r.GetInt32(4);
                list.Add(new SnapshotRow(d.Date, healthy, watch, critical, total));
            }
            return list;
        }
    }
}

