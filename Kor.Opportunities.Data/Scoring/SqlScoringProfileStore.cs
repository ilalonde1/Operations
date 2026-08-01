#nullable enable
using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Scoring;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Scoring;

/// <summary>
/// SQL Server implementation of <see cref="IScoringProfileStore"/>. Reads
/// from / writes to <c>opportunities.ScoringProfile</c> with a fixed
/// <c>ProfileKey = 'Default'</c>. Per-discipline profiles are deferred per
/// plan §15 deferred-features.
/// </summary>
public sealed class SqlScoringProfileStore : IScoringProfileStore
{
    private const int CommandTimeoutSeconds = 15;
    private const string ProfileKey = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _connectionString;

    public SqlScoringProfileStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<ScoringOptions?> LoadAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT ValueJson
FROM opportunities.ScoringProfile
WHERE ProfileKey = @key;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@key", SqlDbType.VarChar, 64).Value = ProfileKey;

        var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScoringOptions>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            // Persisted JSON is malformed — treat as "no profile" so the accessor
            // falls back to KOR defaults rather than crashing the App on startup.
            return null;
        }
    }

    public async Task SaveAsync(ScoringOptions options, CancellationToken ct)
    {
        const string sql = @"
BEGIN TRAN;

UPDATE opportunities.ScoringProfile WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET    ValueJson    = @json,
       UpdatedAtUtc = sysdatetimeoffset(),
       UpdatedBy    = @actor
WHERE  ProfileKey = @key;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.ScoringProfile (ProfileKey, ValueJson, UpdatedAtUtc, UpdatedBy)
    VALUES (@key, @json, sysdatetimeoffset(), @actor);
END

COMMIT TRAN;";

        var payload = JsonSerializer.Serialize(options, JsonOptions);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@key", SqlDbType.VarChar, 64).Value = ProfileKey;
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = payload;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = Environment.UserName;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
