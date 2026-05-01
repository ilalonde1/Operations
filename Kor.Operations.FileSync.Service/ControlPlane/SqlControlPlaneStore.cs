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
}
