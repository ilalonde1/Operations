#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.PMTools
{
    internal sealed class EmployeeScoreSnapshotStore
    {
        private readonly string _cs;

        internal EmployeeScoreSnapshotStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        internal async Task SaveSnapshotsAsync(DateTime snapshotDate, IReadOnlyList<EmployeeSummaryRow> rows, CancellationToken ct = default)
        {
            if (rows.Count == 0) return;

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.EmployeeId)) continue;

                const string sql = @"
MERGE dbo.EmployeeScoreSnapshots AS t
USING (VALUES(@Date, @EmpId, @EmpName, @Role, @Billable, @Efficiency, @Health, @Score, @Grade, @FeePerHr, @Projects, @ConstType))
       AS s(SnapshotDate, EmployeeId, EmployeeName, PrimaryRole, BillableRateScore, EfficiencyScore, ProjectHealthScore, ProductivityScore, ProductivityGrade, FeePerHr, ProjectCount, PrimaryConstructionType)
ON t.SnapshotDate = s.SnapshotDate AND t.EmployeeId = s.EmployeeId
WHEN MATCHED THEN
    UPDATE SET EmployeeName = s.EmployeeName, PrimaryRole = s.PrimaryRole,
               BillableRateScore = s.BillableRateScore, EfficiencyScore = s.EfficiencyScore,
               ProjectHealthScore = s.ProjectHealthScore, ProductivityScore = s.ProductivityScore,
               ProductivityGrade = s.ProductivityGrade, FeePerHr = s.FeePerHr,
               ProjectCount = s.ProjectCount, PrimaryConstructionType = s.PrimaryConstructionType
WHEN NOT MATCHED THEN
    INSERT (SnapshotDate, EmployeeId, EmployeeName, PrimaryRole, BillableRateScore, EfficiencyScore, ProjectHealthScore, ProductivityScore, ProductivityGrade, FeePerHr, ProjectCount, PrimaryConstructionType)
    VALUES (s.SnapshotDate, s.EmployeeId, s.EmployeeName, s.PrimaryRole, s.BillableRateScore, s.EfficiencyScore, s.ProjectHealthScore, s.ProductivityScore, s.ProductivityGrade, s.FeePerHr, s.ProjectCount, s.PrimaryConstructionType);";

                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
                cmd.Parameters.Add("@Date", SqlDbType.Date).Value = snapshotDate.Date;
                cmd.Parameters.Add("@EmpId", SqlDbType.NVarChar, 50).Value = row.EmployeeId;
                cmd.Parameters.Add("@EmpName", SqlDbType.NVarChar, 200).Value = row.EmployeeName;
                cmd.Parameters.Add("@Role", SqlDbType.NVarChar, 50).Value = row.PrimaryRole;
                cmd.Parameters.Add("@Billable", SqlDbType.Float).Value = row.BillableRateScore;
                cmd.Parameters.Add("@Efficiency", SqlDbType.Float).Value = row.EfficiencyScore;
                cmd.Parameters.Add("@Health", SqlDbType.Float).Value = row.ProjectHealthScore;
                cmd.Parameters.Add("@Score", SqlDbType.Float).Value = row.ProductivityScore;
                cmd.Parameters.Add("@Grade", SqlDbType.NVarChar, 5).Value = row.ProductivityGrade;
                cmd.Parameters.Add("@FeePerHr", SqlDbType.Float).Value = row.FeePerHr;
                cmd.Parameters.Add("@Projects", SqlDbType.Int).Value = row.ProjectCount;
                cmd.Parameters.Add("@ConstType", SqlDbType.NVarChar, 100).Value = row.PrimaryConstructionType;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        internal async Task<IReadOnlyList<EmployeeScoreSnapshot>> LoadTrendAsync(string employeeId, CancellationToken ct = default)
        {
            var list = new List<EmployeeScoreSnapshot>();
            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct).ConfigureAwait(false);

            const string sql = @"
SELECT SnapshotDate, ProductivityScore, ProductivityGrade, BillableRateScore, EfficiencyScore, ProjectHealthScore, FeePerHr, ProjectCount
FROM dbo.EmployeeScoreSnapshots
WHERE EmployeeId = @EmpId
ORDER BY SnapshotDate ASC;";

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
            cmd.Parameters.Add("@EmpId", SqlDbType.NVarChar, 50).Value = employeeId;
            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new EmployeeScoreSnapshot
                {
                    SnapshotDate = r.GetDateTime(0),
                    ProductivityScore = r.GetDouble(1),
                    ProductivityGrade = r.GetString(2),
                    BillableRateScore = r.GetDouble(3),
                    EfficiencyScore = r.GetDouble(4),
                    ProjectHealthScore = r.GetDouble(5),
                    FeePerHr = r.GetDouble(6),
                    ProjectCount = r.GetInt32(7),
                });
            }
            return list;
        }
    }

    internal sealed class EmployeeScoreSnapshot
    {
        public DateTime SnapshotDate { get; init; }
        public double ProductivityScore { get; init; }
        public string ProductivityGrade { get; init; } = "";
        public double BillableRateScore { get; init; }
        public double EfficiencyScore { get; init; }
        public double ProjectHealthScore { get; init; }
        public double FeePerHr { get; init; }
        public int ProjectCount { get; init; }
    }
}
