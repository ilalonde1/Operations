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
        // OUTER APPLY pulls each job's latest run in one shot. The TOP(1)
        // ORDER BY StartedAt DESC is covered by IX_FileSync_JobRuns_JobName_StartedAt.
        const string sql = @"
SELECT
    j.JobName, j.DisplayName, j.Mode, j.CronExpression, j.Enabled, j.LastConfigChangedAt, j.Notes,
    lr.RunId, lr.Status, lr.StartedAt, lr.CompletedAt, lr.Summary
FROM FileSync.Jobs j
OUTER APPLY (
    SELECT TOP (1) RunId, Status, StartedAt, CompletedAt, Summary
    FROM FileSync.JobRuns
    WHERE JobName = j.JobName
    ORDER BY StartedAt DESC
) lr
ORDER BY j.JobName;";

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
                LastRunId = r.IsDBNull(7) ? null : r.GetInt64(7),
                LastRunStatus = r.IsDBNull(8) ? null : r.GetString(8),
                LastRunStartedAt = r.IsDBNull(9) ? null : r.GetDateTimeOffset(9),
                LastRunCompletedAt = r.IsDBNull(10) ? null : r.GetDateTimeOffset(10),
                LastRunSummary = r.IsDBNull(11) ? null : r.GetString(11),
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

    public async Task<IReadOnlyList<JobKnobRow>> GetKnobsAsync(string jobName, CancellationToken ct)
    {
        const string sql = @"
SELECT KnobName, KnobValue, UpdatedAt, UpdatedBy
FROM FileSync.JobKnobs
WHERE JobName = @name
ORDER BY KnobName;";

        var rows = new List<JobKnobRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobKnobRow
            {
                KnobName = r.GetString(0),
                KnobValue = r.IsDBNull(1) ? null : r.GetString(1),
                UpdatedAt = r.GetDateTimeOffset(2),
                UpdatedBy = r.GetString(3),
            });
        }

        return rows;
    }

    public async Task UpsertKnobAsync(string jobName, string knobName, string? knobValue, string updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(knobName))
            throw new System.ArgumentException("KnobName is required.", nameof(knobName));

        // Single-statement MERGE keeps insert/update atomic without a transaction.
        const string sql = @"
MERGE FileSync.JobKnobs AS t
USING (SELECT @job AS JobName, @name AS KnobName) AS s
    ON t.JobName = s.JobName AND t.KnobName = s.KnobName
WHEN MATCHED THEN UPDATE SET
    KnobValue = @value,
    UpdatedAt = sysdatetimeoffset(),
    UpdatedBy = @by
WHEN NOT MATCHED THEN INSERT (JobName, KnobName, KnobValue, UpdatedAt, UpdatedBy)
    VALUES (@job, @name, @value, sysdatetimeoffset(), @by);";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@job", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = knobName;
        cmd.Parameters.Add("@value", SqlDbType.NVarChar, 1024).Value = (object?)knobValue ?? System.DBNull.Value;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = updatedBy;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteKnobAsync(string jobName, string knobName, CancellationToken ct)
    {
        const string sql = @"DELETE FROM FileSync.JobKnobs WHERE JobName = @job AND KnobName = @name;";
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@job", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = knobName;
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows == 1;
    }

    public async Task<IReadOnlyList<PendingTriggerRow>> GetPendingTriggersAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT TriggerId, JobName, RequestedAt, RequestedBy, Args
FROM FileSync.JobTriggers
WHERE Status = 'Pending'
ORDER BY RequestedAt;";

        var rows = new List<PendingTriggerRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PendingTriggerRow
            {
                TriggerId = r.GetInt64(0),
                JobName = r.GetString(1),
                RequestedAt = r.GetDateTimeOffset(2),
                RequestedBy = r.GetString(3),
                Args = r.IsDBNull(4) ? null : r.GetString(4),
            });
        }

        return rows;
    }

    public async Task<bool> CancelPendingTriggerAsync(long triggerId, string cancelledBy, CancellationToken ct)
    {
        // Conditional UPDATE so we never race the TriggerPoller: if the
        // service has already CLAIMED the row, we leave it alone and let
        // the run finish normally. Returns false in that case.
        const string sql = @"
UPDATE FileSync.JobTriggers
SET Status      = 'Cancelled',
    CompletedAt = sysdatetimeoffset(),
    Args        = ISNULL(Args, '') + N' [cancelled by ' + @by + N']'
WHERE TriggerId = @id AND Status = 'Pending';";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = cancelledBy;
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows == 1;
    }

    public async Task<IReadOnlyList<JobRunRow>> GetRecentRunsAsync(string jobName, int top, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@top)
    RunId, JobName, StartedAt, CompletedAt, Status, Mode, TriggerSource, TriggeredBy,
    Summary, ErrorMessage, ErrorStack, HostName, ServiceVersion
FROM FileSync.JobRuns
WHERE JobName = @name
ORDER BY StartedAt DESC;";

        var rows = new List<JobRunRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = UiTimeoutSeconds };
        cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new JobRunRow
            {
                RunId = r.GetInt64(0),
                JobName = r.GetString(1),
                StartedAt = r.GetDateTimeOffset(2),
                CompletedAt = r.IsDBNull(3) ? null : r.GetDateTimeOffset(3),
                Status = r.GetString(4),
                Mode = r.GetString(5),
                TriggerSource = r.GetString(6),
                TriggeredBy = r.IsDBNull(7) ? null : r.GetString(7),
                Summary = r.IsDBNull(8) ? null : r.GetString(8),
                ErrorMessage = r.IsDBNull(9) ? null : r.GetString(9),
                ErrorStack = r.IsDBNull(10) ? null : r.GetString(10),
                HostName = r.GetString(11),
                ServiceVersion = r.IsDBNull(12) ? null : r.GetString(12),
            });
        }

        return rows;
    }
}
