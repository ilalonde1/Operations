#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Heartbeat;

/// <summary>
/// SQL Server implementation of <see cref="IHeartbeatStore"/>.
/// Issues a MERGE so the Worker doesn't have to know whether the row already exists.
/// Connection string is injected as a plain string (rather than IOptions&lt;...&gt;) so this
/// project stays free of any host-specific Options class.
/// </summary>
public sealed class SqlHeartbeatStore : IHeartbeatStore
{
    private const int CommandTimeoutSeconds = 15;

    // Heartbeat MERGE gets a longer ceiling than the ping. A brief SQL hiccup mid-burst
    // shouldn't classify the host as "down" through a single skipped tick. Mirrors the
    // FileSync convention.
    private const int HeartbeatTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlHeartbeatStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1;", con) { CommandTimeout = CommandTimeoutSeconds };
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int i && i == 1;
    }

    public async Task WriteHeartbeatAsync(
        string serviceName,
        string machineName,
        string? version,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.ServiceHeartbeat AS t
USING (SELECT @svc AS ServiceName, @host AS MachineName) AS s
    ON t.ServiceName = s.ServiceName AND t.MachineName = s.MachineName
WHEN MATCHED THEN
    UPDATE SET LastBeatUtc = sysdatetimeoffset(),
               Version     = @ver
WHEN NOT MATCHED THEN
    INSERT (ServiceName, MachineName, Version, LastBeatUtc)
    VALUES (@svc,         @host,        @ver,    sysdatetimeoffset());";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = HeartbeatTimeoutSeconds };
        cmd.Parameters.Add("@svc", SqlDbType.NVarChar, 64).Value = serviceName;
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 128).Value = machineName;
        cmd.Parameters.Add("@ver", SqlDbType.NVarChar, 32).Value = (object?)version ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
