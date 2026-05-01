#nullable enable
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.App.FileSync;

public sealed class FileSyncControlPlaneReader
{
    // 30 s matches Kor.Operations.Data.SqlTimeouts.UiFacing for parity with the rest of the App.
    private const int UiTimeoutSeconds = 30;

    private readonly string _cs;

    public FileSyncControlPlaneReader(string connectionString)
    {
        _cs = connectionString;
    }

    public async Task<IReadOnlyList<HeartbeatRow>> GetHeartbeatsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT HostName, StartedAt, LastHeartbeatAt, GlobalMode, ServiceVersion, WatcherGen, JobsRegistered
FROM FileSync.ServiceHeartbeat
ORDER BY HostName;";

        var rows = new List<HeartbeatRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new HeartbeatRow
            {
                HostName = r.GetString(0),
                StartedAt = r.GetDateTimeOffset(1),
                LastHeartbeatAt = r.GetDateTimeOffset(2),
                GlobalMode = r.GetString(3),
                ServiceVersion = r.IsDBNull(4) ? null : r.GetString(4),
                WatcherGen = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                JobsRegistered = r.GetInt32(6),
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<JobRow>> GetJobsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT JobName, DisplayName, Mode, CronExpression, Enabled, LastConfigChangedAt, Notes
FROM FileSync.Jobs
ORDER BY JobName;";

        var rows = new List<JobRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobRow
            {
                JobName = r.GetString(0),
                DisplayName = r.GetString(1),
                Mode = r.GetString(2),
                CronExpression = r.IsDBNull(3) ? null : r.GetString(3),
                Enabled = r.GetBoolean(4),
                LastConfigChangedAt = r.GetDateTimeOffset(5),
                Notes = r.IsDBNull(6) ? null : r.GetString(6),
            });
        }

        return rows;
    }
}
