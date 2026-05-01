#nullable enable
using System.Data;
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.FileSync.Service.ControlPlane;

internal sealed class SqlControlPlaneStore : IControlPlaneStore
{
    private const int CommandTimeoutSeconds = 15;

    private readonly string _cs;

    public SqlControlPlaneStore(IOptions<FileSyncOptions> options)
    {
        _cs = options.Value.KorTransmittalsDb;
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1;", con) { CommandTimeout = CommandTimeoutSeconds };
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int i && i == 1;
    }

    public async Task WriteHeartbeatAsync(
        string hostName,
        DateTimeOffset startedAt,
        string mode,
        string? version,
        int jobsRegistered,
        int? watcherGen,
        CancellationToken ct)
    {
        const string sql = @"
MERGE FileSync.ServiceHeartbeat AS t
USING (SELECT @host AS HostName) AS s
    ON t.HostName = s.HostName
WHEN MATCHED THEN
    UPDATE SET LastHeartbeatAt = sysdatetimeoffset(),
               StartedAt       = @started,
               GlobalMode      = @mode,
               ServiceVersion  = @ver,
               JobsRegistered  = @jobs,
               WatcherGen      = @gen
WHEN NOT MATCHED THEN
    INSERT (HostName, StartedAt, LastHeartbeatAt, ServiceVersion, GlobalMode, WatcherGen, JobsRegistered)
    VALUES (@host,    @started,  sysdatetimeoffset(), @ver,         @mode,      @gen,       @jobs);";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 128).Value = hostName;
        cmd.Parameters.Add("@started", SqlDbType.DateTimeOffset).Value = startedAt;
        cmd.Parameters.Add("@mode", SqlDbType.VarChar, 16).Value = mode;
        cmd.Parameters.Add("@ver", SqlDbType.VarChar, 32).Value = (object?)version ?? DBNull.Value;
        cmd.Parameters.Add("@jobs", SqlDbType.Int).Value = jobsRegistered;
        cmd.Parameters.Add("@gen", SqlDbType.Int).Value = (object?)watcherGen ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<JobConfig?> GetJobAsync(string jobName, CancellationToken ct)
    {
        const string sql = @"
SELECT JobName, Mode, CronExpression, Enabled
FROM FileSync.Jobs
WHERE JobName = @name;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new JobConfig(
            JobName: r.GetString(0),
            Mode: r.GetString(1),
            CronExpression: r.IsDBNull(2) ? null : r.GetString(2),
            Enabled: r.GetBoolean(3));
    }

    public async Task<PendingTrigger?> ClaimNextPendingTriggerAsync(string claimingHost, CancellationToken ct)
    {
        // UPDLOCK + READPAST lets multiple pollers (across hosts) coexist:
        // each poller skips rows another is currently claiming, instead of blocking on them.
        const string sql = @"
WITH NextTrigger AS (
    SELECT TOP(1) TriggerId, JobName, Status, ClaimedAt, ClaimedByHost, RequestedBy, Args
    FROM FileSync.JobTriggers WITH (UPDLOCK, READPAST)
    WHERE Status = 'Pending'
    ORDER BY RequestedAt
)
UPDATE NextTrigger
SET Status        = 'Claimed',
    ClaimedAt     = sysdatetimeoffset(),
    ClaimedByHost = @host
OUTPUT inserted.TriggerId, inserted.JobName, inserted.RequestedBy, inserted.Args;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 128).Value = claimingHost;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new PendingTrigger(
            TriggerId: r.GetInt64(0),
            JobName: r.GetString(1),
            RequestedBy: r.GetString(2),
            Args: r.IsDBNull(3) ? null : r.GetString(3));
    }

    public async Task<long> RecordRunStartAsync(
        string jobName,
        string mode,
        string triggerSource,
        string? triggeredBy,
        string? version,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO FileSync.JobRuns (JobName, Mode, TriggerSource, TriggeredBy, ServiceVersion, Status)
OUTPUT inserted.RunId
VALUES (@name, @mode, @src, @by, @ver, 'Running');";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.VarChar, 64).Value = jobName;
        cmd.Parameters.Add("@mode", SqlDbType.VarChar, 16).Value = mode;
        cmd.Parameters.Add("@src", SqlDbType.VarChar, 16).Value = triggerSource;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = (object?)triggeredBy ?? DBNull.Value;
        cmd.Parameters.Add("@ver", SqlDbType.VarChar, 32).Value = (object?)version ?? DBNull.Value;
        var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (long)idObj!;
    }

    public async Task RecordRunSuccessAsync(long runId, string summary, CancellationToken ct)
    {
        const string sql = @"
UPDATE FileSync.JobRuns
SET Status      = 'Success',
    CompletedAt = sysdatetimeoffset(),
    Summary     = @summary
WHERE RunId = @id;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@summary", SqlDbType.NVarChar, -1).Value = (object?)summary ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordRunFailureAsync(long runId, Exception ex, CancellationToken ct)
    {
        const string sql = @"
UPDATE FileSync.JobRuns
SET Status       = 'Failed',
    CompletedAt  = sysdatetimeoffset(),
    ErrorMessage = @msg,
    ErrorStack   = @stack
WHERE RunId = @id;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@msg", SqlDbType.NVarChar, -1).Value = ex.Message;
        cmd.Parameters.Add("@stack", SqlDbType.NVarChar, -1).Value = (object?)ex.ToString() ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkTriggerCompletedAsync(long triggerId, long runId, CancellationToken ct)
    {
        const string sql = @"
UPDATE FileSync.JobTriggers
SET Status      = 'Completed',
    CompletedAt = sysdatetimeoffset(),
    LinkedRunId = @runId
WHERE TriggerId = @id;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = triggerId;
        cmd.Parameters.Add("@runId", SqlDbType.BigInt).Value = runId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
