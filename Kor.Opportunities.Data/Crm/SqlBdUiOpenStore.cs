#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Minimal adoption instrumentation (plan 2.2c): one row per BD-surface open,
/// consumed by the kill-list review query — never by any UI. Fire-and-forget:
/// losing a row is fine; instrumenting must never slow or break a view.
/// </summary>
public interface IBdUiOpenStore
{
    void RecordOpen(string surface, string? byStaffId);
}

public sealed class SqlBdUiOpenStore : IBdUiOpenStore
{
    private readonly string _connectionString;

    public SqlBdUiOpenStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public void RecordOpen(string surface, string? byStaffId)
    {
        // Deliberately fire-and-forget on the thread pool — the caller is a
        // view Loaded handler and must never wait on instrumentation.
        _ = Task.Run(async () =>
        {
            try
            {
                const string sql = @"
INSERT INTO opportunities.BdUiOpens (Surface, ByStaffId)
VALUES (@surface, @by);";
                await using var con = new SqlConnection(_connectionString);
                await con.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 10 };
                cmd.Parameters.Add("@surface", SqlDbType.NVarChar, 50).Value = surface;
                cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = (object?)byStaffId ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Instrumentation is expendable by design.
            }
        });
    }
}
