#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// City -> market lookup backed by <c>opportunities.EventMarketRegion</c>.
/// Kept in the database so adding a city is an INSERT, not a redeploy.
/// </summary>
public interface IEventMarketRegionStore
{
    /// <summary>
    /// Case-insensitive city -> market map. Empty when the table has no rows,
    /// in which case callers fall back to the source's DefaultMarket.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct);
}

public sealed class SqlEventMarketRegionStore : IEventMarketRegionStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlEventMarketRegionStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT CityName, Market
FROM opportunities.EventMarketRegion;";

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            map[reader.GetString(0)] = reader.GetString(1);
        }

        return map;
    }
}
