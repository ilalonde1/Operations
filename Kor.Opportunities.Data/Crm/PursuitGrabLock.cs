#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One grab at a time, firm-wide. A tender grab decides in C# whether a forward pursuit exists to join, then writes; a
/// forward grab writes the forward pursuit. Run side by side, each missed the other and one project got two live pursuits
/// (audit C5, 2026-10-21). Both now hold this lock (SQL Server sp_getapplock, session-owned, on its own connection) from
/// before the decision until after the write. Grabs take milliseconds, so the wait is never felt; a lock not granted in
/// 15 seconds throws rather than grabbing unguarded.
/// </summary>
internal sealed class PursuitGrabLock : IAsyncDisposable
{
    private const string Resource = "Kor.BD.PursuitGrab";
    private readonly SqlConnection _con;

    private PursuitGrabLock(SqlConnection con) => _con = con;

    public static async Task<PursuitGrabLock> AcquireAsync(string connectionString, CancellationToken ct)
    {
        var con = new SqlConnection(connectionString);
        try
        {
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand("sp_getapplock", con) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            cmd.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = Resource;
            cmd.Parameters.Add("@LockMode", SqlDbType.NVarChar, 32).Value = "Exclusive";
            cmd.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 32).Value = "Session";
            cmd.Parameters.Add("@LockTimeout", SqlDbType.Int).Value = 15_000;
            var result = cmd.Parameters.Add("@Result", SqlDbType.Int);
            result.Direction = ParameterDirection.ReturnValue;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if ((int)result.Value < 0)
                throw new TimeoutException($"Another grab is still running (sp_getapplock returned {(int)result.Value}). Try again.");
            return new PursuitGrabLock(con);
        }
        catch
        {
            await con.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Closing the session releases a session-owned applock; the explicit release just makes it immediate.
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var cmd = new SqlCommand("sp_releaseapplock", _con) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            cmd.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = Resource;
            cmd.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 32).Value = "Session";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // The connection's close below releases it anyway.
        }
        await _con.DisposeAsync().ConfigureAwait(false);
    }
}
