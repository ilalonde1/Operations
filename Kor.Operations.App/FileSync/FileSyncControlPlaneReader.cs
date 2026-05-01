#nullable enable
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.App.FileSync;

// Read + write surface against the FileSync.* schema. Class kept named "Reader"
// for now -- adding writes was the smallest possible change. Rename to
// "Repository" if/when this grows further.
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

    public async Task SetJobModeAsync(string jobName, string mode, string changedBy, CancellationToken ct)
    {
        if (mode != "Shadow" && mode != "Live")
            throw new System.ArgumentException("Mode must be 'Shadow' or 'Live'.", nameof(mode));

        const string sql = @"
UPDATE FileSync.Jobs
SET Mode                = @mode,
    LastConfigChangedAt = sysdatetimeoffset(),
    LastConfigChangedBy = @by
WHERE JobName = @name;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@mode", SqlDbType.VarChar, 16).Value = mode;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = changedBy;
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows == 0)
            throw new System.InvalidOperationException($"Job '{jobName}' was not found in FileSync.Jobs.");
    }

    public async Task<long> QueueManualFireAsync(string jobName, string requestedBy, string? args, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO FileSync.JobTriggers (JobName, RequestedBy, Args, Status)
OUTPUT inserted.TriggerId
VALUES (@name, @by, @args, 'Pending');";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = requestedBy;
        cmd.Parameters.Add("@args", SqlDbType.NVarChar, -1).Value = (object?)args ?? System.DBNull.Value;
        var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (long)idObj!;
    }

    public async Task<int> GetPendingTriggerCountAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM FileSync.JobTriggers WHERE Status = 'Pending';";
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int i ? i : 0;
    }
}
